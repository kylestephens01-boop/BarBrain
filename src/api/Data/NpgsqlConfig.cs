using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Data;

/// <summary>
/// Single home for Npgsql provider configuration so the app (Program.cs), the
/// design-time factory, and the test fixtures can't drift apart.
/// </summary>
public static class NpgsqlConfig
{
    public static DbContextOptionsBuilder UseBarBrainNpgsql(
        this DbContextOptionsBuilder options,
        string connectionString,
        bool enableRetry = true)
        => options.UseNpgsql(connectionString, npgsql =>
        {
            // events.Properties maps Dictionary<string,string> → jsonb, which
            // Npgsql 8+ only serializes with an explicit dynamic-JSON opt-in.
            // Without this, any event write with properties throws (CI caught
            // it: POST /api/events returned 500 whenever properties was set).
            npgsql.ConfigureDataSource(ds => ds.EnableDynamicJson());

            if (enableRetry)
                npgsql.EnableRetryOnFailure();
        });
}
