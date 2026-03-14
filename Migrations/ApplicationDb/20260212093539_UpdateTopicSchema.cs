using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class UpdateTopicSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Topics",
                newName: "TopicPurpose");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "Topics",
                newName: "TopicDescription");

            migrationBuilder.AddColumn<string>(
                name: "TopicCode",
                table: "Topics",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TopicCredits",
                table: "Topics",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TopicPercentage",
                table: "Topics",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TopicCode",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "TopicCredits",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "TopicPercentage",
                table: "Topics");

            migrationBuilder.RenameColumn(
                name: "TopicPurpose",
                table: "Topics",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "TopicDescription",
                table: "Topics",
                newName: "Description");
        }
    }
}
