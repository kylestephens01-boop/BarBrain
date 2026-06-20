using BarBrain.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Data;

/// <summary>
/// The single application DbContext. Sprint 0 ships only the foundation tables
/// (settings, events) plus the pgvector extension so later sprints can add
/// vector columns without an extension-enabling migration (ADR-002).
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<EventRecord> Events => Set<EventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // pgvector available from the start (ADR-002). No vector columns yet;
        // enabling it here proves the pgvector image is wired and lets future
        // migrations add HNSW columns directly.
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Setting>(e =>
        {
            e.ToTable("settings");
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(128);
            e.Property(s => s.Value).IsRequired();
            e.Property(s => s.Description).HasMaxLength(512);
            e.Property(s => s.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<EventRecord>(e =>
        {
            e.ToTable("events");
            e.HasKey(ev => ev.Id);
            e.Property(ev => ev.Name).HasMaxLength(128).IsRequired();
            e.Property(ev => ev.Properties).HasColumnType("jsonb");
            e.Property(ev => ev.OccurredAt).IsRequired();
            e.HasIndex(ev => ev.Name);
            e.HasIndex(ev => ev.OccurredAt);
        });
    }
}
