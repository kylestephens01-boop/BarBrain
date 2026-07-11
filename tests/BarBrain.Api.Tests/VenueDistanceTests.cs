using BarBrain.Api.Venues;

namespace BarBrain.Api.Tests;

/// <summary>
/// The nearby list's distance math (Sprint 5). No database — pure geometry.
/// </summary>
public sealed class VenueDistanceTests
{
    [Fact]
    public void Cedar_rapids_to_iowa_city_is_about_37_km()
    {
        var km = VenueService.HaversineKm(41.9779, -91.6656, 41.6611, -91.5302);
        Assert.InRange(km, 35, 40);
    }

    [Fact]
    public void Zero_distance_at_the_same_point()
        => Assert.Equal(0, VenueService.HaversineKm(41.98, -91.67, 41.98, -91.67), precision: 6);

    [Fact]
    public void Symmetric_in_both_directions()
    {
        var ab = VenueService.HaversineKm(41.9779, -91.6656, 41.6611, -91.5302);
        var ba = VenueService.HaversineKm(41.6611, -91.5302, 41.9779, -91.6656);
        Assert.Equal(ab, ba, precision: 9);
    }

    [Fact]
    public void A_city_block_is_well_inside_the_dedupe_radius()
    {
        // ~55m of latitude — the dedupe geo gate (default 200m) must catch it.
        var meters = VenueService.HaversineKm(41.9779, -91.6656, 41.9784, -91.6656) * 1000;
        Assert.InRange(meters, 40, 70);
    }
}
