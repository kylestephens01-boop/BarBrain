using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarBrain.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sprint5Venues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_merge_queue_pending_pair",
                table: "merge_queue");

            migrationBuilder.DropCheckConstraint(
                name: "ck_merge_queue_distinct_pair",
                table: "merge_queue");

            migrationBuilder.DropCheckConstraint(
                name: "ck_merge_queue_entity_type",
                table: "merge_queue");

            migrationBuilder.DropCheckConstraint(
                name: "ck_merge_queue_typed_pair",
                table: "merge_queue");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "venues",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "venues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hours",
                table: "venues",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "venues",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "venues",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoVenueId",
                table: "venues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "venues",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            // Existing rows must land on a value the ck_venues_status CHECK
            // (added below) accepts — the scaffolded "" would fail the
            // previous sprint's schema.
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "venues",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "active");

            migrationBuilder.AddColumn<string>(
                name: "Tier",
                table: "venues",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            // Data op 1: backfill NormalizedName for pre-Sprint-5 rows (home
            // bars) before the trigram index exists.
            migrationBuilder.Sql(
                """UPDATE venues SET "NormalizedName" = lower("Name") WHERE "NormalizedName" = '';""");

            // Data op 2: Home Bar backfill (Sprint 5 spec / ADR-015). Every
            // user gets exactly one; activation has created them since Sprint
            // 2, so this only catches pre-activation-era stragglers.
            // Idempotent: guarded by NOT EXISTS + the partial unique index.
            migrationBuilder.Sql(
                """
                INSERT INTO venues ("Id", "Name", "NormalizedName", "VenueType", "Visibility", "OwnerUserId", "Status", "CreatedAt")
                SELECT gen_random_uuid(), 'Home Bar', 'home bar', 'home_bar', 'private', u."Id", 'active', now()
                FROM users u
                WHERE NOT EXISTS (
                    SELECT 1 FROM venues v
                    WHERE v."OwnerUserId" = u."Id" AND v."VenueType" = 'home_bar');
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceVenueId",
                table: "merge_queue",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetVenueId",
                table: "merge_queue",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "checkins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checkins", x => x.Id);
                    table.CheckConstraint("ck_checkins_ended", "\"EndedAt\" IS NULL OR \"EndedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("ck_checkins_window", "\"ExpiresAt\" > \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_checkins_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_checkins_venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "venue_menu_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                    DrinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    LastConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_venue_menu_items", x => x.Id);
                    table.CheckConstraint("ck_venue_menu_items_owner_visibility", "\"CreatedByUserId\" IS NOT NULL OR \"Visibility\" = 'public'");
                    table.CheckConstraint("ck_venue_menu_items_price", "\"Price\" IS NULL OR \"Price\" >= 0");
                    table.CheckConstraint("ck_venue_menu_items_source", "\"Source\" IN ('crowd','venue')");
                    table.CheckConstraint("ck_venue_menu_items_visibility", "\"Visibility\" IN ('public','private')");
                    table.ForeignKey(
                        name: "FK_venue_menu_items_drinks_DrinkId",
                        column: x => x.DrinkId,
                        principalTable: "drinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_venue_menu_items_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_venue_menu_items_venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_venues_CreatedByUserId",
                table: "venues",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_venues_MergedIntoVenueId",
                table: "venues",
                column: "MergedIntoVenueId");

            migrationBuilder.CreateIndex(
                name: "ix_venues_normalized_name_trgm",
                table: "venues",
                column: "NormalizedName",
                filter: "\"VenueType\" = 'venue'")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_venues_geo_pairing",
                table: "venues",
                sql: "(\"Latitude\" IS NULL) = (\"Longitude\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_venues_geo_range",
                table: "venues",
                sql: "(\"Latitude\" IS NULL OR (\"Latitude\" >= -90 AND \"Latitude\" <= 90)) AND (\"Longitude\" IS NULL OR (\"Longitude\" >= -180 AND \"Longitude\" <= 180))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_venues_home_bar_never_merged",
                table: "venues",
                sql: "\"VenueType\" <> 'home_bar' OR \"Status\" = 'active'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_venues_home_bar_no_geo",
                table: "venues",
                sql: "\"VenueType\" <> 'home_bar' OR (\"Latitude\" IS NULL AND \"Address\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_venues_merge_pairing",
                table: "venues",
                sql: "(\"Status\" = 'merged') = (\"MergedIntoVenueId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_venues_no_self_merge",
                table: "venues",
                sql: "\"MergedIntoVenueId\" IS NULL OR \"MergedIntoVenueId\" <> \"Id\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_venues_status",
                table: "venues",
                sql: "\"Status\" IN ('active','merged')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_venues_tier",
                table: "venues",
                sql: "\"Tier\" IS NULL OR \"Tier\" IN ('wiki','verified')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_venues_tier_pairing",
                table: "venues",
                sql: "(\"VenueType\" = 'venue') = (\"Tier\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_merge_queue_SourceVenueId",
                table: "merge_queue",
                column: "SourceVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_merge_queue_TargetVenueId",
                table: "merge_queue",
                column: "TargetVenueId");

            migrationBuilder.CreateIndex(
                name: "ux_merge_queue_pending_pair",
                table: "merge_queue",
                columns: new[] { "EntityType", "SourceProducerId", "TargetProducerId", "SourceDrinkId", "TargetDrinkId", "SourceVenueId", "TargetVenueId" },
                unique: true,
                filter: "\"Status\" = 'pending'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_merge_queue_distinct_pair",
                table: "merge_queue",
                sql: "(\"SourceProducerId\" IS NULL OR \"SourceProducerId\" <> \"TargetProducerId\") AND (\"SourceDrinkId\" IS NULL OR \"SourceDrinkId\" <> \"TargetDrinkId\") AND (\"SourceVenueId\" IS NULL OR \"SourceVenueId\" <> \"TargetVenueId\")");

            migrationBuilder.AddCheckConstraint(
                name: "ck_merge_queue_entity_type",
                table: "merge_queue",
                sql: "\"EntityType\" IN ('producer','drink','venue')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_merge_queue_typed_pair",
                table: "merge_queue",
                sql: "(\"EntityType\" = 'producer' AND \"SourceProducerId\" IS NOT NULL AND \"TargetProducerId\" IS NOT NULL AND \"SourceDrinkId\" IS NULL AND \"TargetDrinkId\" IS NULL AND \"SourceVenueId\" IS NULL AND \"TargetVenueId\" IS NULL) OR (\"EntityType\" = 'drink' AND \"SourceDrinkId\" IS NOT NULL AND \"TargetDrinkId\" IS NOT NULL AND \"SourceProducerId\" IS NULL AND \"TargetProducerId\" IS NULL AND \"SourceVenueId\" IS NULL AND \"TargetVenueId\" IS NULL) OR (\"EntityType\" = 'venue' AND \"SourceVenueId\" IS NOT NULL AND \"TargetVenueId\" IS NOT NULL AND \"SourceProducerId\" IS NULL AND \"TargetProducerId\" IS NULL AND \"SourceDrinkId\" IS NULL AND \"TargetDrinkId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_checkins_venue_recent",
                table: "checkins",
                columns: new[] { "VenueId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ux_checkins_one_open_per_user",
                table: "checkins",
                column: "UserId",
                unique: true,
                filter: "\"EndedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_venue_menu_items_CreatedByUserId",
                table: "venue_menu_items",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_venue_menu_items_DrinkId",
                table: "venue_menu_items",
                column: "DrinkId");

            migrationBuilder.CreateIndex(
                name: "ux_venue_menu_items_venue_drink",
                table: "venue_menu_items",
                columns: new[] { "VenueId", "DrinkId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_merge_queue_venues_SourceVenueId",
                table: "merge_queue",
                column: "SourceVenueId",
                principalTable: "venues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_merge_queue_venues_TargetVenueId",
                table: "merge_queue",
                column: "TargetVenueId",
                principalTable: "venues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_venues_users_CreatedByUserId",
                table: "venues",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_venues_venues_MergedIntoVenueId",
                table: "venues",
                column: "MergedIntoVenueId",
                principalTable: "venues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_merge_queue_venues_SourceVenueId",
                table: "merge_queue");

            migrationBuilder.DropForeignKey(
                name: "FK_merge_queue_venues_TargetVenueId",
                table: "merge_queue");

            migrationBuilder.DropForeignKey(
                name: "FK_venues_users_CreatedByUserId",
                table: "venues");

            migrationBuilder.DropForeignKey(
                name: "FK_venues_venues_MergedIntoVenueId",
                table: "venues");

            migrationBuilder.DropTable(
                name: "checkins");

            migrationBuilder.DropTable(
                name: "venue_menu_items");

            migrationBuilder.DropIndex(
                name: "IX_venues_CreatedByUserId",
                table: "venues");

            migrationBuilder.DropIndex(
                name: "IX_venues_MergedIntoVenueId",
                table: "venues");

            migrationBuilder.DropIndex(
                name: "ix_venues_normalized_name_trgm",
                table: "venues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venues_geo_pairing",
                table: "venues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venues_geo_range",
                table: "venues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venues_home_bar_never_merged",
                table: "venues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venues_home_bar_no_geo",
                table: "venues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venues_merge_pairing",
                table: "venues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venues_no_self_merge",
                table: "venues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venues_status",
                table: "venues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venues_tier",
                table: "venues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venues_tier_pairing",
                table: "venues");

            migrationBuilder.DropIndex(
                name: "IX_merge_queue_SourceVenueId",
                table: "merge_queue");

            migrationBuilder.DropIndex(
                name: "IX_merge_queue_TargetVenueId",
                table: "merge_queue");

            migrationBuilder.DropIndex(
                name: "ux_merge_queue_pending_pair",
                table: "merge_queue");

            migrationBuilder.DropCheckConstraint(
                name: "ck_merge_queue_distinct_pair",
                table: "merge_queue");

            migrationBuilder.DropCheckConstraint(
                name: "ck_merge_queue_entity_type",
                table: "merge_queue");

            migrationBuilder.DropCheckConstraint(
                name: "ck_merge_queue_typed_pair",
                table: "merge_queue");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "Hours",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "MergedIntoVenueId",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "SourceVenueId",
                table: "merge_queue");

            migrationBuilder.DropColumn(
                name: "TargetVenueId",
                table: "merge_queue");

            migrationBuilder.CreateIndex(
                name: "ux_merge_queue_pending_pair",
                table: "merge_queue",
                columns: new[] { "EntityType", "SourceProducerId", "TargetProducerId", "SourceDrinkId", "TargetDrinkId" },
                unique: true,
                filter: "\"Status\" = 'pending'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_merge_queue_distinct_pair",
                table: "merge_queue",
                sql: "(\"SourceProducerId\" IS NULL OR \"SourceProducerId\" <> \"TargetProducerId\") AND (\"SourceDrinkId\" IS NULL OR \"SourceDrinkId\" <> \"TargetDrinkId\")");

            migrationBuilder.AddCheckConstraint(
                name: "ck_merge_queue_entity_type",
                table: "merge_queue",
                sql: "\"EntityType\" IN ('producer','drink')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_merge_queue_typed_pair",
                table: "merge_queue",
                sql: "(\"EntityType\" = 'producer' AND \"SourceProducerId\" IS NOT NULL AND \"TargetProducerId\" IS NOT NULL AND \"SourceDrinkId\" IS NULL AND \"TargetDrinkId\" IS NULL) OR (\"EntityType\" = 'drink' AND \"SourceDrinkId\" IS NOT NULL AND \"TargetDrinkId\" IS NOT NULL AND \"SourceProducerId\" IS NULL AND \"TargetProducerId\" IS NULL)");
        }
    }
}
