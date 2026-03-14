using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETDP.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class UpdateModelsForNewFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Workbooks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Version",
                table: "Workbooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Topics",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "Topics",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Subtopics",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "Subtopics",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Qualifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndDate",
                table: "Qualifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Level",
                table: "Qualifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartDate",
                table: "Qualifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Qualifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Date",
                table: "LessonPlans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "LessonPlans",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "LessonPlans",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "LearnerGuides",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Version",
                table: "LearnerGuides",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "KnowledgeQuestionnaires",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Version",
                table: "KnowledgeQuestionnaires",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Females",
                table: "Demographics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Males",
                table: "Demographics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Other",
                table: "Demographics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Total",
                table: "Demographics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CriteriaType",
                table: "AssessmentCriteria",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Weight",
                table: "AssessmentCriteria",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Activities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "Activities",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "Workbooks");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Workbooks");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Subtopics");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "Subtopics");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "LessonPlans");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "LessonPlans");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "LessonPlans");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "LearnerGuides");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "LearnerGuides");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "KnowledgeQuestionnaires");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "KnowledgeQuestionnaires");

            migrationBuilder.DropColumn(
                name: "Females",
                table: "Demographics");

            migrationBuilder.DropColumn(
                name: "Males",
                table: "Demographics");

            migrationBuilder.DropColumn(
                name: "Other",
                table: "Demographics");

            migrationBuilder.DropColumn(
                name: "Total",
                table: "Demographics");

            migrationBuilder.DropColumn(
                name: "CriteriaType",
                table: "AssessmentCriteria");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "AssessmentCriteria");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "Activities");
        }
    }
}
