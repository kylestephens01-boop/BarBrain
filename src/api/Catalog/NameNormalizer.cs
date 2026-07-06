using System.Globalization;
using System.Text;

namespace BarBrain.Api.Catalog;

/// <summary>
/// Produces the canonical <c>NormalizedName</c> used for search, dedup
/// candidates, and the drinks canonical-identity unique index (ADR-008).
/// Deterministic and idempotent: normalize(normalize(x)) == normalize(x).
///
/// Rules: lowercase (invariant) → strip diacritics (NFD fold) → "&" becomes
/// " and " → every other non-alphanumeric becomes a space → whitespace
/// collapsed → a few high-value token expansions (co→company, bros→brothers)
/// so "Toppling Goliath Brewing Co" and "… Brewing Company" collide in dedup.
/// Trigram similarity absorbs the rest of the variance — resist growing the
/// token map reactively; every entry reshapes existing normalized keys.
/// </summary>
public static class NameNormalizer
{
    private static readonly Dictionary<string, string> TokenExpansions = new()
    {
        ["co"] = "company",
        ["bros"] = "brothers",
    };

    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var folded = name.Trim().ToLowerInvariant().Replace("&", " and ");

        var sb = new StringBuilder(folded.Length);
        foreach (var ch in folded.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
                continue; // diacritic — dropped (ü → u)
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else
                sb.Append(' ');
        }

        var tokens = sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => TokenExpansions.GetValueOrDefault(t, t));

        return string.Join(' ', tokens);
    }
}
