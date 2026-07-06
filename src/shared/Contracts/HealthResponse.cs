namespace BarBrain.Shared.Contracts;

/// <summary>
/// Response from <c>GET /health</c>. Used by the web shell smoke check and the
/// deploy health gate. Carries enough to prove which build is live.
/// </summary>
/// <param name="Status">Always "ok" when the API can serve requests.</param>
/// <param name="Version">Product version (Directory.Build.props).</param>
/// <param name="Sha">Git commit SHA the image was built from, or "local".</param>
public sealed record HealthResponse(string Status, string Version, string Sha);
