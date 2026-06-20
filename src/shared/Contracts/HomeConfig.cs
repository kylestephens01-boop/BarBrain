namespace BarBrain.Shared.Contracts;

/// <summary>
/// Public, flag-driven config the web shell reads on load. Flipping these flags
/// via the admin API changes the home page without a redeploy — the Sprint 0
/// proof of the feature-flag pipeline.
/// </summary>
/// <param name="BannerText">String rendered on the home page (home.banner_text).</param>
/// <param name="ShowStatus">
/// Whether to show the live API status line (home.show_status). Exercises a
/// typed (bool) flag end-to-end alongside the string flag.
/// </param>
public sealed record HomeConfig(string BannerText, bool ShowStatus);
