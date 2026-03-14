using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Text;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TemplatesController : ControllerBase
    {
        private static readonly string[] LessonPlanContentHeaders =
        {
            "QualificationCode",
            "QualificationDescription",
            "SubjectCode",
            "SubjectDescription",
            "ModuleCode",
            "TopicCode",
            "TopicName",
            "AssessmentCriterionCode",
            "AssessmentCriterion",
            "LPN",
            "LPNPriorityKey",
            "LessonPlanTitle",
            "LessonPlanContent",
            "LearningObjectives",
            "PracticalActivities",
            "AssessmentMethod",
            "EvidenceRequired",
            "ToolsAndEquipment",
            "PPEAndSafety",
            "DurationMinutes",
            "BloomLevel",
            "SourceReference",
            "Keywords"
        };

        private static readonly string[] LessonPlanContentSampleRow =
        {
            "90400",
            "Fitter and Turner Curriculum (New Intake)",
            "KM-01-KT01",
            "Introduction to the Fitting and Turning Trade",
            "KM-01",
            "KT0101",
            "Responsibilities and work setting",
            "AC1",
            "Roles and responsibilities in a workshop are explained accurately.",
            "LPN 1",
            "1",
            "Workshop roles and responsibilities",
            "Demonstrate the roles of fitter and turner personnel and explain typical workshop workflow with safety checkpoints.",
            "Describe roles, interpret workflow, explain safe conduct.",
            "Group discussion, tool identification walk-through, role-play briefing.",
            "Questioning + practical observation checklist.",
            "Learner explains role boundaries, workflow sequence, and safety duties.",
            "PPE set, sample work order, toolbox talk checklist.",
            "Mandatory PPE, lockout awareness, housekeeping requirements.",
            "90",
            "Understand",
            "Workbook Topic KT0101 + workshop SOP v1",
            "roles;workshop;responsibility;safety"
        };

        private static string? ResolveTemplate(params string[] names)
        {
            foreach (var root in GetTemplateRoots())
            {
                foreach (var name in names)
                {
                    var path = Path.Combine(root, name);
                    if (System.IO.File.Exists(path)) return path;
                }
            }
            return null;
        }

        private static List<string> GetTemplateRoots()
        {
            var roots = new List<string>();
            AddTemplateRoot(roots, Path.Combine(Directory.GetCurrentDirectory(), "Imports", "ExcelCSVTemplates"));
            AddTemplateRoot(roots, Path.Combine(AppContext.BaseDirectory, "Imports", "ExcelCSVTemplates"));
            AddTemplateRoot(roots, Path.Combine(AppContext.BaseDirectory, "..", "Imports", "ExcelCSVTemplates"));
            AddTemplateRoot(roots, Path.Combine(AppContext.BaseDirectory, "..", "..", "Imports", "ExcelCSVTemplates"));
            AddTemplateRoot(roots, Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Imports", "ExcelCSVTemplates"));
            AddTemplateRoot(roots, @"E:\ETDP\ETDP\Imports\ExcelCSVTemplates");
            AddTemplateRoot(roots, @"C:\ETDP\ETDP\Imports\ExcelCSVTemplates");
            return roots;
        }

        private static void AddTemplateRoot(List<string> roots, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath)) return;

            foreach (var existing in roots)
            {
                if (string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            roots.Add(fullPath);
        }

        [HttpGet("Subjects")]
        public IActionResult Subjects()
        {
            var path = ResolveTemplate("Subjects.csv", "SubjectsV2.csv");
            if (path == null) return NotFound("Template not found: Subjects.csv or SubjectsV2.csv");
            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, "text/csv", "Subjects.csv");
        }

        [HttpGet("Topics")]
        public IActionResult Topics()
        {
            var path = ResolveTemplate("Topics.csv", "TopicsV2.csv");
            if (path == null) return NotFound("Template not found: Topics.csv or TopicsV2.csv");
            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, "text/csv", "Topics.csv");
        }

        [HttpGet("LessonPlan")]
        public IActionResult LessonPlan()
        {
            var path = ResolveTemplate("Lesson PLan.csv", "Lesson Plan.csv", "LessonPlan.csv");
            if (path == null) return NotFound("Template not found: Lesson PLan.csv");
            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, "text/csv", "LessonPlan.csv");
        }

        [HttpGet("LecturerToolkit")]
        public IActionResult LecturerToolkit()
        {
            var path = ResolveTemplate("Lesson PLan.csv", "Lesson Plan.csv", "LessonPlan.csv");
            if (path == null) return NotFound("Template not found: Lesson PLan.csv");
            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, "text/csv", "Lesson PLan.csv");
        }

        [HttpGet("Outcomes")]
        public IActionResult Outcomes()
        {
            var path = ResolveTemplate("OutcomesV2.csv", "Outcomes.csv");
            if (path != null)
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                return File(bytes, "text/csv", "Outcomes.csv");
            }

            var header = "Qualification Code;Phases Code;Subject Description;Outcome Code;Outcome Description;Outcome Order\n";
            var fallback = Encoding.UTF8.GetBytes(header);
            return File(fallback, "text/csv", "Outcomes.csv");
        }

        [HttpGet("Phases")]
        public IActionResult Phases()
        {
            var path = ResolveTemplate("Phases.csv", "CurriculumPhases.csv");
            if (path != null)
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                return File(bytes, "text/csv", "Phases.csv");
            }

            var header = "Qualification Code;Learning Phases;Phases Code;Phases Description;Phases Purpose;Phases Credits;Phases NQF Level;Phases Percentage\n";
            var fallback = Encoding.UTF8.GetBytes(header);
            return File(fallback, "text/csv", "Phases.csv");
        }

        [HttpGet("LessonPlanContentXlsx")]
        public IActionResult LessonPlanContentXlsx()
        {
            var bytes = BuildLessonPlanContentTemplateXlsx();
            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "LessonPlan_Content_Template.xlsx");
        }

        private static byte[] BuildLessonPlanContentTemplateXlsx()
        {
            using var ms = new MemoryStream();
            using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, true))
            {
                var workbookPart = doc.AddWorkbookPart();
                workbookPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();
                var sheets = workbookPart.Workbook.AppendChild(new Sheets());

                var instructionPart = workbookPart.AddNewPart<WorksheetPart>();
                instructionPart.Worksheet = new Worksheet(new SheetData());
                var instructionData = instructionPart.Worksheet.GetFirstChild<SheetData>()!;
                AppendTextRow(instructionData, "Lesson Plan Content Template - Instructions");
                AppendTextRow(instructionData, "1) Fill all required columns exactly as header names define.");
                AppendTextRow(instructionData, "2) Mandatory fields: QualificationCode, SubjectCode, TopicCode, AssessmentCriterion, LPN, LessonPlanContent.");
                AppendTextRow(instructionData, "3) Use one row per lesson-plan unit for one assessment criterion.");
                AppendTextRow(instructionData, "4) Keep codes normalized (e.g., KM-01-KT01, KT0101, AC1, LPN 1).");
                AppendTextRow(instructionData, "5) Optional: set LPNPriorityKey as numeric order (1,2,3...). If blank, LPN numeric value is used.");
                AppendTextRow(instructionData, "6) DurationMinutes must be numeric.");
                AppendTextRow(instructionData, "7) Avoid PDF artefacts in content: no page numbers, dot leaders, or author/footer lines.");
                AppendTextRow(instructionData, "8) Save as .xlsx.");

                var dataPart = workbookPart.AddNewPart<WorksheetPart>();
                dataPart.Worksheet = new Worksheet(new SheetData());
                var dataSheet = dataPart.Worksheet.GetFirstChild<SheetData>()!;
                AppendTextRow(dataSheet, LessonPlanContentHeaders);
                AppendTextRow(dataSheet, LessonPlanContentSampleRow);

                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(instructionPart),
                    SheetId = 1U,
                    Name = "1 - Instructions"
                });
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(dataPart),
                    SheetId = 2U,
                    Name = "2 - LessonPlanContent"
                });

                workbookPart.Workbook.Save();
            }

            ms.Position = 0;
            return ms.ToArray();
        }

        private static void AppendTextRow(SheetData sheetData, params string[] values)
        {
            var row = new Row();
            foreach (var v in values)
            {
                row.Append(new Cell
                {
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(v ?? string.Empty))
                });
            }
            sheetData.Append(row);
        }
    }
}
