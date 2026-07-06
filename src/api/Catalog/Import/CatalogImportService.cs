using System.Text;
using System.Text.Json;
using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Catalog.Import;

public sealed record ImportResult(string Source, int Created, int Updated, int Unchanged, int Skipped)
{
    public override string ToString()
        => $"{Source}: {Created} created, {Updated} updated, {Unchanged} unchanged, {Skipped} skipped";
}

/// <summary>
/// All catalog importers (Sprint 1 spec): idempotent (re-run = no dupes, via
/// the unique (Source, SourceRef) indexes and natural-key style matching),
/// provenance-tagged, and LICENSE-GATED — every source here has an entry in
/// docs/DATA-SOURCES.md (ADR-024). Do not add a source without one.
/// Importers never reach the network; bulk sources take a local file/dir path
/// so runs are resumable and auditable (RUNBOOK documents the downloads).
/// </summary>
public sealed class CatalogImportService(
    AppDbContext db,
    AttributeVectorService vectors,
    MergeService merges,
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

    public async Task<ImportResult> ImportCorridorAsync(string seedDir, CancellationToken ct = default)
    {
        var file = ReadJson<CorridorSeedFile>(Path.Combine(seedDir, "corridor-priority.json"));

        var styles = await db.Styles.AsNoTracking().ToListAsync(ct);
        var stylesByCategoryCode = styles.Where(s => s.Code != null)
            .ToDictionary(s => (s.Category, s.Code!), s => s.Id);
        var stylesByCategoryName = styles
            .ToDictionary(s => (s.Category, s.NormalizedName), s => s.Id);

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
                    logger.LogWarning("Corridor drink {Ref}: invalid category {Category}; skipped.",
                        drinkSeed.Ref, drinkSeed.Category);
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
                        logger.LogWarning("Corridor drink {Ref}: unknown style '{Style}'; importing unstyled.",
                            drinkSeed.Ref, styleRef);
                    }
                }

                var existing = await db.Drinks.FirstOrDefaultAsync(
                    d => d.Source == file.Source && d.SourceRef == drinkSeed.Ref, ct);
                if (existing is null)
                {
                    var drink = new Drink
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
                    touchedDrinks.Add(drink.Id);
                    created++;
                }
                else if (existing.Name != drinkSeed.Name || existing.StyleId != styleId
                         || existing.Abv != drinkSeed.Abv)
                {
                    existing.Name = drinkSeed.Name;
                    existing.NormalizedName = NameNormalizer.Normalize(drinkSeed.Name);
                    existing.StyleId = styleId;
                    existing.Abv = drinkSeed.Abv;
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                    touchedDrinks.Add(existing.Id);
                    updated++;
                }
                else
                {
                    unchanged++;
                }
            }
        }

        if (touchedDrinks.Count > 0)
            await vectors.RecomputeDrinkVectorsAsync(touchedDrinks, ct);
        await merges.GenerateProducerCandidatesAsync(ct);
        await merges.GenerateDrinkCandidatesAsync(ct);

        return Log(new ImportResult("corridor", created, updated, unchanged, skipped));
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
