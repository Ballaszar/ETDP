using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddWorkExperienceLogbooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkExperienceLogbooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QualificationId = table.Column<int>(type: "INTEGER", nullable: true),
                    QualificationNumber = table.Column<string>(type: "TEXT", nullable: false),
                    LearnerRegistrationId = table.Column<int>(type: "INTEGER", nullable: true),
                    LearningInstitutionName = table.Column<string>(type: "TEXT", nullable: false),
                    LearningInstitutionAddress = table.Column<string>(type: "TEXT", nullable: false),
                    LearningInstitutionContactPerson = table.Column<string>(type: "TEXT", nullable: false),
                    LearningInstitutionContactPhone = table.Column<string>(type: "TEXT", nullable: false),
                    LearningInstitutionContactEmail = table.Column<string>(type: "TEXT", nullable: false),
                    EmployerName = table.Column<string>(type: "TEXT", nullable: false),
                    EmployerAddress = table.Column<string>(type: "TEXT", nullable: false),
                    SupervisorName = table.Column<string>(type: "TEXT", nullable: false),
                    SupervisorPhone = table.Column<string>(type: "TEXT", nullable: false),
                    SupervisorEmail = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkExperienceLogbooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkExperienceLogbooks_LearnerRegistrations_LearnerRegistrationId",
                        column: x => x.LearnerRegistrationId,
                        principalTable: "LearnerRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WorkExperienceLogbookEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkExperienceLogbookId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectCode = table.Column<string>(type: "TEXT", nullable: false),
                    TopicCode = table.Column<string>(type: "TEXT", nullable: false),
                    TopicDescription = table.Column<string>(type: "TEXT", nullable: false),
                    EntryDate = table.Column<string>(type: "TEXT", nullable: false),
                    Signature = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkExperienceLogbookEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkExperienceLogbookEntries_WorkExperienceLogbooks_WorkExperienceLogbookId",
                        column: x => x.WorkExperienceLogbookId,
                        principalTable: "WorkExperienceLogbooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkExperienceLogbookEntries_WorkExperienceLogbookId_SortOrder",
                table: "WorkExperienceLogbookEntries",
                columns: new[] { "WorkExperienceLogbookId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkExperienceLogbooks_LearnerRegistrationId",
                table: "WorkExperienceLogbooks",
                column: "LearnerRegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkExperienceLogbooks_QualificationId_LearnerRegistrationId_UpdatedAtUtc",
                table: "WorkExperienceLogbooks",
                columns: new[] { "QualificationId", "LearnerRegistrationId", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkExperienceLogbookEntries");

            migrationBuilder.DropTable(
                name: "WorkExperienceLogbooks");
        }
    }
}
