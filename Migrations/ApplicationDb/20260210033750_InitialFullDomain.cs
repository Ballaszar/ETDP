using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class InitialFullDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subjects_CurriculumPhases_CurriculumPhaseId",
                table: "Subjects");

            migrationBuilder.DropTable(
                name: "QualificationPhases");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_QualificationId_CurriculumPhaseId",
                table: "Subjects");

            migrationBuilder.DeleteData(
                table: "CurriculumPhases",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "CurriculumPhases",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "CurriculumPhases",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "CurriculumPhases",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "Credits",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "NqfLevel",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "Percentage",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "Subjects");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "Subjects",
                newName: "Name");

            migrationBuilder.CreateTable(
                name: "KnowledgeQuestionnaires",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Questions = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeQuestionnaires", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeQuestionnaires_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearnerGuides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerGuides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearnerGuides_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Qualifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Qualifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Topics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Topics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Topics_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Workbooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workbooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Workbooks_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Demographics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QualificationId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgeGroup = table.Column<string>(type: "TEXT", nullable: false),
                    Region = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Demographics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Demographics_Qualifications_QualificationId",
                        column: x => x.QualificationId,
                        principalTable: "Qualifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentCriteria",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    TopicId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentCriteria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentCriteria_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subtopics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TopicId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subtopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subtopics_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LessonPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AssessmentCriteriaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonPlans_AssessmentCriteria_AssessmentCriteriaId",
                        column: x => x.AssessmentCriteriaId,
                        principalTable: "AssessmentCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SubtopicId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Activities_Subtopics_SubtopicId",
                        column: x => x.SubtopicId,
                        principalTable: "Subtopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_QualificationId",
                table: "Subjects",
                column: "QualificationId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_SubtopicId",
                table: "Activities",
                column: "SubtopicId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCriteria_TopicId",
                table: "AssessmentCriteria",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_Demographics_QualificationId",
                table: "Demographics",
                column: "QualificationId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeQuestionnaires_SubjectId",
                table: "KnowledgeQuestionnaires",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerGuides_SubjectId",
                table: "LearnerGuides",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonPlans_AssessmentCriteriaId",
                table: "LessonPlans",
                column: "AssessmentCriteriaId");

            migrationBuilder.CreateIndex(
                name: "IX_Subtopics_TopicId",
                table: "Subtopics",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_Topics_SubjectId",
                table: "Topics",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Workbooks_SubjectId",
                table: "Workbooks",
                column: "SubjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Subjects_CurriculumPhases_CurriculumPhaseId",
                table: "Subjects",
                column: "CurriculumPhaseId",
                principalTable: "CurriculumPhases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Subjects_Qualifications_QualificationId",
                table: "Subjects",
                column: "QualificationId",
                principalTable: "Qualifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subjects_CurriculumPhases_CurriculumPhaseId",
                table: "Subjects");

            migrationBuilder.DropForeignKey(
                name: "FK_Subjects_Qualifications_QualificationId",
                table: "Subjects");

            migrationBuilder.DropTable(
                name: "Activities");

            migrationBuilder.DropTable(
                name: "Demographics");

            migrationBuilder.DropTable(
                name: "KnowledgeQuestionnaires");

            migrationBuilder.DropTable(
                name: "LearnerGuides");

            migrationBuilder.DropTable(
                name: "LessonPlans");

            migrationBuilder.DropTable(
                name: "Workbooks");

            migrationBuilder.DropTable(
                name: "Subtopics");

            migrationBuilder.DropTable(
                name: "Qualifications");

            migrationBuilder.DropTable(
                name: "AssessmentCriteria");

            migrationBuilder.DropTable(
                name: "Topics");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_QualificationId",
                table: "Subjects");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Subjects",
                newName: "Description");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Subjects",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Credits",
                table: "Subjects",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NqfLevel",
                table: "Subjects",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Percentage",
                table: "Subjects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "Subjects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QualificationPhases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CurriculumPhaseId = table.Column<int>(type: "INTEGER", nullable: false),
                    QualificationId = table.Column<int>(type: "INTEGER", nullable: false)
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
                name: "IX_Subjects_QualificationId_CurriculumPhaseId",
                table: "Subjects",
                columns: new[] { "QualificationId", "CurriculumPhaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_QualificationPhases_CurriculumPhaseId",
                table: "QualificationPhases",
                column: "CurriculumPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_QualificationPhases_QualificationId_CurriculumPhaseId",
                table: "QualificationPhases",
                columns: new[] { "QualificationId", "CurriculumPhaseId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Subjects_CurriculumPhases_CurriculumPhaseId",
                table: "Subjects",
                column: "CurriculumPhaseId",
                principalTable: "CurriculumPhases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
