using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class UpdateSubjectSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Subjects",
                newName: "SubjectPurpose");

            migrationBuilder.AddColumn<string>(
                name: "SubjectCode",
                table: "Subjects",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SubjectCredits",
                table: "Subjects",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectDescription",
                table: "Subjects",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SubjectNQFLevel",
                table: "Subjects",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubjectPercentage",
                table: "Subjects",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubjectCode",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "SubjectCredits",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "SubjectDescription",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "SubjectNQFLevel",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "SubjectPercentage",
                table: "Subjects");

            migrationBuilder.RenameColumn(
                name: "SubjectPurpose",
                table: "Subjects",
                newName: "Name");
        }
    }
}
