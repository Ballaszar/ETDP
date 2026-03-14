using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddCurriculumPhasesAndSubjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CurriculumPhases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumPhases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QualificationPhases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QualificationId = table.Column<int>(type: "INTEGER", nullable: false),
                    CurriculumPhaseId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualificationPhases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualificationPhases_CurriculumPhases_CurriculumPhaseId",
                        column: x => x.CurriculumPhaseId,
                        principalTable: "CurriculumPhases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QualificationId = table.Column<int>(type: "INTEGER", nullable: false),
                    CurriculumPhaseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Credits = table.Column<int>(type: "INTEGER", nullable: true),
                    NqfLevel = table.Column<int>(type: "INTEGER", nullable: true),
                    Percentage = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subjects_CurriculumPhases_CurriculumPhaseId",
                        column: x => x.CurriculumPhaseId,
                        principalTable: "CurriculumPhases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "CurriculumPhases",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Fundamental Learning" },
                    { 2, "Knowledge Learning" },
                    { 3, "Practical Learning" },
                    { 4, "Workplace Experience" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_QualificationPhases_CurriculumPhaseId",
                table: "QualificationPhases",
                column: "CurriculumPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_QualificationPhases_QualificationId_CurriculumPhaseId",
                table: "QualificationPhases",
                columns: new[] { "QualificationId", "CurriculumPhaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_CurriculumPhaseId",
                table: "Subjects",
                column: "CurriculumPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_QualificationId_CurriculumPhaseId",
                table: "Subjects",
                columns: new[] { "QualificationId", "CurriculumPhaseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QualificationPhases");

            migrationBuilder.DropTable(
                name: "Subjects");

            migrationBuilder.DropTable(
                name: "CurriculumPhases");
        }
    }
}
