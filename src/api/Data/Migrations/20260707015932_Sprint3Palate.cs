using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace BarBrain.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sprint3Palate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "ratings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                // Pre-Sprint-3 rows are organic user ratings; "" would violate
                // ck_ratings_origin on any database that already has ratings.
                defaultValue: "user");

            migrationBuilder.CreateTable(
                name: "user_category_interests",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_category_interests", x => new { x.UserId, x.Category });
                    table.CheckConstraint("ck_user_category_interests_category", "\"Category\" IN ('beer','whiskey','wine')");
                    table.ForeignKey(
                        name: "FK_user_category_interests_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_palate_profiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PreferenceVector = table.Column<Vector>(type: "vector(8)", nullable: true),
                    CentroidVector = table.Column<Vector>(type: "vector(8)", nullable: true),
                    BridgeVector = table.Column<Vector>(type: "vector(6)", nullable: true),
                    RatingsCount = table.Column<int>(type: "integer", nullable: false),
                    UserMean = table.Column<float>(type: "real", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_palate_profiles", x => new { x.UserId, x.Category });
                    table.CheckConstraint("ck_user_palate_profiles_category", "\"Category\" IN ('beer','whiskey','wine')");
                    table.CheckConstraint("ck_user_palate_profiles_ratings_count", "\"RatingsCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_user_palate_profiles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ratings_origin",
                table: "ratings",
                sql: "\"Origin\" IN ('user','quiz')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_category_interests");

            migrationBuilder.DropTable(
                name: "user_palate_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ratings_origin",
                table: "ratings");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "ratings");
        }
    }
}
