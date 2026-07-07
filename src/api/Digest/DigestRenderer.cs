using System.Net;
using System.Text;

namespace BarBrain.Api.Digest;

/// <summary>
/// Renders a <see cref="DigestModel"/> to an email subject + HTML body (ADR-019).
/// Pure and static so a test can litmus-render it and a screenshot can be
/// attached in CI. Inline styles only (email clients strip stylesheets); no CDN
/// fonts. Two-temperature grammar held loosely for email: amber for the DRINK,
/// teal for the INTELLIGENCE (matches, reasons). Copy obeys BRAND.md + ADR-016 —
/// breadth/discovery framing, never volume or "drink more".
///
/// The footer is the CAN-SPAM contract: a physical mailing address and a
/// one-click unsubscribe link. Both are always rendered.
/// </summary>
public static class DigestRenderer
{
    private const string Ink = "#0f1115";
    private const string Card = "#171a21";
    private const string Text = "#e8e8ea";
    private const string Muted = "#9aa0aa";
    private const string Pour = "#d99a2b";   // amber — beverages
    private const string Synapse = "#3ecfb8"; // teal — intelligence

    public static (string Subject, string Html) Render(DigestModel m)
    {
        var subject = m.MatchHook is not null
            ? "Your BarBrain week — a palate match and fresh picks"
            : "Your BarBrain week — fresh picks tuned to your palate";

        var body = new StringBuilder();
        body.Append($"""
            <div style="background:{Ink};padding:24px 0;font-family:Helvetica,Arial,sans-serif;color:{Text}">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%">
            <tr><td style="padding:0 24px 16px">
              <span style="font-size:20px;font-weight:600">Bar<span style="color:{Pour}">Brain</span></span>
              <p style="color:{Muted};margin:4px 0 0">Hi @{Enc(m.Handle)} — your week in taste.</p>
            </td></tr>
            """);

        if (m.Recap is { } recap)
            body.Append(Block("This week", RecapCopy(recap)));

        if (m.Streak is { } streak)
            body.Append(Block("Your discovery streak",
                $"<span style=\"color:{Synapse};font-weight:600\">{streak.Weeks} weeks</span> of exploring in a row" +
                (streak.DistinctDrinksThisWeek > 0
                    ? $" — {streak.DistinctDrinksThisWeek} distinct drink{(streak.DistinctDrinksThisWeek == 1 ? "" : "s")} this week."
                    : ".")));

        if (m.TopPicks.Count > 0)
        {
            var picks = new StringBuilder();
            foreach (var p in m.TopPicks)
                picks.Append($"""
                    <p style="margin:0 0 12px">
                      <span style="color:{Muted};font-size:12px;text-transform:uppercase;letter-spacing:.04em">{Enc(p.SectionTitle)}</span><br>
                      <span style="color:{Pour};font-weight:600">{Enc(p.DrinkName)}</span>
                      <span style="color:{Muted}"> · {Enc(p.ProducerName)}</span><br>
                      <span style="color:{Synapse};font-size:14px">{Enc(p.Reason)}</span>
                    </p>
                    """);
            body.Append(Block("Top picks", picks.ToString()));
        }

        if (m.MatchHook is { } hook)
            body.Append(Block("A palate match", MatchCopy(hook)));

        // CAN-SPAM footer — physical address + unsubscribe, always present.
        var address = string.IsNullOrWhiteSpace(m.PhysicalAddress)
            ? "[mailing address pending]"
            : Enc(m.PhysicalAddress);
        body.Append($"""
            <tr><td style="padding:24px;border-top:1px solid #2a2e37;margin-top:16px">
              <p style="color:{Muted};font-size:12px;margin:0 0 8px">
                You get this because you have a BarBrain account.
                <a href="{Enc(m.UnsubscribeUrl)}" style="color:{Muted};text-decoration:underline">Unsubscribe</a>.
              </p>
              <p style="color:{Muted};font-size:12px;margin:0">{address}</p>
            </td></tr>
            </table></td></tr></table></div>
            """);

        return (subject, body.ToString());
    }

    private static string RecapCopy(DigestRecap r)
    {
        var drinks = $"{r.DrinksRated} drink{(r.DrinksRated == 1 ? "" : "s")}";
        var cats = $"{r.Categories} categor{(r.Categories == 1 ? "y" : "ies")}";
        return $"You rated <span style=\"color:{Pour};font-weight:600\">{drinks}</span> across {cats} this week.";
    }

    private static string MatchCopy(DigestMatchHook h)
    {
        var handle = $"<span style=\"color:{Synapse};font-weight:600\">@{Enc(h.MatchHandle)}</span>";
        if (h.MatchDrinkName is { Length: > 0 })
            return $"{handle} shares your palate and recently loved " +
                   $"<span style=\"color:{Pour};font-weight:600\">{Enc(h.MatchDrinkName)}</span>. See who else matches you.";
        return h.MatchCount > 1
            ? $"{handle} and {h.MatchCount - 1} others share your palate. See who matches you."
            : $"{handle} shares your palate. See who matches you.";
    }

    private static string Block(string title, string innerHtml) => $"""
        <tr><td style="padding:0 24px 8px">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Card};border-radius:12px">
          <tr><td style="padding:16px 20px">
            <h2 style="font-size:15px;margin:0 0 8px;color:{Text}">{Enc(title)}</h2>
            <div style="font-size:15px;line-height:1.5">{innerHtml}</div>
          </td></tr></table>
        </td></tr>
        """;

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
}
