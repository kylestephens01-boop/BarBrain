using BarBrain.Api.Moderation;

namespace BarBrain.Api.Tests;

/// <summary>
/// The anomaly detectors' pure math (Sprint 6). Detection evidence for HUMAN
/// review — these tests pin what "outlier" and "rapid-fire" mean.
/// </summary>
public class AnomalyMathTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 10, 20, 0, 0, TimeSpan.Zero);

    // ---- z-score outliers ----------------------------------------------------

    [Fact]
    public void Too_few_users_yields_no_scores()
    {
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var drink = Guid.NewGuid();
        var scores = AnomalyScanService.ZScores(
            [(u1, drink, 5), (u2, drink, 1)], minRatingsPerUser: 1);
        Assert.Empty(scores); // < 3 users: no population to compare against
    }

    [Fact]
    public void Systematic_low_rater_gets_a_negative_deviation_and_high_absolute_zscore()
    {
        // 5 normal users rate near each drink's mean; one bombs everything.
        var drinks = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        var normals = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var bomber = Guid.NewGuid();

        var ratings = new List<(Guid, Guid, double)>();
        foreach (var drink in drinks)
        {
            foreach (var user in normals) ratings.Add((user, drink, 4.0));
            ratings.Add((bomber, drink, 1.0));
        }

        var scores = AnomalyScanService.ZScores(ratings, minRatingsPerUser: 8);

        var (meanDev, z, count) = scores[bomber];
        Assert.True(meanDev < -1.5, $"bomber mean deviation {meanDev} should be strongly negative");
        Assert.True(Math.Abs(z) > 2.0, $"bomber |z| {Math.Abs(z)} should stand out");
        Assert.Equal(10, count);
        // The normal users hug the population mean.
        Assert.All(normals, u => Assert.True(Math.Abs(scores[u].ZScore) < Math.Abs(z)));
    }

    [Fact]
    public void Users_below_the_minimum_rating_floor_are_not_scored()
    {
        var drinks = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        var users = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();
        var sparse = Guid.NewGuid();

        var ratings = new List<(Guid, Guid, double)>();
        foreach (var drink in drinks)
            foreach (var user in users) ratings.Add((user, drink, 3.5));
        ratings.Add((sparse, drinks[0], 1.0)); // one rating only

        var scores = AnomalyScanService.ZScores(ratings, minRatingsPerUser: 8);
        Assert.DoesNotContain(sparse, scores.Keys);
    }

    // ---- rapid-fire sliding window --------------------------------------------

    [Fact]
    public void Empty_timeline_is_zero()
        => Assert.Equal(0, AnomalyScanService.MaxInWindow([], TimeSpan.FromMinutes(10)));

    [Fact]
    public void Spread_out_ratings_peak_low()
    {
        var times = Enumerable.Range(0, 20).Select(i => T0.AddMinutes(i * 30)).ToList();
        Assert.Equal(1, AnomalyScanService.MaxInWindow(times, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Burst_inside_the_window_is_counted_fully()
    {
        var times = Enumerable.Range(0, 30).Select(i => T0.AddSeconds(i * 10)).ToList(); // 30 in 5 min
        Assert.Equal(30, AnomalyScanService.MaxInWindow(times, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Window_slides_to_find_the_densest_stretch()
    {
        var times = new List<DateTimeOffset>
        {
            T0, T0.AddMinutes(30), T0.AddMinutes(31), T0.AddMinutes(32), T0.AddMinutes(90),
        };
        Assert.Equal(3, AnomalyScanService.MaxInWindow(times, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Unsorted_input_is_handled()
    {
        var times = new List<DateTimeOffset> { T0.AddMinutes(5), T0, T0.AddMinutes(2) };
        Assert.Equal(3, AnomalyScanService.MaxInWindow(times, TimeSpan.FromMinutes(10)));
    }
}
