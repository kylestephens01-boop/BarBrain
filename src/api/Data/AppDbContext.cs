using BarBrain.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Data;

/// <summary>
/// The single application DbContext. Sprint 0 shipped the foundation tables
/// (settings, events); Sprint 1 adds the catalog schema (docs/SCHEMA.md is the
/// annotated ERD): users stub, producers, styles, attribute_definitions,
/// style_attributes, drinks, drink_attributes, merge_queue.
///
/// Constraint posture (ADR-026): closed vocabularies, value ranges, ownership/
/// visibility invariants, and category coherence are enforced HERE as CHECK
/// constraints and composite FKs — the database refuses bad rows even if app
/// code regresses.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<EventRecord> Events => Set<EventRecord>();

    public DbSet<User> Users => Set<User>();
    public DbSet<Producer> Producers => Set<Producer>();
    public DbSet<Style> Styles => Set<Style>();
    public DbSet<AttributeDefinition> AttributeDefinitions => Set<AttributeDefinition>();
    public DbSet<StyleAttributeValue> StyleAttributes => Set<StyleAttributeValue>();
    public DbSet<Drink> Drinks => Set<Drink>();
    public DbSet<DrinkAttributeValue> DrinkAttributes => Set<DrinkAttributeValue>();
    public DbSet<MergeCandidate> MergeQueue => Set<MergeCandidate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // pgvector: attribute-similarity recs (ADR-002/025).
        // pg_trgm: fuzzy search + dedup candidate generation (Sprint 1 spec).
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.HasPostgresExtension("pg_trgm");

        const string categoryCheck = "\"Category\" IN ('beer','whiskey','wine')";

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

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            // Handles are stored lowercase (normalized on write when Sprint 2
            // introduces claiming); unique among non-null values.
            e.Property(u => u.Handle).HasMaxLength(32);
            e.HasIndex(u => u.Handle).IsUnique();
        });

        modelBuilder.Entity<Producer>(e =>
        {
            e.ToTable("producers", t =>
            {
                t.HasCheckConstraint("ck_producers_visibility",
                    "\"Visibility\" IN ('public','private')");
                // Ownerless (imported/seeded) rows can never be private (ADR-026).
                t.HasCheckConstraint("ck_producers_owner_visibility",
                    "\"CreatedByUserId\" IS NOT NULL OR \"Visibility\" = 'public'");
                t.HasCheckConstraint("ck_producers_status",
                    "\"Status\" IN ('active','merged')");
                // status and redirect target come and go together.
                t.HasCheckConstraint("ck_producers_merge_pairing",
                    "(\"Status\" = 'merged') = (\"MergedIntoProducerId\" IS NOT NULL)");
                t.HasCheckConstraint("ck_producers_no_self_merge",
                    "\"MergedIntoProducerId\" IS NULL OR \"MergedIntoProducerId\" <> \"Id\"");
                t.HasCheckConstraint("ck_producers_type",
                    "\"ProducerType\" IS NULL OR \"ProducerType\" IN ('brewery','distillery','winery','cidery','meadery','multi','other')");
            });
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(256).IsRequired();
            e.Property(p => p.NormalizedName).HasMaxLength(256).IsRequired();
            e.Property(p => p.ProducerType).HasMaxLength(32);
            e.Property(p => p.Country).HasMaxLength(64);
            e.Property(p => p.Region).HasMaxLength(64);
            e.Property(p => p.City).HasMaxLength(128);
            e.Property(p => p.Source).HasMaxLength(64).IsRequired();
            e.Property(p => p.SourceRef).HasMaxLength(128);
            e.Property(p => p.Visibility).HasMaxLength(16);
            e.Property(p => p.Status).HasMaxLength(16);

            e.HasOne(p => p.CreatedBy).WithMany()
                .HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.MergedInto).WithMany()
                .HasForeignKey(p => p.MergedIntoProducerId).OnDelete(DeleteBehavior.Restrict);

            // Fuzzy search / dedup candidates.
            e.HasIndex(p => p.NormalizedName).HasMethod("gin").HasOperators("gin_trgm_ops")
                .HasDatabaseName("ix_producers_normalized_name_trgm");
            // Idempotent re-import: one row per upstream record per source.
            e.HasIndex(p => new { p.Source, p.SourceRef }).IsUnique()
                .HasFilter("\"SourceRef\" IS NOT NULL");
        });

        modelBuilder.Entity<Style>(e =>
        {
            e.ToTable("styles", t =>
            {
                t.HasCheckConstraint("ck_styles_category", categoryCheck);
                t.HasCheckConstraint("ck_styles_abv_range",
                    "(\"AbvMin\" IS NULL OR \"AbvMin\" >= 0) AND (\"AbvMin\" IS NULL OR \"AbvMax\" IS NULL OR \"AbvMin\" <= \"AbvMax\")");
                t.HasCheckConstraint("ck_styles_ibu_range",
                    "(\"IbuMin\" IS NULL OR \"IbuMin\" >= 0) AND (\"IbuMin\" IS NULL OR \"IbuMax\" IS NULL OR \"IbuMin\" <= \"IbuMax\")");
                t.HasCheckConstraint("ck_styles_srm_range",
                    "(\"SrmMin\" IS NULL OR \"SrmMin\" >= 0) AND (\"SrmMin\" IS NULL OR \"SrmMax\" IS NULL OR \"SrmMin\" <= \"SrmMax\")");
                t.HasCheckConstraint("ck_styles_og_range",
                    "(\"OgMin\" IS NULL OR \"OgMax\" IS NULL OR \"OgMin\" <= \"OgMax\")");
                t.HasCheckConstraint("ck_styles_fg_range",
                    "(\"FgMin\" IS NULL OR \"FgMax\" IS NULL OR \"FgMin\" <= \"FgMax\")");
            });
            e.HasKey(s => s.Id);
            e.Property(s => s.Category).HasMaxLength(16).IsRequired();
            e.Property(s => s.Code).HasMaxLength(16);
            e.Property(s => s.Name).HasMaxLength(128).IsRequired();
            e.Property(s => s.NormalizedName).HasMaxLength(128).IsRequired();
            e.Property(s => s.Source).HasMaxLength(64).IsRequired();
            e.Property(s => s.AbvMin).HasPrecision(4, 1);
            e.Property(s => s.AbvMax).HasPrecision(4, 1);
            e.Property(s => s.SrmMin).HasPrecision(5, 1);
            e.Property(s => s.SrmMax).HasPrecision(5, 1);
            e.Property(s => s.OgMin).HasPrecision(5, 3);
            e.Property(s => s.OgMax).HasPrecision(5, 3);
            e.Property(s => s.FgMin).HasPrecision(5, 3);
            e.Property(s => s.FgMax).HasPrecision(5, 3);
            e.Property(s => s.CategoryVector).HasColumnType("vector(8)");
            e.Property(s => s.BridgeVector).HasColumnType("vector(6)");

            e.HasOne(s => s.Parent).WithMany(s => s.Children)
                .HasForeignKey(s => s.ParentStyleId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(s => new { s.Category, s.Code }).IsUnique()
                .HasFilter("\"Code\" IS NOT NULL");
            // NOT unique: BJCP legitimately reuses a name across tree levels
            // (category 29 "Fruit Beer" contains substyle 29A "Fruit Beer").
            // Code is the unique handle; this index only serves name lookups.
            e.HasIndex(s => new { s.Category, s.NormalizedName });
        });

        modelBuilder.Entity<AttributeDefinition>(e =>
        {
            e.ToTable("attribute_definitions", t =>
            {
                t.HasCheckConstraint("ck_attribute_definitions_category", categoryCheck);
                t.HasCheckConstraint("ck_attribute_definitions_dim_index",
                    "\"DimIndex\" >= 0 AND \"DimIndex\" < 8");
                t.HasCheckConstraint("ck_attribute_definitions_bridge_index",
                    "\"BridgeIndex\" IS NULL OR (\"BridgeIndex\" >= 0 AND \"BridgeIndex\" < 6)");
            });
            e.HasKey(a => a.Key);
            e.Property(a => a.Key).HasMaxLength(64);
            e.Property(a => a.Category).HasMaxLength(16).IsRequired();
            e.Property(a => a.DisplayName).HasMaxLength(64).IsRequired();
            e.Property(a => a.Description).HasMaxLength(512);

            // Vector positions are unique per category — the geometry contract.
            e.HasIndex(a => new { a.Category, a.DimIndex }).IsUnique();
            e.HasIndex(a => new { a.Category, a.BridgeIndex }).IsUnique()
                .HasFilter("\"BridgeIndex\" IS NOT NULL");
        });

        modelBuilder.Entity<StyleAttributeValue>(e =>
        {
            e.ToTable("style_attributes", t =>
            {
                t.HasCheckConstraint("ck_style_attributes_value",
                    "\"Value\" >= 0 AND \"Value\" <= 1");
            });
            e.HasKey(sa => new { sa.StyleId, sa.AttributeKey });
            e.Property(sa => sa.AttributeKey).HasMaxLength(64);
            e.HasOne(sa => sa.Style).WithMany(s => s.Attributes)
                .HasForeignKey(sa => sa.StyleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(sa => sa.Attribute).WithMany()
                .HasForeignKey(sa => sa.AttributeKey).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Drink>(e =>
        {
            e.ToTable("drinks", t =>
            {
                t.HasCheckConstraint("ck_drinks_category", categoryCheck);
                t.HasCheckConstraint("ck_drinks_visibility",
                    "\"Visibility\" IN ('public','private')");
                t.HasCheckConstraint("ck_drinks_owner_visibility",
                    "\"CreatedByUserId\" IS NOT NULL OR \"Visibility\" = 'public'");
                t.HasCheckConstraint("ck_drinks_status",
                    "\"Status\" IN ('active','merged')");
                t.HasCheckConstraint("ck_drinks_merge_pairing",
                    "(\"Status\" = 'merged') = (\"MergedIntoDrinkId\" IS NOT NULL)");
                t.HasCheckConstraint("ck_drinks_no_self_merge",
                    "\"MergedIntoDrinkId\" IS NULL OR \"MergedIntoDrinkId\" <> \"Id\"");
                t.HasCheckConstraint("ck_drinks_abv",
                    "\"Abv\" IS NULL OR (\"Abv\" >= 0 AND \"Abv\" <= 100)");
            });
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(256).IsRequired();
            e.Property(d => d.NormalizedName).HasMaxLength(256).IsRequired();
            e.Property(d => d.Category).HasMaxLength(16).IsRequired();
            e.Property(d => d.Abv).HasPrecision(4, 1);
            e.Property(d => d.Source).HasMaxLength(64).IsRequired();
            e.Property(d => d.SourceRef).HasMaxLength(128);
            e.Property(d => d.Visibility).HasMaxLength(16);
            e.Property(d => d.Status).HasMaxLength(16);
            e.Property(d => d.CategoryVector).HasColumnType("vector(8)");
            e.Property(d => d.BridgeVector).HasColumnType("vector(6)");

            e.HasOne(d => d.Producer).WithMany(p => p.Drinks)
                .HasForeignKey(d => d.ProducerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.CreatedBy).WithMany()
                .HasForeignKey(d => d.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

            // Category coherence IN THE DATABASE: a drink can only reference a
            // style of its own category, and can only merge into a drink of its
            // own category (composite FKs against (Id, Category)).
            e.HasOne(d => d.Style).WithMany(s => s.Drinks)
                .HasForeignKey(d => new { d.StyleId, d.Category })
                .HasPrincipalKey(s => new { s.Id, s.Category })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.MergedInto).WithMany()
                .HasForeignKey(d => new { d.MergedIntoDrinkId, d.Category })
                .HasPrincipalKey(d => new { d.Id, d.Category })
                .OnDelete(DeleteBehavior.Restrict);

            // Canonical identity (ADR-008): active drinks are unique per
            // (producer, category, normalized name); merged redirects keep
            // their name without blocking the survivor.
            e.HasIndex(d => new { d.ProducerId, d.Category, d.NormalizedName }).IsUnique()
                .HasFilter("\"Status\" = 'active'")
                .HasDatabaseName("ux_drinks_canonical_identity");

            e.HasIndex(d => d.NormalizedName).HasMethod("gin").HasOperators("gin_trgm_ops")
                .HasDatabaseName("ix_drinks_normalized_name_trgm");
            e.HasIndex(d => new { d.Category, d.StyleId });
            e.HasIndex(d => new { d.Source, d.SourceRef }).IsUnique()
                .HasFilter("\"SourceRef\" IS NOT NULL");

            // HNSW cosine indexes for attribute-similarity recs (ADR-025).
            e.HasIndex(d => d.CategoryVector).HasMethod("hnsw").HasOperators("vector_cosine_ops")
                .HasDatabaseName("ix_drinks_category_vector_hnsw");
            e.HasIndex(d => d.BridgeVector).HasMethod("hnsw").HasOperators("vector_cosine_ops")
                .HasDatabaseName("ix_drinks_bridge_vector_hnsw");
        });

        modelBuilder.Entity<DrinkAttributeValue>(e =>
        {
            e.ToTable("drink_attributes", t =>
            {
                t.HasCheckConstraint("ck_drink_attributes_value",
                    "\"Value\" >= 0 AND \"Value\" <= 1");
                t.HasCheckConstraint("ck_drink_attributes_confidence",
                    "\"Confidence\" >= 0 AND \"Confidence\" <= 1");
                t.HasCheckConstraint("ck_drink_attributes_source",
                    "\"Source\" IN ('inherited','manufacturer','crowd','llm','moderator')");
            });
            e.HasKey(da => new { da.DrinkId, da.AttributeKey });
            e.Property(da => da.AttributeKey).HasMaxLength(64);
            e.Property(da => da.Source).HasMaxLength(16).IsRequired();
            e.HasOne(da => da.Drink).WithMany(d => d.Attributes)
                .HasForeignKey(da => da.DrinkId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(da => da.Attribute).WithMany()
                .HasForeignKey(da => da.AttributeKey).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MergeCandidate>(e =>
        {
            e.ToTable("merge_queue", t =>
            {
                t.HasCheckConstraint("ck_merge_queue_entity_type",
                    "\"EntityType\" IN ('producer','drink')");
                t.HasCheckConstraint("ck_merge_queue_status",
                    "\"Status\" IN ('pending','approved','rejected')");
                t.HasCheckConstraint("ck_merge_queue_similarity",
                    "\"Similarity\" >= 0 AND \"Similarity\" <= 1");
                // Exactly the FK pair matching EntityType is populated.
                t.HasCheckConstraint("ck_merge_queue_typed_pair",
                    "(\"EntityType\" = 'producer' AND \"SourceProducerId\" IS NOT NULL AND \"TargetProducerId\" IS NOT NULL AND \"SourceDrinkId\" IS NULL AND \"TargetDrinkId\" IS NULL)" +
                    " OR (\"EntityType\" = 'drink' AND \"SourceDrinkId\" IS NOT NULL AND \"TargetDrinkId\" IS NOT NULL AND \"SourceProducerId\" IS NULL AND \"TargetProducerId\" IS NULL)");
                t.HasCheckConstraint("ck_merge_queue_distinct_pair",
                    "(\"SourceProducerId\" IS NULL OR \"SourceProducerId\" <> \"TargetProducerId\")" +
                    " AND (\"SourceDrinkId\" IS NULL OR \"SourceDrinkId\" <> \"TargetDrinkId\")");
                t.HasCheckConstraint("ck_merge_queue_decision",
                    "(\"Status\" = 'pending') = (\"DecidedAt\" IS NULL)");
            });
            e.HasKey(m => m.Id);
            e.Property(m => m.EntityType).HasMaxLength(16).IsRequired();
            e.Property(m => m.Status).HasMaxLength(16);
            e.Property(m => m.Reason).HasMaxLength(256);
            e.Property(m => m.DecidedBy).HasMaxLength(64);

            e.HasOne(m => m.SourceProducer).WithMany()
                .HasForeignKey(m => m.SourceProducerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.TargetProducer).WithMany()
                .HasForeignKey(m => m.TargetProducerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.SourceDrink).WithMany()
                .HasForeignKey(m => m.SourceDrinkId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.TargetDrink).WithMany()
                .HasForeignKey(m => m.TargetDrinkId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(m => new { m.Status, m.CreatedAt });
            // One PENDING candidate per exact pair (generator canonicalizes
            // pair order, so symmetric dupes don't arise).
            e.HasIndex(m => new { m.EntityType, m.SourceProducerId, m.TargetProducerId, m.SourceDrinkId, m.TargetDrinkId })
                .IsUnique()
                .HasFilter("\"Status\" = 'pending'")
                .HasDatabaseName("ux_merge_queue_pending_pair");
        });
    }
}
