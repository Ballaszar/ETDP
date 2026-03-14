using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class LecturerToolkitEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LecturerToolkitEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QualificationsId = table.Column<int>(type: "INTEGER", nullable: false),
                    LearningInstitutionName = table.Column<string>(type: "TEXT", nullable: false),
                    LecturerName = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectCode = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectDescription = table.Column<string>(type: "TEXT", nullable: false),
                    AssessmentCriteriaId = table.Column<int>(type: "INTEGER", nullable: true),
                    AssessmentCriteriaDescription = table.Column<string>(type: "TEXT", nullable: true),
                    Lpn = table.Column<string>(type: "TEXT", nullable: false),
                    LessonPlanDescription = table.Column<string>(type: "TEXT", nullable: false),
                    LessonPlanContent = table.Column<string>(type: "TEXT", nullable: false),
                    TimeStart = table.Column<string>(type: "TEXT", nullable: false),
                    TimeEnd = table.Column<string>(type: "TEXT", nullable: false),
                    LecturerActions = table.Column<string>(type: "TEXT", nullable: false),
                    LearnerActions = table.Column<string>(type: "TEXT", nullable: false),
                    LearningAids = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LecturerToolkitEntries", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LecturerToolkitEntries");
        }
    }
}
