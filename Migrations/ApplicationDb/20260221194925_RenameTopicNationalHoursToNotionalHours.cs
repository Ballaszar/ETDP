using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class RenameTopicNationalHoursToNotionalHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "NationalHours",
                table: "Topics",
                newName: "NotionalHours");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "NotionalHours",
                table: "Topics",
                newName: "NationalHours");
        }
    }
}
