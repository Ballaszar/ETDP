using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddTopicPeriodsManualOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PeriodsPerTopicManualOverride",
                table: "Topics",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PeriodsPerTopicManualOverride",
                table: "Topics");
        }
    }
}
