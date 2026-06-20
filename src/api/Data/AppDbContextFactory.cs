using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BarBrain.Api.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> (migrations/scaffolding). Keeps the
/// tooling off the app's startup path (which migrates + seeds). Connection
/// string comes from ConnectionStrings__Default, else a localhost default that
/// matches the port the local compose binds for tooling.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=barbrain;Username=barbrain;Password=barbrain";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
