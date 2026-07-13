namespace BarBrain.Api.Tests;

/// <summary>
/// Sprint 7 hardening: static assertions over the compose files (Hard Rule 8 —
/// Postgres is never exposed publicly). No Docker needed; these guard the
/// files themselves so a "temporary" port mapping can't merge.
/// </summary>
public class ComposeHardeningTests
{
    private static string InfraDir { get; } = FindInfraDir();

    private static string FindInfraDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BarBrain.slnx")))
            dir = dir.Parent!;
        return Path.Combine(dir!.FullName, "infra");
    }

    [Fact]
    public void Prod_overlay_publishes_no_postgres_or_api_ports()
    {
        var prod = File.ReadAllText(Path.Combine(InfraDir, "docker-compose.prod.yml"));

        // The overlay must explicitly EMPTY the port lists ("ports: []"), not
        // merely omit them — compose merges lists from the base file otherwise.
        var services = SplitServices(prod);
        Assert.Contains("[]", ServiceLine(services["postgres"], "ports"));
        Assert.Contains("[]", ServiceLine(services["api"], "ports"));
    }

    [Fact]
    public void Dev_compose_binds_postgres_and_api_to_loopback_only()
    {
        var dev = File.ReadAllText(Path.Combine(InfraDir, "docker-compose.yml"));
        foreach (var line in dev.Split('\n'))
        {
            if (line.TrimStart().StartsWith('#')) continue;
            var trimmed = line.Trim().Trim('"', '\'', '-', ' ');
            // Any host-port mapping for 5432/8080 must be loopback-scoped.
            if (trimmed.Contains(":5432") || trimmed.EndsWith(":8080"))
                Assert.StartsWith("127.0.0.1:", trimmed);
        }
    }

    [Fact]
    public void Caddyfile_carries_the_security_header_block()
    {
        var caddy = File.ReadAllText(Path.Combine(InfraDir, "Caddyfile"));
        Assert.Contains("Content-Security-Policy", caddy);
        Assert.Contains("X-Content-Type-Options", caddy);
        Assert.Contains("frame-ancestors 'none'", caddy);
        Assert.Contains("Strict-Transport-Security", caddy);
    }

    private static Dictionary<string, string> SplitServices(string yaml)
    {
        // Just enough YAML: top-level "services:" children split by 2-space keys.
        var result = new Dictionary<string, string>();
        string? current = null;
        var buffer = new List<string>();
        foreach (var line in yaml.Split('\n'))
        {
            if (line.StartsWith("  ") && !line.StartsWith("   ") && line.TrimEnd().EndsWith(":"))
            {
                if (current is not null) result[current] = string.Join('\n', buffer);
                current = line.Trim().TrimEnd(':');
                buffer.Clear();
            }
            else if (current is not null)
            {
                buffer.Add(line);
            }
        }
        if (current is not null) result[current] = string.Join('\n', buffer);
        return result;
    }

    private static string ServiceLine(string serviceYaml, string key)
        => serviceYaml.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith($"{key}:"))?.Trim()
           ?? throw new InvalidOperationException($"'{key}:' line not found in service block");
}
