using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace BarBrain.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sprint1Catalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "attribute_definitions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DimIndex = table.Column<short>(type: "smallint", nullable: false),
                    BridgeIndex = table.Column<short>(type: "smallint", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attribute_definitions", x => x.Key);
                    table.CheckConstraint("ck_attribute_definitions_bridge_index", "\"BridgeIndex\" IS NULL OR (\"BridgeIndex\" >= 0 AND \"BridgeIndex\" < 6)");
                    table.CheckConstraint("ck_attribute_definitions_category", "\"Category\" IN ('beer','whiskey','wine')");
                    table.CheckConstraint("ck_attribute_definitions_dim_index", "\"DimIndex\" >= 0 AND \"DimIndex\" < 8");
                });

            migrationBuilder.CreateTable(
                name: "styles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ParentStyleId = table.Column<Guid>(type: "uuid", nullable: true),
                    AbvMin = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    AbvMax = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    IbuMin = table.Column<short>(type: "smallint", nullable: true),
                    IbuMax = table.Column<short>(type: "smallint", nullable: true),
                    SrmMin = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    SrmMax = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    OgMin = table.Column<decimal>(type: "numeric(5,3)", precision: 5, scale: 3, nullable: true),
                    OgMax = table.Column<decimal>(type: "numeric(5,3)", precision: 5, scale: 3, nullable: true),
                    FgMin = table.Column<decimal>(type: "numeric(5,3)", precision: 5, scale: 3, nullable: true),
                    FgMax = table.Column<decimal>(type: "numeric(5,3)", precision: 5, scale: 3, nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CategoryVector = table.Column<Vector>(type: "vector(8)", nullable: true),
                    BridgeVector = table.Column<Vector>(type: "vector(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_styles", x => x.Id);
                    table.UniqueConstraint("AK_styles_Id_Category", x => new { x.Id, x.Category });
                    table.CheckConstraint("ck_styles_abv_range", "(\"AbvMin\" IS NULL OR \"AbvMin\" >= 0) AND (\"AbvMin\" IS NULL OR \"AbvMax\" IS NULL OR \"AbvMin\" <= \"AbvMax\")");
                    table.CheckConstraint("ck_styles_category", "\"Category\" IN ('beer','whiskey','wine')");
                    table.CheckConstraint("ck_styles_fg_range", "(\"FgMin\" IS NULL OR \"FgMax\" IS NULL OR \"FgMin\" <= \"FgMax\")");
                    table.CheckConstraint("ck_styles_ibu_range", "(\"IbuMin\" IS NULL OR \"IbuMin\" >= 0) AND (\"IbuMin\" IS NULL OR \"IbuMax\" IS NULL OR \"IbuMin\" <= \"IbuMax\")");
                    table.CheckConstraint("ck_styles_og_range", "(\"OgMin\" IS NULL OR \"OgMax\" IS NULL OR \"OgMin\" <= \"OgMax\")");
                    table.CheckConstraint("ck_styles_srm_range", "(\"SrmMin\" IS NULL OR \"SrmMin\" >= 0) AND (\"SrmMin\" IS NULL OR \"SrmMax\" IS NULL OR \"SrmMin\" <= \"SrmMax\")");
                    table.ForeignKey(
                        name: "FK_styles_styles_ParentStyleId",
                        column: x => x.ParentStyleId,
                        principalTable: "styles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Handle = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "style_attributes",
                columns: table => new
                {
                    StyleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttributeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Value = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_style_attributes", x => new { x.StyleId, x.AttributeKey });
                    table.CheckConstraint("ck_style_attributes_value", "\"Value\" >= 0 AND \"Value\" <= 1");
                    table.ForeignKey(
                        name: "FK_style_attributes_attribute_definitions_AttributeKey",
                        column: x => x.AttributeKey,
                        principalTable: "attribute_definitions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_style_attributes_styles_StyleId",
                        column: x => x.StyleId,
                        principalTable: "styles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "producers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProducerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Country = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Region = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MergedIntoProducerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_producers", x => x.Id);
                    table.CheckConstraint("ck_producers_merge_pairing", "(\"Status\" = 'merged') = (\"MergedIntoProducerId\" IS NOT NULL)");
                    table.CheckConstraint("ck_producers_no_self_merge", "\"MergedIntoProducerId\" IS NULL OR \"MergedIntoProducerId\" <> \"Id\"");
                    table.CheckConstraint("ck_producers_owner_visibility", "\"CreatedByUserId\" IS NOT NULL OR \"Visibility\" = 'public'");
                    table.CheckConstraint("ck_producers_status", "\"Status\" IN ('active','merged')");
                    table.CheckConstraint("ck_producers_type", "\"ProducerType\" IS NULL OR \"ProducerType\" IN ('brewery','distillery','winery','cidery','meadery','multi','other')");
                    table.CheckConstraint("ck_producers_visibility", "\"Visibility\" IN ('public','private')");
                    table.ForeignKey(
                        name: "FK_producers_producers_MergedIntoProducerId",
                        column: x => x.MergedIntoProducerId,
                        principalTable: "producers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_producers_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "drinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProducerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StyleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Abv = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MergedIntoDrinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryVector = table.Column<Vector>(type: "vector(8)", nullable: true),
                    BridgeVector = table.Column<Vector>(type: "vector(6)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drinks", x => x.Id);
                    table.UniqueConstraint("AK_drinks_Id_Category", x => new { x.Id, x.Category });
                    table.CheckConstraint("ck_drinks_abv", "\"Abv\" IS NULL OR (\"Abv\" >= 0 AND \"Abv\" <= 100)");
                    table.CheckConstraint("ck_drinks_category", "\"Category\" IN ('beer','whiskey','wine')");
                    table.CheckConstraint("ck_drinks_merge_pairing", "(\"Status\" = 'merged') = (\"MergedIntoDrinkId\" IS NOT NULL)");
                    table.CheckConstraint("ck_drinks_no_self_merge", "\"MergedIntoDrinkId\" IS NULL OR \"MergedIntoDrinkId\" <> \"Id\"");
                    table.CheckConstraint("ck_drinks_owner_visibility", "\"CreatedByUserId\" IS NOT NULL OR \"Visibility\" = 'public'");
                    table.CheckConstraint("ck_drinks_status", "\"Status\" IN ('active','merged')");
                    table.CheckConstraint("ck_drinks_visibility", "\"Visibility\" IN ('public','private')");
                    table.ForeignKey(
                        name: "FK_drinks_drinks_MergedIntoDrinkId_Category",
                        columns: x => new { x.MergedIntoDrinkId, x.Category },
                        principalTable: "drinks",
                        principalColumns: new[] { "Id", "Category" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_drinks_producers_ProducerId",
                        column: x => x.ProducerId,
                        principalTable: "producers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_drinks_styles_StyleId_Category",
                        columns: x => new { x.StyleId, x.Category },
                        principalTable: "styles",
                        principalColumns: new[] { "Id", "Category" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_drinks_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "drink_attributes",
                columns: table => new
                {
                    DrinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttributeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Value = table.Column<float>(type: "real", nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Confidence = table.Column<float>(type: "real", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drink_attributes", x => new { x.DrinkId, x.AttributeKey });
                    table.CheckConstraint("ck_drink_attributes_confidence", "\"Confidence\" >= 0 AND \"Confidence\" <= 1");
                    table.CheckConstraint("ck_drink_attributes_source", "\"Source\" IN ('inherited','manufacturer','crowd','llm','moderator')");
                    table.CheckConstraint("ck_drink_attributes_value", "\"Value\" >= 0 AND \"Value\" <= 1");
                    table.ForeignKey(
                        name: "FK_drink_attributes_attribute_definitions_AttributeKey",
                        column: x => x.AttributeKey,
                        principalTable: "attribute_definitions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_drink_attributes_drinks_DrinkId",
                        column: x => x.DrinkId,
                        principalTable: "drinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "merge_queue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceProducerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetProducerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceDrinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetDrinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    Similarity = table.Column<float>(type: "real", nullable: false),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merge_queue", x => x.Id);
                    table.CheckConstraint("ck_merge_queue_decision", "(\"Status\" = 'pending') = (\"DecidedAt\" IS NULL)");
                    table.CheckConstraint("ck_merge_queue_distinct_pair", "(\"SourceProducerId\" IS NULL OR \"SourceProducerId\" <> \"TargetProducerId\") AND (\"SourceDrinkId\" IS NULL OR \"SourceDrinkId\" <> \"TargetDrinkId\")");
                    table.CheckConstraint("ck_merge_queue_entity_type", "\"EntityType\" IN ('producer','drink')");
                    table.CheckConstraint("ck_merge_queue_similarity", "\"Similarity\" >= 0 AND \"Similarity\" <= 1");
                    table.CheckConstraint("ck_merge_queue_status", "\"Status\" IN ('pending','approved','rejected')");
                    table.CheckConstraint("ck_merge_queue_typed_pair", "(\"EntityType\" = 'producer' AND \"SourceProducerId\" IS NOT NULL AND \"TargetProducerId\" IS NOT NULL AND \"SourceDrinkId\" IS NULL AND \"TargetDrinkId\" IS NULL) OR (\"EntityType\" = 'drink' AND \"SourceDrinkId\" IS NOT NULL AND \"TargetDrinkId\" IS NOT NULL AND \"SourceProducerId\" IS NULL AND \"TargetProducerId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_merge_queue_drinks_SourceDrinkId",
                        column: x => x.SourceDrinkId,
                        principalTable: "drinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_merge_queue_drinks_TargetDrinkId",
                        column: x => x.TargetDrinkId,
                        principalTable: "drinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_merge_queue_producers_SourceProducerId",
                        column: x => x.SourceProducerId,
                        principalTable: "producers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_merge_queue_producers_TargetProducerId",
                        column: x => x.TargetProducerId,
                        principalTable: "producers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attribute_definitions_Category_BridgeIndex",
                table: "attribute_definitions",
                columns: new[] { "Category", "BridgeIndex" },
                unique: true,
                filter: "\"BridgeIndex\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_attribute_definitions_Category_DimIndex",
                table: "attribute_definitions",
                columns: new[] { "Category", "DimIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drink_attributes_AttributeKey",
                table: "drink_attributes",
                column: "AttributeKey");

            migrationBuilder.CreateIndex(
                name: "ix_drinks_bridge_vector_hnsw",
                table: "drinks",
                column: "BridgeVector")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_drinks_Category_StyleId",
                table: "drinks",
                columns: new[] { "Category", "StyleId" });

            migrationBuilder.CreateIndex(
                name: "ix_drinks_category_vector_hnsw",
                table: "drinks",
                column: "CategoryVector")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_drinks_CreatedByUserId",
                table: "drinks",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_drinks_MergedIntoDrinkId_Category",
                table: "drinks",
                columns: new[] { "MergedIntoDrinkId", "Category" });

            migrationBuilder.CreateIndex(
                name: "ix_drinks_normalized_name_trgm",
                table: "drinks",
                column: "NormalizedName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_drinks_Source_SourceRef",
                table: "drinks",
                columns: new[] { "Source", "SourceRef" },
                unique: true,
                filter: "\"SourceRef\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_drinks_StyleId_Category",
                table: "drinks",
                columns: new[] { "StyleId", "Category" });

            migrationBuilder.CreateIndex(
                name: "ux_drinks_canonical_identity",
                table: "drinks",
                columns: new[] { "ProducerId", "Category", "NormalizedName" },
                unique: true,
                filter: "\"Status\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_merge_queue_SourceDrinkId",
                table: "merge_queue",
                column: "SourceDrinkId");

            migrationBuilder.CreateIndex(
                name: "IX_merge_queue_SourceProducerId",
                table: "merge_queue",
                column: "SourceProducerId");

            migrationBuilder.CreateIndex(
                name: "IX_merge_queue_Status_CreatedAt",
                table: "merge_queue",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_merge_queue_TargetDrinkId",
                table: "merge_queue",
                column: "TargetDrinkId");

            migrationBuilder.CreateIndex(
                name: "IX_merge_queue_TargetProducerId",
                table: "merge_queue",
                column: "TargetProducerId");

            migrationBuilder.CreateIndex(
                name: "ux_merge_queue_pending_pair",
                table: "merge_queue",
                columns: new[] { "EntityType", "SourceProducerId", "TargetProducerId", "SourceDrinkId", "TargetDrinkId" },
                unique: true,
                filter: "\"Status\" = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_producers_CreatedByUserId",
                table: "producers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_producers_MergedIntoProducerId",
                table: "producers",
                column: "MergedIntoProducerId");

            migrationBuilder.CreateIndex(
                name: "ix_producers_normalized_name_trgm",
                table: "producers",
                column: "NormalizedName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_producers_Source_SourceRef",
                table: "producers",
                columns: new[] { "Source", "SourceRef" },
                unique: true,
                filter: "\"SourceRef\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_style_attributes_AttributeKey",
                table: "style_attributes",
                column: "AttributeKey");

            migrationBuilder.CreateIndex(
                name: "IX_styles_Category_Code",
                table: "styles",
                columns: new[] { "Category", "Code" },
                unique: true,
                filter: "\"Code\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_styles_Category_NormalizedName",
                table: "styles",
                columns: new[] { "Category", "NormalizedName" });

            migrationBuilder.CreateIndex(
                name: "IX_styles_ParentStyleId",
                table: "styles",
                column: "ParentStyleId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Handle",
                table: "users",
                column: "Handle",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "drink_attributes");

            migrationBuilder.DropTable(
                name: "merge_queue");

            migrationBuilder.DropTable(
                name: "style_attributes");

            migrationBuilder.DropTable(
                name: "drinks");

            migrationBuilder.DropTable(
                name: "attribute_definitions");

            migrationBuilder.DropTable(
                name: "producers");

            migrationBuilder.DropTable(
                name: "styles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
