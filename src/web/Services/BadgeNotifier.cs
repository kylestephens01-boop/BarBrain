using System.Net.Http.Json;
using BarBrain.Shared.Contracts;

namespace BarBrain.Web.Services;

/// <summary>
/// Award-toast plumbing (Sprint 6). Pages call <see cref="CheckAsync"/> after
/// actions that can award a badge (rating, check-in, wiki edit); any unseen
/// awards flow to the toast component via <see cref="OnAwards"/>, and are
/// acknowledged server-side once shown. Deliberately fire-and-forget: a badge
/// toast must never break the action that earned it.
/// </summary>
public sealed class BadgeNotifier(HttpClient http)
{
    public event Action<IReadOnlyList<BadgeDto>>? OnAwards;

    public async Task CheckAsync()
    {
        try
        {
            var unseen = await http.GetFromJsonAsync<UnseenBadgesResponse>("api/badges/unseen");
            if (unseen is null || unseen.Badges.Count == 0) return;

            OnAwards?.Invoke(unseen.Badges);
            await http.PostAsync("api/badges/seen", null);
        }
        catch
        {
            // Toasts are best-effort; the gallery still shows every award.
        }
    }
}
