using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarBrain.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sprint7Privacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeletionMode",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletionRequestedAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_deletion_mode",
                table: "users",
                sql: "\"DeletionMode\" IS NULL OR \"DeletionMode\" IN ('delete','anonymize')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_deletion_pairing",
                table: "users",
                sql: "(\"DeletionRequestedAt\" IS NULL) = (\"DeletionMode\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_deletion_mode",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_deletion_pairing",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DeletionMode",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DeletionRequestedAt",
                table: "users");
        }
    }
}
