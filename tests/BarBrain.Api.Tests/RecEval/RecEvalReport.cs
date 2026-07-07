using System.Text;

namespace BarBrain.Api.Tests.RecEval;

/// <summary>
/// Writes the per-persona eval table (Sprint 3 acceptance: "Eval suite green
/// in CI with report artifact"). Lands in TestResults/, which CI already
/// uploads; written pass OR fail so a red run still ships its evidence.
/// </summary>
public static class RecEvalReport
{
    public static void Write(RecEvalFixture fixture)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Rec-quality eval report (golden set, Gate C1)");
        sb.AppendLine();
        sb.AppendLine($"Catalog: {fixture.Catalog.Count} synthetic drinks " +
            $"({RecEvalFixture.RandomDrinksPerCategory} random + 8 archetypes per category), " +
            $"seed {RecEvalFixture.CatalogSeed}, frozen clock {RecEvalFixture.FixedNow:u}.");
        sb.AppendLine();
        if (fixture.SetupFailure is not null)
        {
            sb.AppendLine("## SETUP FAILED");
            sb.AppendLine("```");
            sb.AppendLine(fixture.SetupFailure);
            sb.AppendLine("```");
        }

        sb.AppendLine("| persona | category | dense | ratings | precision@10 | LOO hit | alley dist | wildcard dist | deterministic |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var r in fixture.Results)
        {
            sb.AppendLine(
                $"| {r.Persona.Handle} | {r.Persona.Category} | {(r.Persona.Dense ? "yes" : "no")} " +
                $"| {r.Feed.RatingsCount} | {r.Precision10:0.00} | {(r.LooHit is null ? "—" : r.LooHit.Value ? "hit" : "miss")} " +
                $"| {Fmt(r.AlleyMeanDistance)} | {Fmt(r.WildcardMeanDistance)} | {(r.Deterministic ? "yes" : "NO")} |");
        }

        sb.AppendLine();
        if (fixture.BridgePersona is { } bridge)
        {
            sb.AppendLine("## Cross-category bridge (ADR-027 moat check)");
            var crossItems = bridge.Feed.Sections.SelectMany(s => s.Items).Where(i => i.CrossCategory).ToList();
            sb.AppendLine($"peat-lover (whiskey-only ratings) received {crossItems.Count} cross-category recs:");
            foreach (var item in crossItems)
                sb.AppendLine($"- {item.Category}: **{item.Name}** — \"{item.Reason}\"");
        }

        var dir = Path.Combine(RepoRoot(), "TestResults");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "rec-eval-report.md"), sb.ToString());
    }

    private static string Fmt(double value) => double.IsNaN(value) ? "—" : value.ToString("0.000");

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
