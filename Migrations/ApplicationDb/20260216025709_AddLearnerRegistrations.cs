using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddLearnerRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LearnerRegistrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QualificationId = table.Column<int>(type: "INTEGER", nullable: true),
                    SdpAccreditationNumber = table.Column<string>(type: "TEXT", nullable: true),
                    NationalId = table.Column<string>(type: "TEXT", nullable: true),
                    LearnerAlternateId = table.Column<string>(type: "TEXT", nullable: true),
                    AlternateIdType = table.Column<string>(type: "TEXT", nullable: true),
                    LearnerLastName = table.Column<string>(type: "TEXT", nullable: true),
                    LearnerFirstName = table.Column<string>(type: "TEXT", nullable: true),
                    LearnerMiddleName = table.Column<string>(type: "TEXT", nullable: true),
                    LearnerTitle = table.Column<string>(type: "TEXT", nullable: true),
                    LearnerBirthDate = table.Column<string>(type: "TEXT", nullable: true),
                    EquityCode = table.Column<string>(type: "TEXT", nullable: true),
                    NationalityCode = table.Column<string>(type: "TEXT", nullable: true),
                    HomeLanguageCode = table.Column<string>(type: "TEXT", nullable: true),
                    GenderCode = table.Column<string>(type: "TEXT", nullable: true),
                    CitizenStatusCode = table.Column<string>(type: "TEXT", nullable: true),
                    SocioeconomicCode = table.Column<string>(type: "TEXT", nullable: true),
                    DisabilityCode = table.Column<string>(type: "TEXT", nullable: true),
                    DisabilityRating = table.Column<string>(type: "TEXT", nullable: true),
                    ImmigrantStatus = table.Column<string>(type: "TEXT", nullable: true),
                    HomeAddress1 = table.Column<string>(type: "TEXT", nullable: true),
                    HomeAddress2 = table.Column<string>(type: "TEXT", nullable: true),
                    HomeAddress3 = table.Column<string>(type: "TEXT", nullable: true),
                    PostalAddress1 = table.Column<string>(type: "TEXT", nullable: true),
                    PostalAddress2 = table.Column<string>(type: "TEXT", nullable: true),
                    PostalAddress3 = table.Column<string>(type: "TEXT", nullable: true),
                    LearnerHomeAddressPostalCode = table.Column<string>(type: "TEXT", nullable: true),
                    LearnerHomeAddressPhysicalCode = table.Column<string>(type: "TEXT", nullable: true),
                    LearnerPhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    LearnerCellPhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    LearnerFaxNumber = table.Column<string>(type: "TEXT", nullable: true),
                    LearnerEmailAddress = table.Column<string>(type: "TEXT", nullable: true),
                    ProvinceCode = table.Column<string>(type: "TEXT", nullable: true),
                    StatssaAreaCode = table.Column<string>(type: "TEXT", nullable: true),
                    PopiActAgree = table.Column<string>(type: "TEXT", nullable: true),
                    PopiActDate = table.Column<string>(type: "TEXT", nullable: true),
                    SkillsProgrammeId = table.Column<string>(type: "TEXT", nullable: true),
                    EmploymentStatus = table.Column<string>(type: "TEXT", nullable: true),
                    LearnerEnrolledDate = table.Column<string>(type: "TEXT", nullable: true),
                    DateOfFisa = table.Column<string>(type: "TEXT", nullable: true),
                    FinalFisaResult = table.Column<string>(type: "TEXT", nullable: true),
                    DateSubmittedToQcto = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerRegistrations", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearnerRegistrations");
        }
    }
}
