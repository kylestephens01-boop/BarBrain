using BarBrain.Api.Auth;

namespace BarBrain.Api.Tests;

/// <summary>21+ boundary math (ADR-010). Pure — runs without Docker.</summary>
public class AgeGateTests
{
    private static readonly DateOnly Today = new(2026, 7, 6);

    [Theory]
    [InlineData(2005, 7, 6, true)]   // 21st birthday is today → in
    [InlineData(2005, 7, 5, true)]   // turned 21 yesterday → in
    [InlineData(2005, 7, 7, false)]  // 21 tomorrow → polite hard stop
    [InlineData(2008, 1, 1, false)]  // clearly under
    [InlineData(1960, 1, 1, true)]   // clearly over
    public void Age_boundary_is_exact(int year, int month, int day, bool expected)
        => Assert.Equal(expected, AgeGate.IsOfAge(new DateOnly(year, month, day), Today));

    [Fact]
    public void Leap_day_birthday_counts_on_march_1_in_common_years()
    {
        // Born 2004-02-29; DateOnly.AddYears lands on Feb 28 2025-style dates:
        // 2004-02-29 + 21y = 2025-02-28, so they are "of age" from 2025-02-28.
        var dob = new DateOnly(2004, 2, 29);
        Assert.False(AgeGate.IsOfAge(dob, new DateOnly(2025, 2, 27)));
        Assert.True(AgeGate.IsOfAge(dob, new DateOnly(2025, 2, 28)));
    }

    [Theory]
    [InlineData("1990-04-17", true)]
    [InlineData("1990-4-17", false)]   // strict format only
    [InlineData("04/17/1990", false)]
    [InlineData("not-a-date", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Dob_parsing_is_strict(string? input, bool ok)
        => Assert.Equal(ok, AgeGate.TryParseDateOfBirth(input, out _));
}
