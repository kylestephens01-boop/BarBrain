using System.Net.Http.Json;
using BarBrain.Shared.Contracts;

namespace BarBrain.Web.Services;

public static class ApiErrors
{
    /// <summary>Reads the API's machine-readable error body, or null.</summary>
    public static async Task<ApiError?> ReadAsync(HttpResponseMessage response)
    {
        try { return await response.Content.ReadFromJsonAsync<ApiError>(); }
        catch { return null; }
    }
}
