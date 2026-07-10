using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Ratings;

/// <summary>
/// The core rating loop (ADR-012): append-only history, DB-enforced single
/// latest per (user, drink), pseudonymous-public by default with a per-rating
/// private toggle. Every read here is filtered by the ADR-026 ownership/
/// visibility columns — this class IS the sprint's authz surface for ratings,
/// and the integration tests attack it as user A vs user B.
/// </summary>
public sealed class RatingService(
    AppDbContext db,
    TimeProvider clock,
    Palate.PalateProfileService palateProfiles,
    Venues.CheckinService checkins)
{
    public sealed record Failure(int Status, ApiError Error);
    private const int MergeHopLimit = 10;

    public static bool IsValidValue(decimal value)
        => value is >= 1.0m and <= 5.0m && (value * 2) % 1 == 0;

    public async Task<(RatingDto? Rating, Failure? Failure)> CreateAsync(
        Guid userId, RateRequest request, CancellationToken ct = default)
    {
        if (!IsValidValue(request.Value))
            return Fail(400, "invalid_value", "Ratings are 1.0 to 5.0 in half-star steps.");

        var visibility = request.Visibility ?? Visibility.Public;
        if (visibility is not (Visibility.Public or Visibility.Private))
            return Fail(400, "invalid_visibility", "Visibility is 'public' or 'private'.");

        if (!LocationContext.IsValid(request.LocationContext))
            return Fail(400, "invalid_location", "Location is 'home_bar', 'venue', or 'untagged'.");

        if (request.Note is { Length: > 500 })
            return Fail(400, "note_too_long", "Notes max out at 500 characters.");

        var origin = request.Origin ?? RatingOrigin.User;
        if (origin is not (RatingOrigin.User or RatingOrigin.Quiz))
            return Fail(400, "invalid_origin", "Origin is 'user' or 'quiz'.");

        // Resolve the drink, following merge redirects to the canonical row.
        var drink = await db.Drinks.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DrinkId, ct);
        for (var hops = 0; drink is { Status: EntityStatus.Merged, MergedIntoDrinkId: { } targetId } && hops < MergeHopLimit; hops++)
            drink = await db.Drinks.AsNoTracking().FirstOrDefaultAsync(d => d.Id == targetId, ct);
        if (drink is null || drink.Status != EntityStatus.Active)
            return Fail(404, "drink_not_found", "That drink isn't in the catalog.");
        // A private (user-submitted) drink is only ratable by its owner.
        if (drink.Visibility == Visibility.Private && drink.CreatedByUserId != userId)
            return Fail(404, "drink_not_found", "That drink isn't in the catalog.");

        // Auto-tag (ADR-015 / Sprint 5): an untagged rating made during an
        // active check-in belongs to that venue. Explicit contexts (home_bar,
        // venue) always win — the user said where they are.
        var locationContext = request.LocationContext;
        var requestedVenueId = request.VenueId;
        if (locationContext == LocationContext.Untagged
            && await checkins.ActiveVenueIdAsync(userId, ct) is { } checkedInVenueId)
        {
            locationContext = LocationContext.Venue;
            requestedVenueId = checkedInVenueId;
        }

        var venueId = await ResolveVenueAsync(userId, locationContext, requestedVenueId, ct);
        if (venueId.Failure is not null)
            return (null, venueId.Failure);

        var now = clock.GetUtcNow();
        var rating = new Rating
        {
            CreatedByUserId = userId,
            DrinkId = drink.Id,
            Value = request.Value,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            Visibility = visibility,
            LocationContext = locationContext,
            VenueId = venueId.VenueId,
            Origin = origin,
            IsLatest = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Append + retire the previous latest, atomically (the partial unique
        // index refuses two latest rows even if this code regresses). The
        // retry strategy requires transactions to run inside ExecuteAsync.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var firstEver = !await db.Ratings.AnyAsync(r => r.CreatedByUserId == userId, ct);

            await db.Ratings
                .Where(r => r.CreatedByUserId == userId && r.DrinkId == drink.Id && r.IsLatest)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsLatest, false), ct);

            db.Ratings.Add(rating);

            // First-party events (ADR-017): pseudonymous ids only.
            if (firstEver)
                db.Events.Add(NewEvent("first_rating", now, userId, drink));
            db.Events.Add(NewEvent("rating", now, userId, drink));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        // Profiles are derived data — refresh this (user, category) so the
        // feed reflects the rating immediately (Sprint 3 spec: on-demand
        // recompute after rating; cheap at this scale).
        await palateProfiles.RecomputeAsync(userId, drink.Category, ct);

        var dto = await OwnRatingDtoAsync(userId, rating.Id, ct);
        return (dto, null);
    }

    /// <summary>My journal: full append-only history, newest first (spec).</summary>
    public async Task<PagedResult<RatingDto>> JournalAsync(
        Guid userId, string? category, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Ratings.AsNoTracking()
            .Where(r => r.CreatedByUserId == userId);
        if (category is not null)
            query = query.Where(r => r.Drink.Category == category);

        var total = await query.CountAsync(ct);
        var items = await ProjectOwn(query
                .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                .Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(ct);

        return new PagedResult<RatingDto>(page, pageSize, total, items);
    }

    /// <summary>My history for one drink (drink-page prefill + inline history).</summary>
    public Task<List<RatingDto>> OwnForDrinkAsync(Guid userId, Guid drinkId, CancellationToken ct = default)
        => ProjectOwn(db.Ratings.AsNoTracking()
                .Where(r => r.CreatedByUserId == userId && r.DrinkId == drinkId)
                .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id))
            .ToListAsync(ct);

    /// <summary>
    /// Note/visibility edit in place; the VALUE is immutable history (a new
    /// value = a new rating). 404 for other people's rows — existence of a
    /// private rating must not leak (the authz tests assert this).
    /// </summary>
    public async Task<(RatingDto? Rating, Failure? Failure)> UpdateAsync(
        Guid userId, Guid ratingId, RatingUpdateRequest request, CancellationToken ct = default)
    {
        var rating = await db.Ratings
            .FirstOrDefaultAsync(r => r.Id == ratingId && r.CreatedByUserId == userId, ct);
        if (rating is null)
            return Fail(404, "rating_not_found", "That rating doesn't exist.");

        if (request.Visibility is not null)
        {
            if (request.Visibility is not (Visibility.Public or Visibility.Private))
                return Fail(400, "invalid_visibility", "Visibility is 'public' or 'private'.");
            rating.Visibility = request.Visibility;
        }
        if (request.Note is not null)
        {
            if (request.Note.Length > 500)
                return Fail(400, "note_too_long", "Notes max out at 500 characters.");
            rating.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        }
        rating.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return (await OwnRatingDtoAsync(userId, rating.Id, ct), null);
    }

    /// <summary>Delete own rating; if it was the latest, the previous one steps up.</summary>
    public async Task<Failure?> DeleteAsync(Guid userId, Guid ratingId, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        Failure? failure = null;
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var rating = await db.Ratings
                .FirstOrDefaultAsync(r => r.Id == ratingId && r.CreatedByUserId == userId, ct);
            if (rating is null)
            {
                failure = new Failure(404, new ApiError("rating_not_found", "That rating doesn't exist."));
                return;
            }

            db.Ratings.Remove(rating);
            await db.SaveChangesAsync(ct);

            if (rating.IsLatest)
            {
                var previous = await db.Ratings
                    .Where(r => r.CreatedByUserId == userId && r.DrinkId == rating.DrinkId)
                    .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                    .FirstOrDefaultAsync(ct);
                if (previous is not null)
                {
                    previous.IsLatest = true;
                    await db.SaveChangesAsync(ct);
                }
            }

            await tx.CommitAsync(ct);
        });

        if (failure is null)
        {
            // A deletion can change the latest row — refresh every category
            // the user has (the drink's category isn't in scope here; a full
            // per-user refresh is three cheap queries).
            await palateProfiles.RecomputeAllForUserAsync(userId, ct);
        }
        return failure;
    }

    /// <summary>
    /// The drink page's rating data, for ANYONE (no auth): latest + public
    /// rows only. Flipping a rating private removes it from here — the Gate B
    /// phone check and an e2e test both verify that.
    /// </summary>
    public async Task<DrinkRatingsResponse> PublicForDrinkAsync(
        Guid drinkId, int recentLimit, CancellationToken ct = default)
    {
        var latestPublic = db.Ratings.AsNoTracking()
            .Where(r => r.DrinkId == drinkId && r.IsLatest && r.Visibility == Visibility.Public);

        var count = await latestPublic.CountAsync(ct);
        var average = count == 0
            ? (decimal?)null
            : Math.Round(await latestPublic.AverageAsync(r => r.Value, ct), 2);

        var recent = await latestPublic
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Take(recentLimit)
            .Select(r => new PublicRatingDto(
                r.CreatedBy.UserName!, r.Value, r.Note, r.CreatedAt))
            .ToListAsync(ct);

        return new DrinkRatingsResponse(count, average, recent);
    }

    private async Task<(Guid? VenueId, Failure? Failure)> ResolveVenueAsync(
        Guid userId, string locationContext, Guid? requestedVenueId, CancellationToken ct)
    {
        switch (locationContext)
        {
            case LocationContext.Untagged:
                return (null, null);

            case LocationContext.HomeBar:
                // The caller's OWN Home Bar, always — a venue id in the request
                // can't point a rating at someone else's.
                var homeBarId = await db.Venues
                    .Where(v => v.OwnerUserId == userId && v.VenueType == VenueType.HomeBar)
                    .Select(v => (Guid?)v.Id)
                    .FirstOrDefaultAsync(ct);
                if (homeBarId is null)
                    return (null, new Failure(409, new ApiError("no_home_bar",
                        "Your Home Bar is missing — contact support.")));
                return (homeBarId, null);

            case LocationContext.Venue:
                // Real venues arrive in Sprint 5; the contract is complete now.
                if (requestedVenueId is null)
                    return (null, new Failure(400, new ApiError("venue_required",
                        "Tag a venue or rate at your Home Bar.")));
                var venue = await db.Venues.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == requestedVenueId, ct);
                if (venue is null || venue.VenueType != VenueType.Venue
                    || (venue.Visibility == Visibility.Private && venue.OwnerUserId != userId))
                    return (null, new Failure(404, new ApiError("venue_not_found", "That venue doesn't exist.")));
                return (venue.Id, null);

            default:
                return (null, new Failure(400, new ApiError("invalid_location",
                    "Location is 'home_bar', 'venue', or 'untagged'.")));
        }
    }

    private Task<RatingDto?> OwnRatingDtoAsync(Guid userId, Guid ratingId, CancellationToken ct)
        => ProjectOwn(db.Ratings.AsNoTracking()
                .Where(r => r.Id == ratingId && r.CreatedByUserId == userId))
            .FirstOrDefaultAsync(ct);

    private static IQueryable<RatingDto> ProjectOwn(IQueryable<Rating> query)
        => query.Select(r => new RatingDto(
            r.Id, r.DrinkId, r.Drink.Name, r.Drink.Category,
            r.Drink.Style != null ? r.Drink.Style.Name : null,
            r.Value, r.Note, r.Visibility, r.LocationContext,
            r.Venue != null ? r.Venue.Name : null,
            r.IsLatest, r.CreatedAt, r.Origin));

    private static (RatingDto?, Failure?) Fail(int status, string code, string message)
        => (null, new Failure(status, new ApiError(code, message)));

    private static EventRecord NewEvent(string name, DateTimeOffset at, Guid userId, Drink drink) => new()
    {
        Name = name,
        OccurredAt = at,
        Properties = new Dictionary<string, string>
        {
            ["userId"] = userId.ToString(),
            ["drinkId"] = drink.Id.ToString(),
            ["category"] = drink.Category,
        },
    };
}
