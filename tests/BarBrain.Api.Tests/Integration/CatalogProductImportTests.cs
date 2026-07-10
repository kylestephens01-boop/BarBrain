using BarBrain.Api.Catalog.Import;
using BarBrain.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Generic product-seed importer acceptance (sprint 4.6 / ADR-028): per-file
/// provenance tags, moderator-sourced editorial attribute overrides with
/// inheritance fallback, idempotent re-runs, loud rejection of malformed
/// overrides, the fail-closed ADR-024 license gate, and (sprint 4.7) the
/// --clear-attribute corrective verb. The corridor path is
/// the same code (ImportCorridorAsync delegates here) and stays covered by
/// CatalogImportTests unchanged.
/// </summary>
[Collection("postgres")]
public sealed class CatalogProductImportTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string TestSource = "seed:test-national";

    private string _connectionString = "";
    private string _dir = "";

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"barbrain-seed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        if (!fixture.DockerAvailable)
            return;
        _connectionString = await fixture.CreateEmptyDatabaseAsync($"catalog_products_{Guid.NewGuid():N}");
        using var harness = new CatalogTestHarness(_connectionString);
        await harness.Db.Database.MigrateAsync();
        await harness.Import.ImportAttributesAsync(CatalogTestHarness.SeedDir);
        await harness.Import.ImportStylesAsync(CatalogTestHarness.SeedDir);
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_dir, recursive: true);
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------- fixtures:
    /// <summary>Registry fixture standing in for docs/DATA-SOURCES.md.</summary>
    private string WriteRegistry(params string[] documentedTags)
    {
        var path = Path.Combine(_dir, "data-sources.md");
        File.WriteAllText(path, "# Test registry\n"
            + string.Join("\n", documentedTags.Select(t => $"- `source = \"{t}\"` (first-party test fixture)")));
        return path;
    }

    private string WriteSeed(string json)
    {
        var path = Path.Combine(_dir, "products.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>Two whiskeys on one style: one with editorial overrides, one pure-inheritance.</summary>
    private static string SeedJson(
        string source = TestSource, string? attributeConfidence = null, string smoke = "0.85") => $$"""
        {
          "source": "{{source}}",
          {{(attributeConfidence is null ? "" : $"\"attributeConfidence\": {attributeConfidence},")}}
          "producers": [
            { "ref": "test-distillery", "name": "Test Ridge Distillery", "type": "distillery",
              "city": "Testville", "region": "KY", "country": "US", "drinks": [
              { "ref": "td-flagship", "name": "Test Ridge Flagship Bourbon", "category": "whiskey",
                "style": "WH-AM-BRB", "abv": 45.0,
                "attributes": { "smoke": {{smoke}}, "sweetness": 0.9 } },
              { "ref": "td-plain", "name": "Test Ridge Wheated", "category": "whiskey",
                "style": "WH-AM-BRB", "abv": 43.0 }
            ] }
          ]
        }
        """;

    // ---------------------------------------------------------------- tests:
    [SkippableFact]
    public async Task Products_seed_gets_per_file_provenance_moderator_overrides_and_inheritance()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);
        var registry = WriteRegistry(TestSource);
        var seed = WriteSeed(SeedJson(attributeConfidence: "0.85"));

        var result = await harness.Import.ImportProductsAsync(seed, registry);
        Assert.Equal(3, result.Created); // 1 producer + 2 drinks
        Assert.Equal(0, result.Skipped);

        // Provenance is the FILE's tag, not seed:corridor.
        var producer = await harness.Db.Producers.SingleAsync(p => p.SourceRef == "test-distillery");
        Assert.Equal(TestSource, producer.Source);
        var drinks = await harness.Db.Drinks.Include(d => d.Attributes)
            .Where(d => d.Source == TestSource).ToListAsync();
        Assert.Equal(2, drinks.Count);
        Assert.False(await harness.Db.Drinks.AnyAsync(d => d.Source == "seed:corridor"));

        // Overridden dims are moderator rows at the file's confidence; the
        // rest inherit — and the vector carries the override on both scales
        // (whiskey.smoke is dim 3 and bridge 3).
        var flagship = drinks.Single(d => d.SourceRef == "td-flagship");
        Assert.Equal(8, flagship.Attributes.Count);
        var smoke = flagship.Attributes.Single(a => a.AttributeKey == "whiskey.smoke");
        Assert.Equal(AttributeValueSource.Moderator, smoke.Source);
        Assert.Equal(0.85, smoke.Value, 3);
        Assert.Equal(0.85, smoke.Confidence, 3);
        var sweetness = flagship.Attributes.Single(a => a.AttributeKey == "whiskey.sweetness");
        Assert.Equal(AttributeValueSource.Moderator, sweetness.Source);
        Assert.Equal(0.9, sweetness.Value, 3);
        Assert.Equal(6, flagship.Attributes.Count(a => a.Source == AttributeValueSource.Inherited));
        Assert.NotNull(flagship.CategoryVector);
        Assert.NotNull(flagship.BridgeVector);
        Assert.Equal(0.85, flagship.CategoryVector!.ToArray()[3], 3);
        Assert.Equal(0.85, flagship.BridgeVector!.ToArray()[3], 3);

        // No override block → pure style inheritance, exactly as before.
        var plain = drinks.Single(d => d.SourceRef == "td-plain");
        Assert.Equal(8, plain.Attributes.Count);
        Assert.All(plain.Attributes, a => Assert.Equal(AttributeValueSource.Inherited, a.Source));
        Assert.NotNull(plain.CategoryVector);
    }

    [SkippableFact]
    public async Task Products_reimport_is_idempotent_and_override_edits_apply_without_dupes()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var registry = WriteRegistry(TestSource);
        // No attributeConfidence in the file → the flag default (80%) decides.
        var seed = WriteSeed(SeedJson());

        using (var harness = new CatalogTestHarness(_connectionString))
        {
            await harness.Import.ImportProductsAsync(seed, registry);
            var smoke = await harness.Db.DrinkAttributes
                .SingleAsync(a => a.Drink.SourceRef == "td-flagship" && a.AttributeKey == "whiskey.smoke");
            Assert.Equal(0.80, smoke.Confidence, 3);
        }

        int drinks, producers, attributeRows;
        using (var rerun = new CatalogTestHarness(_connectionString))
        {
            drinks = await rerun.Db.Drinks.CountAsync();
            producers = await rerun.Db.Producers.CountAsync();
            attributeRows = await rerun.Db.DrinkAttributes.CountAsync();

            var second = await rerun.Import.ImportProductsAsync(seed, registry);
            Assert.Equal(0, second.Created);
            Assert.Equal(0, second.Updated);
            Assert.Equal(3, second.Unchanged);
            Assert.Equal(drinks, await rerun.Db.Drinks.CountAsync());
            Assert.Equal(producers, await rerun.Db.Producers.CountAsync());
            Assert.Equal(attributeRows, await rerun.Db.DrinkAttributes.CountAsync());
        }

        // Editing an override value updates the row in place and recomputes
        // the vector — still no new rows.
        var edited = WriteSeed(SeedJson(smoke: "0.4"));
        using (var harness = new CatalogTestHarness(_connectionString))
        {
            var third = await harness.Import.ImportProductsAsync(edited, registry);
            Assert.Equal(1, third.Updated);
            Assert.Equal(2, third.Unchanged);
            var flagship = await harness.Db.Drinks.Include(d => d.Attributes)
                .SingleAsync(d => d.SourceRef == "td-flagship");
            var smoke = flagship.Attributes.Single(a => a.AttributeKey == "whiskey.smoke");
            Assert.Equal(AttributeValueSource.Moderator, smoke.Source);
            Assert.Equal(0.4, smoke.Value, 3);
            Assert.Equal(8, flagship.Attributes.Count);
            Assert.Equal(0.4, flagship.CategoryVector!.ToArray()[3], 3);
            Assert.Equal(attributeRows, await harness.Db.DrinkAttributes.CountAsync());
        }
    }

    [SkippableFact]
    public async Task Clear_attribute_removes_the_moderator_override_and_reverts_to_baseline()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var registry = WriteRegistry(TestSource);
        var seed = WriteSeed(SeedJson()); // flagship overrides smoke 0.85 + sweetness 0.9

        using (var harness = new CatalogTestHarness(_connectionString))
            await harness.Import.ImportProductsAsync(seed, registry);

        using (var harness = new CatalogTestHarness(_connectionString))
        {
            var result = await harness.Import.ClearAttributeOverrideAsync(
                TestSource, "td-flagship", "smoke");
            Assert.Equal(ClearOverrideResult.Cleared, result);

            var flagship = await harness.Db.Drinks.Include(d => d.Attributes)
                .SingleAsync(d => d.SourceRef == "td-flagship");
            var plain = await harness.Db.Drinks.Include(d => d.Attributes)
                .SingleAsync(d => d.SourceRef == "td-plain");

            // The dim fell back to the materialized style baseline — same
            // value/source/confidence as the never-overridden drink's row.
            var smoke = flagship.Attributes.Single(a => a.AttributeKey == "whiskey.smoke");
            var plainSmoke = plain.Attributes.Single(a => a.AttributeKey == "whiskey.smoke");
            Assert.Equal(AttributeValueSource.Inherited, smoke.Source);
            Assert.Equal(plainSmoke.Value, smoke.Value, 3);
            Assert.Equal(plainSmoke.Confidence, smoke.Confidence, 3);

            // Vectors resynced to the baseline on BOTH scales (whiskey.smoke
            // is dim 3 and bridge 3), with full 8-dim coverage intact.
            Assert.Equal(8, flagship.Attributes.Count);
            Assert.Equal(plainSmoke.Value, flagship.CategoryVector!.ToArray()[3], 3);
            Assert.Equal(plainSmoke.Value, flagship.BridgeVector!.ToArray()[3], 3);

            // The drink's OTHER override is untouched.
            var sweetness = flagship.Attributes.Single(a => a.AttributeKey == "whiskey.sweetness");
            Assert.Equal(AttributeValueSource.Moderator, sweetness.Source);
            Assert.Equal(0.9, sweetness.Value, 3);
        }

        // Idempotent: a re-run (the dim now holds the materialized inherited
        // row) and a never-overridden key are both no-ops, not errors.
        using (var harness = new CatalogTestHarness(_connectionString))
        {
            var rows = await harness.Db.DrinkAttributes.CountAsync();
            Assert.Equal(ClearOverrideResult.AlreadyBaseline,
                await harness.Import.ClearAttributeOverrideAsync(TestSource, "td-flagship", "smoke"));
            Assert.Equal(ClearOverrideResult.AlreadyBaseline,
                await harness.Import.ClearAttributeOverrideAsync(TestSource, "td-plain", "oak"));
            Assert.Equal(rows, await harness.Db.DrinkAttributes.CountAsync());
        }
    }

    [SkippableFact]
    public async Task Clear_attribute_refuses_non_moderator_rows_and_typoed_targets()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);
        var registry = WriteRegistry(TestSource);
        await harness.Import.ImportProductsAsync(WriteSeed(SeedJson()), registry);

        // Re-provenance the smoke row as crowd data — clear must refuse it
        // and leave it intact (this verb removes editorial overrides ONLY).
        var smoke = await harness.Db.DrinkAttributes
            .SingleAsync(a => a.Drink.SourceRef == "td-flagship" && a.AttributeKey == "whiskey.smoke");
        smoke.Source = AttributeValueSource.Crowd;
        await harness.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Import.ClearAttributeOverrideAsync(TestSource, "td-flagship", "smoke"));
        Assert.Contains("'crowd'", ex.Message);
        var survivor = await harness.Db.DrinkAttributes
            .SingleAsync(a => a.Drink.SourceRef == "td-flagship" && a.AttributeKey == "whiskey.smoke");
        Assert.Equal(AttributeValueSource.Crowd, survivor.Source);
        Assert.Equal(0.85, survivor.Value, 3);

        // Typos fail loudly instead of reading as "cleared".
        var refEx = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Import.ClearAttributeOverrideAsync(TestSource, "td-flagshipp", "smoke"));
        Assert.Contains("No drink", refEx.Message);
        var keyEx = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Import.ClearAttributeOverrideAsync(TestSource, "td-flagship", "smokee"));
        Assert.Contains("Unknown attribute", keyEx.Message);
    }

    [SkippableFact]
    public async Task Undocumented_source_is_refused_by_the_embedded_registry_and_writes_nothing()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);
        var seed = WriteSeed(SeedJson(source: "seed:never-registered"));

        // dataSourcesPath omitted → the gate reads the real embedded
        // docs/DATA-SOURCES.md, which must not know this tag.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Import.ImportProductsAsync(seed));
        Assert.Contains("DATA-SOURCES", ex.Message);
        Assert.False(await harness.Db.Producers.AnyAsync(p => p.Source == "seed:never-registered"));
        Assert.False(await harness.Db.Drinks.AnyAsync(d => d.Source == "seed:never-registered"));

        // A SUBSTRING of a registered tag must not ride through the gate
        // (seed:beer would be a substring hit on seed:beerdb).
        var substring = WriteSeed(SeedJson(source: "seed:beer"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Import.ImportProductsAsync(substring));
    }

    [SkippableFact]
    public async Task Malformed_overrides_fail_the_import_loudly()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);
        var registry = WriteRegistry(TestSource);

        // A typo'd attribute key must stop the run, not silently skip.
        var badKey = WriteSeed(SeedJson().Replace("\"smoke\"", "\"smokee\""));
        var keyEx = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Import.ImportProductsAsync(badKey, registry));
        Assert.Contains("unknown attribute", keyEx.Message);

        // A 0–10-scale slip (7.5 instead of 0.75) likewise.
        var badValue = WriteSeed(SeedJson(smoke: "7.5"));
        var valueEx = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Import.ImportProductsAsync(badValue, registry));
        Assert.Contains("outside 0–1", valueEx.Message);
    }

    [Fact]
    public void Embedded_registry_ships_in_the_binary_with_the_bundled_source_tags()
    {
        // The fail-closed gate depends on this resource existing; if the
        // csproj wiring breaks, EVERY products import (corridor included)
        // would refuse — catch that without Docker.
        using var stream = typeof(CatalogImportService).Assembly
            .GetManifestResourceStream(CatalogImportService.DataSourcesResourceName);
        Assert.NotNull(stream);
        var text = new StreamReader(stream!).ReadToEnd();
        // Quoted form — the exact string the runtime gate matches on.
        Assert.Contains($"\"{CatalogImportService.CorridorSource}\"", text);
        Assert.Contains($"\"{CatalogImportService.StylesSource}\"", text);
        Assert.Contains($"\"{CatalogImportService.OpenBreweryDbSource}\"", text);
        Assert.Contains($"\"{CatalogImportService.BeerDbSource}\"", text);
        Assert.Contains($"\"{CatalogImportService.TtbSource}\"", text);
        Assert.Contains($"\"{CatalogImportService.WhiskeyNationalSource}\"", text);
    }

    [SkippableFact]
    public async Task Bundled_whiskey_national_seed_imports_idempotently_with_full_vector_coverage()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);
        var path = Path.Combine(CatalogTestHarness.SeedDir, "whiskey-national.json");

        // Real bundled file against the REAL embedded registry (dataSourcesPath
        // omitted) — this run passing proves the tag is registered (ADR-024).
        var first = await harness.Import.ImportProductsAsync(path);
        Assert.True(first.Created > 0);
        Assert.Equal(0, first.Skipped);

        // Every drink styled (a typo'd WH-* code imports unstyled → vector
        // NULL → caught here) with category + bridge vectors populated.
        var drinks = await harness.Db.Drinks
            .Where(d => d.Source == CatalogImportService.WhiskeyNationalSource)
            .ToListAsync();
        Assert.NotEmpty(drinks);
        Assert.All(drinks, d => Assert.Equal(DrinkCategory.Whiskey, d.Category));
        Assert.All(drinks, d => Assert.NotNull(d.StyleId));
        Assert.All(drinks, d => Assert.NotNull(d.CategoryVector));
        Assert.All(drinks, d => Assert.NotNull(d.BridgeVector));

        // Overrides land as moderator rows — and SPARINGLY (spec: most drinks
        // are pure style inheritance, per corridor precedent).
        var overridden = await harness.Db.DrinkAttributes
            .Where(a => a.Source == AttributeValueSource.Moderator
                        && a.Drink.Source == CatalogImportService.WhiskeyNationalSource)
            .Select(a => a.DrinkId).Distinct().CountAsync();
        Assert.True(overridden > 0);
        Assert.True(overridden < drinks.Count / 2,
            $"{overridden}/{drinks.Count} drinks overridden — overrides must stay the exception.");

        // Idempotent re-run: all-unchanged, no row growth.
        var attributeRows = await harness.Db.DrinkAttributes.CountAsync();
        var second = await harness.Import.ImportProductsAsync(path);
        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Updated);
        Assert.Equal(0, second.Skipped);
        Assert.Equal(drinks.Count, await harness.Db.Drinks
            .CountAsync(d => d.Source == CatalogImportService.WhiskeyNationalSource));
        Assert.Equal(attributeRows, await harness.Db.DrinkAttributes.CountAsync());
    }
}
