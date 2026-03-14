using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddTopicScheduleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "NationalHours",
                table: "Topics",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PeriodsPerTopic",
                table: "Topics",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SubjectCredits",
                table: "Topics",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NationalHours",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "PeriodsPerTopic",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "SubjectCredits",
                table: "Topics");
        }
    }
}
