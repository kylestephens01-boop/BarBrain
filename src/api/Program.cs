using BarBrain.Api;
using BarBrain.Api.Auth;
using BarBrain.Api.Catalog;
using BarBrain.Api.Data;
using BarBrain.Api.Endpoints;
using BarBrain.Api.Ratings;
using BarBrain.Api.Settings;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

// CLI mode: `dotnet BarBrain.Api.dll import …` / `… report` runs the catalog
// importers against the same config/DbContext and exits — no web server.
if (CatalogCli.IsCliInvocation(args))
    return await CatalogCli.RunAsync(args);

var builder = WebApplication.CreateBuilder(args);

// --- Data layer: PostgreSQL 16 + pgvector (ADR-002) -------------------------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseBarBrainNpgsql(connectionString));

// --- Feature flags / settings (ADR-006) -------------------------------------
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ISettingsService, SettingsService>();

// --- Catalog: vectors, search, entity resolution, importers (Sprint 1) ------
builder.Services.AddCatalogServices();

// --- Identity & the core rating loop (Sprint 2, ADR-010/011/012) ------------
builder.AddBarBrainAuth();
builder.Services.AddScoped<RatingService>();

// Caddy fronts the API in every deployed shape and sets X-Forwarded-Proto;
// honoring it makes the auth cookie Secure over https without breaking
// plain-http localhost e2e. Single-box topology → trust the proxy hop.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

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

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(WebCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

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
app.MapAdminMergeEndpoints();
app.MapEventEndpoints();
app.MapCatalogEndpoints();
app.MapAuthEndpoints();
app.MapRatingEndpoints();

app.Run();
return 0;

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
