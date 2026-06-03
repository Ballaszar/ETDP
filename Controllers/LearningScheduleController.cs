using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Utils;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.RegularExpressions;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LearningScheduleController : ControllerBase
    {
        private const int PeriodMinutes = 40;
        private const int PeriodsPerDay = 13;
        private static readonly TimeSpan DayStart = new(8, 0, 0);
        private const string ScheduleFont = "Arial Narrow";
        private const string TableBodyFontSizeHalfPt = "18"; // 9 pt
        private const string TableHeaderFontSizeHalfPt = "20"; // 10 pt
        private const string PageNumberFontSizeHalfPt = "20"; // 10 pt
        private const uint PortraitA4PageWidthTwips = 11906U;
        private const uint PortraitA4PageHeightTwips = 16838U;
        private static readonly uint HeaderRowMinHeightTwips = (uint)CentimetresToTwips(0.8d);

        private static readonly int[] ColumnWidthsTwips =
        {
            500,  // Date (vertical)
            460,  // Day (vertical)
            420,  // Period (vertical header, compact body)
            500,  // Time Start (vertical)
            500,  // Time End (vertical)
            600,  // Topic Code (vertical)
            1660, // Topic Description
            300,  // LPN (vertical, tightened)
            2230, // Lesson Plan Description
            2230, // Assessment Criteria Description
            2230, // Lecturer Actions
            2230, // Learner Actions
            2230  // Learning Aids
        };

        private static readonly HashSet<int> VerticalHeaderColumnIndexes = new() { 0, 1, 2, 3, 4, 5, 7 };
        private static readonly HashSet<int> VerticalBodyColumnIndexes = new() { 0, 1, 3, 4, 5, 7 };
        private static readonly HashSet<int> NoWrapColumnIndexes = new() { 0, 1, 2, 3, 4, 5, 7 };

        private readonly ApplicationDbContext _context;
        public LearningScheduleController(ApplicationDbContext context) { _context = context; }

        [HttpGet("download")]
        public IActionResult Download(
            [FromQuery] int? qualificationId = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] int? subjectFromId = null,
            [FromQuery] int? subjectToId = null)
        {
            var subjectFilters = ResolveSubjectCodeRange(qualificationId, subjectFromId, subjectToId);
            var rows = BuildRows(qualificationId, startDate, subjectFilters);
            if (rows.Count == 0) return BadRequest("No toolkit data found for learning schedule export.");
            var suffix = BuildScheduleSuffix(qualificationId, subjectFilters);
            return File(BuildCsvBytes(rows), "text/csv", $"learning_schedule{suffix}.csv");
        }

        [HttpGet("download-docx")]
        public IActionResult DownloadDocx(
            [FromQuery] int? qualificationId = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] int? subjectFromId = null,
            [FromQuery] int? subjectToId = null)
        {
            var subjectFilters = ResolveSubjectCodeRange(qualificationId, subjectFromId, subjectToId);
            var rows = BuildRows(qualificationId, startDate, subjectFilters);
            if (rows.Count == 0) return BadRequest("No toolkit data found for learning schedule export.");
            var qualification = ResolveQualification(qualificationId);
            var suffix = BuildScheduleSuffix(qualificationId, subjectFilters);
            return File(BuildDocxBytes(rows, qualificationId, qualification), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"learning_schedule{suffix}.docx");
        }

        [HttpGet("save")]
        public IActionResult SaveCsv(
            [FromQuery] int? qualificationId = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] int? subjectFromId = null,
            [FromQuery] int? subjectToId = null)
        {
            var subjectFilters = ResolveSubjectCodeRange(qualificationId, subjectFromId, subjectToId);
            var rows = BuildRows(qualificationId, startDate, subjectFilters);
            if (rows.Count == 0) return BadRequest("No toolkit data found for learning schedule export.");

            var qualification = ResolveQualification(qualificationId);
            if (qualification == null) return BadRequest("No qualification available for learning schedule export.");

            var suffix = BuildScheduleSuffix(qualificationId, subjectFilters);
            var fileName = $"learning_schedule{suffix}.csv";
            var savedPath = LearningMaterialWorkspacePaths.SaveBytes(
                qualification,
                qualification.Id,
                "Learning Schedule",
                fileName,
                BuildCsvBytes(rows));

            return Ok(new
            {
                fileName,
                savedPath,
                folderPath = Path.GetDirectoryName(savedPath)
            });
        }

        [HttpGet("save-docx")]
        public IActionResult SaveDocx(
            [FromQuery] int? qualificationId = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] int? subjectFromId = null,
            [FromQuery] int? subjectToId = null)
        {
            var subjectFilters = ResolveSubjectCodeRange(qualificationId, subjectFromId, subjectToId);
            var rows = BuildRows(qualificationId, startDate, subjectFilters);
            if (rows.Count == 0) return BadRequest("No toolkit data found for learning schedule export.");

            var qualification = ResolveQualification(qualificationId);
            if (qualification == null) return BadRequest("No qualification available for learning schedule export.");

            var suffix = BuildScheduleSuffix(qualificationId, subjectFilters);
            var fileName = $"learning_schedule{suffix}.docx";
            var savedPath = LearningMaterialWorkspacePaths.SaveBytes(
                qualification,
                qualification.Id,
                "Learning Schedule",
                fileName,
                BuildDocxBytes(rows, qualificationId, qualification));

            return Ok(new
            {
                fileName,
                savedPath,
                folderPath = Path.GetDirectoryName(savedPath)
            });
        }

        private Qualification? ResolveQualification(int? qualificationId)
        {
            if (qualificationId.HasValue && qualificationId.Value > 0)
            {
                var scoped = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId.Value);
                if (scoped != null) return scoped;
            }
            return _context.Qualifications.OrderBy(q => q.Id).FirstOrDefault();
        }

        private string ResolveCoverLecturerDisplayName(int? qualificationId, Qualification? qualification)
        {
            var fromQualification = (qualification?.SeniorLecturer ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(fromQualification)) return fromQualification;

            var fromPrincipal = (qualification?.DeanPrincipalCEO ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(fromPrincipal)) return fromPrincipal;

            var qid = qualification?.Id ?? qualificationId.GetValueOrDefault();
            if (qid > 0)
            {
                var fromToolkit = _context.LecturerToolkitEntries
                    .Where(x => x.QualificationsId == qid && !string.IsNullOrWhiteSpace(x.LecturerName))
                    .OrderBy(x => x.Id)
                    .Select(x => x.LecturerName)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(fromToolkit)) return fromToolkit.Trim();
            }

            return "LECTURER NAME SURNAME";
        }

        private byte[] BuildCsvBytes(IReadOnlyList<ScheduleRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Date,Day,Period,TimeStart,TimeEnd,TopicCode,TopicDescription,LPN,LessonPlanDescription,AssessmentCriteriaDescription,LecturerActions,LearnerActions,LearningAids");
            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",",
                    EscapeCsv(r.Date),
                    EscapeCsv(r.Day),
                    r.Period.ToString(),
                    EscapeCsv(r.TimeStart),
                    EscapeCsv(r.TimeEnd),
                    EscapeCsv(r.TopicCode),
                    EscapeCsv(r.TopicDescription),
                    EscapeCsv(r.Lpn),
                    EscapeCsv(r.LessonPlanDescription),
                    EscapeCsv(r.AssessmentCriteriaDescription),
                    EscapeCsv(r.LecturerActions),
                    EscapeCsv(r.LearnerActions),
                    EscapeCsv(r.LearningAids)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private byte[] BuildDocxBytes(IReadOnlyList<ScheduleRow> rows, int? qualificationId, Qualification? qualification)
        {
            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                var body = main.Document.Body ?? (main.Document.Body = new Body());
                var settingsPart = main.AddNewPart<DocumentSettingsPart>();
                settingsPart.Settings = new Settings(new UpdateFieldsOnOpen() { Val = true });
                settingsPart.Settings.Save();
                var headerPart = main.AddNewPart<HeaderPart>();
                headerPart.Header = new Header(BuildPageNumberHeaderParagraph());
                headerPart.Header.Save();
                var headerPartId = main.GetIdOfPart(headerPart);

                var lecturerDisplayName = ResolveCoverLecturerDisplayName(qualificationId, qualification);
                AppendCoverPage(body, main, qualification, lecturerDisplayName);
                body.Append(new Paragraph(new Run(new Break() { Type = BreakValues.Page })));
                AppendLegalDisclaimerPage(body, qualification);
                body.Append(BuildPortraitSectionBreakParagraph(headerPartId));

                var table = new Table();
                table.Append(new TableProperties(
                    new TableWidth() { Type = TableWidthUnitValues.Pct, Width = "5000" },
                    new TableLayout() { Type = TableLayoutValues.Fixed },
                    new TableCellMarginDefault(
                        new TopMargin() { Width = "12", Type = TableWidthUnitValues.Dxa },
                        new BottomMargin() { Width = "12", Type = TableWidthUnitValues.Dxa },
                        new TableCellLeftMargin() { Width = 12, Type = TableWidthValues.Dxa },
                        new TableCellRightMargin() { Width = 12, Type = TableWidthValues.Dxa }),
                    new TableBorders(
                        new TopBorder() { Val = BorderValues.Single, Size = 6 },
                        new BottomBorder() { Val = BorderValues.Single, Size = 6 },
                        new LeftBorder() { Val = BorderValues.Single, Size = 6 },
                        new RightBorder() { Val = BorderValues.Single, Size = 6 },
                        new InsideHorizontalBorder() { Val = BorderValues.Single, Size = 6 },
                        new InsideVerticalBorder() { Val = BorderValues.Single, Size = 6 })));
                var tableGrid = new TableGrid();
                foreach (var width in ColumnWidthsTwips)
                {
                    tableGrid.Append(new GridColumn() { Width = width.ToString() });
                }
                table.Append(tableGrid);

                table.Append(MakeRow(new[]
                {
                    "Date","Day","Period","Time Start","Time End","Topic Code","Topic Description","LPN","Lesson Plan Description","Assessment Criteria Description","Lecturer Actions","Learner Actions","Learning Aids"
                }, true));

                foreach (var row in rows)
                {
                    table.Append(MakeRow(BuildDocxRowCells(row), false));
                }

                body.Append(table);
                body.Append(BuildLandscapeSectionProperties(headerPartId));
                main.Document.Save();
            }

            return ms.ToArray();
        }

        private static void AppendCoverPage(Body body, MainDocumentPart main, Qualification? qualification, string lecturerDisplayName)
        {
            var institution = (qualification?.LearningInstitutionName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(institution)) institution = "LEARNING INSTITUTION NAME";
            _ = lecturerDisplayName;
            var coverQualificationLine = BuildCoverQualificationLine(qualification);
            const string coverTextColor = "000000";
            var topBlockStartTwips = CentimetresToTwips(7.0d);
            var titleBlockGapTwips = string.IsNullOrWhiteSpace(coverQualificationLine)
                ? CentimetresToTwips(12.0d)
                : CentimetresToTwips(10.4d);

            var coverLines = new List<DocxCoverPageOverlay.CoverTextLine>
            {
                new()
                {
                    Text = institution.ToUpperInvariant(),
                    FontSizeHalfPt = 52,
                    Bold = true,
                    BeforeTwips = topBlockStartTwips,
                    AfterTwips = 120,
                    ColorHex = coverTextColor
                },
                new()
                {
                    Text = "LEARNING SCHEDULE",
                    FontSizeHalfPt = 48,
                    Bold = true,
                    BeforeTwips = titleBlockGapTwips,
                    AfterTwips = 120,
                    ColorHex = coverTextColor
                }
            };
            if (!string.IsNullOrWhiteSpace(coverQualificationLine))
            {
                coverLines.Add(new DocxCoverPageOverlay.CoverTextLine
                {
                    Text = coverQualificationLine,
                    FontSizeHalfPt = 50,
                    Bold = true,
                    BeforeTwips = 520,
                    AfterTwips = 0,
                    ColorHex = coverTextColor
                });
            }

            coverLines = coverLines
                .OrderBy(line => string.Equals(line.Text, institution.ToUpperInvariant(), StringComparison.Ordinal) ? 0 :
                    string.Equals(line.Text, coverQualificationLine, StringComparison.Ordinal) ? 1 : 2)
                .ToList();

            var appended = DocxCoverPageOverlay.TryAppendCenteredPortraitCoverPage(
                body,
                main,
                ResolveLearningScheduleCoverPath(),
                coverLines,
                PortraitA4PageWidthTwips,
                1U);

            if (appended)
            {
                return;
            }

            body.Append(CoverCenteredParagraph(institution.ToUpperInvariant(), "52", true, before: topBlockStartTwips.ToString(), after: "120"));
            if (!string.IsNullOrWhiteSpace(coverQualificationLine))
            {
                body.Append(CoverCenteredParagraph(coverQualificationLine, "50", true, before: "520", after: "120"));
            }
            body.Append(CoverCenteredParagraph("LEARNING SCHEDULE", "48", true, before: titleBlockGapTwips.ToString(), after: "120"));
        }

        private List<ScheduleRow> BuildRows(int? qualificationId, DateTime? startDate, IReadOnlyList<string>? subjectCodes = null)
        {
            var entriesQuery = _context.LecturerToolkitEntries.AsQueryable();
            if (qualificationId.HasValue && qualificationId.Value > 0)
            {
                entriesQuery = entriesQuery.Where(x => x.QualificationsId == qualificationId.Value);
            }

            var entries = entriesQuery.ToList();
            if (entries.Count == 0) return new List<ScheduleRow>();

            var criteriaIds = entries
                .Where(e => e.AssessmentCriteriaId.HasValue && e.AssessmentCriteriaId.Value > 0)
                .Select(e => e.AssessmentCriteriaId!.Value)
                .Distinct()
                .ToList();

            var criteriaMeta = (from c in _context.AssessmentCriteria
                                join t in _context.Topics on c.TopicId equals t.Id
                                join s in _context.Subjects on t.SubjectId equals s.Id
                                join p in _context.CurriculumPhases on s.CurriculumPhaseId equals p.Id
                                where criteriaIds.Contains(c.Id)
                                   && (!qualificationId.HasValue || s.QualificationId == qualificationId.Value)
                                select new CriteriaMeta
                                {
                                    CriteriaId = c.Id,
                                    CriteriaDescription = c.Description ?? string.Empty,
                                    TopicCode = t.TopicCode ?? string.Empty,
                                    TopicDescription = t.TopicDescription ?? string.Empty,
                                    TopicOrder = t.Order ?? int.MaxValue,
                                    SubjectCode = s.SubjectCode ?? string.Empty,
                                    PhaseSequence = p.Sequence
                                }).ToList();
            var metaByCriteriaId = criteriaMeta.ToDictionary(x => x.CriteriaId, x => x);

            var lessonPlansByCriteria = _context.LessonPlans
                .Where(lp => criteriaIds.Contains(lp.AssessmentCriteriaId))
                .OrderBy(lp => lp.SortOrder)
                .ThenBy(lp => lp.Id)
                .ToList()
                .GroupBy(lp => lp.AssessmentCriteriaId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var prepared = entries.Select(e =>
            {
                var cid = e.AssessmentCriteriaId ?? 0;
                metaByCriteriaId.TryGetValue(cid, out var meta);
                return new PreparedEntry
                {
                    Entry = e,
                    Meta = meta,
                    LpnSort = ParseLpnSort(e.Lpn)
                };
            })
            .OrderBy(x => x.Meta?.PhaseSequence ?? int.MaxValue)
            .ThenBy(x => x.Meta?.SubjectCode ?? x.Entry.SubjectCode ?? string.Empty)
            .ThenBy(x => x.Meta?.TopicOrder ?? int.MaxValue)
            .ThenBy(x => x.Meta?.TopicCode ?? string.Empty)
            .ThenBy(x => x.LpnSort)
            .ThenBy(x => x.Entry.Id)
            .ToList();

            var subjectFilters = subjectCodes?
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => NormalizeSubjectFilter(code))
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct()
                .ToList();
            if (subjectFilters?.Count > 0)
            {
                prepared = prepared
                    .Where(x => subjectFilters.Contains(NormalizeSubjectFilter(x.Meta?.SubjectCode ?? x.Entry.SubjectCode)))
                    .ToList();
            }

            var date = (startDate ?? DateTime.Today).Date;
            var period = 1;
            var rows = new List<ScheduleRow>();
            foreach (var item in prepared)
            {
                var slotStart = DayStart.Add(TimeSpan.FromMinutes((period - 1) * PeriodMinutes));
                var slotEnd = slotStart.Add(TimeSpan.FromMinutes(PeriodMinutes));
                var timeStart = slotStart.ToString(@"hh\:mm");
                var timeEnd = slotEnd.ToString(@"hh\:mm");

                var topicCode = item.Meta?.TopicCode;
                if (string.IsNullOrWhiteSpace(topicCode))
                {
                    topicCode = item.Entry.SubjectCode;
                }

                var topicDescription = item.Meta?.TopicDescription;
                if (string.IsNullOrWhiteSpace(topicDescription))
                {
                    topicDescription = item.Entry.SubjectDescription;
                }

                var acDesc = !string.IsNullOrWhiteSpace(item.Meta?.CriteriaDescription)
                    ? item.Meta!.CriteriaDescription
                    : (item.Entry.AssessmentCriteriaDescription ?? string.Empty);

                rows.Add(new ScheduleRow
                {
                    Date = date.ToString("yyyy-MM-dd"),
                    Day = date.ToString("dddd"),
                    Period = period,
                    TimeStart = timeStart,
                    TimeEnd = timeEnd,
                    TopicCode = topicCode ?? string.Empty,
                    TopicDescription = topicDescription ?? string.Empty,
                    Lpn = NormalizeLpn(item.Entry.Lpn),
                    LessonPlanDescription = ResolveLessonPlanDescription(item.Entry, item.Meta, lessonPlansByCriteria),
                    AssessmentCriteriaDescription = acDesc,
                    LecturerActions = item.Entry.LecturerActions ?? string.Empty,
                    LearnerActions = item.Entry.LearnerActions ?? string.Empty,
                    LearningAids = item.Entry.LearningAids ?? string.Empty
                });

                period++;
                if (period > PeriodsPerDay)
                {
                    period = 1;
                    date = date.AddDays(1);
                }
            }

            return rows;
        }

        private List<Subject> GetQualificationSubjects(int qualificationId)
        {
            return _context.Subjects
                .Where(s => s.QualificationId == qualificationId)
                .OrderBy(s => s.SubjectCode)
                .ThenBy(s => s.SubjectDescription)
                .ToList();
        }

        private List<Subject> ResolveSubjectRange(int qualificationId, int? subjectFromId, int? subjectToId)
        {
            var subjects = GetQualificationSubjects(qualificationId);
            if (subjects.Count == 0) return new List<Subject>();

            var fromIndex = 0;
            var toIndex = subjects.Count - 1;

            if (subjectFromId.HasValue && subjectFromId.Value > 0)
            {
                fromIndex = subjects.FindIndex(s => s.Id == subjectFromId.Value);
                if (fromIndex < 0) return new List<Subject>();
            }

            if (subjectToId.HasValue && subjectToId.Value > 0)
            {
                toIndex = subjects.FindIndex(s => s.Id == subjectToId.Value);
                if (toIndex < 0) return new List<Subject>();
            }

            if (fromIndex > toIndex) (fromIndex, toIndex) = (toIndex, fromIndex);

            return subjects.Skip(fromIndex).Take(toIndex - fromIndex + 1).ToList();
        }

        private List<string> ResolveSubjectCodeRange(int? qualificationId, int? subjectFromId, int? subjectToId)
        {
            if (!qualificationId.HasValue || qualificationId.Value <= 0) return new List<string>();
            var range = ResolveSubjectRange(qualificationId.Value, subjectFromId, subjectToId);
            if (range.Count == 0) return new List<string>();
            return range
                .Select(s => NormalizeSubjectFilter(s.SubjectCode))
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToList();
        }

        private static string BuildScheduleSuffix(int? qualificationId, IReadOnlyList<string>? subjectCodes)
        {
            var parts = new List<string>();
            if (qualificationId.HasValue && qualificationId.Value > 0)
            {
                parts.Add($"Q{qualificationId.Value}");
            }

            if (subjectCodes != null && subjectCodes.Count > 0)
            {
                var normalized = subjectCodes
                    .Select(code => NormalizeSubjectFilter(code))
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .ToList();
                if (normalized.Count > 0)
                {
                    var description = normalized.Count == 1
                        ? normalized[0]
                        : $"{normalized.First()}-{normalized.Last()}";
                    parts.Add($"S{description}");
                }
            }

            return parts.Count == 0 ? string.Empty : "_" + string.Join("_", parts);
        }

        private static string NormalizeSubjectFilter(string? subjectCode)
        {
            if (string.IsNullOrWhiteSpace(subjectCode)) return string.Empty;
            var cleaned = Regex.Replace(subjectCode.Trim(), @"\s+", "").ToUpperInvariant();
            return cleaned;
        }

        private static int ParseLpnSort(string? value)
        {
            var raw = (value ?? string.Empty).Trim();
            if (int.TryParse(raw, out var direct)) return direct;
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var parsed)) return parsed;
            return int.MaxValue - 1;
        }

        private static string NormalizeLpn(string? value)
        {
            var raw = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return "LPN 1";
            if (raw.StartsWith("LPN", StringComparison.OrdinalIgnoreCase)) return raw.ToUpperInvariant();
            return $"LPN {raw}";
        }

        private static string[] BuildDocxRowCells(ScheduleRow row)
        {
            return new[]
            {
                CompactDate(row.Date),
                AbbreviateDay(row.Day),
                row.Period.ToString(),
                row.TimeStart,
                row.TimeEnd,
                row.TopicCode,
                row.TopicDescription,
                row.Lpn,
                row.LessonPlanDescription,
                row.AssessmentCriteriaDescription,
                row.LecturerActions,
                row.LearnerActions,
                row.LearningAids
            };
        }

        private static string CompactDate(string? value)
        {
            if (DateTime.TryParse(value, out var parsed))
            {
                return parsed.ToString("yy-MM-dd");
            }
            return (value ?? string.Empty).Trim();
        }

        private static string AbbreviateDay(string? value)
        {
            var raw = (value ?? string.Empty).Trim();
            if (raw.Length <= 3) return raw;
            return raw.Substring(0, 3);
        }

        private static string ResolveLessonPlanDescription(
            LecturerToolkitEntry entry,
            CriteriaMeta? meta,
            Dictionary<int, List<LessonPlan>> lessonPlansByCriteria)
        {
            var criteriaDescription = !string.IsNullOrWhiteSpace(meta?.CriteriaDescription)
                ? meta!.CriteriaDescription
                : (entry.AssessmentCriteriaDescription ?? string.Empty);

            var candidate = (entry.LessonPlanDescription ?? string.Empty).Trim();
            var looksWrong = string.IsNullOrWhiteSpace(candidate) || SameSemanticValue(candidate, criteriaDescription);

            if (!looksWrong)
            {
                return candidate;
            }

            var criteriaId = entry.AssessmentCriteriaId ?? 0;
            if (criteriaId > 0 && lessonPlansByCriteria.TryGetValue(criteriaId, out var lessonPlans))
            {
                var lpnSort = ParseLpnSort(entry.Lpn);
                var exact = lessonPlans.FirstOrDefault(lp => lp.SortOrder == lpnSort && !string.IsNullOrWhiteSpace(lp.Title));
                var byTitle = exact ?? lessonPlans.FirstOrDefault(lp => !string.IsNullOrWhiteSpace(lp.Title));
                if (byTitle != null)
                {
                    return byTitle.Title.Trim();
                }

                var firstSentence = lessonPlans
                    .Select(lp => FirstSentence(lp.Content))
                    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                if (!string.IsNullOrWhiteSpace(firstSentence))
                {
                    return firstSentence;
                }
            }

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }

            if (!string.IsNullOrWhiteSpace(meta?.TopicDescription))
            {
                return $"Lesson activities for {meta.TopicDescription}";
            }
            return "Lesson activities";
        }

        private static string FirstSentence(string? text)
        {
            var cleaned = (text ?? string.Empty).Trim();
            if (cleaned.Length == 0) return string.Empty;
            var match = Regex.Match(cleaned, @"^(.{20,220}?[\.!\?])(\s|$)");
            if (match.Success) return match.Groups[1].Value.Trim();
            return cleaned.Length > 180 ? cleaned.Substring(0, 180).Trim() + "..." : cleaned;
        }

        private static bool SameSemanticValue(string a, string b)
        {
            var na = Regex.Replace((a ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
            var nb = Regex.Replace((b ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
            if (na.Length == 0 || nb.Length == 0) return false;
            return na == nb;
        }

        private static TableRow MakeRow(IEnumerable<string> cells, bool header)
        {
            var row = new TableRow();
            if (header)
            {
                row.AppendChild(new TableRowProperties(
                    new TableHeader(),
                    new TableRowHeight { Val = HeaderRowMinHeightTwips, HeightType = HeightRuleValues.AtLeast }));
            }

            var values = cells?.ToList() ?? new List<string>();
            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i] ?? string.Empty;
                var isPeriodColumn = i == 2;
                var isVerticalColumn = (header && VerticalHeaderColumnIndexes.Contains(i))
                    || (!header && VerticalBodyColumnIndexes.Contains(i));
                var runProps = new RunProperties
                {
                    FontSize = new FontSize() { Val = header ? TableHeaderFontSizeHalfPt : TableBodyFontSizeHalfPt },
                    RunFonts = new RunFonts() { Ascii = ScheduleFont, HighAnsi = ScheduleFont }
                };
                if (header)
                {
                    runProps.Bold = new Bold();
                }

                var paragraphAlignment = header || isVerticalColumn || isPeriodColumn
                    ? JustificationValues.Center
                    : JustificationValues.Left;
                var spacing = isVerticalColumn
                    ? new SpacingBetweenLines { Before = "0", After = "0" }
                    : new SpacingBetweenLines { Before = "0", After = "0", Line = "200", LineRule = LineSpacingRuleValues.Auto };
                var paraProps = new ParagraphProperties(new Justification
                {
                    Val = paragraphAlignment
                },
                spacing,
                new Indentation { Left = "0", Right = "0" });
                var para = new Paragraph(paraProps, new Run(runProps, new Text(value) { Space = SpaceProcessingModeValues.Preserve }));

                var width = i >= 0 && i < ColumnWidthsTwips.Length
                    ? ColumnWidthsTwips[i]
                    : 1200;
                var cellProps = new TableCellProperties(
                    new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = width.ToString() });
                if (header)
                {
                    cellProps.Append(new Shading() { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "EDEDED" });
                }
                cellProps.Append(new TableCellVerticalAlignment
                {
                    Val = header || isVerticalColumn || isPeriodColumn
                        ? TableVerticalAlignmentValues.Center
                        : TableVerticalAlignmentValues.Top
                });
                if (NoWrapColumnIndexes.Contains(i))
                {
                    cellProps.Append(new NoWrap());
                }

                if (isVerticalColumn)
                {
                    cellProps.Append(new TextDirection { Val = TextDirectionValues.TopToBottomRightToLeft });
                }

                row.Append(new TableCell(cellProps, para));
            }

            return row;
        }

        private static Paragraph BuildPortraitSectionBreakParagraph(string headerPartId)
        {
            var sectionProperties = new SectionProperties(
                new HeaderReference() { Type = HeaderFooterValues.Default, Id = headerPartId },
                new SectionType() { Val = SectionMarkValues.NextPage },
                new PageSize() { Orient = PageOrientationValues.Portrait, Width = PortraitA4PageWidthTwips, Height = PortraitA4PageHeightTwips },
                new PageMargin
                {
                    Top = 720,
                    Bottom = 720,
                    Left = 720,
                    Right = 720,
                    Header = 240,
                    Footer = 240,
                    Gutter = 0
                });

            return new Paragraph(
                new ParagraphProperties(sectionProperties),
                new Run(new Text(string.Empty)));
        }

        private static SectionProperties BuildLandscapeSectionProperties(string headerPartId)
        {
            return new SectionProperties(
                new HeaderReference() { Type = HeaderFooterValues.Default, Id = headerPartId },
                new PageSize() { Orient = PageOrientationValues.Landscape, Width = 16838U, Height = 11906U },
                new PageMargin
                {
                    Top = 540,
                    Bottom = 540,
                    Left = 360,
                    Right = 360,
                    Header = 240,
                    Footer = 240,
                    Gutter = 0
                });
        }

        private static int CentimetresToTwips(double centimetres)
        {
            return Math.Max(0, (int)Math.Round(centimetres * 1440d / 2.54d));
        }

        private static string BuildCoverQualificationLine(Qualification? qualification)
        {
            var qualificationDescription = (qualification?.QualificationDescription ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(qualificationDescription)) return qualificationDescription;
            return (qualification?.QualificationNumber ?? string.Empty).Trim();
        }

        private static Paragraph BuildPageNumberHeaderParagraph()
        {
            var baseRunProps = new RunProperties(
                new Bold(),
                new FontSize() { Val = PageNumberFontSizeHalfPt },
                new RunFonts() { Ascii = ScheduleFont, HighAnsi = ScheduleFont });

            var paragraph = new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Right }),
                new Run((RunProperties)baseRunProps.CloneNode(true), new Text("Page ") { Space = SpaceProcessingModeValues.Preserve })
            );

            paragraph.Append(BuildPageNumberField("PAGE", baseRunProps));
            paragraph.Append(new Run((RunProperties)baseRunProps.CloneNode(true), new Text(" of ") { Space = SpaceProcessingModeValues.Preserve }));
            paragraph.Append(BuildPageNumberField("NUMPAGES", baseRunProps));
            return paragraph;
        }

        private static SimpleField BuildPageNumberField(string instruction, RunProperties runProps)
        {
            var field = new SimpleField
            {
                Instruction = $" {instruction} \\* MERGEFORMAT "
            };
            field.Append(new Run((RunProperties)runProps.CloneNode(true), new Text("1")));
            return field;
        }

        private static void AppendLegalDisclaimerPage(Body body, Qualification? qualification)
        {
            var year = DateTime.Now.Year;
            var institution = (qualification?.LearningInstitutionName ?? string.Empty).Trim();

            body.Append(ScheduleHeadingPara("DISCLAIMER", 15));
            body.Append(ScheduleBodyPara(
                $"ETDP Courseware Release ETDP RSA PATENT 004/026785. (C) {year} by Dr P.C. Wepener. This document is generated by the ETDP App under the authority and final approval of the authorised learning-material owner. Neither Dr P.C. Wepener nor the ETDP App is accountable or liable for the correctness, completeness, factual, or academic correctness of this document. The accredited learning institution should be contacted for content inquiries, sources, references, or citations.",
                18));

            body.Append(ScheduleHeadingPara("NOTICE OF RIGHTS", 11));
            body.Append(ScheduleBodyPara("No part of this publication may be reproduced, transmitted, transcribed, stored in a retrieval system, or translated into any language or computer language, in any form or by any means, electronic, mechanical, magnetic, optical, chemical, manual, or otherwise, without prior written permission from the branded learning institution that owns the legal and intellectual property rights to the content of this document.", 18));

            body.Append(ScheduleHeadingPara("TRADEMARK NOTICE", 11));
            body.Append(ScheduleBodyPara("Throughout this courseware title, trademark names may be used. Rather than placing a trademark symbol at every occurrence, names are used in an editorial manner for the benefit of the trademark owner, with no intention of infringement.", 18));

            body.Append(ScheduleHeadingPara("NOTICE OF LIABILITY", 11));
            body.Append(ScheduleBodyPara("The information in this courseware title is distributed on an 'as is' basis, without warranty. While every precaution has been taken in preparation of this courseware, neither Dr P.C. Wepener nor the ETDP App shall have any liability to any person or entity for any loss or damage caused, or alleged to be caused, directly or indirectly by the instructions in this document or by the learning design and development processes described in it.", 18));

            body.Append(ScheduleHeadingPara("DISCLAIMER", 11));
            body.Append(ScheduleBodyPara("A sincere effort has been made to ensure typology accuracy of the material; however, no warranty, express or implied, is made regarding quality, correctness, reliability, accuracy, or freedom from error of this document or the products it describes. Data used in examples and sample files may be fictional. Any resemblance to real persons or companies is coincidental.", 18));

            body.Append(ScheduleHeadingPara("TERMS AND CONDITIONS", 11));
            body.Append(ScheduleBodyPara("This document is developed for the learning institution holding a legal permit and may not be resold by the learning institution. Sample versions may be shared but may not be resold to a third party. For licensed users, this document may only be used under the terms of the license agreement between the learning institution and Dr P.C. Wepener.", 18));

            if (!string.IsNullOrWhiteSpace(institution))
            {
                body.Append(ScheduleBodyPara($"Learning Institution: {institution}", 18));
            }
            body.Append(ScheduleBodyPara("PC WEPENER (Ph.D.) BUSINESS MANAGEMENT UJ 2005", 18));
            body.Append(ScheduleBodyPara($"Pretoria, South Africa, {year}.", 18));
        }

        private static Paragraph ScheduleHeadingPara(string text, int sizePt)
        {
            var runProps = new RunProperties
            {
                Bold = new Bold(),
                FontSize = new FontSize() { Val = (sizePt * 2).ToString() },
                RunFonts = new RunFonts() { Ascii = ScheduleFont, HighAnsi = ScheduleFont }
            };
            var paraProps = new ParagraphProperties(
                new Justification { Val = JustificationValues.Left },
                new SpacingBetweenLines() { Before = "60", After = "40" });
            return new Paragraph(paraProps, new Run(runProps, new Text(text ?? string.Empty)));
        }

        private static Paragraph ScheduleBodyPara(string text, int sizeHalfPt)
        {
            var runProps = new RunProperties
            {
                FontSize = new FontSize() { Val = sizeHalfPt.ToString() },
                RunFonts = new RunFonts() { Ascii = ScheduleFont, HighAnsi = ScheduleFont }
            };
            var paraProps = new ParagraphProperties(
                new Justification { Val = JustificationValues.Left },
                new SpacingBetweenLines() { Before = "20", After = "20", Line = "220", LineRule = LineSpacingRuleValues.Auto });
            return new Paragraph(paraProps, new Run(runProps, new Text(text ?? string.Empty)));
        }

        private static Paragraph CoverCenteredParagraph(string text, string fontHalfPt, bool bold, string before = "0", string after = "0")
        {
            var runProps = new RunProperties
            {
                FontSize = new FontSize { Val = fontHalfPt },
                RunFonts = new RunFonts { Ascii = ScheduleFont, HighAnsi = ScheduleFont }
            };
            if (bold) runProps.Bold = new Bold();
            return new Paragraph(
                new ParagraphProperties(
                    new Justification { Val = JustificationValues.Center },
                    new SpacingBetweenLines { Before = before, After = after }),
                new Run(runProps, new Text(text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve }));
        }

        private static Paragraph BuildLogoParagraph(
            MainDocumentPart main,
            string? imagePath,
            string fallbackText,
            long cx,
            long cy,
            uint drawingId)
        {
            var paraProps = new ParagraphProperties(
                new Justification { Val = JustificationValues.Center },
                new SpacingBetweenLines { Before = "20", After = "20" });

            if (!string.IsNullOrWhiteSpace(imagePath) && System.IO.File.Exists(imagePath))
            {
                try
                {
                    var imagePart = main.AddImagePart(ResolveImagePartType(imagePath));
                    using (var stream = System.IO.File.OpenRead(imagePath))
                    {
                        imagePart.FeedData(stream);
                    }
                    var relId = main.GetIdOfPart(imagePart);
                    return new Paragraph(paraProps, new Run(BuildInlineImage(relId, cx, cy, drawingId, Path.GetFileName(imagePath))));
                }
                catch
                {
                    // fallback text below
                }
            }

            return new Paragraph(
                paraProps,
                new Run(
                    new RunProperties(
                        new FontSize { Val = "28" },
                        new RunFonts { Ascii = ScheduleFont, HighAnsi = ScheduleFont }),
                    new Text(fallbackText)));
        }

        private static string? ResolveExistingPath(string? rawPath)
        {
            var raw = (rawPath ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(raw)) return null;

            if (Path.IsPathRooted(raw) && System.IO.File.Exists(raw))
            {
                return raw;
            }

            var direct = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), raw));
            if (System.IO.File.Exists(direct)) return direct;

            return ResolveFromCurrentOrParents(raw, 6);
        }

        private static string? ResolveQctoLogoPath()
        {
            var candidates = new[]
            {
                Path.Combine("Imports", "Logos", "qcto_logo.jpg"),
                Path.Combine("Imports", "Logos", "qcto_logo.jpeg"),
                Path.Combine("Imports", "Logos", "qcto_logo.png"),
                Path.Combine("ETDP", "Imports", "Logos", "qcto_logo.jpg"),
                Path.Combine("ETDP", "Imports", "Logos", "qcto_logo.jpeg"),
                Path.Combine("ETDP", "Imports", "Logos", "qcto_logo.png")
            };

            foreach (var relative in candidates)
            {
                var resolved = ResolveFromCurrentOrParents(relative, 6);
                if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
            }

            var probeRoots = new[]
            {
                Path.Combine("Imports", "Logos"),
                Path.Combine("ETDP", "Imports", "Logos")
            };
            foreach (var probe in probeRoots)
            {
                var root = ResolveFromCurrentOrParents(probe, 6);
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                var match = Directory.GetFiles(root, "*qcto*.*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(path =>
                    {
                        var ext = Path.GetExtension(path).ToLowerInvariant();
                        return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";
                    });
                if (!string.IsNullOrWhiteSpace(match)) return match;
            }

            return null;
        }

        private static string? ResolveLearningScheduleCoverPath()
        {
            var candidates = new[]
            {
                Path.Combine("Imports", "Coverpages", "clean coverpage.jpg"),
                Path.Combine("ETDP", "Imports", "Coverpages", "clean coverpage.jpg")
            };

            foreach (var relative in candidates)
            {
                var resolved = ResolveFromCurrentOrParents(relative, 6);
                if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
            }

            return null;
        }

        private static string? ResolveFromCurrentOrParents(string relativePath, int maxDepth)
        {
            var current = Directory.GetCurrentDirectory();
            for (var depth = 0; depth <= maxDepth; depth++)
            {
                var combined = Path.Combine(current, relativePath);
                if (System.IO.File.Exists(combined) || Directory.Exists(combined)) return combined;
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            return null;
        }

        private static ImagePartType ResolveImagePartType(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".jpg" => ImagePartType.Jpeg,
                ".jpeg" => ImagePartType.Jpeg,
                ".gif" => ImagePartType.Gif,
                ".bmp" => ImagePartType.Bmp,
                ".tif" => ImagePartType.Tiff,
                ".tiff" => ImagePartType.Tiff,
                _ => ImagePartType.Png
            };
        }

        private static Drawing BuildInlineImage(string relId, long cx, long cy, uint drawingId, string imageName)
        {
            var inline = new DW.Inline(
                new DW.Extent() { Cx = cx, Cy = cy },
                new DW.DocProperties() { Id = drawingId, Name = imageName },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks() { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties() { Id = drawingId, Name = imageName },
                                new PIC.NonVisualPictureDrawingProperties()
                            ),
                            new PIC.BlipFill(
                                new A.Blip() { Embed = relId },
                                new A.Stretch(new A.FillRectangle())
                            ),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset() { X = 0, Y = 0 },
                                    new A.Extents() { Cx = cx, Cy = cy }
                                ),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }
                            )
                        )
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                )
            );
            return new Drawing(inline);
        }

        private static string EscapeCsv(string? value)
        {
            var s = value ?? string.Empty;
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

        private sealed class PreparedEntry
        {
            public LecturerToolkitEntry Entry { get; init; } = new();
            public CriteriaMeta? Meta { get; init; }
            public int LpnSort { get; init; }
        }

        private sealed class CriteriaMeta
        {
            public int CriteriaId { get; init; }
            public string CriteriaDescription { get; init; } = string.Empty;
            public string TopicCode { get; init; } = string.Empty;
            public string TopicDescription { get; init; } = string.Empty;
            public int TopicOrder { get; init; }
            public string SubjectCode { get; init; } = string.Empty;
            public int PhaseSequence { get; init; }
        }

        private sealed class ScheduleRow
        {
            public string Date { get; init; } = string.Empty;
            public string Day { get; init; } = string.Empty;
            public int Period { get; init; }
            public string TimeStart { get; init; } = string.Empty;
            public string TimeEnd { get; init; } = string.Empty;
            public string TopicCode { get; init; } = string.Empty;
            public string TopicDescription { get; init; } = string.Empty;
            public string Lpn { get; init; } = string.Empty;
            public string LessonPlanDescription { get; init; } = string.Empty;
            public string AssessmentCriteriaDescription { get; init; } = string.Empty;
            public string LecturerActions { get; init; } = string.Empty;
            public string LearnerActions { get; init; } = string.Empty;
            public string LearningAids { get; init; } = string.Empty;
        }
    }
}
