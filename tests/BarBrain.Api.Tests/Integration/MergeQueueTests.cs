using BarBrain.Api.Catalog;
using BarBrain.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Entity-resolution acceptance (Sprint 1): near-duplicate fixtures produce
/// candidates; approve turns the source into a redirect (old IDs resolve);
/// reject is remembered and the pair is never proposed again.
/// </summary>
[Collection("postgres")]
public sealed class MergeQueueTests(PostgresFixture fixture) : IAsyncLifetime
{
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable)
            return;
        _connectionString = await fixture.CreateEmptyDatabaseAsync($"merge_queue_{Guid.NewGuid():N}");
        using var harness = new CatalogTestHarness(_connectionString);
        await harness.Db.Database.MigrateAsync();
        await harness.ImportBundledAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    public async Task Demo_dupes_produce_producer_candidates()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);

        await harness.Import.ImportDemoDupesAsync();

        var pending = await harness.Db.MergeQueue
            .Where(m => m.Status == MergeStatus.Pending && m.EntityType == MergeEntityType.Producer)
            .Include(m => m.SourceProducer)
            .Include(m => m.TargetProducer)
            .ToListAsync();
        Assert.NotEmpty(pending);

        // The misspelled Toppling Goliath fixture must be among the pairs.
        Assert.Contains(pending, m =>
            m.SourceProducer!.NormalizedName.Contains("goliath")
            && m.TargetProducer!.NormalizedName.Contains("goliath"));
    }

    [SkippableFact]
    public async Task Approving_a_drink_candidate_redirects_the_old_id()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);

        // Plant a near-duplicate of a corridor drink on the same producer.
        var canonical = await harness.Db.Drinks
            .Include(d => d.Producer)
            .FirstAsync(d => d.SourceRef == "tg-pseudo-sue");
        var dupe = new Drink
        {
            ProducerId = canonical.ProducerId,
            Name = "Psuedo Sue", // deliberate misspelling
            NormalizedName = NameNormalizer.Normalize("Psuedo Sue"),
            Category = canonical.Category,
            Abv = canonical.Abv,
            Source = "seed:test",
            SourceRef = "test-psuedo-sue",
        };
        harness.Db.Drinks.Add(dupe);
        await harness.Db.SaveChangesAsync();

        var generated = await harness.Merges.GenerateDrinkCandidatesAsync();
        Assert.True(generated >= 1, "expected the planted near-duplicate to be detected");

        var candidate = await harness.Db.MergeQueue.SingleAsync(m =>
            m.SourceDrinkId == dupe.Id && m.TargetDrinkId == canonical.Id
            && m.Status == MergeStatus.Pending);

        var outcome = await harness.Merges.ApproveAsync(candidate.Id, actor: "test");
        Assert.Equal(MergeDecisionOutcome.Done, outcome);

        // The old id now resolves to the canonical drink, flagged as redirected.
        using var reader = new CatalogTestHarness(_connectionString);
        var detail = await reader.Queries.GetDrinkAsync(dupe.Id);
        Assert.NotNull(detail);
        Assert.Equal(canonical.Id, detail!.Id);
        Assert.True(detail.RedirectedFromMerged);
        Assert.Equal(dupe.Id, detail.RequestedId);

        // Approving twice is rejected as already decided.
        Assert.Equal(MergeDecisionOutcome.AlreadyDecided,
            await reader.Merges.ApproveAsync(candidate.Id, actor: "test"));
    }

    [SkippableFact]
    public async Task Rejected_pairs_are_never_proposed_again()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);

        await harness.Import.ImportDemoDupesAsync();
        var candidate = await harness.Db.MergeQueue
            .FirstAsync(m => m.Status == MergeStatus.Pending);

        var outcome = await harness.Merges.RejectAsync(candidate.Id, actor: "test");
        Assert.Equal(MergeDecisionOutcome.Done, outcome);

        // Regeneration must not resurrect the decided pair.
        await harness.Merges.GenerateProducerCandidatesAsync();
        await harness.Merges.GenerateDrinkCandidatesAsync();

        var duplicates = await harness.Db.MergeQueue.CountAsync(m =>
            m.EntityType == candidate.EntityType
            && m.SourceProducerId == candidate.SourceProducerId
            && m.TargetProducerId == candidate.TargetProducerId
            && m.SourceDrinkId == candidate.SourceDrinkId
            && m.TargetDrinkId == candidate.TargetDrinkId);
        Assert.Equal(1, duplicates); // just the rejected row itself
    }
}
