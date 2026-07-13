using System.Text.RegularExpressions;

namespace BarBrain.Api.Tests;

/// <summary>
/// Sprint 7 age-gate audit: the copy sweep, made permanent. Every user-facing
/// string in the web app lives inline in .razor markup (no resource files), so
/// this lints the files themselves against the BRAND.md prohibited-language
/// list (Hard Rule 11 / ADR-016): no volume/quantity framing, no intoxication
/// references, no "drink more" urgency, no health claims about alcohol.
/// It also pins the spec's REQUIRED line: the footer's responsibility
/// statement must exist.
/// </summary>
public partial class WebCopyLintTests
{
    // Word-boundary regexes — "shot" must not fire on "screenshot".
    [GeneratedRegex(
        @"\b(crush(ed|ing)? it|binge|power hour|power drinker|chug(ged|ging)?|shots?|pound(ed|ing)? (a|another|beers)|get (drunk|wasted|hammered)|drunk|buzzed|tipsy|wasted|hammered|intoxicat\w*|drink more|another round|keep drinking|one more (round|drink)|liquid courage|hair of the dog)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ProhibitedCopy();

    [GeneratedRegex(
        @"\b(good for (you|your health)|health(y|ier) (choice|option)|antioxidant|heart.healthy|wellness benefit)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex HealthClaims();

    private static string WebDir { get; } = FindWebDir();

    private static string FindWebDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BarBrain.slnx")))
            dir = dir.Parent!;
        return Path.Combine(dir!.FullName, "src", "web");
    }

    private static IEnumerable<string> CopyFiles()
        => Directory.EnumerateFiles(WebDir, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Append(Path.Combine(WebDir, "wwwroot", "index.html"));

    [Fact]
    public void No_user_facing_copy_violates_the_brand_prohibited_language_list()
    {
        var violations = new List<string>();
        foreach (var file in CopyFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var match in ProhibitedCopy().Matches(lines[i]).Cast<Match>())
                    violations.Add($"{Path.GetFileName(file)}:{i + 1} prohibited: '{match.Value}'");
                foreach (var match in HealthClaims().Matches(lines[i]).Cast<Match>())
                    violations.Add($"{Path.GetFileName(file)}:{i + 1} health claim: '{match.Value}'");
            }
        }

        Assert.True(violations.Count == 0,
            "BRAND.md prohibited-language hits (Hard Rule 11):\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Footer_carries_the_required_age_and_responsibility_statement()
    {
        var layout = File.ReadAllText(Path.Combine(WebDir, "Layout", "MainLayout.razor"));
        Assert.Contains("21 and over", layout);
        Assert.Contains("Drink responsibly.", layout); // Sprint 7 spec: required line
        Assert.Contains("/legal/terms", layout);
        Assert.Contains("/legal/privacy", layout);
    }

    [Fact]
    public void Legal_placeholder_pages_exist_with_attorney_flags()
    {
        foreach (var page in new[] { "Terms", "Privacy" })
        {
            var text = File.ReadAllText(Path.Combine(WebDir, "Pages", "Legal", $"{page}.razor"));
            Assert.Contains("ATTORNEY REVIEW REQUIRED", text);
            Assert.Contains("DRAFT", text);
        }
        Assert.Contains("Report",
            File.ReadAllText(Path.Combine(WebDir, "Pages", "Legal", "Contact.razor")));
    }
}
