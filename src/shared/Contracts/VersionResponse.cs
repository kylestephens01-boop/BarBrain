namespace BarBrain.Shared.Contracts;

/// <summary>
/// Response from <c>GET /version</c>. The deploy gate (Sprint 0 acceptance:
/// "https://dev.barbrain.co/health returns version+sha") asserts on this.
/// </summary>
/// <param name="Version">Product version (Directory.Build.props).</param>
/// <param name="Sha">Git commit SHA the image was built from, or "local".</param>
public sealed record VersionResponse(string Version, string Sha);
