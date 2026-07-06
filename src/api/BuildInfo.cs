using System.Reflection;

namespace BarBrain.Api;

/// <summary>
/// Resolves the product version and git SHA surfaced by /health and /version.
/// SHA precedence: GIT_SHA env var (set by CI/compose) → the "+sha" suffix that
/// MSBuild's SourceRevisionId appends to the informational version → "local".
/// </summary>
public static class BuildInfo
{
    public static string Version { get; } = ResolveVersion();
    public static string Sha { get; } = ResolveSha();

    private static string ResolveVersion()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Strip any "+<sha>" build-metadata suffix; keep the semantic version.
        var version = info?.Split('+', 2)[0];
        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
    }

    private static string ResolveSha()
    {
        var fromEnv = Environment.GetEnvironmentVariable("GIT_SHA");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = info?.IndexOf('+') ?? -1;
        if (info is not null && plus >= 0 && plus < info.Length - 1)
            return info[(plus + 1)..];

        return "local";
    }
}
