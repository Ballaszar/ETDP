using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddOutcomesAndQualificationToggle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OutcomeId",
                table: "Topics",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UsesOutcomes",
                table: "Qualifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Outcomes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    OutcomeCode = table.Column<string>(type: "TEXT", nullable: false),
                    OutcomeDescription = table.Column<string>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Outcomes_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Topics_OutcomeId",
                table: "Topics",
                column: "OutcomeId");

            migrationBuilder.CreateIndex(
                name: "IX_Outcomes_SubjectId",
                table: "Outcomes",
                column: "SubjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Topics_Outcomes_OutcomeId",
                table: "Topics",
                column: "OutcomeId",
                principalTable: "Outcomes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Topics_Outcomes_OutcomeId",
                table: "Topics");

            migrationBuilder.DropTable(
                name: "Outcomes");

            migrationBuilder.DropIndex(
                name: "IX_Topics_OutcomeId",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "OutcomeId",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "UsesOutcomes",
                table: "Qualifications");

        }
    }
}
