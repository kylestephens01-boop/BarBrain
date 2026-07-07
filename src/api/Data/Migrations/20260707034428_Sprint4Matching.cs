using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarBrain.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sprint4Matching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Per-row unique token. defaultValueSql (not a scalar default) so
            // Postgres generates a DISTINCT uuid for every EXISTING user —
            // otherwise they'd all share the empty guid and the unique index
            // below would fail (and a shared token is guessable). New rows are
            // assigned in app code (Guid.CreateVersion7); this covers backfill
            // and any raw insert. gen_random_uuid() is built in on PG16.
            migrationBuilder.AddColumn<Guid>(
                name: "DigestUnsubscribeToken",
                table: "users",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DigestUnsubscribedAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HideFromMatches",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "user_match_neighbors",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NeighborUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttributeSimilarity = table.Column<double>(type: "double precision", nullable: false),
                    CoRatingAgreement = table.Column<double>(type: "double precision", nullable: true),
                    CoRatedCount = table.Column<int>(type: "integer", nullable: false),
                    BlendedScore = table.Column<double>(type: "double precision", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_match_neighbors", x => new { x.UserId, x.NeighborUserId, x.Category });
                    table.CheckConstraint("ck_user_match_neighbors_category", "\"Category\" IN ('beer','whiskey','wine')");
                    table.CheckConstraint("ck_user_match_neighbors_corated", "\"CoRatedCount\" >= 0");
                    table.CheckConstraint("ck_user_match_neighbors_no_self", "\"UserId\" <> \"NeighborUserId\"");
                    table.CheckConstraint("ck_user_match_neighbors_score", "\"BlendedScore\" >= 0 AND \"BlendedScore\" <= 1");
                    table.ForeignKey(
                        name: "FK_user_match_neighbors_users_NeighborUserId",
                        column: x => x.NeighborUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_match_neighbors_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_users_digest_unsub_token",
                table: "users",
                column: "DigestUnsubscribeToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_match_neighbors_NeighborUserId",
                table: "user_match_neighbors",
                column: "NeighborUserId");

            migrationBuilder.CreateIndex(
                name: "ix_user_match_neighbors_user_score",
                table: "user_match_neighbors",
                columns: new[] { "UserId", "BlendedScore" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_match_neighbors");

            migrationBuilder.DropIndex(
                name: "ux_users_digest_unsub_token",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DigestUnsubscribeToken",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DigestUnsubscribedAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "HideFromMatches",
                table: "users");
        }
    }
}
