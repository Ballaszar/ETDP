using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class EnsureQualificationPhases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_QualificationPhases_CurriculumPhaseId",
                table: "QualificationPhases",
                column: "CurriculumPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_QualificationPhases_QualificationId_CurriculumPhaseId",
                table: "QualificationPhases",
                columns: new[] { "QualificationId", "CurriculumPhaseId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QualificationPhases");
        }
    }
}
