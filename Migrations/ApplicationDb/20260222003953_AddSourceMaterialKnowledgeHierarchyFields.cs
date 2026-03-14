using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddSourceMaterialKnowledgeHierarchyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KnowledgeLabel",
                table: "SourceMaterials",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KnowledgeNumber",
                table: "SourceMaterials",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KnowledgeRootPath",
                table: "SourceMaterials",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KnowledgeSourceType",
                table: "SourceMaterials",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KnowledgeUploadedAtUtc",
                table: "SourceMaterials",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualificationCode",
                table: "SourceMaterials",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceMaterials_QualificationCode_KnowledgeSourceType_KnowledgeNumber_KnowledgeUploadedAtUtc",
                table: "SourceMaterials",
                columns: new[] { "QualificationCode", "KnowledgeSourceType", "KnowledgeNumber", "KnowledgeUploadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceMaterials_QualificationDescription_KnowledgeSourceType_CreatedAt",
                table: "SourceMaterials",
                columns: new[] { "QualificationDescription", "KnowledgeSourceType", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SourceMaterials_QualificationCode_KnowledgeSourceType_KnowledgeNumber_KnowledgeUploadedAtUtc",
                table: "SourceMaterials");

            migrationBuilder.DropIndex(
                name: "IX_SourceMaterials_QualificationDescription_KnowledgeSourceType_CreatedAt",
                table: "SourceMaterials");

            migrationBuilder.DropColumn(
                name: "KnowledgeLabel",
                table: "SourceMaterials");

            migrationBuilder.DropColumn(
                name: "KnowledgeNumber",
                table: "SourceMaterials");

            migrationBuilder.DropColumn(
                name: "KnowledgeRootPath",
                table: "SourceMaterials");

            migrationBuilder.DropColumn(
                name: "KnowledgeSourceType",
                table: "SourceMaterials");

            migrationBuilder.DropColumn(
                name: "KnowledgeUploadedAtUtc",
                table: "SourceMaterials");

            migrationBuilder.DropColumn(
                name: "QualificationCode",
                table: "SourceMaterials");
        }
    }
}
