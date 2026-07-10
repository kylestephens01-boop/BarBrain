using System.Text;
using System.Text.Json;
using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Catalog.Import;

public sealed record ImportResult(string Source, int Created, int Updated, int Unchanged, int Skipped)
{
    public override string ToString()
        => $"{Source}: {Created} created, {Updated} updated, {Unchanged} unchanged, {Skipped} skipped";
}

/// <summary>Outcome of <see cref="CatalogImportService.ClearAttributeOverrideAsync"/>.</summary>
public enum ClearOverrideResult { Cleared, AlreadyBaseline }

/// <summary>
/// All catalog importers (Sprint 1 spec): idempotent (re-run = no dupes, via
/// the unique (Source, SourceRef) indexes and natural-key style matching),
/// provenance-tagged, and LICENSE-GATED — every source here has an entry in
/// docs/DATA-SOURCES.md (ADR-024). Do not add a source without one. For
/// product-seed files the gate is enforced at runtime: the registry is
/// embedded in the binary and an unregistered source tag refuses to import.
/// Importers never reach the network; bulk sources take a local file/dir path
/// so runs are resumable and auditable (RUNBOOK documents the downloads).
/// </summary>
public sealed class CatalogImportService(
    AppDbContext db,
    AttributeVectorService vectors,
    MergeService merges,
    ISettingsService settings,
    ILogger<CatalogImportService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public const string StylesSource = "seed:barbrain-styles";
    public const string CorridorSource = "seed:corridor";
    public const string OpenBreweryDbSource = "seed:openbrewerydb";
    public const string BeerDbSource = "seed:beerdb";
    public const string TtbSource = "seed:ttb";
    public const string DemoSource = "seed:demo-dupes";

    /// <summary>Config flag (Hard Rule 10): default confidence (percent) for seed attribute overrides.</summary>
    public const string SeedOverrideConfidenceFlag = "catalog.seed_override_confidence_pct";
    /// <summary>Logical name of the embedded docs/DATA-SOURCES.md (license gate, ADR-024).</summary>
    public const string DataSourcesResourceName = "BarBrain.Api.DATA-SOURCES.md";

    // ------------------------------------------------------------------ seed:
    public async Task<ImportResult> ImportAttributesAsync(string seedDir, CancellationToken ct = default)
    {
        var seeds = ReadJson<List<AttributeSeed>>(Path.Combine(seedDir, "attributes.json"));
        var existing = await db.AttributeDefinitions.ToDictionaryAsync(a => a.Key, ct);

        int created = 0, updated = 0, unchanged = 0;
        foreach (var seed in seeds)
        {
            if (existing.TryGetValue(seed.Key, out var row))
            {
                if (row.Category == seed.Category && row.DimIndex == seed.DimIndex
                    && row.BridgeIndex == seed.BridgeIndex && row.DisplayName == seed.DisplayName
                    && row.Description == seed.Description)
                {
                    unchanged++;
                    continue;
                }
                row.Category = seed.Category;
                row.DimIndex = seed.DimIndex;
                row.BridgeIndex = seed.BridgeIndex;
                row.DisplayName = seed.DisplayName;
                row.Description = seed.Description;
                updated++;
            }
            else
            {
                db.AttributeDefinitions.Add(new AttributeDefinition
                {
                    Key = seed.Key,
                    Category = seed.Category,
                    DimIndex = seed.DimIndex,
                    BridgeIndex = seed.BridgeIndex,
                    DisplayName = seed.DisplayName,
                    Description = seed.Description,
                });
                created++;
            }
        }

        await db.SaveChangesAsync(ct);
        return Log(new ImportResult("attributes", created, updated, unchanged, 0));
    }

    public async Task<ImportResult> ImportStylesAsync(string seedDir, CancellationToken ct = default)
    {
        int created = 0, updated = 0, unchanged = 0;
        var touched = new List<Guid>();

        foreach (var category in DrinkCategory.All)
        {
            var path = Path.Combine(seedDir, $"styles.{category}.json");
            var file = ReadJson<StyleSeedFile>(path);
            if (file.Category != category)
                throw new InvalidOperationException($"{path} declares category '{file.Category}'.");

            var existing = await db.Styles
                .Include(s => s.Attributes)
                .Where(s => s.Category == category)
                .ToListAsync(ct);
            var byCode = existing.Where(s => s.Code != null).ToDictionary(s => s.Code!);

            // Two passes: create/update all styles first, then wire parents
            // (a parent may appear later in the file).
            foreach (var seed in file.Styles)
            {
                var normalized = NameNormalizer.Normalize(seed.Name);
                if (!byCode.TryGetValue(seed.Code, out var style))
                {
                    style = new Style
                    {
                        Category = category,
                        Code = seed.Code,
                        Name = seed.Name,
                        NormalizedName = normalized,
                        Source = file.Source,
                    };
                    db.Styles.Add(style);
                    byCode[seed.Code] = style;
                    created++;
                }
                else
                {
                    var same = style.Name == seed.Name
                        && RangeEqual(style.AbvMin, style.AbvMax, seed.Abv)
                        && ShortRangeEqual(style.IbuMin, style.IbuMax, seed.Ibu)
                        && RangeEqual(style.SrmMin, style.SrmMax, seed.Srm)
                        && RangeEqual(style.OgMin, style.OgMax, seed.Og)
                        && RangeEqual(style.FgMin, style.FgMax, seed.Fg)
                        && BaselinesEqual(style, category, seed.Attributes);
                    if (same) { unchanged++; continue; }
                    style.Name = seed.Name;
                    style.NormalizedName = normalized;
                    style.UpdatedAt = DateTimeOffset.UtcNow;
                    updated++;
                }

                (style.AbvMin, style.AbvMax) = Range(seed.Abv);
                (style.IbuMin, style.IbuMax) = ShortRange(seed.Ibu);
                (style.SrmMin, style.SrmMax) = Range(seed.Srm);
                (style.OgMin, style.OgMax) = Range(seed.Og);
                (style.FgMin, style.FgMax) = Range(seed.Fg);

                // The seed is the source of truth for baselines: replace.
                style.Attributes.Clear();
                if (seed.Attributes is { Count: > 0 })
                {
                    foreach (var (shortKey, value) in seed.Attributes)
                    {
                        style.Attributes.Add(new StyleAttributeValue
                        {
                            Style = style,
                            AttributeKey = $"{category}.{shortKey}",
                            Value = value,
                        });
                    }
                }
                touched.Add(style.Id);
            }

            foreach (var seed in file.Styles.Where(s => s.Parent is not null))
            {
                var style = byCode[seed.Code];
                var parent = byCode.GetValueOrDefault(seed.Parent!)
                    ?? throw new InvalidOperationException(
                        $"Style {seed.Code} references unknown parent '{seed.Parent}'.");
                if (style.ParentStyleId != parent.Id)
                {
                    style.Parent = parent;
                    touched.Add(style.Id);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        if (touched.Count > 0)
            await vectors.RecomputeStyleVectorsAsync(touched.Distinct().ToList(), ct);

        return Log(new ImportResult("styles", created, updated, unchanged, 0));
    }

    public Task<ImportResult> ImportCorridorAsync(string seedDir, CancellationToken ct = default)
        => ImportProductsAsync(Path.Combine(seedDir, "corridor-priority.json"), dataSourcesPath: null, ct);

    /// <summary>
    /// Generic product-seed importer (docs/SEED-FORMAT.md): any file in the
    /// <see cref="ProductSeedFile"/> shape, provenance-tagged with the file's
    /// own declared source. Optional per-drink attribute overrides land as
    /// source='moderator' rows (BarBrain editorial judgment); dims without an
    /// override inherit from the style baseline exactly as before.
    /// <paramref name="dataSourcesPath"/> substitutes the license registry the
    /// gate reads (tests); null = the embedded docs/DATA-SOURCES.md.
    /// </summary>
    public async Task<ImportResult> ImportProductsAsync(
        string filePath, string? dataSourcesPath = null, CancellationToken ct = default)
    {
        var file = ReadJson<ProductSeedFile>(filePath);
        EnsureSourceDocumented(file.Source, dataSourcesPath);

        var overrideConfidence = file.AttributeConfidence
            ?? await settings.GetIntAsync(SeedOverrideConfidenceFlag, 80, ct) / 100f;
        if (overrideConfidence is < 0f or > 1f)
            throw new InvalidOperationException(
                $"{filePath}: attributeConfidence {overrideConfidence} is outside 0–1.");
        var validAttributeKeys = file.Producers
                .SelectMany(p => p.Drinks).Any(d => d.Attributes is { Count: > 0 })
            ? (await db.AttributeDefinitions.AsNoTracking().Select(a => a.Key).ToListAsync(ct)).ToHashSet()
            : [];

        var styles = await db.Styles.AsNoTracking().ToListAsync(ct);
        var stylesByCategoryCode = styles.Where(s => s.Code != null)
            .ToDictionary(s => (s.Category, s.Code!), s => s.Id);
        // Names are NOT unique across tree levels (BJCP 29/29A "Fruit Beer");
        // prefer the leaf (has a parent) when a name is ambiguous.
        var stylesByCategoryName = styles
            .GroupBy(s => (s.Category, s.NormalizedName))
            .ToDictionary(g => g.Key,
                g => g.OrderByDescending(s => s.ParentStyleId != null).First().Id);

        int created = 0, updated = 0, unchanged = 0, skipped = 0;
        var touchedDrinks = new List<Guid>();

        foreach (var producerSeed in file.Producers)
        {
            var producer = await UpsertProducerAsync(
                file.Source, producerSeed.Ref, producerSeed.Name, producerSeed.Type,
                producerSeed.City, producerSeed.Region, producerSeed.Country, ct,
                counters: (c, u, n) => { created += c; updated += u; unchanged += n; });

            foreach (var drinkSeed in producerSeed.Drinks)
            {
                if (!DrinkCategory.IsValid(drinkSeed.Category))
                {
                    logger.LogWarning("{Source} drink {Ref}: invalid category {Category}; skipped.",
                        file.Source, drinkSeed.Ref, drinkSeed.Category);
                    skipped++;
                    continue;
                }

                Guid? styleId = null;
                if (drinkSeed.Style is { } styleRef)
                {
                    styleId = stylesByCategoryCode.TryGetValue((drinkSeed.Category, styleRef), out var byCode)
                        ? byCode
                        : stylesByCategoryName.GetValueOrDefault(
                            (drinkSeed.Category, NameNormalizer.Normalize(styleRef)));
                    if (styleId is null)
                    {
                        logger.LogWarning("{Source} drink {Ref}: unknown style '{Style}'; importing unstyled.",
                            file.Source, drinkSeed.Ref, styleRef);
                    }
                }

                var drink = await db.Drinks.FirstOrDefaultAsync(
                    d => d.Source == file.Source && d.SourceRef == drinkSeed.Ref, ct);
                var isNew = drink is null;
                var baseChanged = false;
                if (drink is null)
                {
                    drink = new Drink
                    {
                        ProducerId = producer.Id,
                        Name = drinkSeed.Name,
                        NormalizedName = NameNormalizer.Normalize(drinkSeed.Name),
                        Category = drinkSeed.Category,
                        StyleId = styleId,
                        Abv = drinkSeed.Abv,
                        Source = file.Source,
                        SourceRef = drinkSeed.Ref,
                    };
                    db.Drinks.Add(drink);
                    await db.SaveChangesAsync(ct);
                }
                else if (drink.Name != drinkSeed.Name || drink.StyleId != styleId
                         || drink.Abv != drinkSeed.Abv)
                {
                    drink.Name = drinkSeed.Name;
                    drink.NormalizedName = NameNormalizer.Normalize(drinkSeed.Name);
                    drink.StyleId = styleId;
                    drink.Abv = drinkSeed.Abv;
                    drink.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                    baseChanged = true;
                }

                var overridesChanged = drinkSeed.Attributes is { Count: > 0 }
                    && await ApplyAttributeOverridesAsync(
                        drink, drinkSeed, overrideConfidence, validAttributeKeys, filePath, ct);

                if (isNew) created++;
                else if (baseChanged || overridesChanged) updated++;
                else unchanged++;
                if (isNew || baseChanged || overridesChanged)
                    touchedDrinks.Add(drink.Id);
            }
        }

        if (touchedDrinks.Count > 0)
            await vectors.RecomputeDrinkVectorsAsync(touchedDrinks, ct);
        await merges.GenerateProducerCandidatesAsync(ct);
        await merges.GenerateDrinkCandidatesAsync(ct);

        return Log(new ImportResult(file.Source, created, updated, unchanged, skipped));
    }

    /// <summary>
    /// Upserts a drink's editorial override rows: source='moderator' (a human
    /// curator's judgment — the only allowed source that means that; see
    /// ADR-028), file-level confidence, replacing whatever row holds the dim
    /// (typically the materialized 'inherited' one). Removing an override from
    /// the seed later does NOT revert the row — edit the value instead.
    /// Malformed overrides fail the import: this is first-party editorial
    /// data, and a typo'd key or a 7.5-instead-of-0.75 must stop the run
    /// rather than silently skew the palate engine.
    /// </summary>
    private async Task<bool> ApplyAttributeOverridesAsync(
        Drink drink, ProductDrinkSeed seed, float confidence,
        HashSet<string> validAttributeKeys, string filePath, CancellationToken ct)
    {
        var rows = await db.DrinkAttributes
            .Where(a => a.DrinkId == drink.Id)
            .ToDictionaryAsync(a => a.AttributeKey, ct);

        var changed = false;
        foreach (var (shortKey, value) in seed.Attributes!)
        {
            var key = $"{drink.Category}.{shortKey}";
            if (!validAttributeKeys.Contains(key))
                throw new InvalidOperationException(
                    $"{filePath}: drink '{seed.Ref}' overrides unknown attribute '{shortKey}' "
                    + $"for category '{drink.Category}'.");
            if (value is < 0f or > 1f)
                throw new InvalidOperationException(
                    $"{filePath}: drink '{seed.Ref}' attribute '{shortKey}' value {value} is outside 0–1.");

            if (rows.TryGetValue(key, out var row))
            {
                if (row.Source == AttributeValueSource.Moderator
                    && Math.Abs(row.Value - value) < 0.0001f
                    && Math.Abs(row.Confidence - confidence) < 0.0001f)
                    continue;
                row.Value = value;
                row.Source = AttributeValueSource.Moderator;
                row.Confidence = confidence;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                db.DrinkAttributes.Add(new DrinkAttributeValue
                {
                    DrinkId = drink.Id,
                    AttributeKey = key,
                    Value = value,
                    Source = AttributeValueSource.Moderator,
                    Confidence = confidence,
                });
            }
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>
    /// Sanctioned corrective for a wrong editorial override (ADR-028 note):
    /// deletes the drink's source='moderator' row for one attribute key so the
    /// dim falls back to style-baseline inheritance, then resyncs vectors
    /// through the exact path the importer uses. Only moderator rows are
    /// removable: no row OR an 'inherited' row means the dim is already at
    /// baseline (after a clear, the vector sync MATERIALIZES an inherited row —
    /// that row IS the baseline state, so a re-run must be a no-op here, not a
    /// refusal); manufacturer/crowd/llm provenance is refused outright. Unknown
    /// drink ref or attribute key fails loudly — a typo must not read as "done".
    /// </summary>
    public async Task<ClearOverrideResult> ClearAttributeOverrideAsync(
        string sourceTag, string drinkRef, string shortKey, CancellationToken ct = default)
    {
        var drink = await db.Drinks.FirstOrDefaultAsync(
                d => d.Source == sourceTag && d.SourceRef == drinkRef, ct)
            ?? throw new InvalidOperationException(
                $"No drink with source '{sourceTag}' and ref '{drinkRef}'.");

        var key = $"{drink.Category}.{shortKey}";
        if (!await db.AttributeDefinitions.AnyAsync(a => a.Key == key, ct))
            throw new InvalidOperationException(
                $"Unknown attribute '{shortKey}' for category '{drink.Category}'.");

        var row = await db.DrinkAttributes.FirstOrDefaultAsync(
            a => a.DrinkId == drink.Id && a.AttributeKey == key, ct);
        if (row is null || row.Source == AttributeValueSource.Inherited)
        {
            logger.LogInformation(
                "{Source}/{Ref} {Key}: no moderator override to clear; already at style baseline.",
                sourceTag, drinkRef, key);
            return ClearOverrideResult.AlreadyBaseline;
        }
        if (row.Source != AttributeValueSource.Moderator)
            throw new InvalidOperationException(
                $"Attribute '{key}' on {sourceTag}/{drinkRef} has source '{row.Source}' — "
                + "--clear-attribute removes editorial (moderator) overrides only.");

        db.DrinkAttributes.Remove(row);
        await db.SaveChangesAsync(ct);
        await vectors.RecomputeDrinkVectorsAsync([drink.Id], ct);
        logger.LogInformation(
            "{Source}/{Ref} {Key}: moderator override cleared; dim reverted to style baseline.",
            sourceTag, drinkRef, key);
        return ClearOverrideResult.Cleared;
    }

    /// <summary>
    /// ADR-024 license gate, fail-closed: the source tag must appear in
    /// docs/DATA-SOURCES.md, which is embedded into this assembly at build
    /// time so the gate works identically in dev, CI, and the container.
    /// </summary>
    private static void EnsureSourceDocumented(string sourceTag, string? dataSourcesPath)
    {
        if (!sourceTag.StartsWith("seed:", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Product-seed source tag '{sourceTag}' must start with 'seed:' (provenance convention).");

        string registry;
        if (dataSourcesPath is not null)
        {
            registry = File.ReadAllText(dataSourcesPath);
        }
        else
        {
            using var stream = typeof(CatalogImportService).Assembly
                .GetManifestResourceStream(DataSourcesResourceName)
                ?? throw new InvalidOperationException(
                    "Embedded DATA-SOURCES.md registry is missing from the build — the license "
                    + "gate (ADR-024) cannot run, so the import is refused.");
            using var reader = new StreamReader(stream);
            registry = reader.ReadToEnd();
        }

        // Match the QUOTED tag ("seed:x") as registry entries write it — a bare
        // substring check would let an unregistered tag ride on a registered
        // superstring (e.g. seed:beer via seed:beerdb).
        if (!registry.Contains($"\"{sourceTag}\"", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Source '{sourceTag}' has no entry in docs/DATA-SOURCES.md — the license gate "
                + $"(ADR-024) refuses to import. Register the source (URL, exact license, quoted "
                + $"wording, capture date, incl. the tag as \"{sourceTag}\") and rebuild before importing.");
    }

    // --------------------------------------------------------- bulk sources:
    /// <summary>Open Brewery DB bulk CSV → producers ONLY (ADR-020; MIT — DATA-SOURCES.md).</summary>
    public async Task<ImportResult> ImportOpenBreweryDbAsync(string csvPath, CancellationToken ct = default)
    {
        int created = 0, updated = 0, unchanged = 0, skipped = 0;

        using var reader = new StreamReader(csvPath);
        var header = CsvLine(await reader.ReadLineAsync(ct)
            ?? throw new InvalidOperationException("Empty CSV."));
        int Col(string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        int idCol = Col("id"), nameCol = Col("name"), typeCol = Col("brewery_type"),
            cityCol = Col("city"), regionCol = Col("state_province"), countryCol = Col("country");
        if (regionCol < 0) regionCol = Col("state");
        if (idCol < 0 || nameCol < 0)
            throw new InvalidOperationException("CSV lacks id/name columns — is this the OBDB export?");

        var batch = 0;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = CsvLine(line);
            string Cell(int i) => i >= 0 && i < cells.Length ? cells[i].Trim() : "";

            var name = Cell(nameCol);
            var id = Cell(idCol);
            if (name.Length is 0 or > 256 || id.Length is 0 or > 128) { skipped++; continue; }

            await UpsertProducerAsync(OpenBreweryDbSource, id, name,
                producerType: "brewery",
                city: Truncate(Cell(cityCol), 128), region: Truncate(Cell(regionCol), 64),
                country: Truncate(Cell(countryCol), 64), ct,
                counters: (c, u, n) => { created += c; updated += u; unchanged += n; });

            if (++batch % 500 == 0)
            {
                db.ChangeTracker.Clear();
                logger.LogInformation("openbrewerydb: {Count} rows processed…", batch);
            }
        }

        return Log(new ImportResult("openbrewerydb", created, updated, unchanged, skipped));
    }

    /// <summary>
    /// beer.db — github.com/openbeer plain-text checkout (public domain — see
    /// DATA-SOURCES.md; NOT openbeerdb.com, which is ODbL-prohibited).
    /// breweries.txt: "[key]" blocks (name line, then "City | REGION").
    /// beers.txt: "- Brewery Name" headers followed by product lines.
    /// </summary>
    public async Task<ImportResult> ImportBeerDbAsync(string dirPath, CancellationToken ct = default)
    {
        int created = 0, updated = 0, unchanged = 0, skipped = 0;

        foreach (var file in Directory.EnumerateFiles(dirPath, "*breweries*.txt", SearchOption.AllDirectories))
        {
            var region = RegionFromBeerDbPath(file, dirPath);
            string? key = null, name = null;
            foreach (var raw in await File.ReadAllLinesAsync(file, ct))
            {
                var line = StripBeerDbComment(raw);
                if (line.Length == 0) continue;

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    await FlushBreweryAsync(key, name, city: null, ct);
                    key = line[1..^1].Trim();
                    name = null;
                }
                else if (key is not null && name is null)
                {
                    name = line;
                }
                else if (key is not null && line.Contains('|'))
                {
                    var city = line.Split('|')[0].Trim();
                    await FlushBreweryAsync(key, name, city, ct);
                    key = null; name = null;
                }
            }
            await FlushBreweryAsync(key, name, city: null, ct);

            async Task FlushBreweryAsync(string? k, string? n, string? city, CancellationToken token)
            {
                if (k is null || string.IsNullOrWhiteSpace(n)) return;
                await UpsertProducerAsync(BeerDbSource, Truncate($"brewery:{k}", 128),
                    Truncate(n, 256), "brewery", Truncate(city ?? "", 128), region, "US", token,
                    counters: (c, u, nn) => { created += c; updated += u; unchanged += nn; });
            }
        }

        foreach (var file in Directory.EnumerateFiles(dirPath, "*beers*.txt", SearchOption.AllDirectories))
        {
            Producer? current = null;
            foreach (var raw in await File.ReadAllLinesAsync(file, ct))
            {
                var line = StripBeerDbComment(raw);
                if (line.Length == 0) continue;

                if (line.StartsWith("- "))
                {
                    var breweryName = line[2..].Trim();
                    current = await FindOrCreateBeerDbProducerAsync(breweryName, ct);
                    continue;
                }
                if (current is null || line.Length > 256) { skipped++; continue; }

                var drinkName = line.Trim();
                var sourceRef = Truncate(
                    $"{current.SourceRef ?? current.NormalizedName}:{NameNormalizer.Normalize(drinkName)}", 128);
                var existing = await db.Drinks.FirstOrDefaultAsync(
                    d => d.Source == BeerDbSource && d.SourceRef == sourceRef, ct);
                if (existing is not null) { unchanged++; continue; }

                db.Drinks.Add(new Drink
                {
                    ProducerId = current.Id,
                    Name = drinkName,
                    NormalizedName = NameNormalizer.Normalize(drinkName),
                    Category = DrinkCategory.Beer,
                    Source = BeerDbSource,
                    SourceRef = sourceRef,
                });
                await db.SaveChangesAsync(ct);
                created++;
            }
        }

        await merges.GenerateProducerCandidatesAsync(ct);
        await merges.GenerateDrinkCandidatesAsync(ct);
        return Log(new ImportResult("beerdb", created, updated, unchanged, skipped));
    }

    /// <summary>TTB COLA sample batch (US-gov public domain). Full pipeline is deferred background work.</summary>
    public async Task<ImportResult> ImportTtbSampleAsync(string csvPath, CancellationToken ct = default)
    {
        int created = 0, updated = 0, unchanged = 0, skipped = 0;

        using var reader = new StreamReader(csvPath);
        var header = CsvLine(await reader.ReadLineAsync(ct)
            ?? throw new InvalidOperationException("Empty CSV."));
        int Col(string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        int ttbCol = Col("ttb_id"), brandCol = Col("brand_name"), fancifulCol = Col("fanciful_name"),
            applicantCol = Col("applicant_name"), classCol = Col("class_type");
        if (ttbCol < 0 || brandCol < 0 || applicantCol < 0 || classCol < 0)
            throw new InvalidOperationException("CSV lacks ttb_id/brand_name/applicant_name/class_type columns.");

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = CsvLine(line);
            string Cell(int i) => i >= 0 && i < cells.Length ? cells[i].Trim() : "";

            var category = CategoryFromTtbClass(Cell(classCol));
            var ttbId = Cell(ttbCol);
            var brand = Cell(brandCol);
            var applicant = Cell(applicantCol);
            if (category is null || ttbId.Length == 0 || brand.Length == 0 || applicant.Length == 0)
            {
                skipped++;
                continue;
            }

            var producer = await UpsertProducerAsync(TtbSource,
                Truncate($"applicant:{NameNormalizer.Normalize(applicant)}", 128),
                Truncate(applicant, 256), ProducerTypeFor(category), city: "", region: "",
                country: "US", ct,
                counters: (c, u, n) => { created += c; updated += u; unchanged += n; });

            var fanciful = Cell(fancifulCol);
            var drinkName = Truncate(fanciful.Length > 0 ? $"{brand} {fanciful}" : brand, 256);
            var existing = await db.Drinks.FirstOrDefaultAsync(
                d => d.Source == TtbSource && d.SourceRef == ttbId, ct);
            if (existing is not null) { unchanged++; continue; }

            db.Drinks.Add(new Drink
            {
                ProducerId = producer.Id,
                Name = drinkName,
                NormalizedName = NameNormalizer.Normalize(drinkName),
                Category = category,
                Source = TtbSource,
                SourceRef = Truncate(ttbId, 128),
            });
            await db.SaveChangesAsync(ct);
            created++;
        }

        await merges.GenerateProducerCandidatesAsync(ct);
        await merges.GenerateDrinkCandidatesAsync(ct);
        return Log(new ImportResult("ttb-sample", created, updated, unchanged, skipped));
    }

    // ------------------------------------------------------------- demo/test:
    /// <summary>
    /// Seeds NEAR-DUPLICATE fixtures (acceptance: merge queue demo) and runs
    /// candidate generation. Names are deliberately misspelled variants.
    /// </summary>
    public async Task<ImportResult> ImportDemoDupesAsync(CancellationToken ct = default)
    {
        var fixtures = new (string Ref, string Name, string City)[]
        {
            ("dupe-tg", "Topping Goliath Brewing Company", "Decorah"),
            ("dupe-bg", "Big Grove Brewing", "Solon"),
            ("dupe-ex", "Exile Brewing Company", "Des Moines"),
        };

        int created = 0, updated = 0, unchanged = 0;
        foreach (var (fixtureRef, name, city) in fixtures)
        {
            await UpsertProducerAsync(DemoSource, fixtureRef, name, "brewery", city, "IA", "US", ct,
                counters: (c, u, n) => { created += c; updated += u; unchanged += n; });
        }

        var producerCandidates = await merges.GenerateProducerCandidatesAsync(ct);
        var drinkCandidates = await merges.GenerateDrinkCandidatesAsync(ct);
        logger.LogInformation("demo-dupes: {P} producer + {D} drink candidates generated.",
            producerCandidates, drinkCandidates);

        return Log(new ImportResult("demo-dupes", created, updated, unchanged, 0));
    }

    // ---------------------------------------------------------------- report:
    /// <summary>Seed verification report (Sprint 1 acceptance; CI artifact).</summary>
    public async Task<string> BuildReportAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Seed verification report");
        sb.AppendLine();
        sb.AppendLine($"Generated {DateTimeOffset.UtcNow:u}.");

        sb.AppendLine();
        sb.AppendLine("## Producers by source");
        sb.AppendLine();
        sb.AppendLine("| Source | Active | Merged |");
        sb.AppendLine("|---|---:|---:|");
        var producers = await db.Producers.GroupBy(p => new { p.Source, p.Status })
            .Select(g => new { g.Key.Source, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        foreach (var group in producers.GroupBy(p => p.Source).OrderBy(g => g.Key))
        {
            sb.AppendLine($"| {group.Key} " +
                $"| {group.Where(x => x.Status == EntityStatus.Active).Sum(x => x.Count)} " +
                $"| {group.Where(x => x.Status == EntityStatus.Merged).Sum(x => x.Count)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Drinks by category and source");
        sb.AppendLine();
        sb.AppendLine("| Category | Source | Active | Merged | With vector | Coverage |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|");
        var drinks = await db.Drinks
            .GroupBy(d => new { d.Category, d.Source, d.Status })
            .Select(g => new
            {
                g.Key.Category,
                g.Key.Source,
                g.Key.Status,
                Count = g.Count(),
                WithVector = g.Count(d => d.CategoryVector != null),
            })
            .ToListAsync(ct);
        foreach (var group in drinks.GroupBy(d => new { d.Category, d.Source })
                     .OrderBy(g => g.Key.Category).ThenBy(g => g.Key.Source))
        {
            var active = group.Where(x => x.Status == EntityStatus.Active).Sum(x => x.Count);
            var merged = group.Where(x => x.Status == EntityStatus.Merged).Sum(x => x.Count);
            var withVector = group.Where(x => x.Status == EntityStatus.Active).Sum(x => x.WithVector);
            var coverage = active == 0 ? 0 : 100.0 * withVector / active;
            sb.AppendLine($"| {group.Key.Category} | {group.Key.Source} | {active} | {merged} | {withVector} | {coverage:0.#}% |");
        }

        sb.AppendLine();
        sb.AppendLine("## Styles");
        sb.AppendLine();
        sb.AppendLine("| Category | Styles | With baseline vector |");
        sb.AppendLine("|---|---:|---:|");
        var styles = await db.Styles.GroupBy(s => s.Category)
            .Select(g => new
            {
                Category = g.Key,
                Count = g.Count(),
                WithVector = g.Count(s => s.CategoryVector != null),
            })
            .ToListAsync(ct);
        foreach (var row in styles.OrderBy(s => s.Category))
            sb.AppendLine($"| {row.Category} | {row.Count} | {row.WithVector} |");

        sb.AppendLine();
        sb.AppendLine("## Attribute provenance (drink attribute rows)");
        sb.AppendLine();
        sb.AppendLine("| Source | Rows | Avg confidence |");
        sb.AppendLine("|---|---:|---:|");
        var provenance = await db.DrinkAttributes.GroupBy(a => a.Source)
            .Select(g => new { Source = g.Key, Count = g.Count(), Avg = g.Average(a => a.Confidence) })
            .ToListAsync(ct);
        foreach (var row in provenance.OrderBy(p => p.Source))
            sb.AppendLine($"| {row.Source} | {row.Count} | {row.Avg:0.00} |");

        sb.AppendLine();
        sb.AppendLine("## Duplicate-rate estimate");
        sb.AppendLine();
        var pendingByType = await db.MergeQueue.Where(m => m.Status == MergeStatus.Pending)
            .GroupBy(m => m.EntityType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var activeProducers = await db.Producers.CountAsync(p => p.Status == EntityStatus.Active, ct);
        var activeDrinks = await db.Drinks.CountAsync(d => d.Status == EntityStatus.Active, ct);
        var pendingProducers = pendingByType.FirstOrDefault(p => p.Type == MergeEntityType.Producer)?.Count ?? 0;
        var pendingDrinks = pendingByType.FirstOrDefault(p => p.Type == MergeEntityType.Drink)?.Count ?? 0;
        sb.AppendLine($"- Producers: {pendingProducers} pending candidates / {activeProducers} active "
            + $"({Rate(pendingProducers, activeProducers):0.##}%)");
        sb.AppendLine($"- Drinks: {pendingDrinks} pending candidates / {activeDrinks} active "
            + $"({Rate(pendingDrinks, activeDrinks):0.##}%)");
        sb.AppendLine($"- Decided: {await db.MergeQueue.CountAsync(m => m.Status == MergeStatus.Approved, ct)} approved, "
            + $"{await db.MergeQueue.CountAsync(m => m.Status == MergeStatus.Rejected, ct)} rejected");

        return sb.ToString();

        static double Rate(int part, int whole) => whole == 0 ? 0 : 100.0 * part / whole;
    }

    // --------------------------------------------------------------- helpers:
    private async Task<Producer> UpsertProducerAsync(
        string source, string sourceRef, string name, string? producerType,
        string? city, string? region, string? country, CancellationToken ct,
        Action<int, int, int> counters)
    {
        var existing = await db.Producers.FirstOrDefaultAsync(
            p => p.Source == source && p.SourceRef == sourceRef, ct);
        city = NullIfEmpty(city);
        region = NullIfEmpty(region);
        country = NullIfEmpty(country);

        if (existing is null)
        {
            var producer = new Producer
            {
                Name = name,
                NormalizedName = NameNormalizer.Normalize(name),
                ProducerType = producerType,
                City = city,
                Region = region,
                Country = country,
                Source = source,
                SourceRef = sourceRef,
            };
            db.Producers.Add(producer);
            await db.SaveChangesAsync(ct);
            counters(1, 0, 0);
            return producer;
        }

        if (existing.Name != name || existing.City != city
            || existing.Region != region || existing.Country != country)
        {
            existing.Name = name;
            existing.NormalizedName = NameNormalizer.Normalize(name);
            existing.City = city;
            existing.Region = region;
            existing.Country = country;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            counters(0, 1, 0);
        }
        else
        {
            counters(0, 0, 1);
        }

        return existing;
    }

    private async Task<Producer> FindOrCreateBeerDbProducerAsync(string name, CancellationToken ct)
    {
        var normalized = NameNormalizer.Normalize(name);
        var existing = await db.Producers.FirstOrDefaultAsync(
            p => p.NormalizedName == normalized && p.Status == EntityStatus.Active, ct);
        if (existing is not null)
            return existing;

        var producer = new Producer
        {
            Name = Truncate(name, 256),
            NormalizedName = normalized,
            ProducerType = "brewery",
            Country = "US",
            Source = BeerDbSource,
            SourceRef = Truncate($"brewery:{normalized}", 128),
        };
        db.Producers.Add(producer);
        await db.SaveChangesAsync(ct);
        return producer;
    }

    private ImportResult Log(ImportResult result)
    {
        logger.LogInformation("Import {Result}", result);
        return result;
    }

    private static T ReadJson<T>(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse {path}.");
    }

    /// <summary>Minimal quote-aware CSV splitter (OBDB/TTB exports are RFC-4180-ish).</summary>
    internal static string[] CsvLine(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else sb.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        cells.Add(sb.ToString());
        return [.. cells];
    }

    private static string StripBeerDbComment(string line)
    {
        var text = line;
        var slashes = text.IndexOf("//", StringComparison.Ordinal);
        if (slashes >= 0) text = text[..slashes];
        if (text.TrimStart().StartsWith('#')) return "";
        return text.Trim();
    }

    private static string? RegionFromBeerDbPath(string file, string root)
    {
        // Directory names look like "5--ia-iowa--great-plains" → "IA".
        var relative = Path.GetRelativePath(root, file);
        var dir = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        var parts = dir.Split("--");
        if (parts.Length >= 2 && parts[1].Length >= 2)
            return parts[1][..2].ToUpperInvariant();
        return null;
    }

    internal static string? CategoryFromTtbClass(string classType)
    {
        var upper = classType.ToUpperInvariant();
        if (upper.Contains("WHISK")) return DrinkCategory.Whiskey;
        if (upper.Contains("WINE")) return DrinkCategory.Wine;
        if (upper.Contains("BEER") || upper.Contains("ALE") || upper.Contains("MALT")
            || upper.Contains("LAGER") || upper.Contains("STOUT") || upper.Contains("PORTER"))
            return DrinkCategory.Beer;
        return null;
    }

    private static string ProducerTypeFor(string category) => category switch
    {
        DrinkCategory.Beer => "brewery",
        DrinkCategory.Whiskey => "distillery",
        DrinkCategory.Wine => "winery",
        _ => "other",
    };

    private static (decimal?, decimal?) Range(decimal[]? range)
        => range is { Length: 2 } ? (range[0], range[1]) : (null, null);
    private static (short?, short?) ShortRange(short[]? range)
        => range is { Length: 2 } ? (range[0], range[1]) : (null, null);
    private static bool RangeEqual(decimal? min, decimal? max, decimal[]? range)
        => (min, max) == Range(range);
    private static bool ShortRangeEqual(short? min, short? max, short[]? range)
        => (min, max) == ShortRange(range);

    private bool BaselinesEqual(Style style, string category, Dictionary<string, float>? seed)
    {
        var current = style.Attributes.ToDictionary(a => a.AttributeKey, a => a.Value);
        var wanted = (seed ?? []).ToDictionary(kv => $"{category}.{kv.Key}", kv => kv.Value);
        return current.Count == wanted.Count
            && current.All(kv => wanted.TryGetValue(kv.Key, out var v) && Math.Abs(v - kv.Value) < 0.0001f);
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
