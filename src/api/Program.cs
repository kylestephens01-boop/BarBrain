using BarBrain.Api;
using BarBrain.Api.Data;
using BarBrain.Api.Endpoints;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Data layer: PostgreSQL 16 + pgvector (ADR-002) -------------------------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

// --- Feature flags / settings (ADR-006) -------------------------------------
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ISettingsService, SettingsService>();

// --- CORS: web shell is a separate origin under `dotnet run` (compose proxies
// same-origin via Caddy, so this only matters for non-proxied local dev) ------
const string WebCorsPolicy = "web";
builder.Services.AddCors(options => options.AddPolicy(WebCorsPolicy, policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
    if (origins is { Length: > 0 })
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    else if (builder.Environment.IsDevelopment())
        policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(WebCorsPolicy);

// --- Migrate + seed on startup ----------------------------------------------
// Idempotent; single-instance VPS scale (ADR-004). Guarded by a flag so tests
// and special deploys can opt out and migrate explicitly.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    await db.Database.MigrateAsync();
    await SettingsSeeder.SeedAsync(db, app.Environment.ContentRootPath, logger);
}

// --- Endpoints ---------------------------------------------------------------
app.MapHealthEndpoints();
app.MapConfigEndpoints();
app.MapAdminSettingsEndpoints();
app.MapEventEndpoints();

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
