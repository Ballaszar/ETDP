using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddLearnerRegistrationInstitutionEmployerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LearningInstitutionCityTown",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LearningInstitutionCityTownPhysicalCode",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LearningInstitutionContactPerson",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LearningInstitutionContactPersonEmailAddress",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LearningInstitutionContactPersonPhoneNumber",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LearningInstitutionName",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LearningInstitutionProvince",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LearningInstitutionStreetName",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LearningInstitutionStreetNumber",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkExperienceEmployerCityTown",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkExperienceEmployerCityTownCode",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkExperienceEmployerName",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkExperienceEmployerProvince",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkExperienceEmployerStreetName",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkExperienceEmployerStreetNumber",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkExperienceEmployerSupervisorEmailAddress",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkExperienceEmployerSupervisorName",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkExperienceEmployerSupervisorPhoneNumber",
                table: "LearnerRegistrations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LearningInstitutionCityTown",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "LearningInstitutionCityTownPhysicalCode",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "LearningInstitutionContactPerson",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "LearningInstitutionContactPersonEmailAddress",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "LearningInstitutionContactPersonPhoneNumber",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "LearningInstitutionName",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "LearningInstitutionProvince",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "LearningInstitutionStreetName",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "LearningInstitutionStreetNumber",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "WorkExperienceEmployerCityTown",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "WorkExperienceEmployerCityTownCode",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "WorkExperienceEmployerName",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "WorkExperienceEmployerProvince",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "WorkExperienceEmployerStreetName",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "WorkExperienceEmployerStreetNumber",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "WorkExperienceEmployerSupervisorEmailAddress",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "WorkExperienceEmployerSupervisorName",
                table: "LearnerRegistrations");

            migrationBuilder.DropColumn(
                name: "WorkExperienceEmployerSupervisorPhoneNumber",
                table: "LearnerRegistrations");
        }
    }
}
