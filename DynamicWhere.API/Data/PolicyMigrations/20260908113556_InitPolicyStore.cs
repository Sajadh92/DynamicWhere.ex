using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DynamicWhere.API.Data.PolicyMigrations
{
    /// <inheritdoc />
    public partial class InitPolicyStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DwPolicyRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubjectKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SubjectKeyNormalized = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EntityType = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FieldPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Features = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Effect = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValidTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DwPolicyRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DwPolicyVersion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DwPolicyVersion", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DwPolicyRules_EntityType_FieldPath",
                table: "DwPolicyRules",
                columns: new[] { "EntityType", "FieldPath" });

            migrationBuilder.CreateIndex(
                name: "IX_DwPolicyRules_SubjectKind_SubjectKeyNormalized_EntityType",
                table: "DwPolicyRules",
                columns: new[] { "SubjectKind", "SubjectKeyNormalized", "EntityType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DwPolicyRules");

            migrationBuilder.DropTable(
                name: "DwPolicyVersion");
        }
    }
}
