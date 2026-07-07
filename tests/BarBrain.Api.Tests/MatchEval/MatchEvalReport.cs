using System.Text;

namespace BarBrain.Api.Tests.MatchEval;

/// <summary>
/// Writes the Gate C2 eval artifact: the twin-recovery table and the sparsity
/// simulation ("what will launch month feel like" — % of users with any match
/// and with a med-confidence match at 50 and 500 users). Lands in TestResults/,
/// which CI uploads; written pass OR fail so a red run still ships evidence.
/// </summary>
public static class MatchEvalReport
{
    public static void Write(MatchEvalFixture fixture)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Matching eval report (synthetic twins, Gate C2)");
        sb.AppendLine();
        sb.AppendLine("Eval-only gate this sprint (ADR-014): matching can't be human-reviewed with one user.");
        sb.AppendLine();

        if (fixture.SetupFailure is not null)
        {
            sb.AppendLine("## SETUP FAILED");
            sb.AppendLine("```");
            sb.AppendLine(fixture.SetupFailure);
            sb.AppendLine("```");
            WriteFile(sb);
            return;
        }

        sb.AppendLine("## Planted twins — top-1 match recovery");
        sb.AppendLine();
        sb.AppendLine("| twin | dense | top-1 match | partner? |");
        sb.AppendLine("|---|---|---|---|");
        var hits = 0;
        foreach (var t in fixture.Twins)
        {
            var top = fixture.TopMatchHandle.GetValueOrDefault(t.UserId) ?? "∅";
            var partner = t.Handle.EndsWith('a') ? t.Handle[..^1] + "b" : t.Handle[..^1] + "a";
            var ok = top == partner;
            if (ok) hits++;
            sb.AppendLine($"| {t.Handle} | {(t.Dense ? "yes" : "no")} | {top} | {(ok ? "✅" : "❌")} |");
        }
        var rate = fixture.Twins.Count == 0 ? 0 : hits / (double)fixture.Twins.Count;
        sb.AppendLine();
        sb.AppendLine($"**Twin top-1 hit-rate: {rate:0.0%}** ({hits}/{fixture.Twins.Count}).");
        sb.AppendLine();

        sb.AppendLine("## Sparsity — what launch month feels like");
        sb.AppendLine();
        sb.AppendLine("Random, mostly non-overlapping early users. Attribute matching still connects");
        sb.AppendLine("people (any match), but co-rating confidence stays low until density grows — which");
        sb.AppendLine("is exactly why the match-% display ships eager+labeled, not conservative.");
        sb.AppendLine();
        sb.AppendLine("| users | with ≥1 match | with med-confidence match |");
        sb.AppendLine("|---|---|---|");
        foreach (var s in fixture.Sparsity)
            sb.AppendLine($"| {s.Users} | {s.WithAnyMatch} ({s.AnyFraction:0.0%}) | {s.WithMedConfidence} ({s.MedFraction:0.0%}) |");

        WriteFile(sb);
    }

    private static void WriteFile(StringBuilder sb)
    {
        var dir = Path.Combine(RepoRoot(), "TestResults");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "match-eval-report.md"), sb.ToString());
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
