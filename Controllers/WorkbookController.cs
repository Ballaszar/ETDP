using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Services;
using ETD.Api.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkbookController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly SemanticKernelQuestionService _semanticKernelQuestionService;
        private const uint WorkbookPageWidthTwips = 12240U;
        private const uint WorkbookPageHeightTwips = 15840U;
        private const uint WorkbookPageMarginTwips = 920U;
        private const string WorkbookFullTableWidth = "10400";
        private const int TrueFalseOptionCount = 4;
        private const int MultipleChoiceOptionCount = 4;
        private const string DefaultSmiBaseUrl = "http://127.0.0.1:8099";
        private const int DefaultSmiTimeoutSeconds = 0;
        private const int DefaultSmiTopK = 0;
        private const string ExportFont = "Times New Roman";
        private const string CompactTableCellHalfPt = "24"; // 12pt
        private const int WorkbookMarksPerActivity = 4;
        private const string WorkbookPrepareTimeDisplay = "20 Min";
        private const string WorkbookPresentationTimeDisplay = "5 Min";
        private const string WorkbookQualifierText = "Write your group's decisions within the space provided below, not more than 4 facts. Use a black pen only; no pencils are allowed. You have 15 minutes for preparation and 5 minutes for presentation. Appoint a speaker for your group and present your findings to the rest of the class. You may make use of the whiteboard or the A3 flip charts.";
        private static readonly HttpClient _http = new HttpClient();

        public WorkbookController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            SemanticKernelQuestionService semanticKernelQuestionService)
        {
            _context = context;
            _environment = environment;
            _semanticKernelQuestionService = semanticKernelQuestionService;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var items = _context.Workbooks.Select(wb => new ETD.Api.DTOs.WorkbookDto
                {
                    Id = wb.Id,
                    SubjectId = wb.SubjectId,
                    Title = wb.Title,
                    Version = wb.Version,
                    Content = wb.Content
                }).ToList();
                return Ok(items);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public IActionResult Get(int id)
        {
            var wb = _context.Workbooks.Find(id);
            if (wb == null) return NotFound();
            var dto = new ETD.Api.DTOs.WorkbookDto
            {
                Id = wb.Id,
                SubjectId = wb.SubjectId,
                Title = wb.Title,
                Version = wb.Version,
                Content = wb.Content
            };
            return Ok(dto);
        }

        [HttpPost]
        public IActionResult Create(ETD.Api.DTOs.CreateWorkbookDto dto)
        {
            var model = new Workbook
            {
                SubjectId = dto.SubjectId,
                Title = dto.Title,
                Version = dto.Version,
                Content = dto.Content
            };
            _context.Workbooks.Add(model);
            _context.SaveChanges();
            return CreatedAtAction(nameof(Get), new { id = model.Id }, model);
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, ETD.Api.DTOs.UpdateWorkbookDto dto)
        {
            var item = _context.Workbooks.Find(id);
            if (item == null) return NotFound();
            item.Title = dto.Title;
            item.Version = dto.Version;
            item.Content = dto.Content;
            _context.SaveChanges();
            return Ok(item);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.Workbooks.Find(id);
            if (item == null) return NotFound();

            _context.Workbooks.Remove(item);
            _context.SaveChanges();
            return NoContent();
        }

        [HttpGet("download")]
        public async Task<IActionResult> Download([FromQuery] int? qualificationId = null, [FromQuery] int? subjectId = null, [FromQuery] int maxActivities = 30, [FromQuery] string activityScope = WorkbookActivityScopes.AssessmentCriteria)
        {
            var subject = ResolveSubject(subjectId, qualificationId);
            if (subject == null) return BadRequest("No subject available for workbook export.");
            var qualification = ResolveQualification(subject, qualificationId);
            if (qualification == null) return BadRequest("No qualification available for workbook export.");

            var max = Math.Clamp(maxActivities, 4, 80);
            if (IsAllWorkbookActivityScope(activityScope))
            {
                using var zipStream = new MemoryStream();
                using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
                {
                    foreach (var scope in ExpandWorkbookActivityScopes(activityScope))
                    {
                        var generatedScope = await BuildWorkbookDocumentAsync(subject, qualification, max, scope, HttpContext.RequestAborted);
                        if (!generatedScope.Success) continue;

                        var entry = zip.CreateEntry(generatedScope.FileName, CompressionLevel.Fastest);
                        await using var entryStream = entry.Open();
                        await entryStream.WriteAsync(generatedScope.FileBytes, 0, generatedScope.FileBytes.Length, HttpContext.RequestAborted);
                    }
                }

                zipStream.Position = 0;
                var safeCode = MakeSafeFilePart(subject.SubjectCode, "SUBJECT");
                return File(zipStream.ToArray(), "application/zip", $"Workbook_AllSets_{safeCode}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            }

            var generated = await BuildWorkbookDocumentAsync(subject, qualification, max, activityScope, HttpContext.RequestAborted);
            if (!generated.Success)
            {
                return BadRequest(generated.ErrorMessage);
            }

            return File(
                generated.FileBytes,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                generated.FileName);
        }

        [HttpGet("download-range")]
        public async Task<IActionResult> DownloadRange(
            [FromQuery] int qualificationId,
            [FromQuery] int? subjectFromId = null,
            [FromQuery] int? subjectToId = null,
            [FromQuery] int maxActivities = 30,
            [FromQuery] string activityScope = WorkbookActivityScopes.All)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required for range export.");

            var subjects = ResolveSubjectRange(qualificationId, subjectFromId, subjectToId);
            if (subjects.Count == 0) return BadRequest("No subjects were resolved for the selected range.");

            var max = Math.Clamp(maxActivities, 4, 80);
            using var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
            {
                var failures = new List<string>();
                foreach (var subject in subjects)
                {
                    var qualification = ResolveQualification(subject, qualificationId);
                    if (qualification == null)
                    {
                        failures.Add($"{subject.SubjectCode}: qualification could not be resolved.");
                        continue;
                    }

                    foreach (var scope in ExpandWorkbookActivityScopes(activityScope))
                    {
                        var generated = await BuildWorkbookDocumentAsync(subject, qualification, max, scope, HttpContext.RequestAborted);
                        if (!generated.Success)
                        {
                            failures.Add($"{subject.SubjectCode} ({BuildWorkbookScopeLabel(scope)}): {generated.ErrorMessage}");
                            continue;
                        }

                        var entry = zip.CreateEntry(generated.FileName, CompressionLevel.Fastest);
                        await using var entryStream = entry.Open();
                        await entryStream.WriteAsync(generated.FileBytes, 0, generated.FileBytes.Length, HttpContext.RequestAborted);
                    }
                }

                if (failures.Count > 0)
                {
                    var failEntry = zip.CreateEntry("_errors.txt", CompressionLevel.Fastest);
                    await using var failStream = new StreamWriter(failEntry.Open(), Encoding.UTF8);
                    await failStream.WriteLineAsync("Some subjects could not be exported:");
                    foreach (var item in failures)
                    {
                        await failStream.WriteLineAsync(item);
                    }
                }
            }

            zipStream.Position = 0;
            var fileName = $"Workbook_{(IsAllWorkbookActivityScope(activityScope) ? "AllSets" : BuildWorkbookScopeLabel(activityScope).Replace(" ", string.Empty))}_Range_Q{qualificationId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            return File(zipStream.ToArray(), "application/zip", fileName);
        }

        [HttpGet("download-consolidated")]
        public async Task<IActionResult> DownloadConsolidated([FromQuery] int qualificationId, [FromQuery] int maxActivities = 30, [FromQuery] string activityScope = WorkbookActivityScopes.AssessmentCriteria)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required for consolidated workbook export.");

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId);
            if (qualification == null) return BadRequest("No qualification available for consolidated workbook export.");

            var subjects = ResolveSubjectRange(qualificationId, null, null)
                .Where(HasSubjectIdentity)
                .ToList();
            if (subjects.Count == 0) return BadRequest("No subjects were resolved for this qualification.");

            var max = Math.Clamp(maxActivities, 4, 80);
            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                EnsureWorkbookDocumentSettings(main);
                EnsureWorkbookStyles(main);
                var footerRelId = EnsureWorkbookFooter(main);
                var body = main.Document.Body ?? (main.Document.Body = new Body());

                AppendCleanCoverPage(
                    body,
                    main,
                    qualification,
                    "WORKBOOK",
                    $"All Subjects ({subjects.Count})");
                body.Append(PageBreak());

                AppendLegalDisclaimerPage(body, qualification);
                body.Append(PageBreak());

                AppendTableOfContentsPage(body);
                body.Append(PageBreak());

                var exportedSubjects = 0;
                foreach (var subject in subjects)
                {
                    var activities = BuildWorkbookDiscussionActivities(subject, max, activityScope);
                    if (activities.Count == 0)
                    {
                        continue;
                    }

                    exportedSubjects++;
                    body.Append(StyledHeading($"{subject.SubjectCode} — {subject.SubjectDescription}", "Heading1", 26));
                    body.Append(BodyPara($"Instruction: Complete the {BuildWorkbookScopeLabel(activityScope).ToLowerInvariant()} discussion activities for this subject.", 24));
                    body.Append(Blank());

                    foreach (var activity in activities)
                    {
                        body.Append(StyledHeading($"Workbook Activity {activity.ActivityNumber}", "Heading2", 14));
                        AppendWorkbookActivityBlock(body, activity);
                        body.Append(PageBreak());
                    }
                }

                if (exportedSubjects == 0)
                {
                    body.Append(StyledHeading("No Activities Generated", "Heading1", 24));
                    body.Append(BodyPara("No subjects produced workbook activities for the selected workbook set.", 24));
                }

                body.Append(DefaultSectionProperties(footerRelId));
                main.Document.Save();
            }

            ms.Position = 0;
            var fileName = $"Workbook_{BuildWorkbookScopeLabel(activityScope).Replace(" ", string.Empty)}_AllSubjects_Q{qualificationId}_{DateTime.Now:yyyyMMdd_HHmmss}.docx";
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName);
        }

        [HttpGet("download-memorandum")]
        public async Task<IActionResult> DownloadMemorandum([FromQuery] int? qualificationId = null, [FromQuery] int? subjectId = null, [FromQuery] int maxActivities = 30, [FromQuery] string activityScope = WorkbookActivityScopes.AssessmentCriteria)
        {
            var subject = ResolveSubject(subjectId, qualificationId);
            if (subject == null) return BadRequest("No subject available for workbook memorandum export.");
            var qualification = ResolveQualification(subject, qualificationId);
            if (qualification == null) return BadRequest("No qualification available for workbook memorandum export.");

            var max = Math.Clamp(maxActivities, 4, 80);
            var generated = await BuildWorkbookMemorandumDocumentAsync(subject, qualification, max, HttpContext.RequestAborted);
            if (!generated.Success)
            {
                return BadRequest(generated.ErrorMessage);
            }

            return File(
                generated.FileBytes,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                generated.FileName);
        }

        [HttpGet("download-memorandum-range")]
        public async Task<IActionResult> DownloadMemorandumRange(
            [FromQuery] int qualificationId,
            [FromQuery] int? subjectFromId = null,
            [FromQuery] int? subjectToId = null,
            [FromQuery] int maxActivities = 30,
            [FromQuery] string activityScope = WorkbookActivityScopes.All)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required for range export.");

            var subjects = ResolveSubjectRange(qualificationId, subjectFromId, subjectToId);
            if (subjects.Count == 0) return BadRequest("No subjects were resolved for the selected range.");

            var max = Math.Clamp(maxActivities, 4, 80);
            using var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
            {
                var failures = new List<string>();
                foreach (var subject in subjects)
                {
                    var qualification = ResolveQualification(subject, qualificationId);
                    if (qualification == null)
                    {
                        failures.Add($"{subject.SubjectCode}: qualification could not be resolved.");
                        continue;
                    }

                    var generated = await BuildWorkbookMemorandumDocumentAsync(subject, qualification, max, HttpContext.RequestAborted);
                    if (!generated.Success)
                    {
                        failures.Add($"{subject.SubjectCode}: {generated.ErrorMessage}");
                        continue;
                    }

                    var entry = zip.CreateEntry(generated.FileName, CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(generated.FileBytes, 0, generated.FileBytes.Length, HttpContext.RequestAborted);
                }

                if (failures.Count > 0)
                {
                    var failEntry = zip.CreateEntry("_errors.txt", CompressionLevel.Fastest);
                    await using var failStream = new StreamWriter(failEntry.Open(), Encoding.UTF8);
                    await failStream.WriteLineAsync("Some subjects could not be exported:");
                    foreach (var item in failures)
                    {
                        await failStream.WriteLineAsync(item);
                    }
                }
            }

            zipStream.Position = 0;
            var fileName = $"WorkbookMemorandum_Range_Q{qualificationId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            return File(zipStream.ToArray(), "application/zip", fileName);
        }

        [HttpGet("report")]
        public async Task<IActionResult> Report([FromQuery] int? qualificationId = null, [FromQuery] int? subjectId = null, [FromQuery] int maxActivities = 30, [FromQuery] string activityScope = WorkbookActivityScopes.AssessmentCriteria)
        {
            var subject = ResolveSubject(subjectId, qualificationId);
            if (subject == null) return BadRequest("No subject available for workbook report.");
            var qualification = ResolveQualification(subject, qualificationId);
            if (qualification == null) return BadRequest("No qualification available for workbook report.");

            var max = Math.Clamp(maxActivities, 4, 80);
            var reportResult = await BuildWorkbookReportAsync(subject, qualification, max, activityScope, HttpContext.RequestAborted);
            if (!reportResult.Success || reportResult.Report == null)
            {
                return BadRequest(reportResult.ErrorMessage);
            }

            return Ok(reportResult.Report);
        }

        [HttpGet("download-report")]
        public async Task<IActionResult> DownloadReport([FromQuery] int? qualificationId = null, [FromQuery] int? subjectId = null, [FromQuery] int maxActivities = 30, [FromQuery] string activityScope = WorkbookActivityScopes.AssessmentCriteria)
        {
            var subject = ResolveSubject(subjectId, qualificationId);
            if (subject == null) return BadRequest("No subject available for workbook report.");
            var qualification = ResolveQualification(subject, qualificationId);
            if (qualification == null) return BadRequest("No qualification available for workbook report.");

            var max = Math.Clamp(maxActivities, 4, 80);
            var reportResult = await BuildWorkbookReportAsync(subject, qualification, max, activityScope, HttpContext.RequestAborted);
            if (!reportResult.Success || reportResult.Report == null)
            {
                return BadRequest(reportResult.ErrorMessage);
            }

            var reportText = BuildWorkbookReportText(reportResult.Report);
            var safeCode = MakeSafeFilePart(subject.SubjectCode, "SUBJECT");
            var fileName = $"Workbook_Report_{BuildWorkbookScopeLabel(activityScope).Replace(" ", string.Empty)}_{safeCode}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            return File(Encoding.UTF8.GetBytes(reportText), "text/plain; charset=utf-8", fileName);
        }

        [HttpGet("download-report-range")]
        public async Task<IActionResult> DownloadReportRange(
            [FromQuery] int qualificationId,
            [FromQuery] int? subjectFromId = null,
            [FromQuery] int? subjectToId = null,
            [FromQuery] int maxActivities = 30,
            [FromQuery] string activityScope = WorkbookActivityScopes.All)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required for range report export.");

            var subjects = ResolveSubjectRange(qualificationId, subjectFromId, subjectToId);
            if (subjects.Count == 0) return BadRequest("No subjects were resolved for the selected range.");

            var max = Math.Clamp(maxActivities, 4, 80);
            using var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
            {
                var failures = new List<string>();
                foreach (var subject in subjects)
                {
                    var qualification = ResolveQualification(subject, qualificationId);
                    if (qualification == null)
                    {
                        failures.Add($"{subject.SubjectCode}: qualification could not be resolved.");
                        continue;
                    }

                    foreach (var scope in ExpandWorkbookActivityScopes(activityScope))
                    {
                        var reportResult = await BuildWorkbookReportAsync(
                            subject,
                            qualification,
                            max,
                            scope,
                            HttpContext.RequestAborted);

                        if (!reportResult.Success || reportResult.Report == null)
                        {
                            failures.Add($"{subject.SubjectCode} ({BuildWorkbookScopeLabel(scope)}): {reportResult.ErrorMessage}");
                            continue;
                        }

                        var safeCode = MakeSafeFilePart(subject.SubjectCode, "SUBJECT");
                        var entry = zip.CreateEntry($"Workbook_Report_{BuildWorkbookScopeLabel(scope).Replace(" ", string.Empty)}_{safeCode}.txt", CompressionLevel.Fastest);
                        await using var entryStream = new StreamWriter(entry.Open(), Encoding.UTF8);
                        await entryStream.WriteAsync(BuildWorkbookReportText(reportResult.Report));
                    }
                }

                if (failures.Count > 0)
                {
                    var failEntry = zip.CreateEntry("_errors.txt", CompressionLevel.Fastest);
                    await using var failStream = new StreamWriter(failEntry.Open(), Encoding.UTF8);
                    await failStream.WriteLineAsync("Some reports could not be generated:");
                    foreach (var item in failures)
                    {
                        await failStream.WriteLineAsync(item);
                    }
                }
            }

            zipStream.Position = 0;
            var zipName = $"Workbook_Report_{(IsAllWorkbookActivityScope(activityScope) ? "AllSets" : BuildWorkbookScopeLabel(activityScope).Replace(" ", string.Empty))}_Range_Q{qualificationId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            return File(zipStream.ToArray(), "application/zip", zipName);
        }

        private sealed class GeneratedDocResult
        {
            public bool Success { get; init; }
            public string ErrorMessage { get; init; } = string.Empty;
            public byte[] FileBytes { get; init; } = Array.Empty<byte>();
            public string FileName { get; init; } = string.Empty;

            public static GeneratedDocResult Fail(string message)
                => new() { Success = false, ErrorMessage = message };

            public static GeneratedDocResult Ok(byte[] bytes, string fileName)
                => new() { Success = true, FileBytes = bytes, FileName = fileName };
        }

        private sealed class WorkbookReportResult
        {
            public bool Success { get; init; }
            public string ErrorMessage { get; init; } = string.Empty;
            public WorkbookExportReport? Report { get; init; }

            public static WorkbookReportResult Fail(string message)
                => new() { Success = false, ErrorMessage = message };

            public static WorkbookReportResult Ok(WorkbookExportReport report)
                => new() { Success = true, Report = report };
        }

        private sealed class WorkbookExportReport
        {
            public int QualificationId { get; set; }
            public string QualificationNumber { get; set; } = string.Empty;
            public string QualificationDescription { get; set; } = string.Empty;
            public int SubjectId { get; set; }
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public string ActivityScope { get; set; } = string.Empty;
            public int MaxActivitiesRequested { get; set; }
            public int ActivitiesGenerated { get; set; }
            public int TotalQuestionsGenerated { get; set; }
            public string QuestionSource { get; set; } = string.Empty;
            public bool TableOfContentsIncluded { get; set; }
            public bool BibliographySectionIncluded { get; set; }
            public int BibliographyEntriesFound { get; set; }
            public List<string> BibliographyPreview { get; set; } = new();
            public List<string> TopicCodes { get; set; } = new();
            public DateTime GeneratedAtUtc { get; set; }
        }

        private static class WorkbookActivityScopes
        {
            public const string Topic = "topic";
            public const string AssessmentCriteria = "assessment";
            public const string LessonPlan = "lessonplan";
            public const string All = "all";
        }

        private sealed class WorkbookDiscussionActivity
        {
            public int ActivityNumber { get; init; }
            public string Scope { get; init; } = WorkbookActivityScopes.AssessmentCriteria;
            public string ScopeLabel { get; init; } = string.Empty;
            public string SubjectCode { get; init; } = string.Empty;
            public string SubjectDescription { get; init; } = string.Empty;
            public string TopicCode { get; init; } = string.Empty;
            public string TopicDescription { get; init; } = string.Empty;
            public string PromptDescriptor { get; init; } = string.Empty;
            public string TaskPrompt { get; init; } = string.Empty;
            public string Qualifier { get; init; } = WorkbookQualifierText;
            public string PrepareTime { get; init; } = WorkbookPrepareTimeDisplay;
            public string PresentationTime { get; init; } = WorkbookPresentationTimeDisplay;
            public int MarksPossible { get; init; } = WorkbookMarksPerActivity;
            public string AssessmentCriteriaDescription { get; init; } = string.Empty;
            public string LessonPlanLabel { get; init; } = string.Empty;
            public string LessonPlanDescription { get; init; } = string.Empty;
            public string LessonPlanContent { get; init; } = string.Empty;
            public string SourceIdentity { get; init; } = string.Empty;
        }

        private static string NormalizeWorkbookActivityScope(string? value)
        {
            var scope = (value ?? string.Empty).Trim().ToLowerInvariant();
            return scope switch
            {
                WorkbookActivityScopes.Topic => WorkbookActivityScopes.Topic,
                WorkbookActivityScopes.AssessmentCriteria => WorkbookActivityScopes.AssessmentCriteria,
                WorkbookActivityScopes.LessonPlan => WorkbookActivityScopes.LessonPlan,
                WorkbookActivityScopes.All => WorkbookActivityScopes.All,
                _ => WorkbookActivityScopes.AssessmentCriteria
            };
        }

        private static IReadOnlyList<string> ExpandWorkbookActivityScopes(string? value)
        {
            var normalized = NormalizeWorkbookActivityScope(value);
            return normalized == WorkbookActivityScopes.All
                ? new[] { WorkbookActivityScopes.Topic, WorkbookActivityScopes.AssessmentCriteria, WorkbookActivityScopes.LessonPlan }
                : new[] { normalized };
        }

        private static bool IsAllWorkbookActivityScope(string? value)
            => string.Equals(NormalizeWorkbookActivityScope(value), WorkbookActivityScopes.All, StringComparison.OrdinalIgnoreCase);

        private static string BuildWorkbookScopeLabel(string scope)
        {
            return NormalizeWorkbookActivityScope(scope) switch
            {
                WorkbookActivityScopes.Topic => "Topic",
                WorkbookActivityScopes.LessonPlan => "Lesson Plan",
                _ => "Assessment Criteria"
            };
        }

        private static string BuildWorkbookDocumentTitle(string scope, bool memorandum = false)
        {
            var label = BuildWorkbookScopeLabel(scope).ToUpperInvariant();
            return memorandum ? $"{label} WORKBOOK MEMORANDUM" : $"{label} WORKBOOK";
        }

        private static string BuildWorkbookPromptDescriptor(
            AssessmentDrivenQuestionGenerator.LessonEvidenceItem item,
            string scope)
        {
            var normalizedScope = NormalizeWorkbookActivityScope(scope);
            var raw = normalizedScope switch
            {
                WorkbookActivityScopes.Topic => item.TopicDescription,
                WorkbookActivityScopes.LessonPlan => string.IsNullOrWhiteSpace(item.LessonPlanDescription)
                    ? item.LessonPlanContent
                    : item.LessonPlanDescription,
                _ => item.AssessmentCriteriaDescription
            };

            var descriptor = AssessmentDrivenQuestionGenerator.SanitizeQuestionText(raw);
            if (string.IsNullOrWhiteSpace(descriptor))
            {
                descriptor = AssessmentDrivenQuestionGenerator.SanitizeQuestionText(item.TopicDescription);
            }
            if (string.IsNullOrWhiteSpace(descriptor))
            {
                descriptor = "the topic content";
            }

            descriptor = descriptor.Trim().TrimEnd('.', '!', '?', ':', ';');
            return descriptor;
        }

        private static string BuildWorkbookTaskPrompt(string descriptor)
        {
            var cleaned = (descriptor ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return "In your groups explain the: lesson content";
            }

            var leadVerbs = new[] { "explain", "describe", "identify", "discuss", "outline", "demonstrate", "list" };
            if (leadVerbs.Any(v => cleaned.StartsWith(v + " ", StringComparison.OrdinalIgnoreCase)))
            {
                return $"In your groups: {char.ToUpperInvariant(cleaned[0])}{cleaned[1..]}";
            }

            return $"In your groups explain the: {cleaned}";
        }

        private static string NormalizeWorkbookTimeText(string? value, string fallback)
        {
            var text = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            text = Regex.Replace(text, @"\bminutes?\b", "Min", RegexOptions.IgnoreCase).Trim();
            text = Regex.Replace(text, @"\s+", " ");
            return text;
        }

        private List<WorkbookDiscussionActivity> BuildWorkbookDiscussionActivities(
            Subject subject,
            int maxActivities,
            string activityScope)
        {
            var normalizedScope = NormalizeWorkbookActivityScope(activityScope);
            var items = AssessmentDrivenQuestionGenerator.BuildOrderedLessonEvidence(_context, subject.Id);
            if (items.Count == 0) return new List<WorkbookDiscussionActivity>();

            IEnumerable<AssessmentDrivenQuestionGenerator.LessonEvidenceItem> selected = normalizedScope switch
            {
                WorkbookActivityScopes.Topic => items
                    .GroupBy(item => item.TopicId > 0
                        ? $"TOPIC:{item.TopicId}"
                        : $"{(item.TopicCode ?? string.Empty).Trim().ToUpperInvariant()}|{(item.TopicDescription ?? string.Empty).Trim().ToUpperInvariant()}")
                    .Select(group => group
                        .OrderBy(item => item.AssessmentCriteriaId)
                        .ThenBy(item => item.LessonSortOrder)
                        .ThenBy(item => item.LessonPlanLabel)
                        .First()),
                WorkbookActivityScopes.LessonPlan => items
                    .GroupBy(item => string.IsNullOrWhiteSpace(item.BundleKey)
                        ? $"{item.TopicId}:{item.AssessmentCriteriaId}:{(item.LessonPlanLabel ?? string.Empty).Trim().ToUpperInvariant()}:{(item.LessonPlanDescription ?? string.Empty).Trim().ToUpperInvariant()}"
                        : item.BundleKey)
                    .Select(group => group.First()),
                _ => BuildAssessmentCriteriaFocusedItems(items)
            };

            return selected
                .OrderBy(item => item.TopicOrder)
                .ThenBy(item => item.TopicCode)
                .ThenBy(item => item.AssessmentCriteriaId)
                .ThenBy(item => item.LessonSortOrder)
                .ThenBy(item => item.LessonPlanLabel)
                .Take(Math.Max(1, maxActivities))
                .Select((item, index) =>
                {
                    var descriptor = BuildWorkbookPromptDescriptor(item, normalizedScope);
                    return new WorkbookDiscussionActivity
                    {
                        ActivityNumber = index + 1,
                        Scope = normalizedScope,
                        ScopeLabel = BuildWorkbookScopeLabel(normalizedScope),
                        SubjectCode = (subject.SubjectCode ?? string.Empty).Trim(),
                        SubjectDescription = (subject.SubjectDescription ?? string.Empty).Trim(),
                        TopicCode = (item.TopicCode ?? string.Empty).Trim(),
                        TopicDescription = (item.TopicDescription ?? string.Empty).Trim(),
                        PromptDescriptor = descriptor,
                        TaskPrompt = BuildWorkbookTaskPrompt(descriptor),
                        Qualifier = WorkbookQualifierText,
                        PrepareTime = NormalizeWorkbookTimeText(WorkbookPrepareTimeDisplay, WorkbookPrepareTimeDisplay),
                        PresentationTime = NormalizeWorkbookTimeText(WorkbookPresentationTimeDisplay, WorkbookPresentationTimeDisplay),
                        MarksPossible = WorkbookMarksPerActivity,
                        AssessmentCriteriaDescription = (item.AssessmentCriteriaDescription ?? string.Empty).Trim(),
                        LessonPlanLabel = (item.LessonPlanLabel ?? string.Empty).Trim(),
                        LessonPlanDescription = (item.LessonPlanDescription ?? string.Empty).Trim(),
                        LessonPlanContent = (item.LessonPlanContent ?? string.Empty).Trim(),
                        SourceIdentity = (item.BundleKey ?? string.Empty).Trim()
                    };
                })
                .ToList();
        }

        private async Task<GeneratedDocResult> BuildWorkbookDocumentAsync(
            Subject subject,
            Qualification qualification,
            int maxActivities,
            string activityScope,
            CancellationToken cancellationToken)
        {
            _ = qualification;
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedScope = NormalizeWorkbookActivityScope(activityScope);
            var activities = BuildWorkbookDiscussionActivities(subject, maxActivities, normalizedScope);
            if (activities.Count == 0)
            {
                return GeneratedDocResult.Fail("No workbook activity data found for this subject.");
            }

            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                EnsureWorkbookDocumentSettings(main);
                EnsureWorkbookStyles(main);
                var footerRelId = EnsureWorkbookFooter(main);
                var body = main.Document.Body ?? (main.Document.Body = new Body());

                AppendCleanCoverPage(
                    body,
                    main,
                    qualification,
                    BuildWorkbookDocumentTitle(normalizedScope),
                    $"{subject.SubjectCode} — {subject.SubjectDescription}");
                body.Append(PageBreak());

                body.Append(StyledHeading(BuildWorkbookDocumentTitle(normalizedScope), "Heading1", 20));
                body.Append(BuildWorkbookLearnerParticularsTable(subject, activities.Count * WorkbookMarksPerActivity));
                body.Append(PageBreak());

                body.Append(StyledHeading("APPROVAL AND SIGNATURES", "Heading1", 18));
                body.Append(BuildWorkbookRoleplayersTable());
                body.Append(PageBreak());

                AppendLegalDisclaimerPage(body, qualification);
                body.Append(PageBreak());

                AppendTableOfContentsPage(body);
                body.Append(PageBreak());

                AppendWorkbookInstructionsPage(body);
                body.Append(PageBreak());

                body.Append(StyledHeading($"{BuildWorkbookScopeLabel(normalizedScope).ToUpperInvariant()} ACTIVITIES", "Heading1", 20));
                body.Append(BodyPara($"{subject.SubjectCode} — {subject.SubjectDescription}", 24));
                body.Append(Blank());

                foreach (var activity in activities)
                {
                    body.Append(StyledHeading($"Workbook Activity {activity.ActivityNumber}", "Heading2", 14));
                    AppendWorkbookActivityBlock(body, activity);
                    body.Append(PageBreak());
                }

                body.Append(DefaultSectionProperties(footerRelId));
                main.Document.Save();
            }

            ms.Position = 0;
            var safeCode = MakeSafeFilePart(subject.SubjectCode, "SUBJECT");
            var fileName = $"Workbook_{BuildWorkbookScopeLabel(normalizedScope).Replace(" ", string.Empty)}_{safeCode}_{DateTime.Now:yyyyMMdd}.docx";
            return GeneratedDocResult.Ok(ms.ToArray(), fileName);
        }

        private async Task<WorkbookReportResult> BuildWorkbookReportAsync(
            Subject subject,
            Qualification qualification,
            int maxActivities,
            string activityScope,
            CancellationToken cancellationToken)
        {
            _ = qualification;
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedScope = NormalizeWorkbookActivityScope(activityScope);
            if (normalizedScope == WorkbookActivityScopes.All)
            {
                return WorkbookReportResult.Fail("Select a single workbook set to generate a report.");
            }

            var activities = BuildWorkbookDiscussionActivities(subject, maxActivities, normalizedScope);
            if (activities.Count == 0)
            {
                return WorkbookReportResult.Fail("No workbook activity data found for this subject.");
            }

            var topicCodes = activities
                .Select(b => (b.TopicCode ?? string.Empty).Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var report = new WorkbookExportReport
            {
                QualificationId = qualification.Id,
                QualificationNumber = (qualification.QualificationNumber ?? string.Empty).Trim(),
                QualificationDescription = (qualification.QualificationDescription ?? string.Empty).Trim(),
                SubjectId = subject.Id,
                SubjectCode = (subject.SubjectCode ?? string.Empty).Trim(),
                SubjectDescription = (subject.SubjectDescription ?? string.Empty).Trim(),
                ActivityScope = BuildWorkbookScopeLabel(normalizedScope),
                MaxActivitiesRequested = maxActivities,
                ActivitiesGenerated = activities.Count,
                TotalQuestionsGenerated = activities.Count,
                QuestionSource = $"Workbook set generated from {BuildWorkbookScopeLabel(normalizedScope).ToLowerInvariant()} rows with one activity worth 4 marks each.",
                TableOfContentsIncluded = true,
                BibliographySectionIncluded = false,
                BibliographyEntriesFound = 0,
                BibliographyPreview = new List<string>(),
                TopicCodes = topicCodes,
                GeneratedAtUtc = DateTime.UtcNow
            };

            return WorkbookReportResult.Ok(report);
        }

        private static string BuildWorkbookReportText(WorkbookExportReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Workbook Export Report");
            sb.AppendLine($"Generated (UTC): {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Qualification: {report.QualificationNumber} - {report.QualificationDescription} (ID: {report.QualificationId})");
            sb.AppendLine($"Subject: {report.SubjectCode} - {report.SubjectDescription} (ID: {report.SubjectId})");
            sb.AppendLine($"Workbook Set: {report.ActivityScope}");
            sb.AppendLine($"Max Activities Requested: {report.MaxActivitiesRequested}");
            sb.AppendLine($"Activities Generated: {report.ActivitiesGenerated}");
            sb.AppendLine($"Total Questions Generated: {report.TotalQuestionsGenerated}");
            sb.AppendLine($"Question Source: {report.QuestionSource}");
            sb.AppendLine($"Table Of Contents Included: {(report.TableOfContentsIncluded ? "Yes" : "No")}");
            sb.AppendLine($"Bibliography Section Included: {(report.BibliographySectionIncluded ? "Yes" : "No")}");
            sb.AppendLine($"Bibliography Entries Found: {report.BibliographyEntriesFound}");
            sb.AppendLine();
            sb.AppendLine("Topic Codes Included:");
            if (report.TopicCodes.Count == 0) sb.AppendLine("- None");
            foreach (var code in report.TopicCodes) sb.AppendLine($"- {code}");
            sb.AppendLine();
            sb.AppendLine("Bibliography Preview:");
            if (report.BibliographyPreview.Count == 0) sb.AppendLine("- No bibliography entries detected.");
            foreach (var line in report.BibliographyPreview) sb.AppendLine($"- {line}");
            return sb.ToString();
        }

        private async Task<GeneratedDocResult> BuildWorkbookMemorandumDocumentAsync(
            Subject subject,
            Qualification qualification,
            int maxActivities,
            CancellationToken cancellationToken)
        {
            _ = qualification;
            cancellationToken.ThrowIfCancellationRequested();

            var activities = BuildWorkbookDiscussionActivities(subject, maxActivities, WorkbookActivityScopes.AssessmentCriteria);
            if (activities.Count == 0)
            {
                return GeneratedDocResult.Fail("No workbook activity data found for this subject.");
            }

            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                EnsureWorkbookDocumentSettings(main);
                EnsureWorkbookStyles(main);
                var footerRelId = EnsureWorkbookFooter(main);
                var body = main.Document.Body ?? (main.Document.Body = new Body());

                AppendCleanCoverPage(
                    body,
                    main,
                    qualification,
                    "WORKBOOK MEMORANDUM",
                    $"{subject.SubjectCode} — {subject.SubjectDescription}");
                body.Append(PageBreak());

                body.Append(StyledHeading("WORKBOOK MEMORANDUM", "Heading1", 20));
                body.Append(BodyPara($"{subject.SubjectCode} — {subject.SubjectDescription}", 24));
                body.Append(PageBreak());

                AppendLegalDisclaimerPage(body, qualification);
                body.Append(PageBreak());

                foreach (var activity in activities)
                {
                    AppendWorkbookMemorandumActivityBlock(body, activity);
                    body.Append(Blank());
                }

                body.Append(BodyPara("Marking guide: Award up to 4 marks. Award 1 mark per valid group fact or correct discussion point, up to a maximum of 4 marks.", 22));
                body.Append(DefaultSectionProperties(footerRelId));
                main.Document.Save();
            }

            ms.Position = 0;
            var safeCode = MakeSafeFilePart(subject.SubjectCode, "SUBJECT");
            var fileName = $"Workbook_Memorandum_{safeCode}_{DateTime.Now:yyyyMMdd}.docx";
            return GeneratedDocResult.Ok(ms.ToArray(), fileName);
        }

        private List<Subject> ResolveSubjectRange(int qualificationId, int? subjectFromId, int? subjectToId)
        {
            var subjects = _context.Subjects
                .Where(s => s.QualificationId == qualificationId)
                .OrderBy(s => s.SubjectCode)
                .ThenBy(s => s.SubjectDescription)
                .ToList();
            subjects = subjects.Where(HasSubjectIdentity).ToList();
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

            if (fromIndex > toIndex)
            {
                (fromIndex, toIndex) = (toIndex, fromIndex);
            }

            return subjects
                .Skip(fromIndex)
                .Take((toIndex - fromIndex) + 1)
                .ToList();
        }

        private List<string> BuildBibliographyEntries(
            Qualification qualification,
            Subject subject,
            IReadOnlyList<WorkbookActivityBundle> activityBundles)
        {
            var qualificationCode = (qualification.QualificationNumber ?? string.Empty).Trim();
            var subjectCode = (subject.SubjectCode ?? string.Empty).Trim();
            var subjectDescription = (subject.SubjectDescription ?? string.Empty).Trim();
            var topicCodes = (activityBundles ?? Array.Empty<WorkbookActivityBundle>())
                .Select(b => (b.Item.TopicCode ?? string.Empty).Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var materials = _context.SourceMaterials
                .AsNoTracking()
                .OrderByDescending(s => s.KnowledgeUploadedAtUtc ?? s.CreatedAt)
                .Take(600)
                .ToList();

            bool MatchesSource(SourceMaterial row)
            {
                if (!string.IsNullOrWhiteSpace(qualificationCode) &&
                    !string.Equals((row.QualificationCode ?? string.Empty).Trim(), qualificationCode, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var subjectMatch =
                    (!string.IsNullOrWhiteSpace(subjectDescription) &&
                     (row.SubjectDescription ?? string.Empty).Contains(subjectDescription, StringComparison.OrdinalIgnoreCase))
                    ||
                    (!string.IsNullOrWhiteSpace(subjectCode) &&
                     (
                         (row.SubjectDescription ?? string.Empty).Contains(subjectCode, StringComparison.OrdinalIgnoreCase) ||
                         (row.Title ?? string.Empty).Contains(subjectCode, StringComparison.OrdinalIgnoreCase) ||
                         (row.FileName ?? string.Empty).Contains(subjectCode, StringComparison.OrdinalIgnoreCase)
                     ));

                var topicMatch = topicCodes.Any(topic =>
                    (row.TopicDescription ?? string.Empty).Contains(topic, StringComparison.OrdinalIgnoreCase) ||
                    (row.AssessmentCriteriaDescription ?? string.Empty).Contains(topic, StringComparison.OrdinalIgnoreCase) ||
                    (row.Title ?? string.Empty).Contains(topic, StringComparison.OrdinalIgnoreCase));

                return subjectMatch || topicMatch;
            }

            var entries = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in materials.Where(MatchesSource))
            {
                var title = string.IsNullOrWhiteSpace(row.Title)
                    ? (!string.IsNullOrWhiteSpace(row.FileName) ? row.FileName.Trim() : $"SourceMaterial #{row.Id}")
                    : row.Title.Trim();

                var citation = !string.IsNullOrWhiteSpace(row.Url)
                    ? $"{title}. {row.Url.Trim()}"
                    : (!string.IsNullOrWhiteSpace(row.FileName)
                        ? $"{title}. File: {row.FileName.Trim()}"
                        : title);

                if (!seen.Add(citation)) continue;
                entries.Add(citation);
                if (entries.Count >= 25) break;
            }

            return entries;
        }

        private sealed class WorkbookActivityBundle
        {
            public AssessmentDrivenQuestionGenerator.LessonEvidenceItem Item { get; init; } = new();
            public AssessmentDrivenQuestionGenerator.GeneratedQuestion TrueFalseQuestion { get; init; } = new();
            public AssessmentDrivenQuestionGenerator.GeneratedQuestion MultipleChoiceQuestion { get; init; } = new();
        }

        private static bool HasSubjectIdentity(Subject subject)
        {
            return !string.IsNullOrWhiteSpace((subject.SubjectCode ?? string.Empty).Trim()) ||
                   !string.IsNullOrWhiteSpace((subject.SubjectDescription ?? string.Empty).Trim());
        }

        private static List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem> BuildAssessmentCriteriaFocusedItems(
            List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem> items)
        {
            if (items == null || items.Count == 0) return new List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem>();

            var result = new List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var criteria = (item.AssessmentCriteriaDescription ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(criteria)) continue;

                var key = item.AssessmentCriteriaId > 0
                    ? $"ACID:{item.AssessmentCriteriaId}"
                    : $"{(item.TopicCode ?? string.Empty).Trim().ToUpperInvariant()}|{criteria.ToUpperInvariant()}";
                if (!seen.Add(key)) continue;

                result.Add(item);
            }

            return result;
        }

        private static AssessmentDrivenQuestionGenerator.GeneratedQuestion BuildBinaryTrueFalseCriterionQuestion(
            AssessmentDrivenQuestionGenerator.LessonEvidenceItem item,
            int number)
        {
            var criterion = AssessmentDrivenQuestionGenerator.NormalizeQuestionStatement(
                item.AssessmentCriteriaDescription,
                "Demonstrate competence against the stated learning requirement.");

            var polaritySeed = item.AssessmentCriteriaId > 0 ? item.AssessmentCriteriaId : number;
            var shouldBeTrue = Math.Abs(polaritySeed) % 2 == 1;

            var statement = shouldBeTrue
                ? $"This is an assessment requirement: {criterion}"
                : $"This is not an assessment requirement: {criterion}";

            return new AssessmentDrivenQuestionGenerator.GeneratedQuestion
            {
                Number = number,
                Type = "TrueFalse",
                Prompt = AssessmentDrivenQuestionGenerator.NormalizeQuestionStem(
                    $"True or False: {statement}",
                    "True or False: This is an assessment requirement."),
                Options = new List<string> { "True", "False" },
                CorrectAnswer = shouldBeTrue ? "True" : "False",
                TopicCode = item.TopicCode,
                TopicDescription = item.TopicDescription,
                LessonPlanLabel = "Assessment Criterion",
                AssessmentCriteriaDescription = item.AssessmentCriteriaDescription,
                Rationale = shouldBeTrue
                    ? "The statement reflects the assessment criterion."
                    : "The statement intentionally negates the assessment criterion.",
                Marks = 1,
                BundleKey = item.BundleKey
            };
        }

        private sealed class SmiWorkbookBuildResult
        {
            public List<WorkbookActivityBundle> Bundles { get; init; } = new();
            public List<string> ResourceSuggestions { get; init; } = new();
        }

        private sealed class SmiAnswerResult
        {
            public string Answer { get; init; } = string.Empty;
            public List<string> Citations { get; init; } = new();
        }

        private sealed class SmiGeneratedQuestionEnvelope
        {
            public string Prompt { get; set; } = string.Empty;
            public List<string> Options { get; set; } = new();
            public string CorrectAnswer { get; set; } = string.Empty;
            public string Rationale { get; set; } = string.Empty;
            public List<string> ResourceHints { get; set; } = new();
        }

        private Task<(List<WorkbookActivityBundle> Bundles, string SourceLabel)> BuildWorkbookActivitiesAsync(
            Subject subject,
            Qualification qualification,
            int maxActivities,
            CancellationToken cancellationToken)
        {
            _ = qualification;
            cancellationToken.ThrowIfCancellationRequested();

            var sourceLabel = "Generated in simple True/False mode: one assessment criterion mapped to one binary workbook activity (no distractors).";
            var items = AssessmentDrivenQuestionGenerator.BuildOrderedLessonEvidence(_context, subject.Id);
            var criteriaItems = BuildAssessmentCriteriaFocusedItems(items)
                .Take(maxActivities)
                .ToList();
            if (criteriaItems.Count == 0)
            {
                return Task.FromResult((new List<WorkbookActivityBundle>(), sourceLabel));
            }

            var bundles = new List<WorkbookActivityBundle>();
            var activityNumber = 1;
            foreach (var item in criteriaItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bundles.Add(new WorkbookActivityBundle
                {
                    Item = item,
                    TrueFalseQuestion = BuildBinaryTrueFalseCriterionQuestion(item, activityNumber),
                    MultipleChoiceQuestion = new AssessmentDrivenQuestionGenerator.GeneratedQuestion()
                });
                activityNumber++;
            }

            return Task.FromResult((bundles, sourceLabel));
        }

        private async Task<List<WorkbookActivityBundle>> BuildWorkbookActivitiesWithSemanticKernelFallbackAsync(
            List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem> items,
            CancellationToken cancellationToken)
        {
            var generated = new List<WorkbookActivityBundle>();
            var section = 1;
            foreach (var item in items)
            {
                var trueFalse = await _semanticKernelQuestionService.GenerateTrueFalseQuestionAsync(
                    item,
                    section * 2 - 1,
                    marks: 2,
                    optionCount: TrueFalseOptionCount,
                    cancellationToken)
                    ?? AssessmentDrivenQuestionGenerator.BuildTrueFalseQuestion(item, section * 2 - 1, 2);

                var multipleChoice = await _semanticKernelQuestionService.GenerateMultipleChoiceQuestionAsync(
                    item,
                    section * 2,
                    marks: 2,
                    distractorCount: 4,
                    cancellationToken)
                    ?? AssessmentDrivenQuestionGenerator.BuildMultipleChoiceQuestion(item, section * 2, 2);

                generated.Add(new WorkbookActivityBundle
                {
                    Item = item,
                    TrueFalseQuestion = trueFalse,
                    MultipleChoiceQuestion = multipleChoice
                });
                section++;
            }

            return generated;
        }

        private async Task<SmiWorkbookBuildResult> TryBuildWorkbookActivitiesWithSmiAsync(
            List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem> items,
            Qualification qualification,
            Subject subject,
            CancellationToken cancellationToken)
        {
            var empty = new SmiWorkbookBuildResult();
            if (!IsSmiIntegrationEnabled()) return empty;
            if (items.Count == 0) return empty;

            var bundles = new List<WorkbookActivityBundle>();
            var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var trueFalse = await TryGenerateSingleSmiQuestionAsync(
                    item,
                    qualification,
                    subject,
                    questionType: "TrueFalse",
                    optionCount: TrueFalseOptionCount,
                    marks: 2,
                    cancellationToken,
                    resources);

                var multipleChoice = await TryGenerateSingleSmiQuestionAsync(
                    item,
                    qualification,
                    subject,
                    questionType: "MultipleChoice",
                    optionCount: MultipleChoiceOptionCount,
                    marks: 2,
                    cancellationToken,
                    resources);

                if (trueFalse == null || multipleChoice == null) continue;

                bundles.Add(new WorkbookActivityBundle
                {
                    Item = item,
                    TrueFalseQuestion = trueFalse,
                    MultipleChoiceQuestion = multipleChoice
                });
            }

            if (bundles.Count == 0) return empty;

            return new SmiWorkbookBuildResult
            {
                Bundles = RenumberWorkbookBundles(bundles),
                ResourceSuggestions = resources.Take(25).ToList()
            };
        }

        private async Task<AssessmentDrivenQuestionGenerator.GeneratedQuestion?> TryGenerateSingleSmiQuestionAsync(
            AssessmentDrivenQuestionGenerator.LessonEvidenceItem item,
            Qualification qualification,
            Subject subject,
            string questionType,
            int optionCount,
            int marks,
            CancellationToken cancellationToken,
            HashSet<string> resources)
        {
            var prompt = BuildSmiQuestionPrompt(item, qualification, subject, questionType, optionCount, marks);
            var answer = await TryFetchSmiAnswerAsync(
                prompt,
                qualification.QualificationNumber ?? string.Empty,
                qualification.QualificationDescription ?? string.Empty,
                mode: "workbook",
                cancellationToken);
            if (answer == null || string.IsNullOrWhiteSpace(answer.Answer)) return null;

            foreach (var citation in answer.Citations)
            {
                if (!string.IsNullOrWhiteSpace(citation))
                {
                    resources.Add(citation.Trim());
                }
            }

            var parsed = TryParseSmiQuestionEnvelope(answer.Answer);
            if (parsed == null) return null;

            foreach (var hint in parsed.ResourceHints)
            {
                if (!string.IsNullOrWhiteSpace(hint))
                {
                    resources.Add(hint.Trim());
                }
            }

            return NormalizeSmiQuestion(item, parsed, questionType, optionCount, marks);
        }

        private static AssessmentDrivenQuestionGenerator.GeneratedQuestion? NormalizeSmiQuestion(
            AssessmentDrivenQuestionGenerator.LessonEvidenceItem item,
            SmiGeneratedQuestionEnvelope parsed,
            string questionType,
            int optionCount,
            int marks)
        {
            if (parsed == null) return null;

            var isTrueFalse = string.Equals(questionType, "TrueFalse", StringComparison.OrdinalIgnoreCase);
            var fallbackPrompt = isTrueFalse
                ? $"Read each statement about {AssessmentDrivenQuestionGenerator.BuildLearnerContextLabel(item)}. Mark each option as True or False. Only one option is True."
                : $"Which option best reflects correct practice for {AssessmentDrivenQuestionGenerator.BuildLearnerContextLabel(item)}?";

            var prompt = AssessmentDrivenQuestionGenerator.NormalizeQuestionStem(parsed.Prompt, fallbackPrompt);
            var options = parsed.Options
                .Select(o => AssessmentDrivenQuestionGenerator.NormalizeQuestionStatement(o))
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!IsLikelyEnglishText(prompt) || HasNoiseArtifacts(prompt) || IsCurriculumEchoQuestion(prompt)) return null;
            if (options.Any(o => !IsLikelyEnglishText(o) || HasNoiseArtifacts(o) || IsCurriculumEchoQuestion(o))) return null;

            if (options.Count != optionCount) return null;
            if (options.Any(AssessmentDrivenQuestionGenerator.ContainsQuestionAdministrativeReference)) return null;
            if (options.Any(o => o.Contains("all of the above", StringComparison.OrdinalIgnoreCase))) return null;
            if (options.Any(o => o.Contains("none of the above", StringComparison.OrdinalIgnoreCase))) return null;

            var rationale = AssessmentDrivenQuestionGenerator.SanitizeQuestionText(parsed.Rationale);
            if (string.IsNullOrWhiteSpace(rationale))
            {
                rationale = "Generated by Gemma using lesson-plan content and qualification context.";
            }

            string correctAnswer;
            if (isTrueFalse)
            {
                if (!TryResolveTrueFalseCorrectIndex(parsed.CorrectAnswer, optionCount, out var trueIndex))
                {
                    return null;
                }
                correctAnswer = BuildCanonicalTrueFalseAnswer(trueIndex, optionCount);
            }
            else
            {
                var correctIndex = TryResolveCorrectOptionIndex(parsed.CorrectAnswer, optionCount);
                if (correctIndex < 0 || correctIndex >= optionCount) return null;
                correctAnswer = OptionLabel(correctIndex);
            }

            return new AssessmentDrivenQuestionGenerator.GeneratedQuestion
            {
                Number = 1,
                Type = isTrueFalse ? "TrueFalse" : "MultipleChoice",
                Prompt = prompt,
                Options = options,
                CorrectAnswer = correctAnswer,
                TopicCode = item.TopicCode,
                TopicDescription = item.TopicDescription,
                LessonPlanLabel = item.LessonPlanLabel,
                AssessmentCriteriaDescription = item.AssessmentCriteriaDescription,
                Rationale = rationale,
                Marks = Math.Max(1, marks),
                BundleKey = item.BundleKey
            };
        }

        private static bool TryResolveTrueFalseCorrectIndex(string raw, int optionCount, out int trueIndex)
        {
            trueIndex = -1;
            var text = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) return false;

            var trueKeys = new List<int>();
            var parts = text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var row = part.Trim();
                if (string.IsNullOrWhiteSpace(row)) continue;
                var eq = row.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0) continue;
                var label = row[..eq].Trim();
                var value = row[(eq + 1)..].Trim();
                if (!value.Equals("True", StringComparison.OrdinalIgnoreCase)) continue;
                var idx = LabelToIndex(label);
                if (idx < 0 || idx >= optionCount) continue;
                trueKeys.Add(idx);
            }

            if (trueKeys.Count == 1)
            {
                trueIndex = trueKeys[0];
                return true;
            }

            var fallback = TryResolveCorrectOptionIndex(text, optionCount);
            if (fallback >= 0)
            {
                trueIndex = fallback;
                return true;
            }

            return false;
        }

        private static int TryResolveCorrectOptionIndex(string raw, int optionCount)
        {
            var idx = LabelToIndex(raw);
            if (idx >= 0 && idx < optionCount) return idx;

            var text = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) return -1;
            var match = Regex.Match(text, @"\b([A-Z])\b", RegexOptions.IgnoreCase);
            if (!match.Success) return -1;
            idx = LabelToIndex(match.Groups[1].Value);
            return idx >= 0 && idx < optionCount ? idx : -1;
        }

        private static string BuildCanonicalTrueFalseAnswer(int trueIndex, int optionCount)
        {
            var values = Enumerable.Range(0, optionCount)
                .Select(i => $"{OptionLabel(i)}={(i == trueIndex ? "True" : "False")}");
            return string.Join("; ", values);
        }

        private static List<WorkbookActivityBundle> RenumberWorkbookBundles(List<WorkbookActivityBundle> source)
        {
            var result = new List<WorkbookActivityBundle>();
            if (source == null || source.Count == 0) return result;

            var activityNumber = 1;
            foreach (var bundle in source)
            {
                if (bundle == null) continue;

                result.Add(new WorkbookActivityBundle
                {
                    Item = bundle.Item ?? new AssessmentDrivenQuestionGenerator.LessonEvidenceItem(),
                    TrueFalseQuestion = CloneQuestion(bundle.TrueFalseQuestion, activityNumber * 2 - 1),
                    MultipleChoiceQuestion = CloneQuestion(bundle.MultipleChoiceQuestion, activityNumber * 2)
                });

                activityNumber++;
            }

            return result;
        }

        private static AssessmentDrivenQuestionGenerator.GeneratedQuestion CloneQuestion(
            AssessmentDrivenQuestionGenerator.GeneratedQuestion source,
            int questionNumber)
        {
            source ??= new AssessmentDrivenQuestionGenerator.GeneratedQuestion();

            return new AssessmentDrivenQuestionGenerator.GeneratedQuestion
            {
                Number = questionNumber,
                Type = source.Type,
                Prompt = source.Prompt,
                Options = source.Options?.ToList() ?? new List<string>(),
                CorrectAnswer = source.CorrectAnswer,
                TopicCode = source.TopicCode,
                TopicDescription = source.TopicDescription,
                LessonPlanLabel = source.LessonPlanLabel,
                AssessmentCriteriaDescription = source.AssessmentCriteriaDescription,
                Rationale = source.Rationale,
                Marks = Math.Max(1, source.Marks),
                BundleKey = source.BundleKey
            };
        }

        private static List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem> ResolveMissingLessonEvidence(
            List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem> allItems,
            List<WorkbookActivityBundle> resolvedBundles)
        {
            var missing = new List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem>();
            if (allItems == null || allItems.Count == 0) return missing;

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bundle in resolvedBundles ?? new List<WorkbookActivityBundle>())
            {
                var key = BuildBundleIdentity(bundle?.Item);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    existing.Add(key);
                }
            }

            foreach (var item in allItems)
            {
                var key = BuildBundleIdentity(item);
                if (string.IsNullOrWhiteSpace(key) || !existing.Contains(key))
                {
                    missing.Add(item);
                }
            }

            return missing;
        }

        private static string BuildBundleIdentity(AssessmentDrivenQuestionGenerator.LessonEvidenceItem? item)
        {
            if (item == null) return string.Empty;

            var bundleKey = (item.BundleKey ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(bundleKey))
            {
                return bundleKey.ToUpperInvariant();
            }

            var topicId = item.TopicId;
            var criteriaId = item.AssessmentCriteriaId;
            var lesson = (item.LessonPlanLabel ?? string.Empty).Trim().ToUpperInvariant();
            return $"{topicId}:{criteriaId}:{lesson}";
        }

        private static string BuildSmiQuestionPrompt(
            AssessmentDrivenQuestionGenerator.LessonEvidenceItem item,
            Qualification qualification,
            Subject subject,
            string questionType,
            int optionCount,
            int marks)
        {
            var isTrueFalse = string.Equals(questionType, "TrueFalse", StringComparison.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            sb.AppendLine("You are a strict TVET assessment designer for South African occupational training.");
            sb.AppendLine("Generate exactly one question and return ONLY raw JSON (no markdown, no extra text).");
            sb.AppendLine();
            sb.AppendLine("JSON schema:");
            sb.AppendLine("{");
            sb.AppendLine("  \"prompt\": \"string\",");
            sb.AppendLine("  \"options\": [\"string\"],");
            sb.AppendLine("  \"correctAnswer\": \"string\",");
            sb.AppendLine("  \"rationale\": \"string\",");
            sb.AppendLine("  \"resourceHints\": [");
            sb.AppendLine("    { \"title\": \"string\", \"url\": \"string\", \"reason\": \"string\" }");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine($"QuestionType: {questionType}");
            sb.AppendLine($"OptionCount: {optionCount}");
            sb.AppendLine($"Marks: {Math.Max(1, marks)}");
            sb.AppendLine("Rules:");
            sb.AppendLine("- Use English only.");
            sb.AppendLine("- Use clean plain text (no garbled or corrupted characters).");
            sb.AppendLine("- Apply logical reasoning using only the provided context.");
            sb.AppendLine("- Use the lesson content first and the supporting evidence second as the source of truth.");
            sb.AppendLine("- Infer the real technical concept, task, component, tool, safety point, or workplace decision from the content before writing the question.");
            sb.AppendLine("- Keep the stem self-contained and practical.");
            sb.AppendLine("- Do not include topic codes, AC numbers, LPN labels, or administrative metadata in the stem/options.");
            sb.AppendLine("- Never mention phrases such as 'lesson plan content', 'topic content', 'curriculum', 'assessment criteria', or 'content map' in the stem or options.");
            sb.AppendLine("- Keep options homogeneous and realistic.");
            sb.AppendLine("- Do not use 'All of the above' or 'None of the above'.");
            if (isTrueFalse)
            {
                sb.AppendLine("- Build one stem and OptionCount statements.");
                sb.AppendLine("- Exactly one statement must be True.");
                sb.AppendLine("- correctAnswer format: \"A=True; B=False; C=False; D=False\".");
            }
            else
            {
                sb.AppendLine("- Build one stem and OptionCount options.");
                sb.AppendLine("- Exactly one option is correct.");
                sb.AppendLine("- correctAnswer format: option letter only, for example \"B\".");
            }
            sb.AppendLine("- resourceHints can be empty, but when present include practical learner resources.");
            sb.AppendLine();
            sb.AppendLine("Context:");
            sb.AppendLine($"Qualification: {CleanPromptField(qualification.QualificationNumber)} - {CleanPromptField(qualification.QualificationDescription)}");
            sb.AppendLine($"Subject: {CleanPromptField(subject.SubjectCode)} - {CleanPromptField(subject.SubjectDescription)}");
            sb.AppendLine($"TopicFocus: {CleanPromptField(AssessmentDrivenQuestionGenerator.BuildLearnerContextLabel(item))}");
            sb.AppendLine($"TopicDescription: {CleanPromptField(item.TopicDescription)}");
            sb.AppendLine($"LessonPlanLabel: {CleanPromptField(item.LessonPlanLabel)}");
            sb.AppendLine($"LearningRequirement: {CleanPromptField(AssessmentDrivenQuestionGenerator.SanitizeQuestionText(item.AssessmentCriteriaDescription))}");
            sb.AppendLine($"LessonContent: {CleanPromptField(TrimForPrompt(item.LessonPlanContent, 1600))}");
            sb.AppendLine($"EvidenceText: {CleanPromptField(TrimForPrompt(item.EvidenceText, 1600))}");
            return sb.ToString();
        }

        private static string CleanPromptField(string? value)
            => (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

        private static string TrimForPrompt(string? value, int maxChars)
        {
            var cleaned = CleanPromptField(value);
            if (cleaned.Length <= maxChars) return cleaned;
            return cleaned[..maxChars];
        }

        private static SmiGeneratedQuestionEnvelope? TryParseSmiQuestionEnvelope(string raw)
        {
            var json = TryExtractJsonObject(raw);
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                if (root.TryGetProperty("question", out var questionEl) && questionEl.ValueKind == JsonValueKind.Object)
                {
                    root = questionEl;
                }

                var prompt = ReadJsonString(root, "prompt");
                if (string.IsNullOrWhiteSpace(prompt)) prompt = ReadJsonString(root, "stem");

                var options = new List<string>();
                if (root.TryGetProperty("options", out var optionsEl) && optionsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var option in optionsEl.EnumerateArray())
                    {
                        var text = option.ValueKind == JsonValueKind.String ? option.GetString() ?? string.Empty : string.Empty;
                        text = text.Trim();
                        if (!string.IsNullOrWhiteSpace(text)) options.Add(text);
                    }
                }

                if (options.Count == 0 && root.TryGetProperty("choices", out var choicesEl) && choicesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var option in choicesEl.EnumerateArray())
                    {
                        var text = option.ValueKind == JsonValueKind.String ? option.GetString() ?? string.Empty : string.Empty;
                        text = text.Trim();
                        if (!string.IsNullOrWhiteSpace(text)) options.Add(text);
                    }
                }

                var correctAnswer = ReadJsonString(root, "correctAnswer");
                if (string.IsNullOrWhiteSpace(correctAnswer)) correctAnswer = ReadJsonString(root, "answer");
                if (string.IsNullOrWhiteSpace(correctAnswer)) correctAnswer = ReadJsonString(root, "correctOption");

                var rationale = ReadJsonString(root, "rationale");
                if (string.IsNullOrWhiteSpace(rationale)) rationale = ReadJsonString(root, "explanation");

                var resourceHints = ParseResourceHints(root);

                if (string.IsNullOrWhiteSpace(prompt) || options.Count == 0 || string.IsNullOrWhiteSpace(correctAnswer))
                {
                    return null;
                }

                return new SmiGeneratedQuestionEnvelope
                {
                    Prompt = prompt,
                    Options = options,
                    CorrectAnswer = correctAnswer,
                    Rationale = rationale,
                    ResourceHints = resourceHints
                };
            }
            catch
            {
                return null;
            }
        }

        private static List<string> ParseResourceHints(JsonElement root)
        {
            var results = new List<string>();
            foreach (var key in new[] { "resourceHints", "resources", "learningResources" })
            {
                if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array) continue;
                foreach (var row in value.EnumerateArray())
                {
                    if (row.ValueKind == JsonValueKind.String)
                    {
                        var text = (row.GetString() ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(text)) results.Add(text);
                        continue;
                    }

                    if (row.ValueKind == JsonValueKind.Object)
                    {
                        var title = ReadJsonString(row, "title");
                        var url = ReadJsonString(row, "url");
                        var reason = ReadJsonString(row, "reason");
                        var parts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(title)) parts.Add(title);
                        if (!string.IsNullOrWhiteSpace(url)) parts.Add(url);
                        if (!string.IsNullOrWhiteSpace(reason)) parts.Add(reason);
                        if (parts.Count > 0) results.Add(string.Join(" | ", parts));
                    }
                }
            }

            return results
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList();
        }

        private async Task<SmiAnswerResult?> TryFetchSmiAnswerAsync(
            string prompt,
            string qualificationCode,
            string qualificationDescription,
            string mode,
            CancellationToken cancellationToken)
        {
            if (!IsSmiIntegrationEnabled()) return null;
            if (string.IsNullOrWhiteSpace(prompt)) return null;

            var baseUrl = GetSmiBaseUrl();
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;

            var payload = new
            {
                prompt,
                qualification = qualificationCode,
                curriculum_name = qualificationDescription,
                top_k = GetSmiTopK(),
                mode = string.IsNullOrWhiteSpace(mode) ? "knowledge" : mode.Trim().ToLowerInvariant()
            };

            using var msg = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/etdp/lesson-content");
            msg.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var timeoutSeconds = GetSmiTimeoutSeconds();
                using var timeoutCts = timeoutSeconds > 0
                    ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))
                    : null;
                using var linked = timeoutCts != null
                    ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
                    : null;
                var token = linked?.Token ?? cancellationToken;

                var resp = await _http.SendAsync(msg, token);
                var body = await resp.Content.ReadAsStringAsync(token);
                if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body)) return null;

                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
                {
                    return null;
                }

                var answer = ReadJsonString(doc.RootElement, "answer");
                if (string.IsNullOrWhiteSpace(answer)) answer = ReadJsonString(doc.RootElement, "response");
                if (string.IsNullOrWhiteSpace(answer)) answer = ReadJsonString(doc.RootElement, "output");
                if (string.IsNullOrWhiteSpace(answer)) return null;
                if (IsSmiBusyPlaceholderResponse(answer)) return null;
                answer = CleanSmiAnswerText(answer);
                if (string.IsNullOrWhiteSpace(answer)) return null;

                var citations = new List<string>();
                if (doc.RootElement.TryGetProperty("citations", out var citationsEl) && citationsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var citation in citationsEl.EnumerateArray())
                    {
                        if (citation.ValueKind == JsonValueKind.String)
                        {
                            var text = (citation.GetString() ?? string.Empty).Trim();
                            if (!string.IsNullOrWhiteSpace(text)) citations.Add(text);
                            continue;
                        }

                        if (citation.ValueKind == JsonValueKind.Object)
                        {
                            var sourceId = ReadJsonString(citation, "source_id");
                            var title = ReadJsonString(citation, "title");
                            var publishedDate = ReadJsonString(citation, "published_date");
                            var url = ReadJsonString(citation, "url");
                            var parts = new List<string>();
                            if (!string.IsNullOrWhiteSpace(sourceId)) parts.Add(sourceId);
                            if (!string.IsNullOrWhiteSpace(title)) parts.Add(title);
                            if (!string.IsNullOrWhiteSpace(url)) parts.Add(url);
                            if (!string.IsNullOrWhiteSpace(publishedDate)) parts.Add(publishedDate);
                            if (parts.Count > 0) citations.Add(string.Join(" | ", parts));
                        }
                    }
                }

                return new SmiAnswerResult
                {
                    Answer = answer.Trim(),
                    Citations = citations
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Select(v => v.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(25)
                        .ToList()
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSmiBusyPlaceholderResponse(string? text)
        {
            var normalized = Regex.Replace((text ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
            if (string.IsNullOrWhiteSpace(normalized)) return false;
            if (normalized.Length > 1200) return false;

            return (normalized.Contains("knowledge index") && normalized.Contains("busy"))
                || normalized.Contains("background ingestion")
                || normalized.Contains("quick response mode")
                || normalized.Contains("retry this prompt")
                || (normalized.Contains("retry") && normalized.Contains("fuller context"))
                || normalized.Contains("background clustering")
                || normalized.Contains("ingestion and clustering");
        }

        private static bool IsLikelyEnglishText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            var lowered = text.ToLowerInvariant();
            var foreignHits = Regex.Matches(
                lowered,
                @"\b(que|para|não|nao|por favor|gracias|obrigado|obrigada|hola|olá|porque|está|esta|voc[eê]|usted|mañana|manana|então|entao)\b",
                RegexOptions.CultureInvariant).Count;

            var words = Regex.Matches(lowered, @"[a-z]{2,}", RegexOptions.CultureInvariant)
                .Select(m => m.Value)
                .ToList();
            if (words.Count < 4) return true;

            var commonEnglish = words.Count(w =>
                w is "the" or "and" or "for" or "with" or "from" or "that" or "this" or "which" or "you" or "your" or "is" or "are");
            var ratio = words.Count == 0 ? 0d : (double)commonEnglish / words.Count;

            if (foreignHits >= 2 && ratio < 0.08d) return false;
            return ratio >= 0.05d || foreignHits == 0;
        }

        private static bool HasNoiseArtifacts(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (text.Contains('\uFFFD')) return true;
            if (text.Contains("Ã", StringComparison.Ordinal) || text.Contains("â€", StringComparison.Ordinal)) return true;
            if (Regex.IsMatch(text, @"([!?.,;:\-])\1{5,}", RegexOptions.CultureInvariant)) return true;
            if (Regex.IsMatch(text, @"(?i)<unused\d+>", RegexOptions.CultureInvariant)) return true;

            var nonAscii = text.Count(c => c > 127);
            return nonAscii > Math.Max(6, text.Length / 12);
        }

        private static bool IsCurriculumEchoQuestion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(
                text,
                @"\b(?:lesson\s+plan\s+content|topic\s+content|curriculum(?:\s+content|\s+map)?|assessment\s+criteria?|source\s+excerpt|cited\s+source)\b|[A-Za-z]:\\|\.pdf\b|https?://",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string CleanSmiAnswerText(string? text)
        {
            var value = text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            value = value
                .Replace("\uFFFD", " ")
                .Replace("â€™", "'")
                .Replace("â€˜", "'")
                .Replace("â€œ", "\"")
                .Replace("â€", "\"")
                .Replace("â€“", "-")
                .Replace("â€”", "-")
                .Replace("â€¦", "...")
                .Replace("Â", " ");
            value = value.Normalize(NormalizationForm.FormKC);
            value = Regex.Replace(value, @"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", " ");
            value = value.Replace("\r\n", "\n");
            var lines = value
                .Split('\n')
                .Select(line => Regex.Replace(line, @"[ \t]{2,}", " ").TrimEnd());
            value = string.Join("\n", lines).Trim();
            value = Regex.Replace(value, @"\n{3,}", "\n\n");
            value = StripSmiPreambleNoise(value);
            value = StripNonLessonSections(value);

            if (value.Length > 24000)
            {
                value = value.Substring(0, 24000).TrimEnd();
            }

            return value;
        }

        private static string StripSmiPreambleNoise(string text)
        {
            var value = (text ?? string.Empty).TrimStart();
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var anchor = Regex.Match(value, @"(?i)\bhere(?:'|’)?s a lesson content draft\b", RegexOptions.CultureInvariant);
            if (anchor.Success && anchor.Index > 0 && anchor.Index <= 180)
            {
                value = value.Substring(anchor.Index).TrimStart();
            }

            value = Regex.Replace(
                value,
                @"^\s*(?:<unused\d+>\s*!?\s*|[\p{So}\p{Sk}\p{Cs}\p{Cf}]+\s*|[^\x00-\x7F]{1,24}\s*|[A-Za-z]{1,18}!\s*|[A-Za-z]{1,24}:\s*)(?=(?:Here(?:'|’)?s|Lesson|Module|Topic|Unit|\d{4,}\s+KM-|KM-\d{2}-KT\d{2}))",
                string.Empty,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            return value.TrimStart();
        }

        private static string StripNonLessonSections(string text)
        {
            var value = text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            value = Regex.Replace(
                value,
                @"(?ims)^\s*(?:#+\s*)?(?:table\s+of\s+contents|contents)\s*:?\s*$[\r\n]+(?:^\s*(?:\d+[\).\-\s]+)?[^\r\n]{1,160}(?:\.{2,}\s*\d+)?\s*$[\r\n]*){1,80}",
                string.Empty);

            value = Regex.Replace(
                value,
                @"(?im)^\s*(?:#+\s*)?(?:cover\s+page|title\s+page|abstract(?:\s+page)?|introduction)\s*:?\s*$\r?\n?",
                string.Empty);

            value = Regex.Replace(
                value,
                @"(?ims)^\s*(?:#+\s*)?(?:bibliography|references)\s*:?\s*$[\s\S]*$",
                string.Empty);

            value = Regex.Replace(value, @"\n{3,}", "\n\n").Trim();
            return value;
        }

        private static string TryExtractJsonObject(string raw)
        {
            var text = StripCodeFence(raw).Trim();
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            if (text.StartsWith("{", StringComparison.Ordinal) && text.EndsWith("}", StringComparison.Ordinal))
            {
                return text;
            }

            var firstBrace = text.IndexOf('{');
            if (firstBrace < 0) return string.Empty;
            var depth = 0;
            for (var i = firstBrace; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(firstBrace, i - firstBrace + 1);
                    }
                }
            }

            return string.Empty;
        }

        private static string StripCodeFence(string raw)
        {
            var text = (raw ?? string.Empty).Trim();
            if (!text.StartsWith("```", StringComparison.Ordinal)) return text;

            var firstLineBreak = text.IndexOf('\n');
            if (firstLineBreak < 0) return text.Trim('`').Trim();

            var body = text[(firstLineBreak + 1)..];
            var closeFence = body.LastIndexOf("```", StringComparison.Ordinal);
            if (closeFence >= 0)
            {
                body = body[..closeFence];
            }

            return body.Trim();
        }

        private static bool IsSmiIntegrationEnabled()
        {
            var raw = (Environment.GetEnvironmentVariable("SMI_ENABLED") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return true;

            return !(raw.Equals("0", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("false", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("off", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("no", StringComparison.OrdinalIgnoreCase));
        }

        private static string GetSmiBaseUrl()
        {
            var env = (Environment.GetEnvironmentVariable("SMI_BASE_URL") ?? string.Empty).Trim();
            var baseUrl = string.IsNullOrWhiteSpace(env) ? DefaultSmiBaseUrl : env;
            return baseUrl.TrimEnd('/');
        }

        private static int GetSmiTimeoutSeconds()
        {
            var raw = (Environment.GetEnvironmentVariable("SMI_TIMEOUT_SECONDS") ?? string.Empty).Trim();
            if (int.TryParse(raw, out var parsed))
            {
                return Math.Clamp(parsed, 0, 20);
            }
            return DefaultSmiTimeoutSeconds;
        }

        private static int GetSmiTopK()
        {
            var raw = (Environment.GetEnvironmentVariable("SMI_TOP_K") ?? string.Empty).Trim();
            if (int.TryParse(raw, out var parsed))
            {
                return Math.Clamp(parsed, 0, 20);
            }
            return DefaultSmiTopK;
        }

        private static string ReadJsonString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value)) return string.Empty;
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? string.Empty;
            if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            {
                return value.ToString();
            }
            return string.Empty;
        }

        private static string OptionLabel(int index)
        {
            if (index < 0) return "A";
            return ((char)('A' + index)).ToString();
        }

        private static int LabelToIndex(string? label)
        {
            var raw = (label ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return -1;
            var c = raw[0];
            if (c >= 'a' && c <= 'z') c = char.ToUpperInvariant(c);
            if (c < 'A' || c > 'Z') return -1;
            return c - 'A';
        }

        private static List<WorkbookActivityBundle> BuildWorkbookActivitiesFromQuestionBank(
            List<ExternalQuestionBankRow> rows,
            int maxActivities)
        {
            var activities = new List<WorkbookActivityBundle>();
            if (rows.Count == 0) return activities;

            var grouped = rows
                .GroupBy(r => $"{NormalizeBankKey(r.TopicCode)}::{NormalizeBankKey(r.AssessmentCriterion)}")
                .OrderBy(g => g.Min(r => r.QuestionNumber <= 0 ? int.MaxValue : r.QuestionNumber))
                .ToList();

            foreach (var group in grouped)
            {
                var tfRow = group.FirstOrDefault(r => r.IsTrueFalse);
                var mcqRow = group.FirstOrDefault(r => !r.IsTrueFalse);
                var seed = tfRow ?? mcqRow;
                if (seed == null) continue;

                var item = ExternalQuestionBank.ToLessonEvidenceItem(seed);
                var index = activities.Count + 1;
                var trueFalse = tfRow != null
                    ? ExternalQuestionBank.ToGeneratedQuestion(tfRow, tfRow.QuestionNumber > 0 ? tfRow.QuestionNumber : index * 2 - 1)
                    : AssessmentDrivenQuestionGenerator.BuildTrueFalseQuestion(item, index * 2 - 1, 2);
                var multipleChoice = mcqRow != null
                    ? ExternalQuestionBank.ToGeneratedQuestion(mcqRow, mcqRow.QuestionNumber > 0 ? mcqRow.QuestionNumber : index * 2)
                    : AssessmentDrivenQuestionGenerator.BuildMultipleChoiceQuestion(item, index * 2, 2);

                activities.Add(new WorkbookActivityBundle
                {
                    Item = item,
                    TrueFalseQuestion = trueFalse,
                    MultipleChoiceQuestion = multipleChoice
                });

                if (activities.Count >= maxActivities) break;
            }

            if (activities.Count > 0) return activities;

            var tfRows = rows.Where(r => r.IsTrueFalse).ToList();
            var mcqRows = rows.Where(r => !r.IsTrueFalse).ToList();
            var pairCount = Math.Min(maxActivities, Math.Min(tfRows.Count, mcqRows.Count));
            for (var i = 0; i < pairCount; i++)
            {
                var seed = tfRows[i];
                activities.Add(new WorkbookActivityBundle
                {
                    Item = ExternalQuestionBank.ToLessonEvidenceItem(seed),
                    TrueFalseQuestion = ExternalQuestionBank.ToGeneratedQuestion(tfRows[i], tfRows[i].QuestionNumber > 0 ? tfRows[i].QuestionNumber : i * 2 + 1),
                    MultipleChoiceQuestion = ExternalQuestionBank.ToGeneratedQuestion(mcqRows[i], mcqRows[i].QuestionNumber > 0 ? mcqRows[i].QuestionNumber : i * 2 + 2)
                });
            }

            return activities;
        }

        private static string NormalizeBankKey(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();

        private Subject? ResolveSubject(int? subjectId, int? qualificationId)
        {
            if (subjectId.HasValue && subjectId.Value > 0)
            {
                var byId = _context.Subjects.FirstOrDefault(s => s.Id == subjectId.Value);
                if (byId != null && HasSubjectIdentity(byId) && (!qualificationId.HasValue || byId.QualificationId == qualificationId.Value))
                {
                    return byId;
                }
            }

            if (qualificationId.HasValue && qualificationId.Value > 0)
            {
                return _context.Subjects
                    .Where(s => s.QualificationId == qualificationId.Value)
                    .Where(s => !string.IsNullOrWhiteSpace((s.SubjectCode ?? string.Empty).Trim()) ||
                                !string.IsNullOrWhiteSpace((s.SubjectDescription ?? string.Empty).Trim()))
                    .OrderBy(s => s.SubjectCode)
                    .ThenBy(s => s.SubjectDescription)
                    .FirstOrDefault();
            }

            return _context.Subjects
                .Where(s => !string.IsNullOrWhiteSpace((s.SubjectCode ?? string.Empty).Trim()) ||
                            !string.IsNullOrWhiteSpace((s.SubjectDescription ?? string.Empty).Trim()))
                .OrderBy(s => s.SubjectCode)
                .ThenBy(s => s.SubjectDescription)
                .FirstOrDefault();
        }

        private Qualification? ResolveQualification(Subject subject, int? qualificationId)
        {
            if (qualificationId.HasValue && qualificationId.Value > 0)
            {
                var q = _context.Qualifications.FirstOrDefault(x => x.Id == qualificationId.Value);
                if (q != null) return q;
            }
            return _context.Qualifications.FirstOrDefault(q => q.Id == subject.QualificationId)
                   ?? _context.Qualifications.FirstOrDefault();
        }

        private static void EnsureWorkbookDocumentSettings(MainDocumentPart main)
        {
            var settingsPart = main.DocumentSettingsPart ?? main.AddNewPart<DocumentSettingsPart>();
            settingsPart.Settings = new Settings(new UpdateFieldsOnOpen() { Val = true });
            settingsPart.Settings.Save();
        }

        private static void EnsureWorkbookStyles(MainDocumentPart main)
        {
            var stylePart = main.StyleDefinitionsPart ?? main.AddNewPart<StyleDefinitionsPart>();
            stylePart.Styles ??= new Styles();

            UpsertParagraphStyle(stylePart.Styles, BuildNormalStyle());
            UpsertParagraphStyle(stylePart.Styles, BuildHeadingStyle("Heading1", "heading 1", 0));
            UpsertParagraphStyle(stylePart.Styles, BuildHeadingStyle("Heading2", "heading 2", 1));
            UpsertParagraphStyle(stylePart.Styles, BuildHeadingStyle("Heading3", "heading 3", 2));
            UpsertParagraphStyle(stylePart.Styles, BuildTocStyle("TOC1", "toc 1", 0, bold: true));
            UpsertParagraphStyle(stylePart.Styles, BuildTocStyle("TOC2", "toc 2", 240, bold: true));
            UpsertParagraphStyle(stylePart.Styles, BuildTocStyle("TOC3", "toc 3", 480, bold: false));
            stylePart.Styles.Save();
        }

        private static void UpsertParagraphStyle(Styles styles, Style style)
        {
            var existing = styles.Elements<Style>()
                .FirstOrDefault(candidate => string.Equals(candidate.StyleId?.Value, style.StyleId?.Value, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Remove();
            }

            styles.Append(style);
        }

        private static Style BuildNormalStyle()
        {
            var style = new Style
            {
                Type = StyleValues.Paragraph,
                StyleId = "Normal",
                Default = true
            };
            style.Append(
                new StyleName { Val = "Normal" },
                new PrimaryStyle(),
                new StyleRunProperties(
                    new RunFonts { Ascii = ExportFont, HighAnsi = ExportFont },
                    new FontSize { Val = "22" },
                    new FontSizeComplexScript { Val = "22" }));
            return style;
        }

        private static Style BuildHeadingStyle(string styleId, string styleName, int outlineLevel)
        {
            var style = new Style
            {
                Type = StyleValues.Paragraph,
                StyleId = styleId,
                CustomStyle = false
            };
            style.Append(
                new StyleName { Val = styleName },
                new BasedOn { Val = "Normal" },
                new NextParagraphStyle { Val = "Normal" },
                new UIPriority { Val = 9 },
                new UnhideWhenUsed(),
                new PrimaryStyle(),
                new StyleParagraphProperties(
                    new OutlineLevel { Val = outlineLevel }),
                new StyleRunProperties(
                    new RunFonts { Ascii = ExportFont, HighAnsi = ExportFont }));
            return style;
        }

        private static Style BuildTocStyle(string styleId, string styleName, int leftIndentTwips, bool bold)
        {
            var runProperties = new StyleRunProperties(
                new RunFonts { Ascii = ExportFont, HighAnsi = ExportFont },
                new FontSize { Val = "22" },
                new FontSizeComplexScript { Val = "22" });
            if (bold)
            {
                runProperties.Bold = new Bold();
            }

            var style = new Style
            {
                Type = StyleValues.Paragraph,
                StyleId = styleId,
                CustomStyle = false
            };
            style.Append(
                new StyleName { Val = styleName },
                new BasedOn { Val = "Normal" },
                new NextParagraphStyle { Val = "Normal" },
                new AutoRedefine(),
                new UIPriority { Val = 39 },
                new UnhideWhenUsed(),
                new StyleParagraphProperties(
                    new SpacingBetweenLines { After = "100" },
                    new Indentation { Left = leftIndentTwips.ToString() }),
                runProperties);
            return style;
        }

        private static string EnsureWorkbookFooter(MainDocumentPart main)
        {
            var footerPart = main.AddNewPart<FooterPart>();
            var footer = new Footer();
            footer.Append(new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Right }),
                FooterRun("Page "),
                new SimpleField { Instruction = " PAGE " },
                FooterRun(" of "),
                new SimpleField { Instruction = " NUMPAGES " }));
            footerPart.Footer = footer;
            footerPart.Footer.Save();
            return main.GetIdOfPart(footerPart);
        }

        private static Run FooterRun(string text)
        {
            return new Run(
                new RunProperties(
                    new FontSize { Val = "20" },
                    new RunFonts { Ascii = ExportFont, HighAnsi = ExportFont }),
                new Text(SanitizeXmlText(text ?? string.Empty)) { Space = SpaceProcessingModeValues.Preserve });
        }

        private static int CentimetresToTwips(double centimetres)
        {
            return Math.Max(0, (int)Math.Round(centimetres * 1440d / 2.54d));
        }

        private static SectionProperties DefaultSectionProperties(string? footerRelId = null)
        {
            var section = new SectionProperties();
            if (!string.IsNullOrWhiteSpace(footerRelId))
            {
                section.Append(new FooterReference { Type = HeaderFooterValues.Default, Id = footerRelId });
            }

            section.Append(
                new PageSize() { Orient = PageOrientationValues.Portrait, Width = WorkbookPageWidthTwips, Height = WorkbookPageHeightTwips },
                new PageMargin()
                {
                    Top = 1020,
                    Bottom = 907,
                    Left = WorkbookPageMarginTwips,
                    Right = WorkbookPageMarginTwips,
                    Header = 709,
                    Footer = 709,
                    Gutter = 0
                });

            return section;
        }

        private static TableProperties DefaultTableProperties()
        {
            return new TableProperties(
                new TableWidth() { Type = TableWidthUnitValues.Dxa, Width = WorkbookFullTableWidth },
                new TableLayout() { Type = TableLayoutValues.Fixed },
                new TableJustification() { Val = TableRowAlignmentValues.Left },
                BuildVisibleTableBorders());
        }

        private static TableBorders BuildVisibleTableBorders()
        {
            return new TableBorders(
                new TopBorder() { Val = BorderValues.Single, Size = 8, Color = "000000", Space = 0U },
                new LeftBorder() { Val = BorderValues.Single, Size = 8, Color = "000000", Space = 0U },
                new BottomBorder() { Val = BorderValues.Single, Size = 8, Color = "000000", Space = 0U },
                new RightBorder() { Val = BorderValues.Single, Size = 8, Color = "000000", Space = 0U },
                new InsideHorizontalBorder() { Val = BorderValues.Single, Size = 8, Color = "000000", Space = 0U },
                new InsideVerticalBorder() { Val = BorderValues.Single, Size = 8, Color = "000000", Space = 0U });
        }

        private static TableCellBorders BuildVisibleTableCellBorders()
        {
            return new TableCellBorders(
                new TopBorder() { Val = BorderValues.Single, Size = 8, Color = "000000", Space = 0U },
                new LeftBorder() { Val = BorderValues.Single, Size = 8, Color = "000000", Space = 0U },
                new BottomBorder() { Val = BorderValues.Single, Size = 8, Color = "000000", Space = 0U },
                new RightBorder() { Val = BorderValues.Single, Size = 8, Color = "000000", Space = 0U });
        }

        private static Paragraph Blank() => new(new Run(new Text("")));

        private static Paragraph PageBreak() => new(new Run(new Break() { Type = BreakValues.Page }));

        private static Paragraph CenterPara(string text, int sizePt, bool bold)
        {
            var rPr = new RunProperties();
            if (bold) rPr.Bold = new Bold();
            rPr.FontSize = new FontSize() { Val = (CompactHeadingPt(sizePt) * 2).ToString() };
            rPr.RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont };
            var pPr = new ParagraphProperties(new Justification() { Val = JustificationValues.Center });
            return new Paragraph(pPr, new Run(rPr, new Text(SanitizeXmlText(text ?? string.Empty))));
        }

        private static Paragraph HeadingPara(string text, int sizePt)
        {
            var rPr = new RunProperties
            {
                Bold = new Bold(),
                FontSize = new FontSize() { Val = (CompactHeadingPt(sizePt) * 2).ToString() },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };
            return new Paragraph(new Run(rPr, new Text(SanitizeXmlText(text ?? string.Empty))));
        }

        private static Paragraph StyledHeading(string text, string styleId, int sizePt)
        {
            var runProps = new RunProperties
            {
                Bold = new Bold(),
                FontSize = new FontSize() { Val = (CompactHeadingPt(sizePt) * 2).ToString() },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };

            var paraProps = new ParagraphProperties(
                new ParagraphStyleId() { Val = styleId },
                new SpacingBetweenLines() { Before = "120", After = "80" });

            return new Paragraph(paraProps, new Run(runProps, new Text(SanitizeXmlText(text ?? string.Empty))));
        }

        private static Paragraph BodyPara(string text, int sizeHalfPt)
        {
            var rPr = new RunProperties
            {
                FontSize = new FontSize() { Val = CompactBodyHalfPt(sizeHalfPt).ToString() },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };
            var pPr = new ParagraphProperties(
                new SpacingBetweenLines() { Line = "280", LineRule = LineSpacingRuleValues.Auto },
                new Indentation() { FirstLine = "680" }
            );
            return new Paragraph(pPr, new Run(rPr, new Text(SanitizeXmlText(text ?? string.Empty))));
        }

        private static Table BuildWorkbookLearnerParticularsTable(Subject subject, int totalMarksPossible)
        {
            var table = new Table();
            table.Append(DefaultTableProperties());
            table.Append(new TableGrid(
                new GridColumn() { Width = "5200" },
                new GridColumn() { Width = "5200" }));

            var subjectLine = ((subject.SubjectDescription ?? "WORKBOOK").Trim()).ToUpperInvariant();
            table.Append(BuildMergedWorkbookRow(subjectLine, 2, bold: true, center: true, fontHalfPoints: "32"));
            table.Append(BuildWorkbookLabelValueRow("LEARNER NAME:", string.Empty));
            table.Append(BuildWorkbookLabelValueRow("LEARNER SURNAME:", string.Empty));
            table.Append(BuildWorkbookLabelValueRow("LEARNER RSA ID NUMBER:", string.Empty));
            table.Append(BuildWorkbookLabelValueRow("DATE COMPLETED:", string.Empty));
            table.Append(BuildWorkbookLabelValueRow("FACILITATOR:", string.Empty));
            table.Append(BuildWorkbookLabelValueRow("ASSESSOR:", string.Empty));
            table.Append(BuildWorkbookLabelValueRow("MODERATOR:", string.Empty));
            table.Append(BuildWorkbookLabelValueRow("TOTAL MARKS POSSIBLE:", totalMarksPossible.ToString()));
            table.Append(BuildWorkbookLabelValueRow("TOTAL MARKS ACHIEVED:", string.Empty));
            return table;
        }

        private static Table BuildWorkbookRoleplayersTable()
        {
            var table = new Table();
            table.Append(DefaultTableProperties());
            table.Append(new TableGrid(
                new GridColumn() { Width = "4200" },
                new GridColumn() { Width = "2200" },
                new GridColumn() { Width = "4000" }));
            table.Append(new TableRow(
                BuildWorkbookCell("ROLEPLAYER", width: "4200", bold: true, center: true),
                BuildWorkbookCell("DATE", width: "2200", bold: true, center: true),
                BuildWorkbookCell("SIGNATURE", width: "4000", bold: true, center: true)));
            table.Append(new TableRow(
                BuildWorkbookCell("LECTURER/ ASSESSOR:", width: "4200", bold: true),
                BuildWorkbookCell(string.Empty, width: "2200"),
                BuildWorkbookCell(string.Empty, width: "4000")));
            table.Append(new TableRow(
                BuildWorkbookCell("MODERATOR:", width: "4200", bold: true),
                BuildWorkbookCell(string.Empty, width: "2200"),
                BuildWorkbookCell(string.Empty, width: "4000")));
            table.Append(new TableRow(
                BuildWorkbookCell("LEARNER/ CANDIDATE:", width: "4200", bold: true),
                BuildWorkbookCell(string.Empty, width: "2200"),
                BuildWorkbookCell(string.Empty, width: "4000")));
            return table;
        }

        private static void AppendWorkbookInstructionsPage(Body body)
        {
            body.Append(StyledHeading("WORKBOOK INSTRUCTIONS", "Heading1", 18));
            foreach (var line in new[]
            {
                "This Workbook is an essential part of your Portfolio of Evidence, during the programme you will be required to complete these activities during or after class hours.",
                "This Workbook Counts 10% towards your final summative assessment mark. You therefor need to ensure that you do not abuse, lose it or allow your fellow students to copy from it.",
                "If you lose this workbook, you will be required to start writing in new one at your own time.",
                "Neatness if of critical importance to ensure the assessor and can read and understand what you write.",
                "Most workbook activities will take the form of group discussions during class.",
                "You may not use a pencil to fill in temporary answers and erase it to completed later with a black pen.",
                "You may only use a black pen to complete these activities.",
                "Thie purpose of this workbook is to measure your learning progress, being dishonest will only be to your disadvantage and create a situation where the lecturer is unable to measure your progress and determine learning areas in which you might need further assistance or guidance.",
                "This workbook must be submitted at the end of each phase as part of your portfolio of evidence."
            })
            {
                body.Append(BulletPara(line, 24));
            }
        }

        private static string BuildWorkbookMemorandumExpectation(WorkbookDiscussionActivity activity)
        {
            var focus = (activity.PromptDescriptor ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(focus))
            {
                focus = (activity.TopicDescription ?? string.Empty).Trim();
            }
            if (string.IsNullOrWhiteSpace(focus))
            {
                focus = "the discussed topic";
            }

            return $"Learners must discuss and present correct findings relating to {focus}. Award 1 mark per valid fact, up to 4 marks.";
        }

        private static string CleanWorkbookMemorandumContent(string? content)
        {
            var text = (content ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            text = Regex.Replace(text, @"\bWould you like to\b.*$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }

        private static void AppendTableOfContentsPage(Body body)
        {
            body.Append(StyledHeading("TABLE OF CONTENTS", "Heading1", 22));
            body.Append(BodyPara("If the table is blank, open this file in Microsoft Word and choose Update Field.", 22));
            body.Append(BuildTableOfContentsField());
        }

        private static Paragraph BuildTableOfContentsField()
        {
            return new Paragraph(
                new Run(new FieldChar() { FieldCharType = FieldCharValues.Begin }),
                new Run(new FieldCode(" TOC \\o \"1-3\" \\h \\z \\u ") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new FieldChar() { FieldCharType = FieldCharValues.Separate }),
                new Run(new Text("Table of contents will populate after field update.") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new FieldChar() { FieldCharType = FieldCharValues.End }));
        }

        private static void AppendLegalDisclaimerPage(Body body, Qualification qualification)
        {
            var year = DateTime.Now.Year;
            var institution = (qualification.LearningInstitutionName ?? string.Empty).Trim();

            body.Append(HeadingPara("DISCLAIMER", 18));
            body.Append(BodyPara("ETDP Courseware Release ETDP RSA PATENT 004/026785", 22));
            body.Append(BodyPara($"(C) {year} by Dr P.C. Wepener. This document is generated by the ETDP App under the authority and final approval of the authorised learning-material owner.", 22));
            body.Append(BodyPara("Neither Dr P.C. Wepener nor the ETDP App is accountable or liable for the correctness, completeness, factual, or academic correctness of this document. The accredited learning institution should be contacted for content inquiries, sources, references, or citations.", 22));

            body.Append(HeadingPara("NOTICE OF RIGHTS", 14));
            body.Append(BodyPara("No part of this publication may be reproduced, transmitted, transcribed, stored in a retrieval system, or translated into any language or computer language, in any form or by any means, electronic, mechanical, magnetic, optical, chemical, manual, or otherwise, without prior written permission from the branded learning institution that owns the legal and intellectual property rights to the content of this document.", 22));

            body.Append(HeadingPara("TRADEMARK NOTICE", 14));
            body.Append(BodyPara("Throughout this courseware title, trademark names may be used. Rather than placing a trademark symbol at every occurrence, names are used in an editorial manner for the benefit of the trademark owner, with no intention of infringement.", 22));

            body.Append(HeadingPara("NOTICE OF LIABILITY", 14));
            body.Append(BodyPara("The information in this courseware title is distributed on an 'as is' basis, without warranty. While every precaution has been taken in preparation of this courseware, neither Dr P.C. Wepener nor the ETDP App shall have any liability to any person or entity for any loss or damage caused, or alleged to be caused, directly or indirectly by the instructions in this document or by the learning design and development processes described in it.", 22));

            body.Append(HeadingPara("TERMS AND CONDITIONS", 14));
            body.Append(BodyPara("This document is developed for the learning institution holding a legal permit and may not be resold by the learning institution. Sample versions may be shared but may not be resold to a third party. For licensed users, this document may only be used under the terms of the license agreement between the learning institution and Dr P.C. Wepener.", 22));

            if (!string.IsNullOrWhiteSpace(institution))
            {
                body.Append(BodyPara($"Learning Institution: {institution}", 22));
            }
            body.Append(BodyPara("PC WEPENER (Ph.D.) BUSINESS MANAGEMENT UJ 2005", 22));
            body.Append(BodyPara($"Pretoria, South Africa, {year}.", 22));
        }

        private static void AppendCleanCoverPage(
            Body body,
            MainDocumentPart main,
            Qualification qualification,
            string documentTitle,
            string subjectLine)
        {
            var coverPath = ResolveWorkbookCoverPath(documentTitle);
            var qualificationLine = BuildCoverQualificationLine(qualification);
            var institutionLine = (qualification.LearningInstitutionName ?? "LEARNING INSTITUTION").Trim();
            const string coverTextColor = "000000";
            var topBlockStartTwips = CentimetresToTwips(7.0d);
            var lowerBlockGapTwips = string.IsNullOrWhiteSpace(subjectLine)
                ? CentimetresToTwips(11.4d)
                : CentimetresToTwips(9.8d);
            var coverLines = new List<DocxCoverPageOverlay.CoverTextLine>();
            if (!string.IsNullOrWhiteSpace(institutionLine))
            {
                coverLines.Add(new DocxCoverPageOverlay.CoverTextLine
                {
                    Text = institutionLine.ToUpperInvariant(),
                    FontSizeHalfPt = 52,
                    Bold = true,
                    BeforeTwips = topBlockStartTwips,
                    AfterTwips = 120,
                    ColorHex = coverTextColor
                });
            }
            if (!string.IsNullOrWhiteSpace(qualificationLine))
            {
                coverLines.Add(new DocxCoverPageOverlay.CoverTextLine
                {
                    Text = qualificationLine,
                    FontSizeHalfPt = 50,
                    Bold = true,
                    BeforeTwips = 520,
                    AfterTwips = 120,
                    ColorHex = coverTextColor
                });
            }
            if (!string.IsNullOrWhiteSpace(documentTitle))
            {
                coverLines.Add(new DocxCoverPageOverlay.CoverTextLine
                {
                    Text = documentTitle.ToUpperInvariant(),
                    FontSizeHalfPt = 44,
                    Bold = true,
                    BeforeTwips = lowerBlockGapTwips,
                    AfterTwips = 120,
                    ColorHex = coverTextColor
                });
            }
            if (!string.IsNullOrWhiteSpace(subjectLine))
            {
                coverLines.Add(new DocxCoverPageOverlay.CoverTextLine
                {
                    Text = subjectLine.ToUpperInvariant(),
                    FontSizeHalfPt = 28,
                    Bold = true,
                    BeforeTwips = 240,
                    AfterTwips = 0,
                    ColorHex = coverTextColor
                });
            }

            var appended = DocxCoverPageOverlay.TryAppendCenteredPortraitCoverPage(
                body,
                main,
                coverPath,
                coverLines,
                WorkbookPageWidthTwips,
                2101U);

            if (appended)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(institutionLine))
            {
                body.Append(CenterPara(institutionLine, 26, true));
            }
            if (!string.IsNullOrWhiteSpace(qualificationLine))
            {
                body.Append(CenterPara(qualificationLine, 25, true));
            }
            if (!string.IsNullOrWhiteSpace(documentTitle))
            {
                body.Append(CenterPara(documentTitle, 22, true));
            }
            if (!string.IsNullOrWhiteSpace(subjectLine))
            {
                body.Append(CenterPara(subjectLine, 14, true));
            }
        }

        private static string BuildCoverQualificationLine(Qualification qualification)
        {
            var qualificationNumber = (qualification.QualificationNumber ?? string.Empty).Trim();
            var qualificationDescription = (qualification.QualificationDescription ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(qualificationNumber)) return qualificationDescription;
            if (string.IsNullOrWhiteSpace(qualificationDescription)) return qualificationNumber;
            return $"{qualificationNumber} {qualificationDescription}".Trim();
        }

        private static string? ResolveWorkbookCoverPath(string documentTitle)
        {
            var candidates = new[]
            {
                Path.Combine("Imports", "Coverpages", "clean coverpage.jpg"),
                Path.Combine("ETDP", "Imports", "Coverpages", "clean coverpage.jpg"),
                Path.Combine("Imports", "Coverpages", "Workbook Memorandum Cover Page.png"),
                Path.Combine("ETDP", "Imports", "Coverpages", "Workbook Memorandum Cover Page.png"),
                Path.Combine("Imports", "Coverpages", "Learner Workbook.png"),
                Path.Combine("ETDP", "Imports", "Coverpages", "Learner Workbook.png")
            };

            foreach (var relative in candidates)
            {
                var path = ResolveFromCurrentOrParents(relative, 6);
                if (!string.IsNullOrWhiteSpace(path)) return path;
            }

            return null;
        }

        private static string? ResolveFromCurrentOrParents(string relativePath, int maxDepth)
        {
            var current = Directory.GetCurrentDirectory();
            for (var depth = 0; depth <= maxDepth; depth++)
            {
                var combined = Path.Combine(current, relativePath);
                if (System.IO.File.Exists(combined)) return combined;
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

        private static Paragraph BulletPara(string text, int sizeHalfPt)
        {
            var rPr = new RunProperties
            {
                FontSize = new FontSize() { Val = CompactBodyHalfPt(sizeHalfPt).ToString() },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };
            var pPr = new ParagraphProperties(
                new SpacingBetweenLines() { Line = "280", LineRule = LineSpacingRuleValues.Auto },
                new Indentation() { Left = "720", Hanging = "360" });
            return new Paragraph(pPr, new Run(rPr, new Text(SanitizeXmlText($"- {text ?? string.Empty}"))));
        }

        private static TableRow MemoRow(string c1, string c2, string c3, string c4, string c5, bool header = false)
        {
            var rp = new RunProperties();
            if (header) rp.Bold = new Bold();
            rp.FontSize = new FontSize() { Val = CompactTableCellHalfPt };
            rp.RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont };
            var p1 = new Paragraph(new Run((RunProperties)rp.CloneNode(true), new Text(SanitizeXmlText(c1 ?? string.Empty))));
            var p2 = new Paragraph(new Run((RunProperties)rp.CloneNode(true), new Text(SanitizeXmlText(c2 ?? string.Empty))));
            var p3 = new Paragraph(new Run((RunProperties)rp.CloneNode(true), new Text(SanitizeXmlText(c3 ?? string.Empty))));
            var p4 = new Paragraph(new Run((RunProperties)rp.CloneNode(true), new Text(SanitizeXmlText(c4 ?? string.Empty))));
            var p5 = new Paragraph(new Run((RunProperties)rp.CloneNode(true), new Text(SanitizeXmlText(c5 ?? string.Empty))));
            return new TableRow(new TableCell(p1), new TableCell(p2), new TableCell(p3), new TableCell(p4), new TableCell(p5));
        }

        private static void AppendWorkbookActivityBlock(Body body, WorkbookDiscussionActivity activity)
        {
            body.Append(BuildWorkbookActivityHeadingTable(activity));
            body.Append(Blank());
            body.Append(BuildWorkbookTaskTable(activity));
            body.Append(Blank());
            body.Append(BuildWorkbookAnswerTable());
            body.Append(Blank());
            body.Append(BuildWorkbookMarksTable(activity.MarksPossible));
        }

        private static void AppendWorkbookMemorandumActivityBlock(Body body, WorkbookDiscussionActivity activity)
        {
            body.Append(BuildWorkbookMemorandumActivityTable(activity));
        }

        private static Table BuildWorkbookActivityHeadingTable(WorkbookDiscussionActivity activity)
        {
            var table = new Table();
            table.Append(DefaultTableProperties());
            table.Append(new TableGrid(
                new GridColumn() { Width = "4300" },
                new GridColumn() { Width = "6100" }));
            table.Append(new TableRow(
                BuildWorkbookCell($"Subject Code: {activity.SubjectCode}", width: "4300", bold: true),
                BuildWorkbookCell(activity.SubjectDescription, width: "6100", bold: true)));
            table.Append(new TableRow(
                BuildWorkbookCell($"Subject Topic: {activity.TopicCode}", width: "4300", bold: true),
                BuildWorkbookCell(activity.TopicDescription, width: "6100", bold: true)));
            table.Append(new TableRow(
                BuildWorkbookCell($"Workbook Activity {activity.ActivityNumber}", width: "4300"),
                BuildWorkbookCell($"Marks Possible: {activity.MarksPossible}", width: "6100", bold: true)));
            return table;
        }

        private static Table BuildWorkbookTaskTable(WorkbookDiscussionActivity activity)
        {
            var table = new Table();
            table.Append(DefaultTableProperties());
            table.Append(new TableGrid(
                new GridColumn() { Width = "1800" },
                new GridColumn() { Width = "2800" },
                new GridColumn() { Width = "1400" },
                new GridColumn() { Width = "2800" },
                new GridColumn() { Width = "1600" }));
            table.Append(new TableRow(
                BuildWorkbookCell("TASK", width: "1800", bold: true, center: true),
                BuildWorkbookCell("Time to Prepare", width: "2800", bold: true, center: true),
                BuildWorkbookCell(activity.PrepareTime, width: "1400", bold: true, center: true),
                BuildWorkbookCell("Present time", width: "2800", bold: true, center: true),
                BuildWorkbookCell(activity.PresentationTime, width: "1600", bold: true, center: true)));
            table.Append(BuildMergedWorkbookRow(activity.TaskPrompt, 5, bold: true));
            table.Append(BuildMergedWorkbookRow(activity.Qualifier, 5));
            return table;
        }

        private static Table BuildWorkbookAnswerTable()
        {
            var table = new Table();
            table.Append(DefaultTableProperties());
            table.Append(new TableGrid(
                new GridColumn() { Width = "1100" },
                new GridColumn() { Width = "6500" },
                new GridColumn() { Width = "1400" },
                new GridColumn() { Width = "1400" }));
            table.Append(new TableRow(
                BuildWorkbookCell("Ser no", width: "1100", bold: true, center: true),
                BuildWorkbookCell("Group Answer", width: "6500", bold: true),
                BuildWorkbookCell("Assessor", width: "1400", bold: true, center: true),
                BuildWorkbookCell("Moderator", width: "1400", bold: true, center: true)));
            for (var i = 1; i <= 8; i++)
            {
                table.Append(new TableRow(
                    BuildWorkbookCell(i.ToString(), width: "1100", center: true),
                    BuildWorkbookCell(string.Empty, width: "6500"),
                    BuildWorkbookCell(string.Empty, width: "1400"),
                    BuildWorkbookCell(string.Empty, width: "1400")));
            }
            return table;
        }

        private static Table BuildWorkbookMarksTable(int marksPossible)
        {
            var table = new Table();
            table.Append(DefaultTableProperties());
            table.Append(new TableGrid(
                new GridColumn() { Width = "8200" },
                new GridColumn() { Width = "2200" }));
            table.Append(new TableRow(
                BuildWorkbookCell("MARK POSSIBLE", width: "8200", bold: true),
                BuildWorkbookCell(marksPossible.ToString(), width: "2200", bold: true, center: true)));
            table.Append(new TableRow(
                BuildWorkbookCell("MARK ACHIEVED", width: "8200", bold: true),
                BuildWorkbookCell(string.Empty, width: "2200")));
            return table;
        }

        private static Table BuildWorkbookMemorandumActivityTable(WorkbookDiscussionActivity activity)
        {
            var table = new Table();
            table.Append(DefaultTableProperties());
            table.Append(new TableGrid(
                new GridColumn() { Width = "1800" },
                new GridColumn() { Width = "850" },
                new GridColumn() { Width = "1550" },
                new GridColumn() { Width = "850" },
                new GridColumn() { Width = "1250" },
                new GridColumn() { Width = "4100" }));

            table.Append(new TableRow(
                BuildWorkbookCell("Workbook Activity:", width: "1800", bold: true),
                BuildWorkbookCell(activity.ActivityNumber.ToString(), width: "850", center: true),
                BuildWorkbookCell("Mark", width: "1550", bold: true, center: true),
                BuildWorkbookCell(activity.MarksPossible.ToString(), width: "850", bold: true, center: true),
                BuildWorkbookCell("LPN", width: "1250", bold: true, center: true),
                BuildWorkbookCell(string.IsNullOrWhiteSpace(activity.LessonPlanLabel) ? "LPN" : activity.LessonPlanLabel, width: "4100", bold: true)));

            table.Append(new TableRow(
                BuildWorkbookCell("Subject Code:", width: "1800", bold: true),
                BuildWorkbookCell(activity.SubjectCode, width: "850", center: true),
                BuildWorkbookCell("Subject Description", width: "1550", bold: true),
                BuildWorkbookCell(activity.SubjectDescription, width: "6200", gridSpan: 3)));

            table.Append(new TableRow(
                BuildWorkbookCell("Topic Code:", width: "1800", bold: true),
                BuildWorkbookCell(activity.TopicCode, width: "850", center: true),
                BuildWorkbookCell("Topic Description", width: "1550", bold: true),
                BuildWorkbookCell(activity.TopicDescription, width: "6200", gridSpan: 3)));

            table.Append(new TableRow(
                BuildWorkbookCell("Task:", width: "1800", bold: true),
                BuildWorkbookCell(activity.TaskPrompt, width: "8600", bold: true, gridSpan: 5)));

            table.Append(new TableRow(
                BuildWorkbookCell("Correct Answers", width: "1800", bold: true),
                BuildWorkbookCell("Any 4 Correct Answers from the Content Below", width: "8600", bold: true, gridSpan: 5)));

            table.Append(new TableRow(
                BuildWorkbookMultiParagraphCell(
                    BuildWorkbookMemorandumParagraphs(activity),
                    width: WorkbookFullTableWidth,
                    gridSpan: 6)));

            return table;
        }

        private static TableRow BuildWorkbookLabelValueRow(string label, string value)
        {
            return new TableRow(
                BuildWorkbookCell(label, width: "5200", bold: true),
                BuildWorkbookCell(value, width: "5200"));
        }

        private static TableRow BuildMergedWorkbookRow(string text, int span, bool bold = false, bool center = false, string fontHalfPoints = CompactTableCellHalfPt)
        {
            var cellProps = new TableCellProperties(
                new GridSpan() { Val = span },
                new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = WorkbookFullTableWidth },
                BuildVisibleTableCellBorders(),
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

            var runProps = new RunProperties
            {
                FontSize = new FontSize() { Val = fontHalfPoints },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };
            if (bold) runProps.Bold = new Bold();

            var paraProps = new ParagraphProperties(
                new Justification() { Val = center ? JustificationValues.Center : JustificationValues.Left },
                new SpacingBetweenLines() { Line = "260", LineRule = LineSpacingRuleValues.Auto });

            var paragraph = new Paragraph(paraProps, new Run(runProps, new Text(SanitizeXmlText(text ?? string.Empty))
            {
                Space = SpaceProcessingModeValues.Preserve
            }));

            return new TableRow(new TableCell(cellProps, paragraph));
        }

        private static TableCell BuildWorkbookCell(string text, string width, bool bold = false, bool center = false, int gridSpan = 1, string fontHalfPoints = CompactTableCellHalfPt)
        {
            var props = new TableCellProperties(
                new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = width },
                BuildVisibleTableCellBorders(),
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });
            if (gridSpan > 1)
            {
                props.Append(new GridSpan() { Val = gridSpan });
            }

            return new TableCell(
                props,
                TableCellParagraph(text, bold: bold, center: center, fontHalfPoints: fontHalfPoints));
        }

        private static TableCell BuildWorkbookMultiParagraphCell(IEnumerable<string> paragraphs, string width, int gridSpan = 1)
        {
            var props = new TableCellProperties(
                new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = width },
                BuildVisibleTableCellBorders(),
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top });
            if (gridSpan > 1)
            {
                props.Append(new GridSpan() { Val = gridSpan });
            }

            var content = (paragraphs ?? Array.Empty<string>())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => TableCellParagraph(line.Trim(), fontHalfPoints: "22"))
                .ToList();

            if (content.Count == 0)
            {
                content.Add(TableCellParagraph(string.Empty));
            }

            return new TableCell(new OpenXmlElement[] { props }.Concat(content));
        }

        private static IReadOnlyList<string> BuildWorkbookMemorandumParagraphs(WorkbookDiscussionActivity activity)
        {
            var cleaned = CleanWorkbookMemorandumContent(activity.LessonPlanContent);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = CleanWorkbookMemorandumContent(activity.LessonPlanDescription);
            }
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = CleanWorkbookMemorandumContent(activity.AssessmentCriteriaDescription);
            }

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return new[] { "No lesson plan content was available for this workbook activity." };
            }

            return cleaned
                .Split('\n', StringSplitOptions.TrimEntries)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }

        private static Table BuildActivityTable(
            int activityNumber,
            AssessmentDrivenQuestionGenerator.LessonEvidenceItem item,
            AssessmentDrivenQuestionGenerator.GeneratedQuestion trueFalseQuestion)
        {
            var table = new Table();
            table.Append(DefaultTableProperties());
            table.Append(new TableGrid(
                new GridColumn() { Width = "2600" },
                new GridColumn() { Width = "9500" }));

            table.Append(ActivityRow("Activity", $"Activity {activityNumber}", emphasizeValue: true));
            table.Append(ActivityRow("Topic", $"{item.TopicCode} — {item.TopicDescription}"));
            table.Append(ActivityRow("Lesson Plan", $"{item.LessonPlanLabel} | {item.LessonPlanDescription}"));
            table.Append(ActivityRow("Assessment Criterion", item.AssessmentCriteriaDescription));

            table.Append(ActivityRow("Task 1", "Practical response", emphasizeValue: true));
            table.Append(ActivityRow("Instruction", "Using the lesson material, explain and demonstrate how you would satisfy the criterion above in this lesson context."));
            table.Append(ActivityRow("Learner Response 1", "________________________________________________________________________________"));
            table.Append(ActivityRow("Learner Response 2", "________________________________________________________________________________"));
            table.Append(ActivityRow("Learner Response 3", "________________________________________________________________________________"));

            table.Append(ActivityRow("Task 2", "True / False", emphasizeValue: true));
            table.Append(ActivityRow("Stem", trueFalseQuestion.Prompt));
            table.Append(ActivityNestedTableRow(
                "Options",
                BuildOptionTable(trueFalseQuestion.Options, includeTrueFalseColumns: false)));
            table.Append(ActivityRow("Instruction", "Select one option only: True or False."));

            table.Append(ActivityRow("Evidence Checklist", "Response addresses the topic and lesson objective."));
            table.Append(ActivityRow("Evidence Checklist", "Response aligns with the assessment criterion."));
            table.Append(ActivityRow("Evidence Checklist", "Practical reasoning and examples are included."));
            table.Append(ActivityRow("Evidence Checklist", "Technical terms are used accurately."));

            return table;
        }

        private static Table BuildOptionTable(List<string>? options, bool includeTrueFalseColumns)
        {
            var table = new Table();
            table.Append(DefaultTableProperties());
            table.Append(includeTrueFalseColumns
                ? new TableGrid(
                    new GridColumn() { Width = "1100" },
                    new GridColumn() { Width = "6400" },
                    new GridColumn() { Width = "1000" },
                    new GridColumn() { Width = "1000" })
                : new TableGrid(
                    new GridColumn() { Width = "1100" },
                    new GridColumn() { Width = "8400" }));

            if (includeTrueFalseColumns)
            {
                table.Append(new TableRow(
                    OptionCell("1100", "Option", bold: true, center: true),
                    OptionCell("6400", "Statement", bold: true),
                    OptionCell("1000", "True", bold: true, center: true),
                    OptionCell("1000", "False", bold: true, center: true)));
            }
            else
            {
                table.Append(new TableRow(
                    OptionCell("1100", "Option", bold: true, center: true),
                    OptionCell("8400", "Statement", bold: true)));
            }

            var normalized = options ?? new List<string>();
            if (normalized.Count == 0)
            {
                if (includeTrueFalseColumns)
                {
                    table.Append(new TableRow(
                        OptionCell("1100", "-", center: true),
                        OptionCell("6400", "No options available."),
                        OptionCell("1000", "", center: true),
                        OptionCell("1000", "", center: true)));
                }
                else
                {
                    table.Append(new TableRow(
                        OptionCell("1100", "-", center: true),
                        OptionCell("8400", "No options available.")));
                }
                return table;
            }

            for (var i = 0; i < normalized.Count; i++)
            {
                var label = ((char)('A' + i)).ToString();
                if (includeTrueFalseColumns)
                {
                    table.Append(new TableRow(
                        OptionCell("1100", label, center: true),
                        OptionCell("6400", normalized[i]),
                        OptionCell("1000", "[ ]", center: true),
                        OptionCell("1000", "[ ]", center: true)));
                }
                else
                {
                    table.Append(new TableRow(
                        OptionCell("1100", label, center: true),
                        OptionCell("8400", normalized[i])));
                }
            }

            return table;
        }

        private static TableRow ActivityNestedTableRow(string label, Table nestedTable)
        {
            var labelCell = new TableCell(
                new TableCellProperties(
                    new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = "2600" },
                    BuildVisibleTableCellBorders(),
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
                TableCellParagraph(label, bold: true));

            var valueCell = new TableCell(
                new TableCellProperties(
                    new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = "9500" },
                    BuildVisibleTableCellBorders(),
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
                nestedTable);

            return new TableRow(labelCell, valueCell);
        }

        private static TableRow ActivityRow(string label, string value, bool emphasizeValue = false)
        {
            var labelCell = new TableCell(
                new TableCellProperties(
                    new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = "2600" },
                    BuildVisibleTableCellBorders(),
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
                TableCellParagraph(label, bold: true));

            var valueCell = new TableCell(
                new TableCellProperties(
                    new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = "9500" },
                    BuildVisibleTableCellBorders(),
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
                TableCellParagraph(value, bold: emphasizeValue));

            return new TableRow(labelCell, valueCell);
        }

        private static TableCell OptionCell(string width, string text, bool bold = false, bool center = false)
        {
            return new TableCell(
                new TableCellProperties(
                    new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = width },
                    BuildVisibleTableCellBorders(),
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
                TableCellParagraph(text, bold: bold, center: center));
        }

        private static Paragraph TableCellParagraph(string text, bool bold = false, bool center = false, string fontHalfPoints = CompactTableCellHalfPt)
        {
            var runProps = new RunProperties
            {
                FontSize = new FontSize() { Val = fontHalfPoints },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };
            if (bold) runProps.Bold = new Bold();

            var paraProps = new ParagraphProperties(
                new Justification() { Val = center ? JustificationValues.Center : JustificationValues.Left },
                new SpacingBetweenLines() { Line = "240", LineRule = LineSpacingRuleValues.Auto });

            return new Paragraph(paraProps, new Run(runProps, new Text(SanitizeXmlText(text ?? string.Empty))
            {
                Space = SpaceProcessingModeValues.Preserve
            }));
        }

        private static int CompactHeadingPt(int requestedSizePt)
        {
            return Math.Clamp(Math.Max(1, requestedSizePt), 10, 40);
        }

        private static int CompactBodyHalfPt(int requestedSizeHalfPt)
        {
            return Math.Clamp(Math.Max(1, requestedSizeHalfPt), 18, 32);
        }

        private static string SanitizeXmlText(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var input = value!;
            var sb = new StringBuilder(input.Length);
            for (var i = 0; i < input.Length; i++)
            {
                var ch = input[i];
                if (XmlConvert.IsXmlChar(ch))
                {
                    sb.Append(ch);
                    continue;
                }

                if (char.IsHighSurrogate(ch) &&
                    i + 1 < input.Length &&
                    char.IsLowSurrogate(input[i + 1]) &&
                    XmlConvert.IsXmlSurrogatePair(ch, input[i + 1]))
                {
                    sb.Append(ch);
                    sb.Append(input[i + 1]);
                    i++;
                }
            }
            return sb.ToString();
        }

        private static string MakeSafeFilePart(string? value, string fallback)
        {
            var v = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v)) return fallback;
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                v = v.Replace(c, '_');
            }
            v = v.Replace(" ", "");
            return string.IsNullOrWhiteSpace(v) ? fallback : v;
        }
    }
}
