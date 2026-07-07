using System.Net;
using System.Net.Http.Json;
using BarBrain.Shared.Contracts;

namespace BarBrain.Web.Services;

/// <summary>
/// Client-side session state. The cookie is HttpOnly (the API owns it); this
/// just mirrors GET /api/auth/me so pages know who's signed in. Scoped =
/// singleton in WASM.
/// </summary>
public sealed class UserState(HttpClient http)
{
    private bool _loaded;

    public MeResponse? Me { get; private set; }
    public bool IsSignedIn => Me is not null;

    public event Action? Changed;

    /// <summary>Idempotent; first caller pays the /me round-trip.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            using var response = await http.GetAsync("api/auth/me");
            Me = response.StatusCode == HttpStatusCode.OK
                ? await response.Content.ReadFromJsonAsync<MeResponse>()
                : null;
        }
        catch
        {
            Me = null; // API unreachable — treat as signed out
        }
        _loaded = true;
        Changed?.Invoke();
    }

    public void SetSignedIn(MeResponse me)
    {
        Me = me;
        _loaded = true;
        Changed?.Invoke();
    }

    public async Task SignOutAsync()
    {
        try { await http.PostAsync("api/auth/logout", null); } catch { /* best effort */ }
        Me = null;
        Changed?.Invoke();
    }
}
