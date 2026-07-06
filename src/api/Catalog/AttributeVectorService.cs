using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace BarBrain.Api.Catalog;

/// <summary>
/// Keeps the derived pgvector columns in sync with the relational attribute
/// tables (ADR-025: relational rows are the auditable source of truth; the
/// vector columns exist for HNSW cosine similarity).
///
/// Drink sync also MATERIALIZES style-baseline inheritance: any of the 8 dims
/// a drink lacks is copied from its style's baseline as a
/// <c>source='inherited'</c> row with low confidence, so provenance stays
/// inspectable per dimension. Vectors are only written when all 8 dims exist;
/// partial data leaves the vector NULL (visible as a coverage gap in the seed
/// report rather than a silently wrong similarity).
/// </summary>
public sealed class AttributeVectorService(AppDbContext db, ISettingsService settings)
{
    /// <summary>Config flag (Hard Rule 10): confidence given to inherited values, in percent.</summary>
    public const string InheritedConfidenceFlag = "catalog.inherited_confidence_pct";

    public async Task<int> RecomputeStyleVectorsAsync(
        IReadOnlyCollection<Guid>? styleIds = null, CancellationToken ct = default)
    {
        var defs = await LoadDefinitionsAsync(ct);

        var query = db.Styles.Include(s => s.Attributes).AsQueryable();
        if (styleIds is { Count: > 0 })
            query = query.Where(s => styleIds.Contains(s.Id));

        var updated = 0;
        foreach (var style in await query.ToListAsync(ct))
        {
            var values = style.Attributes.ToDictionary(a => a.AttributeKey, a => a.Value);
            var (category, bridge) = BuildVectors(defs[style.Category], values);
            style.CategoryVector = category;
            style.BridgeVector = bridge;
            style.UpdatedAt = DateTimeOffset.UtcNow;
            updated++;
        }

        await db.SaveChangesAsync(ct);
        return updated;
    }

    public async Task<int> RecomputeDrinkVectorsAsync(
        IReadOnlyCollection<Guid>? drinkIds = null, CancellationToken ct = default)
    {
        var defs = await LoadDefinitionsAsync(ct);
        var inheritedConfidence =
            await settings.GetIntAsync(InheritedConfidenceFlag, 30, ct) / 100f;

        var query = db.Drinks
            .Include(d => d.Attributes)
            .Include(d => d.Style!).ThenInclude(s => s.Attributes)
            .AsQueryable();
        if (drinkIds is { Count: > 0 })
            query = query.Where(d => drinkIds.Contains(d.Id));

        var updated = 0;
        // Chunked so a bulk import (thousands of drinks) doesn't balloon the
        // change tracker; each page is loaded, synced, saved, and released.
        const int pageSize = 500;
        for (var page = 0; ; page++)
        {
            var drinks = await query
                .OrderBy(d => d.Id)
                .Skip(page * pageSize).Take(pageSize)
                .ToListAsync(ct);
            if (drinks.Count == 0)
                break;

            foreach (var drink in drinks)
            {
                var own = drink.Attributes.ToDictionary(a => a.AttributeKey, a => a.Value);

                // Materialize inheritance for missing dims (ADR-009).
                if (drink.Style is not null)
                {
                    foreach (var baseline in drink.Style.Attributes)
                    {
                        if (own.ContainsKey(baseline.AttributeKey))
                            continue;
                        drink.Attributes.Add(new DrinkAttributeValue
                        {
                            DrinkId = drink.Id,
                            AttributeKey = baseline.AttributeKey,
                            Value = baseline.Value,
                            Source = AttributeValueSource.Inherited,
                            Confidence = inheritedConfidence,
                        });
                        own[baseline.AttributeKey] = baseline.Value;
                    }
                }

                var (category, bridge) = BuildVectors(defs[drink.Category], own);
                drink.CategoryVector = category;
                drink.BridgeVector = bridge;
                drink.UpdatedAt = DateTimeOffset.UtcNow;
                updated++;
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            if (drinks.Count < pageSize)
                break;
        }

        return updated;
    }

    private async Task<Dictionary<string, List<AttributeDefinition>>> LoadDefinitionsAsync(CancellationToken ct)
    {
        var all = await db.AttributeDefinitions.AsNoTracking().ToListAsync(ct);
        return all.GroupBy(a => a.Category)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.DimIndex).ToList());
    }

    /// <summary>
    /// Builds (category, bridge) vectors from attribute values, or (null, null)
    /// when any of the 8 dims is missing — a partial vector would silently
    /// distort cosine similarity.
    /// </summary>
    private static (Vector? Category, Vector? Bridge) BuildVectors(
        List<AttributeDefinition> defs, Dictionary<string, float> values)
    {
        if (defs.Count != VectorDims.Category)
            throw new InvalidOperationException(
                $"Attribute vocabulary is misconfigured: expected {VectorDims.Category} dims, found {defs.Count}.");

        var category = new float[VectorDims.Category];
        var bridge = new float[VectorDims.Bridge];
        foreach (var def in defs)
        {
            if (!values.TryGetValue(def.Key, out var value))
                return (null, null);
            category[def.DimIndex] = value;
            if (def.BridgeIndex is { } bi)
                bridge[bi] = value;
        }

        return (new Vector(category), new Vector(bridge));
    }
}
