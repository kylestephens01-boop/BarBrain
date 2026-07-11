using BarBrain.Api.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Data;

/// <summary>
/// The single application DbContext. Sprint 0 shipped the foundation tables
/// (settings, events); Sprint 1 added the catalog schema (docs/SCHEMA.md is
/// the annotated ERD); Sprint 2 extends the users stub into the ASP.NET
/// Identity user (same <c>users</c> table, additive columns) and adds venues
/// (stub) + ratings. Roles are deliberately absent — there is one founder
/// admin, gated by config, not a role system.
///
/// Constraint posture (ADR-026): closed vocabularies, value ranges, ownership/
/// visibility invariants, and category coherence are enforced HERE as CHECK
/// constraints and composite FKs — the database refuses bad rows even if app
/// code regresses.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<User, Guid>(options)
{
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<EventRecord> Events => Set<EventRecord>();

    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<VenueMenuItem> VenueMenuItems => Set<VenueMenuItem>();
    public DbSet<Checkin> Checkins => Set<Checkin>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<UserCategoryInterest> UserCategoryInterests => Set<UserCategoryInterest>();
    public DbSet<UserPalateProfile> UserPalateProfiles => Set<UserPalateProfile>();
    public DbSet<UserMatchNeighbor> UserMatchNeighbors => Set<UserMatchNeighbor>();
    public DbSet<Producer> Producers => Set<Producer>();
    public DbSet<Style> Styles => Set<Style>();
    public DbSet<AttributeDefinition> AttributeDefinitions => Set<AttributeDefinition>();
    public DbSet<StyleAttributeValue> StyleAttributes => Set<StyleAttributeValue>();
    public DbSet<Drink> Drinks => Set<Drink>();
    public DbSet<DrinkAttributeValue> DrinkAttributes => Set<DrinkAttributeValue>();
    public DbSet<MergeCandidate> MergeQueue => Set<MergeCandidate>();
    public DbSet<BadgeDefinition> BadgeDefinitions => Set<BadgeDefinition>();
    public DbSet<UserBadge> UserBadges => Set<UserBadge>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<AnomalyFlag> AnomalyFlags => Set<AnomalyFlag>();
    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();

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
            e.ToTable("users", t =>
            {
                // Handles are canonically lowercase; the endpoint normalizes,
                // the database refuses anything else.
                t.HasCheckConstraint("ck_users_handle_lowercase",
                    "\"Handle\" IS NULL OR \"Handle\" = lower(\"Handle\")");
                t.HasCheckConstraint("ck_users_birth_year",
                    "\"BirthYear\" IS NULL OR (\"BirthYear\" >= 1900 AND \"BirthYear\" <= 2100)");
                // An activated account has always passed the 21+ gate and
                // claimed a handle (ADR-010/011).
                t.HasCheckConstraint("ck_users_activation_requires_gate",
                    "\"ActivatedAt\" IS NULL OR (\"BirthYear\" IS NOT NULL AND \"AttestedAt\" IS NOT NULL AND \"Handle\" IS NOT NULL)");
                // Deletion request comes as a pair with a closed mode
                // vocabulary (Sprint 7, ADR-018).
                t.HasCheckConstraint("ck_users_deletion_pairing",
                    "(\"DeletionRequestedAt\" IS NULL) = (\"DeletionMode\" IS NULL)");
                t.HasCheckConstraint("ck_users_deletion_mode",
                    "\"DeletionMode\" IS NULL OR \"DeletionMode\" IN ('delete','anonymize')");
            });
            // Identity's UserName IS the pseudonymous handle — mapped onto
            // Sprint 1's Handle column so the extension is purely additive.
            e.Property(u => u.UserName).HasColumnName("Handle").HasMaxLength(32);
            e.Property(u => u.NormalizedUserName).HasMaxLength(32);
            e.Property(u => u.Email).HasMaxLength(256);
            e.Property(u => u.NormalizedEmail).HasMaxLength(256);
            e.HasIndex(u => u.UserName).IsUnique().HasDatabaseName("IX_users_Handle");
            // The unsubscribe link looks the user up by this token alone (no
            // session), so it must be unique and indexed.
            e.HasIndex(u => u.DigestUnsubscribeToken).IsUnique()
                .HasDatabaseName("ux_users_digest_unsub_token");
            // DB-level email uniqueness (Identity's own EmailIndex is not unique;
            // UserManager's check alone has a race window).
            e.HasIndex(u => u.NormalizedEmail).IsUnique()
                .HasFilter("\"NormalizedEmail\" IS NOT NULL")
                .HasDatabaseName("ux_users_normalized_email");
            // We never collect phone numbers and don't run SMS 2FA — keep the
            // columns out of the schema entirely (privacy posture, Hard Rules).
            e.Ignore(u => u.PhoneNumber);
            e.Ignore(u => u.PhoneNumberConfirmed);
            e.Ignore(u => u.TwoFactorEnabled);
            e.Property(u => u.ModerationNote).HasMaxLength(256);
            e.Property(u => u.DeletionMode).HasMaxLength(16);
        });

        // Identity link tables, renamed to match the schema's naming style.
        // No roles tables — this context is IdentityUserContext on purpose.
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        modelBuilder.Entity<Venue>(e =>
        {
            e.ToTable("venues", t =>
            {
                t.HasCheckConstraint("ck_venues_type",
                    "\"VenueType\" IN ('home_bar','venue')");
                t.HasCheckConstraint("ck_venues_visibility",
                    "\"Visibility\" IN ('public','private')");
                t.HasCheckConstraint("ck_venues_owner_visibility",
                    "\"OwnerUserId\" IS NOT NULL OR \"Visibility\" = 'public'");
                // A Home Bar is always owned and always private (ADR-015) —
                // it can never leak into discovery.
                t.HasCheckConstraint("ck_venues_home_bar_private",
                    "\"VenueType\" <> 'home_bar' OR (\"OwnerUserId\" IS NOT NULL AND \"Visibility\" = 'private')");
                // Tier exists exactly for public venues (wiki default;
                // verified is admin-set — no billing anywhere, Sprint 5 spec).
                t.HasCheckConstraint("ck_venues_tier",
                    "\"Tier\" IS NULL OR \"Tier\" IN ('wiki','verified')");
                t.HasCheckConstraint("ck_venues_tier_pairing",
                    "(\"VenueType\" = 'venue') = (\"Tier\" IS NOT NULL)");
                // Geo comes as a pair, in range — and NEVER on a Home Bar
                // (it would be the user's home coordinates; privacy posture).
                t.HasCheckConstraint("ck_venues_geo_pairing",
                    "(\"Latitude\" IS NULL) = (\"Longitude\" IS NULL)");
                t.HasCheckConstraint("ck_venues_geo_range",
                    "(\"Latitude\" IS NULL OR (\"Latitude\" >= -90 AND \"Latitude\" <= 90))" +
                    " AND (\"Longitude\" IS NULL OR (\"Longitude\" >= -180 AND \"Longitude\" <= 180))");
                t.HasCheckConstraint("ck_venues_home_bar_no_geo",
                    "\"VenueType\" <> 'home_bar' OR (\"Latitude\" IS NULL AND \"Address\" IS NULL)");
                // Merge lifecycle mirrors producers; Home Bars never merge.
                t.HasCheckConstraint("ck_venues_status",
                    "\"Status\" IN ('active','merged')");
                t.HasCheckConstraint("ck_venues_merge_pairing",
                    "(\"Status\" = 'merged') = (\"MergedIntoVenueId\" IS NOT NULL)");
                t.HasCheckConstraint("ck_venues_no_self_merge",
                    "\"MergedIntoVenueId\" IS NULL OR \"MergedIntoVenueId\" <> \"Id\"");
                t.HasCheckConstraint("ck_venues_home_bar_never_merged",
                    "\"VenueType\" <> 'home_bar' OR \"Status\" = 'active'");
                t.HasCheckConstraint("ck_venues_hidden_pairing",
                    "(\"HiddenAt\" IS NULL) = (\"HiddenBy\" IS NULL)");
            });
            e.HasKey(v => v.Id);
            e.Property(v => v.Name).HasMaxLength(128).IsRequired();
            e.Property(v => v.NormalizedName).HasMaxLength(128).IsRequired();
            e.Property(v => v.VenueType).HasMaxLength(16).IsRequired();
            e.Property(v => v.Tier).HasMaxLength(16);
            e.Property(v => v.Address).HasMaxLength(256);
            e.Property(v => v.Hours).HasMaxLength(256);
            e.Property(v => v.Visibility).HasMaxLength(16);
            e.Property(v => v.Status).HasMaxLength(16);
            e.Property(v => v.HiddenBy).HasMaxLength(64);
            e.HasOne(v => v.Owner).WithMany()
                .HasForeignKey(v => v.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.CreatedBy).WithMany()
                .HasForeignKey(v => v.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.MergedInto).WithMany()
                .HasForeignKey(v => v.MergedIntoVenueId).OnDelete(DeleteBehavior.Restrict);
            // Exactly one Home Bar per user, guaranteed by the database.
            e.HasIndex(v => v.OwnerUserId).IsUnique()
                .HasFilter("\"VenueType\" = 'home_bar'")
                .HasDatabaseName("ux_venues_one_home_bar_per_user");
            // Fuzzy dedupe/search over PUBLIC venues only — thousands of
            // identically-named Home Bars would be pure trigram noise.
            e.HasIndex(v => v.NormalizedName).HasMethod("gin").HasOperators("gin_trgm_ops")
                .HasFilter("\"VenueType\" = 'venue'")
                .HasDatabaseName("ix_venues_normalized_name_trgm");
        });

        modelBuilder.Entity<VenueMenuItem>(e =>
        {
            e.ToTable("venue_menu_items", t =>
            {
                t.HasCheckConstraint("ck_venue_menu_items_source",
                    "\"Source\" IN ('crowd','venue')");
                t.HasCheckConstraint("ck_venue_menu_items_price",
                    "\"Price\" IS NULL OR \"Price\" >= 0");
                t.HasCheckConstraint("ck_venue_menu_items_visibility",
                    "\"Visibility\" IN ('public','private')");
                t.HasCheckConstraint("ck_venue_menu_items_owner_visibility",
                    "\"CreatedByUserId\" IS NOT NULL OR \"Visibility\" = 'public'");
            });
            e.HasKey(mi => mi.Id);
            e.Property(mi => mi.Price).HasPrecision(6, 2);
            e.Property(mi => mi.Source).HasMaxLength(16).IsRequired();
            e.Property(mi => mi.Visibility).HasMaxLength(16);
            // Menu rows die with their venue; drinks in use can't be deleted.
            e.HasOne(mi => mi.Venue).WithMany()
                .HasForeignKey(mi => mi.VenueId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(mi => mi.Drink).WithMany()
                .HasForeignKey(mi => mi.DrinkId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(mi => mi.CreatedBy).WithMany()
                .HasForeignKey(mi => mi.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            // One menu row per (venue, drink); venue merges dedupe on move.
            e.HasIndex(mi => new { mi.VenueId, mi.DrinkId }).IsUnique()
                .HasDatabaseName("ux_venue_menu_items_venue_drink");
            e.HasIndex(mi => mi.DrinkId);
        });

        modelBuilder.Entity<Checkin>(e =>
        {
            e.ToTable("checkins", t =>
            {
                t.HasCheckConstraint("ck_checkins_window",
                    "\"ExpiresAt\" > \"CreatedAt\"");
                t.HasCheckConstraint("ck_checkins_ended",
                    "\"EndedAt\" IS NULL OR \"EndedAt\" >= \"CreatedAt\"");
            });
            e.HasKey(c => c.Id);
            e.HasOne(c => c.User).WithMany()
                .HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Venue).WithMany()
                .HasForeignKey(c => c.VenueId).OnDelete(DeleteBehavior.Restrict);
            // At most one OPEN check-in per user (ADR-015: check-in is THE
            // session primitive) — the service ends the old one first, the
            // database refuses a second either way.
            e.HasIndex(c => c.UserId).IsUnique()
                .HasFilter("\"EndedAt\" IS NULL")
                .HasDatabaseName("ux_checkins_one_open_per_user");
            // Venue recent activity, newest first.
            e.HasIndex(c => new { c.VenueId, c.CreatedAt })
                .HasDatabaseName("ix_checkins_venue_recent");
        });

        modelBuilder.Entity<Rating>(e =>
        {
            e.ToTable("ratings", t =>
            {
                // 1.0–5.0 in half-star steps: doubling must land on an integer.
                t.HasCheckConstraint("ck_ratings_value",
                    "\"Value\" >= 1.0 AND \"Value\" <= 5.0 AND (\"Value\" * 2) = floor(\"Value\" * 2)");
                t.HasCheckConstraint("ck_ratings_visibility",
                    "\"Visibility\" IN ('public','private')");
                t.HasCheckConstraint("ck_ratings_location_context",
                    "\"LocationContext\" IN ('home_bar','venue','untagged')");
                // Venue ref present exactly when the context says so.
                t.HasCheckConstraint("ck_ratings_venue_pairing",
                    "(\"LocationContext\" = 'untagged') = (\"VenueId\" IS NULL)");
                // Quiz ratings are real ratings with provenance (Sprint 3).
                t.HasCheckConstraint("ck_ratings_origin",
                    "\"Origin\" IN ('user','quiz')");
                // Feed-section provenance for exploration badges (Sprint 6).
                // Values = the feed's own section keys (one vocabulary).
                t.HasCheckConstraint("ck_ratings_rec_section",
                    "\"RecSection\" IS NULL OR \"RecSection\" IN ('up_your_alley','stretch_a_little','wildcard','loved_by_your_matches')");
                // Moderation hide comes as a pair (Sprint 6).
                t.HasCheckConstraint("ck_ratings_hidden_pairing",
                    "(\"HiddenAt\" IS NULL) = (\"HiddenBy\" IS NULL)");
            });
            e.HasKey(r => r.Id);
            e.Property(r => r.Value).HasPrecision(2, 1);
            e.Property(r => r.Note).HasMaxLength(500);
            e.Property(r => r.Visibility).HasMaxLength(16);
            e.Property(r => r.LocationContext).HasMaxLength(16).IsRequired();
            e.Property(r => r.Origin).HasMaxLength(16);
            e.Property(r => r.RecSection).HasMaxLength(24);
            e.Property(r => r.HiddenBy).HasMaxLength(64);
            e.HasOne(r => r.CreatedBy).WithMany()
                .HasForeignKey(r => r.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Drink).WithMany()
                .HasForeignKey(r => r.DrinkId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Venue).WithMany()
                .HasForeignKey(r => r.VenueId).OnDelete(DeleteBehavior.Restrict);
            // Single-latest invariant (ADR-012), enforced by the database.
            e.HasIndex(r => new { r.CreatedByUserId, r.DrinkId }).IsUnique()
                .HasFilter("\"IsLatest\"")
                .HasDatabaseName("ux_ratings_latest_per_user_drink");
            // Drink-page reads: latest public ratings for a drink.
            e.HasIndex(r => new { r.DrinkId, r.Visibility, r.IsLatest });
            // Journal reads: my ratings, newest first.
            e.HasIndex(r => new { r.CreatedByUserId, r.CreatedAt })
                .HasDatabaseName("ix_ratings_journal");
        });

        modelBuilder.Entity<UserCategoryInterest>(e =>
        {
            e.ToTable("user_category_interests", t =>
            {
                t.HasCheckConstraint("ck_user_category_interests_category", categoryCheck);
            });
            e.HasKey(i => new { i.UserId, i.Category });
            e.Property(i => i.Category).HasMaxLength(16);
            e.HasOne(i => i.User).WithMany()
                .HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserPalateProfile>(e =>
        {
            e.ToTable("user_palate_profiles", t =>
            {
                t.HasCheckConstraint("ck_user_palate_profiles_category", categoryCheck);
                t.HasCheckConstraint("ck_user_palate_profiles_ratings_count",
                    "\"RatingsCount\" >= 0");
            });
            e.HasKey(p => new { p.UserId, p.Category });
            e.Property(p => p.Category).HasMaxLength(16);
            e.Property(p => p.PreferenceVector).HasColumnType("vector(8)");
            e.Property(p => p.CentroidVector).HasColumnType("vector(8)");
            e.Property(p => p.BridgeVector).HasColumnType("vector(6)");
            e.HasOne(p => p.User).WithMany()
                .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserMatchNeighbor>(e =>
        {
            e.ToTable("user_match_neighbors", t =>
            {
                t.HasCheckConstraint("ck_user_match_neighbors_category", categoryCheck);
                // A user is never their own match.
                t.HasCheckConstraint("ck_user_match_neighbors_no_self",
                    "\"UserId\" <> \"NeighborUserId\"");
                t.HasCheckConstraint("ck_user_match_neighbors_corated",
                    "\"CoRatedCount\" >= 0");
                // The blended score is a similarity in [0, 1].
                t.HasCheckConstraint("ck_user_match_neighbors_score",
                    "\"BlendedScore\" >= 0 AND \"BlendedScore\" <= 1");
            });
            e.HasKey(m => new { m.UserId, m.NeighborUserId, m.Category });
            e.Property(m => m.Category).HasMaxLength(16);
            e.HasOne(m => m.User).WithMany()
                .HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            // Cascade only from one side (User); the neighbor FK restricts so a
            // single user deletion doesn't try to multi-cascade the same rows.
            e.HasOne(m => m.Neighbor).WithMany()
                .HasForeignKey(m => m.NeighborUserId).OnDelete(DeleteBehavior.Restrict);
            // "Who matches me", strongest first.
            e.HasIndex(m => new { m.UserId, m.BlendedScore })
                .HasDatabaseName("ix_user_match_neighbors_user_score");
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
                t.HasCheckConstraint("ck_drinks_hidden_pairing",
                    "(\"HiddenAt\" IS NULL) = (\"HiddenBy\" IS NULL)");
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
            e.Property(d => d.HiddenBy).HasMaxLength(64);
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
                    "\"EntityType\" IN ('producer','drink','venue')");
                t.HasCheckConstraint("ck_merge_queue_status",
                    "\"Status\" IN ('pending','approved','rejected')");
                t.HasCheckConstraint("ck_merge_queue_similarity",
                    "\"Similarity\" >= 0 AND \"Similarity\" <= 1");
                // Exactly the FK pair matching EntityType is populated.
                t.HasCheckConstraint("ck_merge_queue_typed_pair",
                    "(\"EntityType\" = 'producer' AND \"SourceProducerId\" IS NOT NULL AND \"TargetProducerId\" IS NOT NULL AND \"SourceDrinkId\" IS NULL AND \"TargetDrinkId\" IS NULL AND \"SourceVenueId\" IS NULL AND \"TargetVenueId\" IS NULL)" +
                    " OR (\"EntityType\" = 'drink' AND \"SourceDrinkId\" IS NOT NULL AND \"TargetDrinkId\" IS NOT NULL AND \"SourceProducerId\" IS NULL AND \"TargetProducerId\" IS NULL AND \"SourceVenueId\" IS NULL AND \"TargetVenueId\" IS NULL)" +
                    " OR (\"EntityType\" = 'venue' AND \"SourceVenueId\" IS NOT NULL AND \"TargetVenueId\" IS NOT NULL AND \"SourceProducerId\" IS NULL AND \"TargetProducerId\" IS NULL AND \"SourceDrinkId\" IS NULL AND \"TargetDrinkId\" IS NULL)");
                t.HasCheckConstraint("ck_merge_queue_distinct_pair",
                    "(\"SourceProducerId\" IS NULL OR \"SourceProducerId\" <> \"TargetProducerId\")" +
                    " AND (\"SourceDrinkId\" IS NULL OR \"SourceDrinkId\" <> \"TargetDrinkId\")" +
                    " AND (\"SourceVenueId\" IS NULL OR \"SourceVenueId\" <> \"TargetVenueId\")");
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
            e.HasOne(m => m.SourceVenue).WithMany()
                .HasForeignKey(m => m.SourceVenueId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.TargetVenue).WithMany()
                .HasForeignKey(m => m.TargetVenueId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(m => new { m.Status, m.CreatedAt });
            // One PENDING candidate per exact pair (generator canonicalizes
            // pair order, so symmetric dupes don't arise).
            e.HasIndex(m => new { m.EntityType, m.SourceProducerId, m.TargetProducerId, m.SourceDrinkId, m.TargetDrinkId, m.SourceVenueId, m.TargetVenueId })
                .IsUnique()
                .HasFilter("\"Status\" = 'pending'")
                .HasDatabaseName("ux_merge_queue_pending_pair");
        });

        modelBuilder.Entity<BadgeDefinition>(e =>
        {
            e.ToTable("badge_definitions", t =>
            {
                t.HasCheckConstraint("ck_badge_definitions_group",
                    "\"DisplayGroup\" IN ('breadth','exploration','venues','contribution','streak')");
                // Every metric is a distinct-entity or weekly-streak count —
                // the vocabulary itself is the ADR-016 guardrail: there is no
                // way to express a volume/frequency criterion.
                t.HasCheckConstraint("ck_badge_definitions_metric",
                    "\"Metric\" IN ('distinct_styles_rated','distinct_categories_rated','wildcard_distinct_drinks','distinct_venues_checked_in','wiki_contributions','menu_confirms','accepted_merge_contributions','weekly_streak_weeks')");
                t.HasCheckConstraint("ck_badge_definitions_threshold",
                    "\"Threshold\" >= 1");
            });
            e.HasKey(b => b.Slug);
            e.Property(b => b.Slug).HasMaxLength(64);
            e.Property(b => b.Name).HasMaxLength(64).IsRequired();
            e.Property(b => b.Description).HasMaxLength(256).IsRequired();
            e.Property(b => b.Icon).HasMaxLength(32).IsRequired();
            e.Property(b => b.DisplayGroup).HasMaxLength(16).IsRequired();
            e.Property(b => b.Metric).HasMaxLength(48).IsRequired();
        });

        modelBuilder.Entity<UserBadge>(e =>
        {
            e.ToTable("user_badges");
            e.HasKey(ub => ub.Id);
            e.Property(ub => ub.BadgeSlug).HasMaxLength(64);
            e.HasOne(ub => ub.User).WithMany()
                .HasForeignKey(ub => ub.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ub => ub.Badge).WithMany()
                .HasForeignKey(ub => ub.BadgeSlug).OnDelete(DeleteBehavior.Restrict);
            // Permanent, never duplicated — the evaluator can re-run freely.
            e.HasIndex(ub => new { ub.UserId, ub.BadgeSlug }).IsUnique()
                .HasDatabaseName("ux_user_badges_user_badge");
            // Toast polling: the unseen slice per user.
            e.HasIndex(ub => ub.UserId)
                .HasFilter("\"SeenAt\" IS NULL")
                .HasDatabaseName("ix_user_badges_unseen");
        });

        modelBuilder.Entity<Report>(e =>
        {
            e.ToTable("reports", t =>
            {
                t.HasCheckConstraint("ck_reports_entity_type",
                    "\"EntityType\" IN ('rating','venue','drink')");
                t.HasCheckConstraint("ck_reports_reason",
                    "\"Reason\" IN ('inaccurate','spam','offensive','other')");
                t.HasCheckConstraint("ck_reports_status",
                    "\"Status\" IN ('open','actioned','dismissed')");
                // Exactly the FK matching EntityType is populated (merge_queue pattern).
                t.HasCheckConstraint("ck_reports_typed_target",
                    "(\"EntityType\" = 'rating' AND \"RatingId\" IS NOT NULL AND \"VenueId\" IS NULL AND \"DrinkId\" IS NULL)" +
                    " OR (\"EntityType\" = 'venue' AND \"VenueId\" IS NOT NULL AND \"RatingId\" IS NULL AND \"DrinkId\" IS NULL)" +
                    " OR (\"EntityType\" = 'drink' AND \"DrinkId\" IS NOT NULL AND \"RatingId\" IS NULL AND \"VenueId\" IS NULL)");
                t.HasCheckConstraint("ck_reports_decision",
                    "(\"Status\" = 'open') = (\"DecidedAt\" IS NULL)");
            });
            e.HasKey(r => r.Id);
            e.Property(r => r.EntityType).HasMaxLength(16).IsRequired();
            e.Property(r => r.Reason).HasMaxLength(16).IsRequired();
            e.Property(r => r.Note).HasMaxLength(500);
            e.Property(r => r.Status).HasMaxLength(16);
            e.Property(r => r.DecidedBy).HasMaxLength(64);
            // Reports die with their target (the merge_queue convention);
            // the audit log is what survives deletion.
            e.HasOne(r => r.Rating).WithMany()
                .HasForeignKey(r => r.RatingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Venue).WithMany()
                .HasForeignKey(r => r.VenueId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Drink).WithMany()
                .HasForeignKey(r => r.DrinkId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Reporter).WithMany()
                .HasForeignKey(r => r.ReporterUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.Status, r.CreatedAt });
            // One OPEN report per (reporter, target) — the report button can't
            // be hammered into queue noise.
            e.HasIndex(r => new { r.ReporterUserId, r.RatingId, r.VenueId, r.DrinkId })
                .IsUnique()
                .HasFilter("\"Status\" = 'open'")
                .HasDatabaseName("ux_reports_open_per_reporter_target");
        });

        modelBuilder.Entity<AnomalyFlag>(e =>
        {
            e.ToTable("anomaly_flags", t =>
            {
                t.HasCheckConstraint("ck_anomaly_flags_kind",
                    "\"Kind\" IN ('rating_zscore_outlier','rapid_fire')");
                t.HasCheckConstraint("ck_anomaly_flags_status",
                    "\"Status\" IN ('open','cleared','actioned')");
                t.HasCheckConstraint("ck_anomaly_flags_decision",
                    "(\"Status\" = 'open') = (\"DecidedAt\" IS NULL)");
            });
            e.HasKey(a => a.Id);
            e.Property(a => a.Kind).HasMaxLength(32).IsRequired();
            e.Property(a => a.Evidence).HasMaxLength(512).IsRequired();
            e.Property(a => a.Status).HasMaxLength(16);
            e.Property(a => a.DecidedBy).HasMaxLength(64);
            e.HasOne(a => a.User).WithMany()
                .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.Status, a.Score });
            // One OPEN flag per (user, kind): nightly re-scans refresh evidence
            // in place instead of stacking duplicates.
            e.HasIndex(a => new { a.UserId, a.Kind }).IsUnique()
                .HasFilter("\"Status\" = 'open'")
                .HasDatabaseName("ux_anomaly_flags_open_per_user_kind");
        });

        modelBuilder.Entity<ModerationAction>(e =>
        {
            e.ToTable("moderation_actions", t =>
            {
                t.HasCheckConstraint("ck_moderation_actions_action",
                    "\"Action\" IN ('merge_approved','merge_rejected','report_actioned','report_dismissed','content_hidden','content_unhidden','shadow_limited','shadow_cleared','banned','unbanned','anomaly_cleared')");
            });
            e.HasKey(a => a.Id);
            e.Property(a => a.Actor).HasMaxLength(64).IsRequired();
            e.Property(a => a.Action).HasMaxLength(32).IsRequired();
            e.Property(a => a.EntityType).HasMaxLength(16);
            e.Property(a => a.Details).HasColumnType("jsonb");
            // Audit rows deliberately have NO FK — they must survive whatever
            // they describe. Queried newest-first and by target.
            e.HasIndex(a => a.CreatedAt);
            e.HasIndex(a => new { a.EntityType, a.EntityId });
        });
    }
}
