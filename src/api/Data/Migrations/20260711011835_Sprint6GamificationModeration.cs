using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarBrain.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sprint6GamificationModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HiddenAt",
                table: "venues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HiddenBy",
                table: "venues",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BannedAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModerationNote",
                table: "users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShadowLimitedAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HiddenAt",
                table: "ratings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HiddenBy",
                table: "ratings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecSection",
                table: "ratings",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HiddenAt",
                table: "drinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HiddenBy",
                table: "drinks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "anomaly_flags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Evidence = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anomaly_flags", x => x.Id);
                    table.CheckConstraint("ck_anomaly_flags_decision", "(\"Status\" = 'open') = (\"DecidedAt\" IS NULL)");
                    table.CheckConstraint("ck_anomaly_flags_kind", "\"Kind\" IN ('rating_zscore_outlier','rapid_fire')");
                    table.CheckConstraint("ck_anomaly_flags_status", "\"Status\" IN ('open','cleared','actioned')");
                    table.ForeignKey(
                        name: "FK_anomaly_flags_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "badge_definitions",
                columns: table => new
                {
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Icon = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayGroup = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Metric = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Threshold = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_badge_definitions", x => x.Slug);
                    table.CheckConstraint("ck_badge_definitions_group", "\"DisplayGroup\" IN ('breadth','exploration','venues','contribution','streak')");
                    table.CheckConstraint("ck_badge_definitions_metric", "\"Metric\" IN ('distinct_styles_rated','distinct_categories_rated','wildcard_distinct_drinks','distinct_venues_checked_in','wiki_contributions','menu_confirms','accepted_merge_contributions','weekly_streak_weeks')");
                    table.CheckConstraint("ck_badge_definitions_threshold", "\"Threshold\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "moderation_actions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Actor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moderation_actions", x => x.Id);
                    table.CheckConstraint("ck_moderation_actions_action", "\"Action\" IN ('merge_approved','merge_rejected','report_actioned','report_dismissed','content_hidden','content_unhidden','shadow_limited','shadow_cleared','banned','unbanned','anomaly_cleared')");
                });

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RatingId = table.Column<Guid>(type: "uuid", nullable: true),
                    VenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    DrinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReporterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.Id);
                    table.CheckConstraint("ck_reports_decision", "(\"Status\" = 'open') = (\"DecidedAt\" IS NULL)");
                    table.CheckConstraint("ck_reports_entity_type", "\"EntityType\" IN ('rating','venue','drink')");
                    table.CheckConstraint("ck_reports_reason", "\"Reason\" IN ('inaccurate','spam','offensive','other')");
                    table.CheckConstraint("ck_reports_status", "\"Status\" IN ('open','actioned','dismissed')");
                    table.CheckConstraint("ck_reports_typed_target", "(\"EntityType\" = 'rating' AND \"RatingId\" IS NOT NULL AND \"VenueId\" IS NULL AND \"DrinkId\" IS NULL) OR (\"EntityType\" = 'venue' AND \"VenueId\" IS NOT NULL AND \"RatingId\" IS NULL AND \"DrinkId\" IS NULL) OR (\"EntityType\" = 'drink' AND \"DrinkId\" IS NOT NULL AND \"RatingId\" IS NULL AND \"VenueId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_reports_drinks_DrinkId",
                        column: x => x.DrinkId,
                        principalTable: "drinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reports_ratings_RatingId",
                        column: x => x.RatingId,
                        principalTable: "ratings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reports_users_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reports_venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_badges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BadgeSlug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AwardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_badges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_badges_badge_definitions_BadgeSlug",
                        column: x => x.BadgeSlug,
                        principalTable: "badge_definitions",
                        principalColumn: "Slug",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_badges_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_venues_hidden_pairing",
                table: "venues",
                sql: "(\"HiddenAt\" IS NULL) = (\"HiddenBy\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ratings_hidden_pairing",
                table: "ratings",
                sql: "(\"HiddenAt\" IS NULL) = (\"HiddenBy\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ratings_rec_section",
                table: "ratings",
                sql: "\"RecSection\" IS NULL OR \"RecSection\" IN ('up_your_alley','stretch_a_little','wildcard','loved_by_your_matches')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_drinks_hidden_pairing",
                table: "drinks",
                sql: "(\"HiddenAt\" IS NULL) = (\"HiddenBy\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_anomaly_flags_Status_Score",
                table: "anomaly_flags",
                columns: new[] { "Status", "Score" });

            migrationBuilder.CreateIndex(
                name: "ux_anomaly_flags_open_per_user_kind",
                table: "anomaly_flags",
                columns: new[] { "UserId", "Kind" },
                unique: true,
                filter: "\"Status\" = 'open'");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_actions_CreatedAt",
                table: "moderation_actions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_actions_EntityType_EntityId",
                table: "moderation_actions",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_reports_DrinkId",
                table: "reports",
                column: "DrinkId");

            migrationBuilder.CreateIndex(
                name: "IX_reports_RatingId",
                table: "reports",
                column: "RatingId");

            migrationBuilder.CreateIndex(
                name: "IX_reports_Status_CreatedAt",
                table: "reports",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_reports_VenueId",
                table: "reports",
                column: "VenueId");

            migrationBuilder.CreateIndex(
                name: "ux_reports_open_per_reporter_target",
                table: "reports",
                columns: new[] { "ReporterUserId", "RatingId", "VenueId", "DrinkId" },
                unique: true,
                filter: "\"Status\" = 'open'");

            migrationBuilder.CreateIndex(
                name: "IX_user_badges_BadgeSlug",
                table: "user_badges",
                column: "BadgeSlug");

            migrationBuilder.CreateIndex(
                name: "ix_user_badges_unseen",
                table: "user_badges",
                column: "UserId",
                filter: "\"SeenAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_user_badges_user_badge",
                table: "user_badges",
                columns: new[] { "UserId", "BadgeSlug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anomaly_flags");

            migrationBuilder.DropTable(
                name: "moderation_actions");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "user_badges");

            migrationBuilder.DropTable(
                name: "badge_definitions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venues_hidden_pairing",
                table: "venues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ratings_hidden_pairing",
                table: "ratings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ratings_rec_section",
                table: "ratings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_drinks_hidden_pairing",
                table: "drinks");

            migrationBuilder.DropColumn(
                name: "HiddenAt",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "HiddenBy",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "BannedAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ModerationNote",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ShadowLimitedAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "HiddenAt",
                table: "ratings");

            migrationBuilder.DropColumn(
                name: "HiddenBy",
                table: "ratings");

            migrationBuilder.DropColumn(
                name: "RecSection",
                table: "ratings");

            migrationBuilder.DropColumn(
                name: "HiddenAt",
                table: "drinks");

            migrationBuilder.DropColumn(
                name: "HiddenBy",
                table: "drinks");
        }
    }
}
