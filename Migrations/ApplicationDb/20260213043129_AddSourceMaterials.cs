using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddSourceMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceMaterials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    FileType = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    QualificationDescription = table.Column<string>(type: "TEXT", nullable: true),
                    SubjectDescription = table.Column<string>(type: "TEXT", nullable: true),
                    TopicDescription = table.Column<string>(type: "TEXT", nullable: true),
                    AssessmentCriteriaDescription = table.Column<string>(type: "TEXT", nullable: true),
                    ExtractedText = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceMaterials", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceMaterials");
        }
    }
}
