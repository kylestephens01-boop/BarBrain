using BarBrain.Api.Catalog;

namespace BarBrain.Api.Tests;

/// <summary>Pure unit tests — run everywhere, no Docker.</summary>
public class NameNormalizerTests
{
    [Theory]
    [InlineData("Toppling Goliath Brewing Co", "toppling goliath brewing company")]
    [InlineData("Toppling Goliath Brewing Company", "toppling goliath brewing company")]
    [InlineData("  Bell's   Brewery ", "bell s brewery")]
    [InlineData("Kölsch", "kolsch")]
    [InlineData("Bière de Garde", "biere de garde")]
    [InlineData("Ale & Lager Works", "ale and lager works")]
    [InlineData("BROS. Brewing", "brothers brewing")]
    [InlineData("Gewürztraminer", "gewurztraminer")]
    public void Normalizes_expected_forms(string input, string expected)
        => Assert.Equal(expected, NameNormalizer.Normalize(input));

    [Theory]
    [InlineData("Pseudo Sue")]
    [InlineData("Café Olé & Friends")]
    [InlineData("St. Bernardus Abt 12")]
    public void Is_idempotent(string input)
    {
        var once = NameNormalizer.Normalize(input);
        Assert.Equal(once, NameNormalizer.Normalize(once));
    }

    [Fact]
    public void Collapses_punctuation_to_single_spaces()
        => Assert.Equal("old no 7", NameNormalizer.Normalize("Old   No. 7!!"));
}
