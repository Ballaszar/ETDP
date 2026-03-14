using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddDemographicsColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "African",
                table: "Demographics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Asian",
                table: "Demographics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Coloureds",
                table: "Demographics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalNumberOfStudents",
                table: "Demographics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Whites",
                table: "Demographics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WithDisabilities",
                table: "Demographics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "CurriculumPhases",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Sequence",
                table: "CurriculumPhases",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "African",
                table: "Demographics");

            migrationBuilder.DropColumn(
                name: "Asian",
                table: "Demographics");

            migrationBuilder.DropColumn(
                name: "Coloureds",
                table: "Demographics");

            migrationBuilder.DropColumn(
                name: "TotalNumberOfStudents",
                table: "Demographics");

            migrationBuilder.DropColumn(
                name: "Whites",
                table: "Demographics");

            migrationBuilder.DropColumn(
                name: "WithDisabilities",
                table: "Demographics");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "CurriculumPhases");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "CurriculumPhases");
        }
    }
}
