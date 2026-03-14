using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class QualificationFullSpecSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Code",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Qualifications");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "Qualifications",
                newName: "LogoPath");

            migrationBuilder.RenameColumn(
                name: "StartDate",
                table: "Qualifications",
                newName: "LearningDateStart");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Qualifications",
                newName: "SeniorLecturer");

            migrationBuilder.RenameColumn(
                name: "Level",
                table: "Qualifications",
                newName: "LearningDateEnd");

            migrationBuilder.RenameColumn(
                name: "EndDate",
                table: "Qualifications",
                newName: "DeanPrincipalCEO");

            migrationBuilder.AddColumn<string>(
                name: "AccreditationNumber",
                table: "Qualifications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Credits",
                table: "Qualifications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LearningInstitutionName",
                table: "Qualifications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NqfLevel",
                table: "Qualifications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "Qualifications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "QualificationDescription",
                table: "Qualifications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "QualificationNumber",
                table: "Qualifications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "QualificationType",
                table: "Qualifications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccreditationNumber",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "Credits",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "LearningInstitutionName",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "NqfLevel",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "QualificationDescription",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "QualificationNumber",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "QualificationType",
                table: "Qualifications");

            migrationBuilder.RenameColumn(
                name: "SeniorLecturer",
                table: "Qualifications",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "LogoPath",
                table: "Qualifications",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "LearningDateStart",
                table: "Qualifications",
                newName: "StartDate");

            migrationBuilder.RenameColumn(
                name: "LearningDateEnd",
                table: "Qualifications",
                newName: "Level");

            migrationBuilder.RenameColumn(
                name: "DeanPrincipalCEO",
                table: "Qualifications",
                newName: "EndDate");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Qualifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Qualifications",
                type: "TEXT",
                nullable: true);
        }
    }
}
