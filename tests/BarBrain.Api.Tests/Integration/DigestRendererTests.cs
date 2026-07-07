using BarBrain.Api.Digest;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Pure render litmus (ADR-019 acceptance: "digest renders correctly"). No
/// database — asserts the composed HTML carries every block plus the CAN-SPAM
/// footer, and writes the rendered email to TestResults/ so CI (and Playwright)
/// can screenshot it.
/// </summary>
public sealed class DigestRendererTests
{
    private static DigestModel Sample() => new(
        Handle: "hopfiend",
        UnsubscribeUrl: "https://dev.barbrain.co/api/digest/unsubscribe?token=11111111-1111-1111-1111-111111111111",
        PhysicalAddress: "BarBrain LLC, 123 Registered Agent Way, Des Moines, IA 50309",
        Recap: new DigestRecap(4, 2),
        Streak: new DigestStreak(3, 4),
        TopPicks:
        [
            new DigestPick("Up your alley", "Pseudo Sue", "Toppling Goliath", "Because your beer ratings lean hoppy and citrus — this fits that shape."),
            new DigestPick("Stretch a little", "King Sue", "Toppling Goliath", "A little bigger than your usual — same backbone."),
            new DigestPick("Loved by your matches", "Zombie Dust", "3 Floyds", "@lupulin, a strong palate match, rated this highly."),
        ],
        MatchHook: new DigestMatchHook(3, "lupulin", "Zombie Dust"));

    [Fact]
    public void Rendered_digest_carries_all_blocks_and_the_can_spam_footer()
    {
        var (subject, html) = DigestRenderer.Render(Sample());

        Assert.False(string.IsNullOrWhiteSpace(subject));
        Assert.Contains("hopfiend", html);
        Assert.Contains("This week", html);            // recap block
        Assert.Contains("streak", html);               // streak block
        Assert.Contains("Top picks", html);            // top picks block
        Assert.Contains("palate match", html);         // match hook block
        // CAN-SPAM: physical address + unsubscribe, both present.
        Assert.Contains("Registered Agent Way", html);
        Assert.Contains("/api/digest/unsubscribe?token=", html);
        Assert.Contains("Unsubscribe", html);

        // No consumption-volume/"drink more" framing (BRAND.md, ADR-016).
        foreach (var banned in new[] { "drink more", "another round", "shots", "buzz", "wasted" })
            Assert.DoesNotContain(banned, html, StringComparison.OrdinalIgnoreCase);

        var dir = Path.Combine(RepoRoot(), "TestResults");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "digest-litmus.html"), html);
    }

    [Fact]
    public void Empty_model_reports_no_content()
    {
        var empty = new DigestModel("nobody", "https://x/api/digest/unsubscribe?token=t", null, null, null, [], null);
        Assert.False(empty.HasContent);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BarBrain.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
