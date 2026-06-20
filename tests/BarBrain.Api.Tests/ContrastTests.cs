using System.Globalization;
using System.Text.RegularExpressions;

namespace BarBrain.Api.Tests;

/// <summary>
/// WCAG contrast enforcement against the brand tokens (BRAND.md: "CI-enforced
/// against tokens, incl. SURFACE pairs"). Parses the single source of truth —
/// design-tokens.css — so a token change is checked automatically. Includes
/// text-on-surface pairs (e.g. muted on back-bar), not just background pairs.
///
/// Runs in `dotnet test`; CI also surfaces it as a dedicated job via the
/// "Contrast" trait. No database/Docker needed.
/// </summary>
[Trait("Category", "Contrast")]
public sealed class ContrastTests
{
    // fg token, bg token, minimum ratio. 4.5 = body text (AA), 3.0 = large/AA.
    [Theory]
    [InlineData("--bb-coaster", "--bb-ink", 4.5)]       // primary text on app bg
    [InlineData("--bb-coaster", "--bb-surface", 4.5)]   // primary text on cards
    [InlineData("--bb-text-muted", "--bb-ink", 4.5)]    // secondary text on app bg
    [InlineData("--bb-text-muted", "--bb-surface", 4.5)] // secondary text on cards (the flagged pair)
    [InlineData("--bb-synapse", "--bb-ink", 4.5)]       // intelligence accent
    [InlineData("--bb-synapse", "--bb-surface", 4.5)]
    [InlineData("--bb-pour", "--bb-ink", 4.5)]          // beverage accent
    [InlineData("--bb-pour", "--bb-surface", 4.5)]
    [InlineData("--bb-bitters", "--bb-ink", 3.0)]       // destructive: large text only
    public void Token_pair_meets_wcag_minimum(string fgToken, string bgToken, double minRatio)
    {
        var tokens = LoadTokens();

        Assert.True(tokens.ContainsKey(fgToken), $"Token {fgToken} not found in design-tokens.css");
        Assert.True(tokens.ContainsKey(bgToken), $"Token {bgToken} not found in design-tokens.css");

        var ratio = ContrastRatio(tokens[fgToken], tokens[bgToken]);

        Assert.True(
            ratio >= minRatio,
            $"{fgToken} on {bgToken} = {ratio:F2}:1, below required {minRatio:F1}:1");
    }

    // --- WCAG math ---------------------------------------------------------

    private static double ContrastRatio((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (light, dark) = la >= lb ? (la, lb) : (lb, la);
        return (light + 0.05) / (dark + 0.05);
    }

    private static double RelativeLuminance((double R, double G, double B) c)
        => 0.2126 * Linearize(c.R) + 0.7152 * Linearize(c.G) + 0.0722 * Linearize(c.B);

    private static double Linearize(double channel8bit)
    {
        var c = channel8bit / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    // --- token parsing -----------------------------------------------------

    private static (double R, double G, double B) ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        var r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
        var g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
        var b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
        return (r, g, b);
    }

    private static Dictionary<string, (double R, double G, double B)> LoadTokens()
    {
        var css = File.ReadAllText(FindTokensFile());
        var matches = Regex.Matches(css, @"(--bb-[a-z0-9-]+)\s*:\s*#([0-9A-Fa-f]{6})\b");
        var dict = new Dictionary<string, (double, double, double)>(StringComparer.Ordinal);
        foreach (Match m in matches)
            dict[m.Groups[1].Value] = ParseHex(m.Groups[2].Value);
        return dict;
    }

    private static string FindTokensFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "web", "wwwroot", "css", "design-tokens.css");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate src/web/wwwroot/css/design-tokens.css from the test output directory.");
    }
}
