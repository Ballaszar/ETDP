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
    public class KnowledgeQuestionnaireController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly KnowledgeQuestionnaireV1Service _knowledgeQuestionnaireV1Service;
        private readonly SemanticKernelQuestionService _semanticKernelQuestionService;
        private const int FixedTotalMarks = 100;
        private const int TrueFalseOptionCount = 4;
        private const int DefaultMcqDistractors = 3;
        private const string DefaultSmiBaseUrl = "http://127.0.0.1:8099";
        private const int DefaultSmiTimeoutSeconds = 30;
        private const int SmiQuestionTimeoutSeconds = 6;
        private const int SmiHealthProbeTimeoutSeconds = 2;
        private const int SmiRepairTimeoutSeconds = 8;
        private const int DefaultSmiTopK = 0;
        private const string ExportFont = "Times New Roman";
        private const string CompactTableCellHalfPt = "20"; // 10pt
        private const uint PortraitA4PageWidthTwips = 11906U;
        private const uint PortraitA4PageHeightTwips = 16838U;
        private const uint PortraitCoverUsableWidthTwips = 9866U;
        private const string QuestionnaireUsableWidthTwips = "9866";
        private static readonly HttpClient _http = new HttpClient();

        public KnowledgeQuestionnaireController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            KnowledgeQuestionnaireV1Service knowledgeQuestionnaireV1Service,
            SemanticKernelQuestionService semanticKernelQuestionService)
        {
            _context = context;
            _environment = environment;
            _knowledgeQuestionnaireV1Service = knowledgeQuestionnaireV1Service;
            _semanticKernelQuestionService = semanticKernelQuestionService;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var items = _context.KnowledgeQuestionnaires.Select(kq => new ETD.Api.DTOs.KnowledgeQuestionnaireDto
                {
                    Id = kq.Id,
                    SubjectId = kq.SubjectId,
                    Title = kq.Title,
                    Version = kq.Version,
                    Questions = kq.Questions
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
            var kq = _context.KnowledgeQuestionnaires.Find(id);
            if (kq == null) return NotFound();
            var dto = new ETD.Api.DTOs.KnowledgeQuestionnaireDto
            {
                Id = kq.Id,
                SubjectId = kq.SubjectId,
                Title = kq.Title,
                Version = kq.Version,
                Questions = kq.Questions
            };
            return Ok(dto);
        }

        [HttpPost]
        public IActionResult Create(ETD.Api.DTOs.CreateKnowledgeQuestionnaireDto dto)
        {
            var model = new KnowledgeQuestionnaire
            {
                SubjectId = dto.SubjectId,
                Title = dto.Title,
                Version = dto.Version,
                Questions = dto.Questions
            };
            _context.KnowledgeQuestionnaires.Add(model);
            _context.SaveChanges();
            return CreatedAtAction(nameof(Get), new { id = model.Id }, model);
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, ETD.Api.DTOs.UpdateKnowledgeQuestionnaireDto dto)
        {
            var item = _context.KnowledgeQuestionnaires.Find(id);
            if (item == null) return NotFound();
            item.Title = dto.Title;
            item.Version = dto.Version;
            item.Questions = dto.Questions;
            _context.SaveChanges();
            return Ok(item);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.KnowledgeQuestionnaires.Find(id);
            if (item == null) return NotFound();

            _context.KnowledgeQuestionnaires.Remove(item);
            _context.SaveChanges();
            return NoContent();
        }

        [HttpGet("v1-draft")]
        public IActionResult GetV1Draft([FromQuery] int qualificationId, [FromQuery] int topicId)
        {
            try
            {
                var draft = _knowledgeQuestionnaireV1Service.BuildDraft(qualificationId, topicId);
                return Ok(draft);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("v1-phase-draft")]
        public IActionResult GetV1PhaseDraft([FromQuery] int qualificationId, [FromQuery] int phaseId)
        {
            try
            {
                var draft = _knowledgeQuestionnaireV1Service.BuildPhaseDraft(qualificationId, phaseId);
                return Ok(draft);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        public sealed class SmiDraftRequest
        {
            public int QualificationId { get; set; }
            public int SubjectId { get; set; }
            public int? TopicId { get; set; }
            public int TrueFalseCount { get; set; } = 12;
            public int MultipleChoiceCount { get; set; }
            public int McqDistractors { get; set; } = DefaultMcqDistractors;
            public string? LessonPlanContent { get; set; }
        }

        public sealed class PhaseSmiDraftRequest
        {
            public int QualificationId { get; set; }
            public int PhaseId { get; set; }
            public List<int> SubjectIds { get; set; } = new();
            public List<int> AssessmentCriteriaIds { get; set; } = new();
            public int TrueFalseCount { get; set; }
            public int MultipleChoiceCount { get; set; }
            public int? MinimumQuestionsPerCriterion { get; set; }
            public int? MinimumTotalQuestions { get; set; }
            public int McqDistractors { get; set; } = DefaultMcqDistractors;
        }

        public sealed class PhaseQuestionnaireDocxQuestionRow
        {
            public int Number { get; set; }
            public string Type { get; set; } = string.Empty;
            public string Prompt { get; set; } = string.Empty;
            public List<string> Options { get; set; } = new();
            public string CorrectAnswer { get; set; } = string.Empty;
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string AssessmentCriteriaNumber { get; set; } = string.Empty;
            public string LessonPlanLabel { get; set; } = string.Empty;
            public string AssessmentCriteriaDescription { get; set; } = string.Empty;
            public string Rationale { get; set; } = string.Empty;
            public int Marks { get; set; }
        }

        public sealed class PhaseQuestionnaireDocxExportRequest
        {
            public int QualificationId { get; set; }
            public int PhaseId { get; set; }
            public string PhaseName { get; set; } = string.Empty;
            public string PhaseDescription { get; set; } = string.Empty;
            public string MainCategoryCode { get; set; } = string.Empty;
            public string MainCategoryLabel { get; set; } = string.Empty;
            public string QuestionnaireTitle { get; set; } = string.Empty;
            public string PassMark { get; set; } = string.Empty;
            public string CreatedBy { get; set; } = string.Empty;
            public string ReviewedBy { get; set; } = string.Empty;
            public int TrueFalseCount { get; set; }
            public int MultipleChoiceCount { get; set; }
            public int TotalQuestions { get; set; }
            public int TotalMarks { get; set; }
            public List<PhaseQuestionnaireDocxQuestionRow> Questions { get; set; } = new();
        }

        private sealed class SmiDraftQuestionDto
        {
            public int Number { get; set; }
            public string Type { get; set; } = string.Empty;
            public string Prompt { get; set; } = string.Empty;
            public List<string> Options { get; set; } = new();
            public string CorrectAnswer { get; set; } = string.Empty;
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string AssessmentCriteriaNumber { get; set; } = string.Empty;
            public string LessonPlanLabel { get; set; } = string.Empty;
            public string AssessmentCriteriaDescription { get; set; } = string.Empty;
            public string Rationale { get; set; } = string.Empty;
            public int Marks { get; set; }
        }

        private sealed class SmiDraftResponse
        {
            public bool Success { get; set; }
            public string QuestionSource { get; set; } = string.Empty;
            public int TotalQuestions { get; set; }
            public int TrueFalseQuestions { get; set; }
            public int MultipleChoiceQuestions { get; set; }
            public List<SmiDraftQuestionDto> Questions { get; set; } = new();
            public List<string> LearningResourceSuggestions { get; set; } = new();
        }

        [HttpPost("smi-draft")]
        public async Task<IActionResult> GenerateSmiDraft([FromBody] SmiDraftRequest? request)
        {
            if (!IsSmiIntegrationEnabled())
            {
                return BadRequest("SMI integration is disabled. Set SMI_ENABLED=true to enable SMI-first generation.");
            }

            var payload = request ?? new SmiDraftRequest();
            var qualificationId = payload.QualificationId > 0 ? payload.QualificationId : (int?)null;
            var subjectId = payload.SubjectId > 0 ? payload.SubjectId : (int?)null;
            var subject = ResolveSubject(subjectId, qualificationId);
            if (subject == null) return BadRequest("No subject available for SMI draft generation.");
            var qualification = ResolveQualification(subject, qualificationId);
            if (qualification == null) return BadRequest("No qualification available for SMI draft generation.");

            var trueFalseCount = Math.Clamp(payload.TrueFalseCount, 0, 80);
            var multipleChoiceCount = Math.Clamp(payload.MultipleChoiceCount, 0, 80);
            if ((trueFalseCount + multipleChoiceCount) == 0)
            {
                trueFalseCount = 12;
                multipleChoiceCount = 0;
            }

            var distractorCount = DefaultMcqDistractors;
            var items = AssessmentDrivenQuestionGenerator.BuildOrderedLessonEvidence(_context, subject.Id);
            if (payload.TopicId.HasValue && payload.TopicId.Value > 0)
            {
                items = items.Where(i => i.TopicId == payload.TopicId.Value).ToList();
            }

            var lessonPlanOverride = (payload.LessonPlanContent ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(lessonPlanOverride) && items.Count > 0)
            {
                items = ApplyLessonContentOverride(items, lessonPlanOverride);
            }

            if (items.Count == 0)
            {
                return BadRequest("No lesson evidence was resolved for the selected scope.");
            }

            var smiResult = await TryBuildQuestionsWithSmiAsync(
                items,
                qualification,
                subject,
                trueFalseCount,
                multipleChoiceCount,
                distractorCount,
                HttpContext.RequestAborted);

            if (smiResult.Questions.Count == 0)
            {
                return BadRequest("SMI did not return parseable questionnaire JSON for the selected lesson content.");
            }

            var response = new SmiDraftResponse
            {
                Success = true,
                QuestionSource = smiResult.UsedDeterministicFallback
                    ? "Generated with SMI lesson-content workflow (deterministic fallback used for non-parseable responses)."
                    : "Generated with SMI lesson-content workflow",
                TotalQuestions = smiResult.Questions.Count,
                TrueFalseQuestions = smiResult.Questions.Count(q => string.Equals(q.Type, "TrueFalse", StringComparison.OrdinalIgnoreCase)),
                MultipleChoiceQuestions = smiResult.Questions.Count(q => !string.Equals(q.Type, "TrueFalse", StringComparison.OrdinalIgnoreCase)),
                Questions = smiResult.Questions
                    .Select(q => new SmiDraftQuestionDto
                    {
                        Number = q.Number,
                        Type = q.Type,
                        Prompt = q.Prompt,
                        Options = q.Options ?? new List<string>(),
                        CorrectAnswer = q.CorrectAnswer,
                        SubjectCode = (subject.SubjectCode ?? string.Empty).Trim(),
                        SubjectDescription = (subject.SubjectDescription ?? string.Empty).Trim(),
                        TopicCode = q.TopicCode,
                        TopicDescription = q.TopicDescription,
                        AssessmentCriteriaNumber = string.Empty,
                        LessonPlanLabel = q.LessonPlanLabel,
                        AssessmentCriteriaDescription = q.AssessmentCriteriaDescription,
                        Rationale = q.Rationale,
                        Marks = q.Marks
                    })
                    .ToList(),
                LearningResourceSuggestions = smiResult.ResourceSuggestions
            };

            return Ok(response);
        }

        [HttpPost("v1-phase-smi-draft")]
        public async Task<IActionResult> GeneratePhaseSmiDraft([FromBody] PhaseSmiDraftRequest? request)
        {
            if (!IsSmiIntegrationEnabled())
            {
                return BadRequest("SMI integration is disabled. Set SMI_ENABLED=true to enable SMI-first generation.");
            }

            var payload = request ?? new PhaseSmiDraftRequest();
            if (payload.QualificationId <= 0) return BadRequest("qualificationId is required.");
            if (payload.PhaseId <= 0) return BadRequest("phaseId is required.");

            KnowledgeQuestionnaireV1Service.KnowledgeQuestionnaireV1Draft draft;
            try
            {
                draft = _knowledgeQuestionnaireV1Service.BuildPhaseDraft(payload.QualificationId, payload.PhaseId);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }

            var requestedCriteriaIds = (payload.AssessmentCriteriaIds ?? new List<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToHashSet();
            if (requestedCriteriaIds.Count > 0)
            {
                draft.Criteria = (draft.Criteria ?? new List<KnowledgeQuestionnaireV1Service.KnowledgeQuestionnaireV1CriterionIntentDraft>())
                    .Where(row => requestedCriteriaIds.Contains(row.AssessmentCriteriaId))
                    .ToList();

                var allowedTopicIds = draft.Criteria
                    .Select(row => row.TopicId)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToHashSet();
                if (allowedTopicIds.Count > 0)
                {
                    draft.Topics = (draft.Topics ?? new List<KnowledgeQuestionnaireV1Service.KnowledgeQuestionnaireV1TopicScope>())
                        .Where(row => allowedTopicIds.Contains(row.TopicId))
                        .ToList();
                }
            }

            var requestedSubjectIds = (payload.SubjectIds ?? new List<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToHashSet();

            var criteriaSubjectIds = (draft.Criteria ?? new List<KnowledgeQuestionnaireV1Service.KnowledgeQuestionnaireV1CriterionIntentDraft>())
                .Select(row => row.SubjectId)
                .Where(id => id > 0)
                .Distinct()
                .ToHashSet();

            if (requestedSubjectIds.Count == 0 && criteriaSubjectIds.Count > 0)
            {
                requestedSubjectIds = criteriaSubjectIds;
            }
            else if (requestedSubjectIds.Count > 0 && criteriaSubjectIds.Count > 0)
            {
                requestedSubjectIds.IntersectWith(criteriaSubjectIds);
            }

            if (requestedSubjectIds.Count > 0)
            {
                draft.Subjects = (draft.Subjects ?? new List<KnowledgeQuestionnaireV1Service.KnowledgeQuestionnaireV1SubjectScope>())
                    .Where(row => requestedSubjectIds.Contains(row.SubjectId))
                    .ToList();
                draft.Topics = (draft.Topics ?? new List<KnowledgeQuestionnaireV1Service.KnowledgeQuestionnaireV1TopicScope>())
                    .Where(row => requestedSubjectIds.Contains(row.SubjectId))
                    .ToList();
                draft.Criteria = (draft.Criteria ?? new List<KnowledgeQuestionnaireV1Service.KnowledgeQuestionnaireV1CriterionIntentDraft>())
                    .Where(row => requestedSubjectIds.Contains(row.SubjectId))
                    .ToList();
            }

            var draftSubjectIds = (draft.Subjects ?? new List<KnowledgeQuestionnaireV1Service.KnowledgeQuestionnaireV1SubjectScope>())
                .Select(s => s.SubjectId)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            var subjects = _context.Subjects
                .AsNoTracking()
                .Include(s => s.Qualification)
                .Where(s => s.QualificationId == draft.QualificationId && draftSubjectIds.Contains(s.Id))
                .OrderBy(s => s.SubjectCode)
                .ThenBy(s => s.Id)
                .ToList();

            if (subjects.Count == 0)
            {
                return BadRequest("No subjects were found for the selected curriculum phase.");
            }

            var qualification = subjects
                .Select(s => s.Qualification)
                .FirstOrDefault(q => q != null);
            if (qualification == null)
            {
                return BadRequest("No qualification available for SMI draft generation.");
            }

            var minimumQuestionsPerCriterion = payload.MinimumQuestionsPerCriterion.HasValue
                ? Math.Max(0, payload.MinimumQuestionsPerCriterion.Value)
                : Math.Max(0, draft.Metadata.MinimumQuestionsPerCriterion);
            var minimumTotalQuestions = payload.MinimumTotalQuestions.HasValue
                ? Math.Max(0, payload.MinimumTotalQuestions.Value)
                : Math.Max(0, draft.Metadata.MinimumTotalQuestions);

            var effectiveCounts = NormalizePhaseQuestionCounts(
                payload.TrueFalseCount,
                payload.MultipleChoiceCount,
                minimumTotalQuestions);

            var phaseSeeds = BuildPhaseCriterionSeeds(subjects, draft);
            if (phaseSeeds.Count == 0)
            {
                return BadRequest("No KQ-routed lesson evidence was resolved for the selected curriculum phase.");
            }

            var smiResult = await TryBuildPhaseQuestionsWithSmiAsync(
                phaseSeeds,
                qualification,
                effectiveCounts.TrueFalseCount,
                effectiveCounts.MultipleChoiceCount,
                minimumQuestionsPerCriterion,
                DefaultMcqDistractors,
                HttpContext.RequestAborted);

            if (smiResult.Questions.Count == 0)
            {
                return BadRequest("SMI did not return parseable questionnaire JSON for the selected knowledge-learning phase.");
            }

            var seedLookup = phaseSeeds.ToDictionary(
                row => row.Item.BundleKey,
                row => row,
                StringComparer.OrdinalIgnoreCase);

            var response = new SmiDraftResponse
            {
                Success = true,
                QuestionSource = smiResult.UsedDeterministicFallback
                    ? "Generated with the consolidated phase-wide SMI workflow (deterministic fallback used where SMI output was not parseable)."
                    : "Generated with the consolidated phase-wide SMI workflow",
                TotalQuestions = smiResult.Questions.Count,
                TrueFalseQuestions = smiResult.Questions.Count(q => string.Equals(q.Type, "TrueFalse", StringComparison.OrdinalIgnoreCase)),
                MultipleChoiceQuestions = smiResult.Questions.Count(q => !string.Equals(q.Type, "TrueFalse", StringComparison.OrdinalIgnoreCase)),
                Questions = smiResult.Questions
                    .Select(q =>
                    {
                        seedLookup.TryGetValue(q.BundleKey ?? string.Empty, out var seed);
                        return new SmiDraftQuestionDto
                        {
                            Number = q.Number,
                            Type = q.Type,
                            Prompt = q.Prompt,
                            Options = q.Options ?? new List<string>(),
                            CorrectAnswer = q.CorrectAnswer,
                            SubjectCode = seed?.SubjectCode ?? string.Empty,
                            SubjectDescription = seed?.SubjectDescription ?? string.Empty,
                            TopicCode = q.TopicCode,
                            TopicDescription = q.TopicDescription,
                            AssessmentCriteriaNumber = seed?.AssessmentCriteriaNumber ?? string.Empty,
                            LessonPlanLabel = q.LessonPlanLabel,
                            AssessmentCriteriaDescription = q.AssessmentCriteriaDescription,
                            Rationale = q.Rationale,
                            Marks = q.Marks
                        };
                    })
                    .ToList(),
                LearningResourceSuggestions = smiResult.ResourceSuggestions
            };

            return Ok(response);
        }

        [HttpPost("v1-phase-export-docx")]
        public IActionResult ExportPhaseQuestionnaireDocx([FromBody] PhaseQuestionnaireDocxExportRequest? request)
        {
            var payload = request ?? new PhaseQuestionnaireDocxExportRequest();
            if (payload.QualificationId <= 0) return BadRequest("qualificationId is required.");

            var qualification = _context.Qualifications
                .AsNoTracking()
                .FirstOrDefault(q => q.Id == payload.QualificationId);
            if (qualification == null)
            {
                return BadRequest("No qualification available for Knowledge Questionnaire DOCX export.");
            }

            var questions = (payload.Questions ?? new List<PhaseQuestionnaireDocxQuestionRow>())
                .Select(NormalizePhaseQuestionnaireDocxQuestionRow)
                .Where(row => row.Number > 0 && !string.IsNullOrWhiteSpace(row.Prompt))
                .OrderBy(row => row.Number)
                .ThenBy(row => row.SubjectCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.TopicCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (questions.Count == 0)
            {
                return BadRequest("No generated questionnaire rows were supplied for DOCX export.");
            }

            var generated = BuildPhaseQuestionnaireDocument(
                qualification,
                payload,
                questions);

            if (!generated.Success)
            {
                return BadRequest(generated.ErrorMessage);
            }

            return File(
                generated.FileBytes,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                generated.FileName);
        }

        [HttpGet("download")]
        public async Task<IActionResult> Download([FromQuery] int? qualificationId = null, [FromQuery] int? subjectId = null, [FromQuery] int mcqDistractors = DefaultMcqDistractors)
        {
            var subject = ResolveSubject(subjectId, qualificationId);
            if (subject == null) return BadRequest("No subject available for questionnaire export.");
            var qualification = ResolveQualification(subject, qualificationId);
            if (qualification == null) return BadRequest("No qualification available for questionnaire export.");

            var distractorCount = DefaultMcqDistractors;
            var generated = await BuildQuestionnaireDocumentAsync(
                subject,
                qualification,
                distractorCount,
                HttpContext.RequestAborted);
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
            [FromQuery] int mcqDistractors = DefaultMcqDistractors)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required for range export.");

            var subjects = ResolveSubjectRange(qualificationId, subjectFromId, subjectToId);
            if (subjects.Count == 0) return BadRequest("No subjects were resolved for the selected range.");

            var distractorCount = DefaultMcqDistractors;
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

                    var generated = await BuildQuestionnaireDocumentAsync(
                        subject,
                        qualification,
                        distractorCount,
                        HttpContext.RequestAborted);

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
            var fileName = $"KnowledgeQuestionnaire_Range_Q{qualificationId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            return File(zipStream.ToArray(), "application/zip", fileName);
        }

        [HttpGet("download-consolidated")]
        public async Task<IActionResult> DownloadConsolidated([FromQuery] int qualificationId)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required for consolidated questionnaire export.");

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId);
            if (qualification == null) return BadRequest("No qualification available for consolidated questionnaire export.");

            var subjects = ResolveSubjectRange(qualificationId, null, null)
                .Where(HasSubjectIdentity)
                .ToList();
            if (subjects.Count == 0) return BadRequest("No subjects were resolved for this qualification.");

            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                var body = main.Document.Body ?? (main.Document.Body = new Body());
                var settingsPart = main.AddNewPart<DocumentSettingsPart>();
                settingsPart.Settings = new Settings(new UpdateFieldsOnOpen() { Val = true });
                settingsPart.Settings.Save();

                AppendCleanCoverPage(
                    body,
                    main,
                    qualification,
                    "KNOWLEDGE QUESTIONNAIRE",
                    $"All Subjects ({subjects.Count})");
                body.Append(PageBreak());

                AppendLegalDisclaimerPage(body, qualification);
                body.Append(PageBreak());

                DocumentRevisionQualityControlPage.Append(body, qualification, new DocumentRevisionQualityControlPageOptions
                {
                    DocumentTitle = "Knowledge Questionnaire",
                    DocumentType = "Summative Assessment",
                    Phase = "Knowledge Learning"
                });
                body.Append(PageBreak());

                AppendTableOfContentsPage(body);
                body.Append(PageBreak());

                var exportedSubjects = 0;
                foreach (var subject in subjects)
                {
                    var built = await BuildQuestionsForSubjectAsync(
                        subject,
                        qualification,
                        FixedTotalMarks,
                        DefaultMcqDistractors,
                        HttpContext.RequestAborted);

                    if (built.Questions.Count == 0)
                    {
                        continue;
                    }

                    exportedSubjects++;
                    body.Append(StyledHeading($"{subject.SubjectCode} — {subject.SubjectDescription}", "Heading1", 26));
                    body.Append(BodyPara($"Total marks: {built.Questions.Sum(x => x.Marks)}", 22));
                    body.Append(BodyPara("Instruction: For each statement, answer only True or False.", 22));
                    body.Append(Blank());

                    foreach (var q in built.Questions)
                    {
                        body.Append(BuildQuestionTable(
                            q,
                            "True / False",
                            "Select one answer only: True or False."));
                        body.Append(Blank());
                    }

                    body.Append(BodyPara($"Question source: {built.SourceLabel}.", 22));
                    body.Append(PageBreak());
                }

                if (exportedSubjects == 0)
                {
                    body.Append(StyledHeading("No Questions Generated", "Heading1", 24));
                    body.Append(BodyPara("No subjects produced assessment-criteria questionnaire items.", 22));
                }

                body.Append(DefaultSectionProperties());
                main.Document.Save();
            }

            ms.Position = 0;
            var fileName = $"KnowledgeQuestionnaire_AllSubjects_Q{qualificationId}_{DateTime.Now:yyyyMMdd_HHmmss}.docx";
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName);
        }

        [HttpGet("download-memorandum")]
        public async Task<IActionResult> DownloadMemorandum([FromQuery] int? qualificationId = null, [FromQuery] int? subjectId = null, [FromQuery] int mcqDistractors = DefaultMcqDistractors)
        {
            var subject = ResolveSubject(subjectId, qualificationId);
            if (subject == null) return BadRequest("No subject available for memorandum export.");
            var qualification = ResolveQualification(subject, qualificationId);
            if (qualification == null) return BadRequest("No qualification available for memorandum export.");

            var distractorCount = DefaultMcqDistractors;
            var generated = await BuildMemorandumDocumentAsync(
                subject,
                qualification,
                distractorCount,
                HttpContext.RequestAborted);
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
            [FromQuery] int mcqDistractors = DefaultMcqDistractors)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required for range export.");

            var subjects = ResolveSubjectRange(qualificationId, subjectFromId, subjectToId);
            if (subjects.Count == 0) return BadRequest("No subjects were resolved for the selected range.");

            var distractorCount = DefaultMcqDistractors;
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

                    var generated = await BuildMemorandumDocumentAsync(
                        subject,
                        qualification,
                        distractorCount,
                        HttpContext.RequestAborted);

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
            var fileName = $"KnowledgeQuestionnaireMemorandum_Range_Q{qualificationId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            return File(zipStream.ToArray(), "application/zip", fileName);
        }

        [HttpGet("download-memorandum-consolidated-range")]
        public async Task<IActionResult> DownloadMemorandumConsolidatedRange(
            [FromQuery] int qualificationId,
            [FromQuery] int? subjectFromId = null,
            [FromQuery] int? subjectToId = null,
            [FromQuery] int mcqDistractors = DefaultMcqDistractors)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required for consolidated memorandum export.");

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId);
            if (qualification == null) return BadRequest("No qualification available for consolidated memorandum export.");

            var subjects = ResolveSubjectRange(qualificationId, subjectFromId, subjectToId);
            if (subjects.Count == 0) return BadRequest("No subjects were resolved for the selected range.");

            var distractorCount = DefaultMcqDistractors;
            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                var body = main.Document.Body ?? (main.Document.Body = new Body());

                var firstCode = (subjects.FirstOrDefault()?.SubjectCode ?? string.Empty).Trim();
                var lastCode = (subjects.LastOrDefault()?.SubjectCode ?? string.Empty).Trim();
                var rangeLabel = string.IsNullOrWhiteSpace(firstCode) && string.IsNullOrWhiteSpace(lastCode)
                    ? $"Subjects: {subjects.Count}"
                    : $"Subject Range: {firstCode} to {lastCode}";

                AppendCleanCoverPage(
                    body,
                    main,
                    qualification,
                    "KNOWLEDGE QUESTIONNAIRE MEMORANDUM",
                    rangeLabel);
                body.Append(PageBreak());

                AppendLegalDisclaimerPage(body, qualification);
                body.Append(PageBreak());

                DocumentRevisionQualityControlPage.Append(body, qualification, new DocumentRevisionQualityControlPageOptions
                {
                    DocumentTitle = "Knowledge Questionnaire Memorandum",
                    DocumentType = "Summative Assessment Memorandum",
                    Phase = "Knowledge Learning"
                });
                body.Append(PageBreak());

                var included = 0;
                foreach (var subject in subjects)
                {
                    var built = await BuildQuestionsForSubjectAsync(
                        subject,
                        qualification,
                        FixedTotalMarks,
                        distractorCount,
                        HttpContext.RequestAborted);
                    var questions = built.Questions;
                    if (questions.Count == 0)
                    {
                        body.Append(StyledHeading($"{subject.SubjectCode} — {subject.SubjectDescription}", "Heading1", 26));
                        body.Append(BodyPara("No questions were generated for this subject.", 22));
                        body.Append(PageBreak());
                        continue;
                    }

                    included++;
                    body.Append(StyledHeading($"{subject.SubjectCode} — {subject.SubjectDescription}", "Heading1", 26));
                    body.Append(BodyPara($"Total marks: {questions.Sum(x => x.Marks)}", 22));

                    var table = new Table();
                    table.Append(DefaultTableProperties());
                    table.Append(new TableGrid());
                    table.Append(MemoRow("Q#", "Type", "Stem (short)", "Assessment Criterion", "Correct Answer", header: true));
                    foreach (var q in questions)
                    {
                        var stemShort = q.Prompt.Length <= 80 ? q.Prompt : $"{q.Prompt.Substring(0, 80)}...";
                        table.Append(MemoRow(
                            q.Number.ToString(),
                            "True/False",
                            stemShort,
                            q.AssessmentCriteriaDescription,
                            q.CorrectAnswer));
                    }

                    body.Append(table);
                    body.Append(Blank());
                    body.Append(BodyPara($"Question source: {built.SourceLabel}.", 22));
                    body.Append(PageBreak());
                }

                if (included == 0)
                {
                    body.Append(StyledHeading("No Subjects Exported", "Heading1", 24));
                    body.Append(BodyPara("No subject in the selected range produced questionnaire questions.", 22));
                }

                body.Append(DefaultSectionProperties());
                main.Document.Save();
            }

            ms.Position = 0;
            var fileName = $"KnowledgeQuestionnaire_Memorandum_AllSubjects_Q{qualificationId}_{DateTime.Now:yyyyMMdd_HHmmss}.docx";
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName);
        }

        [HttpGet("report")]
        public async Task<IActionResult> Report([FromQuery] int? qualificationId = null, [FromQuery] int? subjectId = null, [FromQuery] int mcqDistractors = DefaultMcqDistractors)
        {
            var subject = ResolveSubject(subjectId, qualificationId);
            if (subject == null) return BadRequest("No subject available for questionnaire report.");
            var qualification = ResolveQualification(subject, qualificationId);
            if (qualification == null) return BadRequest("No qualification available for questionnaire report.");

            var distractorCount = DefaultMcqDistractors;
            var reportResult = await BuildQuestionnaireReportAsync(subject, qualification, distractorCount, HttpContext.RequestAborted);
            if (!reportResult.Success || reportResult.Report == null)
            {
                return BadRequest(reportResult.ErrorMessage);
            }

            return Ok(reportResult.Report);
        }

        [HttpGet("download-report")]
        public async Task<IActionResult> DownloadReport([FromQuery] int? qualificationId = null, [FromQuery] int? subjectId = null, [FromQuery] int mcqDistractors = DefaultMcqDistractors)
        {
            var subject = ResolveSubject(subjectId, qualificationId);
            if (subject == null) return BadRequest("No subject available for questionnaire report.");
            var qualification = ResolveQualification(subject, qualificationId);
            if (qualification == null) return BadRequest("No qualification available for questionnaire report.");

            var distractorCount = DefaultMcqDistractors;
            var reportResult = await BuildQuestionnaireReportAsync(subject, qualification, distractorCount, HttpContext.RequestAborted);
            if (!reportResult.Success || reportResult.Report == null)
            {
                return BadRequest(reportResult.ErrorMessage);
            }

            var reportText = BuildQuestionnaireReportText(reportResult.Report);
            var safeCode = MakeSafeFilePart(subject.SubjectCode, "SUBJECT");
            var fileName = $"KnowledgeQuestionnaire_Report_{safeCode}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            return File(Encoding.UTF8.GetBytes(reportText), "text/plain; charset=utf-8", fileName);
        }

        [HttpGet("download-report-range")]
        public async Task<IActionResult> DownloadReportRange(
            [FromQuery] int qualificationId,
            [FromQuery] int? subjectFromId = null,
            [FromQuery] int? subjectToId = null,
            [FromQuery] int mcqDistractors = DefaultMcqDistractors)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required for range report export.");

            var subjects = ResolveSubjectRange(qualificationId, subjectFromId, subjectToId);
            if (subjects.Count == 0) return BadRequest("No subjects were resolved for the selected range.");

            var distractorCount = DefaultMcqDistractors;
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

                    var reportResult = await BuildQuestionnaireReportAsync(
                        subject,
                        qualification,
                        distractorCount,
                        HttpContext.RequestAborted);

                    if (!reportResult.Success || reportResult.Report == null)
                    {
                        failures.Add($"{subject.SubjectCode}: {reportResult.ErrorMessage}");
                        continue;
                    }

                    var safeCode = MakeSafeFilePart(subject.SubjectCode, "SUBJECT");
                    var entry = zip.CreateEntry($"KnowledgeQuestionnaire_Report_{safeCode}.txt", CompressionLevel.Fastest);
                    await using var entryStream = new StreamWriter(entry.Open(), Encoding.UTF8);
                    await entryStream.WriteAsync(BuildQuestionnaireReportText(reportResult.Report));
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
            var zipName = $"KnowledgeQuestionnaire_Report_Range_Q{qualificationId}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
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

        private sealed class QuestionnaireReportResult
        {
            public bool Success { get; init; }
            public string ErrorMessage { get; init; } = string.Empty;
            public QuestionnaireExportReport? Report { get; init; }

            public static QuestionnaireReportResult Fail(string message)
                => new() { Success = false, ErrorMessage = message };

            public static QuestionnaireReportResult Ok(QuestionnaireExportReport report)
                => new() { Success = true, Report = report };
        }

        private sealed class QuestionnaireExportReport
        {
            public int QualificationId { get; set; }
            public string QualificationNumber { get; set; } = string.Empty;
            public string QualificationDescription { get; set; } = string.Empty;
            public int SubjectId { get; set; }
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public int McqDistractors { get; set; }
            public int TotalMarks { get; set; }
            public int TotalQuestions { get; set; }
            public int TrueFalseQuestions { get; set; }
            public int MultipleChoiceQuestions { get; set; }
            public string QuestionSource { get; set; } = string.Empty;
            public bool TableOfContentsIncluded { get; set; }
            public bool BibliographySectionIncluded { get; set; }
            public int BibliographyEntriesFound { get; set; }
            public List<string> BibliographyPreview { get; set; } = new();
            public int LearningResourceSuggestionsFound { get; set; }
            public List<string> LearningResourceSuggestions { get; set; } = new();
            public List<string> TopicCodes { get; set; } = new();
            public DateTime GeneratedAtUtc { get; set; }
        }

        private sealed class QuestionBuildResult
        {
            public List<AssessmentDrivenQuestionGenerator.GeneratedQuestion> Questions { get; init; } = new();
            public string SourceLabel { get; init; } = string.Empty;
            public List<string> ResourceSuggestions { get; init; } = new();
        }

        private static bool HasSubjectIdentity(Subject subject)
        {
            return !string.IsNullOrWhiteSpace((subject.SubjectCode ?? string.Empty).Trim()) ||
                   !string.IsNullOrWhiteSpace((subject.SubjectDescription ?? string.Empty).Trim());
        }

        private async Task<GeneratedDocResult> BuildQuestionnaireDocumentAsync(
            Subject subject,
            Qualification qualification,
            int mcqDistractors,
            CancellationToken cancellationToken)
        {
            var built = await BuildQuestionsForSubjectAsync(
                subject,
                qualification,
                FixedTotalMarks,
                mcqDistractors,
                cancellationToken);
            var questions = built.Questions;
            var questionSource = built.SourceLabel;

            if (questions.Count == 0)
            {
                return GeneratedDocResult.Fail("No topics/assessment criteria/lesson plans found to build questionnaire.");
            }

            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                var body = main.Document.Body ?? (main.Document.Body = new Body());
                var settingsPart = main.AddNewPart<DocumentSettingsPart>();
                settingsPart.Settings = new Settings(new UpdateFieldsOnOpen() { Val = true });
                settingsPart.Settings.Save();

                AppendCleanCoverPage(
                    body,
                    main,
                    qualification,
                    "KNOWLEDGE QUESTIONNAIRE",
                    $"{subject.SubjectCode} — {subject.SubjectDescription}");
                body.Append(PageBreak());

                body.Append(StyledHeading("KNOWLEDGE QUESTIONNAIRE", "Heading1", 30));
                body.Append(BodyPara($"{subject.SubjectCode} — {subject.SubjectDescription}", 24));
                body.Append(Blank());
                body.Append(BodyPara("Learner name: ________________________________", 24));
                body.Append(BodyPara("Learner number: ______________________________", 24));
                body.Append(BodyPara("Date completed: ______________________________", 24));
                body.Append(BodyPara("Assessment Instructions", 26));
                body.Append(BodyPara("1. This paper is developed per subject and each assessment criterion is tested once.", 24));
                body.Append(BodyPara("2. Answer all questions.", 24));
                body.Append(BodyPara("3. Each question has one statement stem with only two possible answers: True or False.", 24));
                body.Append(BodyPara("4. Select one answer only per question.", 24));
                body.Append(PageBreak());

                AppendLegalDisclaimerPage(body, qualification);
                body.Append(PageBreak());

                DocumentRevisionQualityControlPage.Append(body, qualification, new DocumentRevisionQualityControlPageOptions
                {
                    DocumentTitle = "Knowledge Questionnaire",
                    DocumentType = "Summative Assessment",
                    Phase = "Knowledge Learning"
                });
                body.Append(PageBreak());

                AppendTableOfContentsPage(body);
                body.Append(PageBreak());

                body.Append(StyledHeading("SECTION 1: TRUE OR FALSE QUESTIONS", "Heading1", 28));
                body.Append(BodyPara($"Questions in this section: {questions.Count} (1 mark each)", 22));
                body.Append(Blank());

                foreach (var q in questions)
                {
                    body.Append(BuildQuestionTable(
                        q,
                        "True / False",
                        "Select one answer only: True or False."));
                    body.Append(Blank());
                }

                body.Append(StyledHeading("QUESTIONNAIRE SUMMARY", "Heading1", 24));
                body.Append(BodyPara($"TOTAL MARKS: {questions.Sum(x => x.Marks)}", 22));
                body.Append(BodyPara($"Question source: {questionSource}.", 22));

                body.Append(PageBreak());
                body.Append(StyledHeading("BIBLIOGRAPHY", "Heading1", 22));
                var bibliography = BuildBibliographyEntries(qualification, subject, questions);
                if (built.ResourceSuggestions.Count > 0)
                {
                    bibliography = built.ResourceSuggestions
                        .Concat(bibliography)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                if (bibliography.Count == 0)
                {
                    body.Append(BodyPara("No bibliography entries were detected for this subject export.", 22));
                }
                else
                {
                    foreach (var entry in bibliography)
                    {
                        body.Append(BulletPara(entry, 22));
                    }
                }

                body.Append(DefaultSectionProperties());
                main.Document.Save();
            }

            ms.Position = 0;
            var safeCode = MakeSafeFilePart(subject.SubjectCode, "SUBJECT");
            var fileName = $"KnowledgeQuestionnaire_{safeCode}_{DateTime.Now:yyyyMMdd}.docx";
            return GeneratedDocResult.Ok(ms.ToArray(), fileName);
        }

        private async Task<QuestionnaireReportResult> BuildQuestionnaireReportAsync(
            Subject subject,
            Qualification qualification,
            int mcqDistractors,
            CancellationToken cancellationToken)
        {
            var built = await BuildQuestionsForSubjectAsync(
                subject,
                qualification,
                FixedTotalMarks,
                mcqDistractors,
                cancellationToken);
            var questions = built.Questions;
            var questionSource = built.SourceLabel;

            if (questions.Count == 0)
            {
                return QuestionnaireReportResult.Fail("No topics/assessment criteria/lesson plans found to build questionnaire report.");
            }

            var trueFalseCount = questions.Count(q => q.Type == "TrueFalse");
            var multipleChoiceCount = questions.Count - trueFalseCount;
            var bibliography = BuildBibliographyEntries(qualification, subject, questions);
            var topicCodes = questions
                .Select(q => (q.TopicCode ?? string.Empty).Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var report = new QuestionnaireExportReport
            {
                QualificationId = qualification.Id,
                QualificationNumber = (qualification.QualificationNumber ?? string.Empty).Trim(),
                QualificationDescription = (qualification.QualificationDescription ?? string.Empty).Trim(),
                SubjectId = subject.Id,
                SubjectCode = (subject.SubjectCode ?? string.Empty).Trim(),
                SubjectDescription = (subject.SubjectDescription ?? string.Empty).Trim(),
                McqDistractors = 0,
                TotalMarks = questions.Sum(x => x.Marks),
                TotalQuestions = questions.Count,
                TrueFalseQuestions = trueFalseCount,
                MultipleChoiceQuestions = multipleChoiceCount,
                QuestionSource = questionSource ?? string.Empty,
                TableOfContentsIncluded = true,
                BibliographySectionIncluded = true,
                BibliographyEntriesFound = bibliography.Count,
                BibliographyPreview = bibliography.Take(8).ToList(),
                LearningResourceSuggestionsFound = built.ResourceSuggestions.Count,
                LearningResourceSuggestions = built.ResourceSuggestions.Take(12).ToList(),
                TopicCodes = topicCodes,
                GeneratedAtUtc = DateTime.UtcNow
            };

            return QuestionnaireReportResult.Ok(report);
        }

        private static string BuildQuestionnaireReportText(QuestionnaireExportReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Knowledge Questionnaire Export Report");
            sb.AppendLine($"Generated (UTC): {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Qualification: {report.QualificationNumber} - {report.QualificationDescription} (ID: {report.QualificationId})");
            sb.AppendLine($"Subject: {report.SubjectCode} - {report.SubjectDescription} (ID: {report.SubjectId})");
            sb.AppendLine("MCQ Distractors: Disabled (True/False only mode)");
            sb.AppendLine($"Question Source: {report.QuestionSource}");
            sb.AppendLine($"Total Questions: {report.TotalQuestions}");
            sb.AppendLine($"True/False Questions: {report.TrueFalseQuestions}");
            sb.AppendLine($"Multiple Choice Questions: {report.MultipleChoiceQuestions}");
            sb.AppendLine($"Total Marks: {report.TotalMarks}");
            sb.AppendLine($"Table Of Contents Included: {(report.TableOfContentsIncluded ? "Yes" : "No")}");
            sb.AppendLine($"Bibliography Section Included: {(report.BibliographySectionIncluded ? "Yes" : "No")}");
            sb.AppendLine($"Bibliography Entries Found: {report.BibliographyEntriesFound}");
            sb.AppendLine($"Learning Resource Suggestions Found: {report.LearningResourceSuggestionsFound}");
            sb.AppendLine();
            sb.AppendLine("Topic Codes Included:");
            if (report.TopicCodes.Count == 0) sb.AppendLine("- None");
            foreach (var code in report.TopicCodes) sb.AppendLine($"- {code}");
            sb.AppendLine();
            sb.AppendLine("Learning Resource Suggestions:");
            if (report.LearningResourceSuggestions.Count == 0) sb.AppendLine("- No SMI resource suggestions were returned.");
            foreach (var line in report.LearningResourceSuggestions) sb.AppendLine($"- {line}");
            sb.AppendLine();
            sb.AppendLine("Bibliography Preview:");
            if (report.BibliographyPreview.Count == 0) sb.AppendLine("- No bibliography entries detected.");
            foreach (var line in report.BibliographyPreview) sb.AppendLine($"- {line}");
            return sb.ToString();
        }

        private async Task<GeneratedDocResult> BuildMemorandumDocumentAsync(
            Subject subject,
            Qualification qualification,
            int mcqDistractors,
            CancellationToken cancellationToken)
        {
            var built = await BuildQuestionsForSubjectAsync(
                subject,
                qualification,
                FixedTotalMarks,
                mcqDistractors,
                cancellationToken);
            var questions = built.Questions;
            var questionSource = built.SourceLabel;

            if (questions.Count == 0)
            {
                return GeneratedDocResult.Fail("No topics/assessment criteria/lesson plans found to build memorandum.");
            }

            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                var body = main.Document.Body ?? (main.Document.Body = new Body());

                AppendCleanCoverPage(
                    body,
                    main,
                    qualification,
                    "KNOWLEDGE QUESTIONNAIRE MEMORANDUM",
                    $"{subject.SubjectCode} — {subject.SubjectDescription}");
                body.Append(PageBreak());

                body.Append(StyledHeading("Knowledge Questionnaire Memorandum", "Heading1", 32));
                body.Append(BodyPara($"{qualification.QualificationNumber} — {qualification.QualificationDescription}", 24));
                body.Append(BodyPara($"{subject.SubjectCode} — {subject.SubjectDescription}", 24));
                body.Append(BodyPara($"Total marks: {FixedTotalMarks}", 24));
                body.Append(PageBreak());

                AppendLegalDisclaimerPage(body, qualification);
                body.Append(PageBreak());

                DocumentRevisionQualityControlPage.Append(body, qualification, new DocumentRevisionQualityControlPageOptions
                {
                    DocumentTitle = "Knowledge Questionnaire Memorandum",
                    DocumentType = "Summative Assessment Memorandum",
                    Phase = "Knowledge Learning"
                });
                body.Append(PageBreak());

                var table = new Table();
                table.Append(DefaultTableProperties());
                table.Append(new TableGrid());
                table.Append(MemoRow("Q#", "Type", "Stem (short)", "Assessment Criterion", "Correct Answer", header: true));
                foreach (var q in questions)
                {
                    var stemShort = q.Prompt.Length <= 80 ? q.Prompt : $"{q.Prompt.Substring(0, 80)}...";
                    table.Append(MemoRow(
                        q.Number.ToString(),
                        "True/False",
                        stemShort,
                        q.AssessmentCriteriaDescription,
                        q.CorrectAnswer));
                }

                body.Append(table);
                body.Append(Blank());
                body.Append(BodyPara($"Question source: {questionSource}.", 22));
                body.Append(DefaultSectionProperties());
                main.Document.Save();
            }

            ms.Position = 0;
            var safeCode = MakeSafeFilePart(subject.SubjectCode, "SUBJECT");
            var fileName = $"KnowledgeQuestionnaire_Memorandum_{safeCode}_{DateTime.Now:yyyyMMdd}.docx";
            return GeneratedDocResult.Ok(ms.ToArray(), fileName);
        }

        private GeneratedDocResult BuildPhaseQuestionnaireDocument(
            Qualification qualification,
            PhaseQuestionnaireDocxExportRequest payload,
            IReadOnlyList<PhaseQuestionnaireDocxQuestionRow> questions)
        {
            if (questions == null || questions.Count == 0)
            {
                return GeneratedDocResult.Fail("No generated questionnaire rows were available for DOCX export.");
            }

            var totalMarks = questions.Sum(row => Math.Max(1, row.Marks));
            var trueFalseCount = questions.Count(row => string.Equals(row.Type, "TrueFalse", StringComparison.OrdinalIgnoreCase));
            var multipleChoiceCount = questions.Count - trueFalseCount;
            var questionnaireTitle = string.IsNullOrWhiteSpace(payload.QuestionnaireTitle)
                ? "Knowledge Questionnaire"
                : payload.QuestionnaireTitle.Trim();
            var phaseLabel = BuildPhaseScopeLabel(payload.PhaseName, payload.PhaseDescription);
            var categoryLine = string.Join(" - ", new[]
            {
                (payload.MainCategoryCode ?? string.Empty).Trim(),
                (payload.MainCategoryLabel ?? string.Empty).Trim()
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
            if (string.IsNullOrWhiteSpace(categoryLine))
            {
                categoryLine = "Main Category";
            }

            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                var body = main.Document.Body ?? (main.Document.Body = new Body());
                var settingsPart = main.AddNewPart<DocumentSettingsPart>();
                settingsPart.Settings = new Settings(new UpdateFieldsOnOpen() { Val = true });
                settingsPart.Settings.Save();

                AppendCleanCoverPage(
                    body,
                    main,
                    qualification,
                    questionnaireTitle.ToUpperInvariant(),
                    categoryLine);
                body.Append(PageBreak());

                AppendLegalDisclaimerPage(body, qualification);
                body.Append(PageBreak());

                DocumentRevisionQualityControlPage.Append(body, qualification, new DocumentRevisionQualityControlPageOptions
                {
                    DocumentTitle = questionnaireTitle,
                    DocumentType = "Summative Assessment",
                    Phase = string.IsNullOrWhiteSpace(phaseLabel) ? "Knowledge Learning" : phaseLabel,
                    DocumentDeveloper = (payload.CreatedBy ?? string.Empty).Trim(),
                    Moderator = (payload.ReviewedBy ?? string.Empty).Trim()
                });
                body.Append(PageBreak());

                AppendTableOfContentsPage(body);
                body.Append(PageBreak());

                body.Append(StyledHeading(questionnaireTitle.ToUpperInvariant(), "Heading1", 30));
                body.Append(BodyPara($"{qualification.QualificationNumber} — {qualification.QualificationDescription}", 24));
                body.Append(BodyPara(categoryLine, 24));
                if (!string.IsNullOrWhiteSpace(phaseLabel))
                {
                    body.Append(BodyPara($"Phase: {phaseLabel}", 22));
                }
                body.Append(BodyPara($"Total questions: {questions.Count}", 22));
                body.Append(BodyPara($"True/False questions: {trueFalseCount}", 22));
                body.Append(BodyPara($"Multiple Choice questions: {multipleChoiceCount}", 22));
                body.Append(BodyPara($"Total marks: {totalMarks}", 22));
                if (!string.IsNullOrWhiteSpace((payload.PassMark ?? string.Empty).Trim()))
                {
                    body.Append(BodyPara($"Pass mark: {(payload.PassMark ?? string.Empty).Trim()}", 22));
                }
                body.Append(Blank());
                body.Append(HeadingPara("ASSESSMENT INSTRUCTIONS", 16));
                body.Append(BodyPara("1. Answer all questions.", 22));
                body.Append(BodyPara("2. Each question carries 1 mark unless otherwise stated.", 22));
                body.Append(BodyPara("3. Multiple Choice questions require one correct answer only.", 22));
                body.Append(BodyPara("4. True/False questions require you to mark each option as True or False where indicated.", 22));
                body.Append(BodyPara("5. Read each question carefully before answering.", 22));
                body.Append(Blank());

                string? lastSubjectLine = null;
                string? lastTopicLine = null;
                foreach (var question in questions.OrderBy(row => row.Number))
                {
                    var subjectLine = string.Join(" - ", new[]
                    {
                        (question.SubjectCode ?? string.Empty).Trim(),
                        (question.SubjectDescription ?? string.Empty).Trim()
                    }.Where(part => !string.IsNullOrWhiteSpace(part)));
                    var topicLine = string.Join(" - ", new[]
                    {
                        (question.TopicCode ?? string.Empty).Trim(),
                        (question.TopicDescription ?? string.Empty).Trim()
                    }.Where(part => !string.IsNullOrWhiteSpace(part)));

                    if (!string.IsNullOrWhiteSpace(subjectLine) &&
                        !string.Equals(subjectLine, lastSubjectLine, StringComparison.OrdinalIgnoreCase))
                    {
                        body.Append(StyledHeading(subjectLine.ToUpperInvariant(), "Heading2", 20));
                        lastSubjectLine = subjectLine;
                        lastTopicLine = null;
                    }

                    if (!string.IsNullOrWhiteSpace(topicLine) &&
                        !string.Equals(topicLine, lastTopicLine, StringComparison.OrdinalIgnoreCase))
                    {
                        body.Append(QuestionMetaParagraph($"Topic: {topicLine}", 18, bold: true));
                        lastTopicLine = topicLine;
                    }

                    body.Append(BuildPhaseQuestionTable(question));
                    body.Append(Blank());
                }

                body.Append(StyledHeading("END OF QUESTIONNAIRE", "Heading1", 20));
                body.Append(BodyPara($"Total marks: {totalMarks}", 22));
                body.Append(DefaultSectionProperties());
                main.Document.Save();
            }

            ms.Position = 0;
            var qualificationCode = MakeSafeFilePart(qualification.QualificationNumber, "QUALIFICATION");
            var categoryCode = MakeSafeFilePart(payload.MainCategoryCode, "CATEGORY");
            var fileName = $"KnowledgeQuestionnaire_{qualificationCode}_{categoryCode}_{DateTime.Now:yyyyMMdd_HHmmss}.docx";
            return GeneratedDocResult.Ok(ms.ToArray(), fileName);
        }

        private static PhaseQuestionnaireDocxQuestionRow NormalizePhaseQuestionnaireDocxQuestionRow(PhaseQuestionnaireDocxQuestionRow? row)
        {
            var options = (row?.Options ?? new List<string>())
                .Select(option => (option ?? string.Empty).Trim())
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .Take(4)
                .ToList();

            return new PhaseQuestionnaireDocxQuestionRow
            {
                Number = Math.Max(0, row?.Number ?? 0),
                Type = string.Equals((row?.Type ?? string.Empty).Trim(), "TrueFalse", StringComparison.OrdinalIgnoreCase)
                    ? "TrueFalse"
                    : "MultipleChoice",
                Prompt = (row?.Prompt ?? string.Empty).Trim(),
                Options = options,
                CorrectAnswer = (row?.CorrectAnswer ?? string.Empty).Trim(),
                SubjectCode = (row?.SubjectCode ?? string.Empty).Trim(),
                SubjectDescription = (row?.SubjectDescription ?? string.Empty).Trim(),
                TopicCode = (row?.TopicCode ?? string.Empty).Trim(),
                TopicDescription = (row?.TopicDescription ?? string.Empty).Trim(),
                AssessmentCriteriaNumber = (row?.AssessmentCriteriaNumber ?? string.Empty).Trim(),
                LessonPlanLabel = (row?.LessonPlanLabel ?? string.Empty).Trim(),
                AssessmentCriteriaDescription = (row?.AssessmentCriteriaDescription ?? string.Empty).Trim(),
                Rationale = (row?.Rationale ?? string.Empty).Trim(),
                Marks = Math.Max(1, row?.Marks ?? 1)
            };
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
            IReadOnlyList<AssessmentDrivenQuestionGenerator.GeneratedQuestion> questions)
        {
            var qualificationCode = (qualification.QualificationNumber ?? string.Empty).Trim();
            var subjectCode = (subject.SubjectCode ?? string.Empty).Trim();
            var subjectDescription = (subject.SubjectDescription ?? string.Empty).Trim();
            var topicCodes = (questions ?? Array.Empty<AssessmentDrivenQuestionGenerator.GeneratedQuestion>())
                .Select(q => (q.TopicCode ?? string.Empty).Trim())
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

        private Task<QuestionBuildResult> BuildQuestionsForSubjectAsync(
            Subject subject,
            Qualification qualification,
            int totalMarks,
            int mcqDistractors,
            CancellationToken cancellationToken)
        {
            _ = totalMarks;
            _ = mcqDistractors;
            var items = AssessmentDrivenQuestionGenerator.BuildOrderedLessonEvidence(_context, subject.Id);
            var criteriaItems = BuildAssessmentCriteriaFocusedItems(items);
            var questions = new List<AssessmentDrivenQuestionGenerator.GeneratedQuestion>();

            var qn = 1;
            foreach (var item in criteriaItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                questions.Add(BuildBinaryTrueFalseCriterionQuestion(item, qn++));
            }

            return Task.FromResult(new QuestionBuildResult
            {
                Questions = questions,
                SourceLabel = "Generated from Lesson Plan Content: one simple True/False statement per assessment criterion.",
                ResourceSuggestions = new List<string>()
            });
        }

        private static List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem> ApplyLessonContentOverride(
            List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem> items,
            string lessonPlanOverride)
        {
            var overrideText = (lessonPlanOverride ?? string.Empty).Trim();
            if (items.Count == 0 || string.IsNullOrWhiteSpace(overrideText)) return items;

            return items.Select(item => new AssessmentDrivenQuestionGenerator.LessonEvidenceItem
            {
                TopicId = item.TopicId,
                TopicCode = item.TopicCode,
                TopicDescription = item.TopicDescription,
                AssessmentCriteriaId = item.AssessmentCriteriaId,
                AssessmentCriteriaDescription = item.AssessmentCriteriaDescription,
                LessonPlanLabel = item.LessonPlanLabel,
                LessonPlanDescription = item.LessonPlanDescription,
                LessonPlanContent = overrideText,
                EvidenceText = BuildMergedEvidenceText(overrideText, item.EvidenceText),
                TopicOrder = item.TopicOrder,
                LessonSortOrder = item.LessonSortOrder,
                BundleKey = item.BundleKey
            }).ToList();
        }

        private static string BuildMergedEvidenceText(string primaryText, string fallbackText)
        {
            var primary = AssessmentDrivenQuestionGenerator.SanitizeQuestionText(primaryText);
            var fallback = AssessmentDrivenQuestionGenerator.SanitizeQuestionText(fallbackText);
            if (string.IsNullOrWhiteSpace(primary)) return fallback;
            if (string.IsNullOrWhiteSpace(fallback)) return primary;
            var combined = $"{primary} {fallback}".Trim();
            return combined.Length <= 2200 ? combined : combined[..2200];
        }

        private static List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem> BuildAssessmentCriteriaFocusedItems(
            List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem> items)
        {
            if (items == null || items.Count == 0) return new List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem>();

            return items
                .Where(item => !string.IsNullOrWhiteSpace((item.AssessmentCriteriaDescription ?? string.Empty).Trim()))
                .GroupBy(item => item.AssessmentCriteriaId > 0
                    ? $"ACID:{item.AssessmentCriteriaId}"
                    : $"{(item.TopicCode ?? string.Empty).Trim().ToUpperInvariant()}|{(item.AssessmentCriteriaDescription ?? string.Empty).Trim().ToUpperInvariant()}")
                .Select(group => group
                    .OrderBy(item => string.IsNullOrWhiteSpace((item.LessonPlanContent ?? string.Empty).Trim()) ? 1 : 0)
                    .ThenBy(item => item.LessonSortOrder)
                    .ThenBy(item => item.LessonPlanLabel)
                    .First())
                .ToList();
        }

        private static AssessmentDrivenQuestionGenerator.GeneratedQuestion BuildBinaryTrueFalseCriterionQuestion(
            AssessmentDrivenQuestionGenerator.LessonEvidenceItem item,
            int number)
        {
            return AssessmentDrivenQuestionGenerator.BuildBinaryTrueFalseQuestion(item, number, marks: 1);
        }

        private sealed class SmiQuestionBuildResult
        {
            public List<AssessmentDrivenQuestionGenerator.GeneratedQuestion> Questions { get; init; } = new();
            public List<string> ResourceSuggestions { get; init; } = new();
            public bool UsedDeterministicFallback { get; init; }
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

        private sealed class PhaseCriterionSeed
        {
            public Subject Subject { get; init; } = new();
            public AssessmentDrivenQuestionGenerator.LessonEvidenceItem Item { get; init; } = new();
            public string AssessmentCriteriaNumber { get; init; } = string.Empty;
            public string SubjectCode => (Subject.SubjectCode ?? string.Empty).Trim();
            public string SubjectDescription => (Subject.SubjectDescription ?? string.Empty).Trim();
        }

        private readonly record struct PhaseQuestionCountPlan(int TrueFalseCount, int MultipleChoiceCount)
        {
            public int TotalQuestions => TrueFalseCount + MultipleChoiceCount;
        }

        private sealed class PhaseQuestionAssignment
        {
            public PhaseCriterionSeed Seed { get; set; } = new();
            public string QuestionType { get; set; } = string.Empty;
            public int QuestionNumber { get; set; }
            public int TypeOccurrence { get; set; }
            public int VariantIndex { get; set; }
            public int VariantCount { get; set; }
        }

        private PhaseQuestionCountPlan NormalizePhaseQuestionCounts(
            int requestedTrueFalseCount,
            int requestedMultipleChoiceCount,
            int minimumTotalQuestions)
        {
            var trueFalseCount = Math.Max(0, requestedTrueFalseCount);
            var multipleChoiceCount = Math.Max(0, requestedMultipleChoiceCount);
            var minimumRequired = Math.Max(0, minimumTotalQuestions);

            if ((trueFalseCount + multipleChoiceCount) == 0)
            {
                trueFalseCount = minimumRequired / 2;
                multipleChoiceCount = minimumRequired - trueFalseCount;
            }
            else if ((trueFalseCount + multipleChoiceCount) < minimumRequired)
            {
                var gap = minimumRequired - (trueFalseCount + multipleChoiceCount);
                var tfAdd = gap / 2;
                var mcqAdd = gap - tfAdd;
                if (trueFalseCount <= multipleChoiceCount)
                {
                    trueFalseCount += mcqAdd;
                    multipleChoiceCount += tfAdd;
                }
                else
                {
                    trueFalseCount += tfAdd;
                    multipleChoiceCount += mcqAdd;
                }
            }

            return new PhaseQuestionCountPlan(trueFalseCount, multipleChoiceCount);
        }

        private List<PhaseCriterionSeed> BuildPhaseCriterionSeeds(
            List<Subject> subjects,
            KnowledgeQuestionnaireV1Service.KnowledgeQuestionnaireV1Draft draft)
        {
            var kqCriteria = (draft.Criteria ?? new List<KnowledgeQuestionnaireV1Service.KnowledgeQuestionnaireV1CriterionIntentDraft>())
                .Where(row => string.Equals(row.RoutingStatus, "KQ", StringComparison.OrdinalIgnoreCase) && row.AssessmentCriteriaId > 0)
                .GroupBy(row => row.AssessmentCriteriaId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(row => row.CoverageType == "direct" ? 0 : 1)
                        .ThenBy(row => row.IntentId, StringComparer.OrdinalIgnoreCase)
                        .First());

            if (kqCriteria.Count == 0) return new List<PhaseCriterionSeed>();

            var result = new List<PhaseCriterionSeed>();
            foreach (var subject in subjects)
            {
                var items = AssessmentDrivenQuestionGenerator.BuildOrderedLessonEvidence(_context, subject.Id)
                    .Where(item => item.AssessmentCriteriaId > 0 && kqCriteria.ContainsKey(item.AssessmentCriteriaId))
                    .GroupBy(item => item.AssessmentCriteriaId)
                    .Select(group => group
                        .OrderBy(item => string.IsNullOrWhiteSpace((item.LessonPlanContent ?? string.Empty).Trim()) ? 1 : 0)
                        .ThenBy(item => item.LessonSortOrder)
                        .ThenBy(item => item.LessonPlanLabel)
                        .First())
                    .ToList();

                foreach (var item in items)
                {
                    if (!kqCriteria.TryGetValue(item.AssessmentCriteriaId, out var criterionRow)) continue;
                    result.Add(new PhaseCriterionSeed
                    {
                        Subject = subject,
                        Item = item,
                        AssessmentCriteriaNumber = criterionRow.AssessmentCriteriaNumber
                    });
                }
            }

            return result
                .OrderBy(seed => seed.SubjectCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(seed => seed.Item.TopicCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(seed => seed.AssessmentCriteriaNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(seed => seed.Item.AssessmentCriteriaId)
                .ToList();
        }

        private async Task<SmiQuestionBuildResult> TryBuildPhaseQuestionsWithSmiAsync(
            List<PhaseCriterionSeed> seeds,
            Qualification qualification,
            int trueFalseCount,
            int mcqCount,
            int minimumQuestionsPerCriterion,
            int mcqDistractors,
            CancellationToken cancellationToken)
        {
            var empty = new SmiQuestionBuildResult();
            if (!IsSmiIntegrationEnabled()) return empty;
            if (seeds.Count == 0) return empty;
            if ((trueFalseCount + mcqCount) <= 0) return empty;

            var assignments = BuildPhaseQuestionAssignments(
                seeds,
                trueFalseCount,
                mcqCount,
                minimumQuestionsPerCriterion);
            if (assignments.Count == 0) return empty;

            var built = new List<AssessmentDrivenQuestionGenerator.GeneratedQuestion>(assignments.Count);
            var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedDeterministicFallback = !await IsSmiServiceResponsiveAsync(cancellationToken);
            var skipRemoteGeneration = usedDeterministicFallback;

            foreach (var seedGroup in assignments.GroupBy(row => row.Seed.Item.BundleKey, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var seed = seedGroup.First().Seed;
                var priorQuestionsForSeed = new List<AssessmentDrivenQuestionGenerator.GeneratedQuestion>();

                foreach (var assignment in seedGroup.OrderBy(row => row.QuestionNumber))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var isTrueFalse = string.Equals(assignment.QuestionType, "TrueFalse", StringComparison.OrdinalIgnoreCase);
                    AssessmentDrivenQuestionGenerator.GeneratedQuestion? question = null;

                    if (!skipRemoteGeneration)
                    {
                        question = await TryGenerateSingleSmiQuestionAsync(
                            seed.Item,
                            qualification,
                            seed.Subject,
                            questionType: assignment.QuestionType,
                            optionCount: isTrueFalse ? TrueFalseOptionCount : DefaultMcqDistractors + 1,
                            marks: 1,
                            cancellationToken,
                            resources,
                            variantIndex: assignment.VariantIndex,
                            variantCount: assignment.VariantCount,
                            existingQuestions: priorQuestionsForSeed,
                            timeoutOverrideSeconds: SmiQuestionTimeoutSeconds);

                        if (question == null)
                        {
                            usedDeterministicFallback = true;
                            skipRemoteGeneration = true;
                        }
                    }

                    if (question == null || IsDuplicateQuestionForSeed(question, priorQuestionsForSeed))
                    {
                        usedDeterministicFallback = true;
                        question = isTrueFalse
                            ? BuildTrueFalseSingleCorrectQuestion(seed.Item, assignment.QuestionNumber, assignment.TypeOccurrence)
                            : BuildMultipleChoiceSubjectQuestion(seed.Item, assignment.QuestionNumber, mcqDistractors, assignment.TypeOccurrence);
                    }
                    else
                    {
                        question = CloneQuestion(question, assignment.QuestionNumber);
                    }

                    built.Add(question);
                    priorQuestionsForSeed.Add(question);
                }
            }

            built = built
                .OrderBy(question => question.Number)
                .ToList();

            return new SmiQuestionBuildResult
            {
                Questions = built,
                ResourceSuggestions = resources.Take(25).ToList(),
                UsedDeterministicFallback = usedDeterministicFallback
            };
        }

        private static List<PhaseQuestionAssignment> BuildPhaseQuestionAssignments(
            List<PhaseCriterionSeed> seeds,
            int trueFalseCount,
            int mcqCount,
            int minimumQuestionsPerCriterion)
        {
            var remainingTrueFalse = Math.Max(0, trueFalseCount);
            var remainingMultipleChoice = Math.Max(0, mcqCount);
            var minimumPerCriterion = Math.Max(0, minimumQuestionsPerCriterion);
            if (seeds == null || seeds.Count == 0) return new List<PhaseQuestionAssignment>();
            if ((remainingTrueFalse + remainingMultipleChoice) <= 0) return new List<PhaseQuestionAssignment>();

            var assignments = new List<PhaseQuestionAssignment>(remainingTrueFalse + remainingMultipleChoice);
            var seedAssignments = new Dictionary<string, List<PhaseQuestionAssignment>>(StringComparer.OrdinalIgnoreCase);
            var typeOccurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var questionNumber = 1;

            foreach (var seed in seeds)
            {
                var bundleKey = seed.Item.BundleKey ?? $"{seed.SubjectCode}|{seed.AssessmentCriteriaNumber}";
                seedAssignments[bundleKey] = new List<PhaseQuestionAssignment>();

                var localCount = 0;
                if (minimumPerCriterion > 0 && remainingTrueFalse > 0)
                {
                    assignments.Add(CreatePhaseQuestionAssignment(
                        seed,
                        "TrueFalse",
                        ref questionNumber,
                        typeOccurrences,
                        seedAssignments[bundleKey]));
                    localCount++;
                    remainingTrueFalse--;
                }

                if (minimumPerCriterion > 1 && remainingMultipleChoice > 0)
                {
                    assignments.Add(CreatePhaseQuestionAssignment(
                        seed,
                        "MultipleChoice",
                        ref questionNumber,
                        typeOccurrences,
                        seedAssignments[bundleKey]));
                    localCount++;
                    remainingMultipleChoice--;
                }

                var preferTrueFalse = remainingTrueFalse >= remainingMultipleChoice;
                while (localCount < minimumPerCriterion && (remainingTrueFalse > 0 || remainingMultipleChoice > 0))
                {
                    var nextType = SelectNextQuestionType(ref remainingTrueFalse, ref remainingMultipleChoice, ref preferTrueFalse);
                    if (string.IsNullOrWhiteSpace(nextType)) break;
                    assignments.Add(CreatePhaseQuestionAssignment(
                        seed,
                        nextType,
                        ref questionNumber,
                        typeOccurrences,
                        seedAssignments[bundleKey]));
                    localCount++;
                }
            }

            var roundRobinIndex = 0;
            var preferExtraTrueFalse = remainingTrueFalse >= remainingMultipleChoice;
            while (remainingTrueFalse > 0 || remainingMultipleChoice > 0)
            {
                var seed = seeds[roundRobinIndex % seeds.Count];
                var bundleKey = seed.Item.BundleKey ?? $"{seed.SubjectCode}|{seed.AssessmentCriteriaNumber}";
                var nextType = SelectNextQuestionType(ref remainingTrueFalse, ref remainingMultipleChoice, ref preferExtraTrueFalse);
                if (string.IsNullOrWhiteSpace(nextType)) break;
                assignments.Add(CreatePhaseQuestionAssignment(
                    seed,
                    nextType,
                    ref questionNumber,
                    typeOccurrences,
                    seedAssignments[bundleKey]));
                roundRobinIndex++;
            }

            return assignments;
        }

        private static PhaseQuestionAssignment CreatePhaseQuestionAssignment(
            PhaseCriterionSeed seed,
            string questionType,
            ref int questionNumber,
            Dictionary<string, int> typeOccurrences,
            List<PhaseQuestionAssignment> existingAssignmentsForSeed)
        {
            var occurrenceKey = $"{seed.Item.BundleKey}|{questionType}";
            var occurrence = typeOccurrences.TryGetValue(occurrenceKey, out var seen) ? seen : 0;
            typeOccurrences[occurrenceKey] = occurrence + 1;

            var assignment = new PhaseQuestionAssignment
            {
                Seed = seed,
                QuestionType = questionType,
                QuestionNumber = questionNumber++,
                TypeOccurrence = occurrence,
                VariantIndex = existingAssignmentsForSeed.Count,
                VariantCount = 0
            };
            existingAssignmentsForSeed.Add(assignment);

            for (var i = 0; i < existingAssignmentsForSeed.Count; i++)
            {
                existingAssignmentsForSeed[i].VariantIndex = i;
                existingAssignmentsForSeed[i].VariantCount = existingAssignmentsForSeed.Count;
            }

            return existingAssignmentsForSeed[^1];
        }

        private static string SelectNextQuestionType(
            ref int remainingTrueFalse,
            ref int remainingMultipleChoice,
            ref bool preferTrueFalse)
        {
            if (remainingTrueFalse <= 0 && remainingMultipleChoice <= 0) return string.Empty;

            string nextType;
            if ((preferTrueFalse && remainingTrueFalse > 0) || remainingMultipleChoice == 0)
            {
                nextType = "TrueFalse";
                remainingTrueFalse--;
            }
            else
            {
                nextType = "MultipleChoice";
                remainingMultipleChoice--;
            }

            preferTrueFalse = !preferTrueFalse;
            return nextType;
        }

        private static bool IsDuplicateQuestionForSeed(
            AssessmentDrivenQuestionGenerator.GeneratedQuestion candidate,
            IReadOnlyCollection<AssessmentDrivenQuestionGenerator.GeneratedQuestion> existingQuestions)
        {
            if (candidate == null || existingQuestions == null || existingQuestions.Count == 0) return false;

            var candidatePrompt = NormalizeQuestionComparisonKey(candidate.Prompt);
            var candidateAnswer = NormalizeQuestionComparisonKey(candidate.CorrectAnswer);
            var candidateType = (candidate.Type ?? string.Empty).Trim();

            foreach (var existing in existingQuestions)
            {
                var existingPrompt = NormalizeQuestionComparisonKey(existing.Prompt);
                if (!string.IsNullOrWhiteSpace(candidatePrompt) &&
                    string.Equals(candidatePrompt, existingPrompt, StringComparison.Ordinal))
                {
                    return true;
                }

                if (string.Equals(candidateType, existing.Type, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(candidateAnswer) &&
                    string.Equals(candidateAnswer, NormalizeQuestionComparisonKey(existing.CorrectAnswer), StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeQuestionComparisonKey(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return Regex.Replace(text.Trim().ToLowerInvariant(), @"\s+", " ");
        }

        private static List<string> BuildQuestionTypePlan(int trueFalseCount, int mcqCount)
        {
            var remainingTrueFalse = Math.Max(0, trueFalseCount);
            var remainingMultipleChoice = Math.Max(0, mcqCount);
            var result = new List<string>(remainingTrueFalse + remainingMultipleChoice);
            var preferTrueFalse = remainingTrueFalse >= remainingMultipleChoice;

            while (remainingTrueFalse > 0 || remainingMultipleChoice > 0)
            {
                if ((preferTrueFalse && remainingTrueFalse > 0) || remainingMultipleChoice == 0)
                {
                    result.Add("TrueFalse");
                    remainingTrueFalse--;
                }
                else if (remainingMultipleChoice > 0)
                {
                    result.Add("MultipleChoice");
                    remainingMultipleChoice--;
                }

                preferTrueFalse = !preferTrueFalse;
            }

            return result;
        }

        private async Task<SmiQuestionBuildResult> TryBuildQuestionsWithSmiAsync(
            List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem> items,
            Qualification qualification,
            Subject subject,
            int trueFalseCount,
            int mcqCount,
            int mcqDistractors,
            CancellationToken cancellationToken)
        {
            var empty = new SmiQuestionBuildResult();
            if (!IsSmiIntegrationEnabled()) return empty;
            if (items.Count == 0) return empty;
            if ((trueFalseCount + mcqCount) <= 0) return empty;

            var maxSeedItems = Math.Min(2, items.Count);
            var seedItems = items
                .Take(maxSeedItems)
                .ToList();

            var tfSeedTarget = trueFalseCount > 0 ? 1 : 0;
            var mcqSeedTarget = mcqCount > 0 ? 1 : 0;

            var tfSeeds = new List<AssessmentDrivenQuestionGenerator.GeneratedQuestion>();
            var mcqSeeds = new List<AssessmentDrivenQuestionGenerator.GeneratedQuestion>();
            var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedDeterministicFallback = !await IsSmiServiceResponsiveAsync(cancellationToken);
            var skipRemoteGeneration = usedDeterministicFallback;

            foreach (var item in seedItems)
            {
                if (cancellationToken.IsCancellationRequested) break;

                if (!skipRemoteGeneration && trueFalseCount > 0)
                {
                    var tf = await TryGenerateSingleSmiQuestionAsync(
                        item,
                        qualification,
                        subject,
                        questionType: "TrueFalse",
                        optionCount: TrueFalseOptionCount,
                        marks: 1,
                        cancellationToken,
                        resources,
                        timeoutOverrideSeconds: SmiQuestionTimeoutSeconds);
                    if (tf != null) tfSeeds.Add(tf);
                    else
                    {
                        usedDeterministicFallback = true;
                        skipRemoteGeneration = true;
                    }
                }

                if (!skipRemoteGeneration && mcqCount > 0)
                {
                    var mcq = await TryGenerateSingleSmiQuestionAsync(
                        item,
                        qualification,
                        subject,
                        questionType: "MultipleChoice",
                        optionCount: DefaultMcqDistractors + 1,
                        marks: 1,
                        cancellationToken,
                        resources,
                        timeoutOverrideSeconds: SmiQuestionTimeoutSeconds);
                    if (mcq != null) mcqSeeds.Add(mcq);
                    else
                    {
                        usedDeterministicFallback = true;
                        skipRemoteGeneration = true;
                    }
                }

                if (skipRemoteGeneration) break;

                if ((tfSeedTarget == 0 || tfSeeds.Count >= tfSeedTarget)
                    && (mcqSeedTarget == 0 || mcqSeeds.Count >= mcqSeedTarget))
                {
                    break;
                }
            }

            if (trueFalseCount > 0 && tfSeeds.Count == 0)
            {
                usedDeterministicFallback = true;
                var fallbackNumber = 1;
                foreach (var item in seedItems)
                {
                    tfSeeds.Add(BuildTrueFalseSingleCorrectQuestion(item, fallbackNumber++));
                }
            }

            if (mcqCount > 0 && mcqSeeds.Count == 0)
            {
                usedDeterministicFallback = true;
                var fallbackNumber = 1;
                foreach (var item in seedItems)
                {
                    mcqSeeds.Add(BuildMultipleChoiceSubjectQuestion(item, fallbackNumber++, mcqDistractors));
                }
            }

            if (trueFalseCount > 0 && tfSeeds.Count == 0) return empty;
            if (mcqCount > 0 && mcqSeeds.Count == 0) return empty;

            var built = new List<AssessmentDrivenQuestionGenerator.GeneratedQuestion>();
            var qn = 1;
            for (var i = 0; i < trueFalseCount; i++)
            {
                built.Add(CloneQuestion(tfSeeds[i % tfSeeds.Count], qn++));
            }

            for (var i = 0; i < mcqCount; i++)
            {
                built.Add(CloneQuestion(mcqSeeds[i % mcqSeeds.Count], qn++));
            }

            return new SmiQuestionBuildResult
            {
                Questions = built,
                ResourceSuggestions = resources.Take(25).ToList(),
                UsedDeterministicFallback = usedDeterministicFallback
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
            HashSet<string> resources,
            int variantIndex = 0,
            int variantCount = 1,
            IReadOnlyCollection<AssessmentDrivenQuestionGenerator.GeneratedQuestion>? existingQuestions = null,
            int? timeoutOverrideSeconds = null)
        {
            var prompt = BuildSmiQuestionPrompt(item, qualification, subject, questionType, optionCount, marks, variantIndex, variantCount, existingQuestions);
            var answer = await TryFetchSmiAnswerAsync(
                prompt,
                qualification.QualificationNumber ?? string.Empty,
                qualification.QualificationDescription ?? string.Empty,
                mode: "questionnaire",
                cancellationToken,
                timeoutOverrideSeconds);
            if (answer == null || string.IsNullOrWhiteSpace(answer.Answer)) return null;

            foreach (var citation in answer.Citations)
            {
                if (!string.IsNullOrWhiteSpace(citation))
                {
                    resources.Add(citation.Trim());
                }
            }

            var parsed = TryParseSmiQuestionEnvelope(answer.Answer);
            if (parsed == null)
            {
                parsed = await TryRepairSmiQuestionEnvelopeAsync(
                    rawAnswer: answer.Answer,
                    qualificationCode: qualification.QualificationNumber ?? string.Empty,
                    qualificationDescription: qualification.QualificationDescription ?? string.Empty,
                    questionType: questionType,
                    optionCount: optionCount,
                    cancellationToken: cancellationToken);
            }
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

        private async Task<SmiGeneratedQuestionEnvelope?> TryRepairSmiQuestionEnvelopeAsync(
            string rawAnswer,
            string qualificationCode,
            string qualificationDescription,
            string questionType,
            int optionCount,
            CancellationToken cancellationToken)
        {
            var source = (rawAnswer ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(source)) return null;

            var isTrueFalse = string.Equals(questionType, "TrueFalse", StringComparison.OrdinalIgnoreCase);
            var snippet = source.Length <= 2200 ? source : source[..2200];
            var sb = new StringBuilder();
            sb.AppendLine("Convert the INPUT into valid JSON only.");
            sb.AppendLine("No markdown. No code fences. No extra text.");
            sb.AppendLine("Return exactly this schema:");
            sb.AppendLine("{");
            sb.AppendLine("  \"prompt\": \"string\",");
            sb.AppendLine("  \"options\": [\"string\"],");
            sb.AppendLine("  \"correctAnswer\": \"string\",");
            sb.AppendLine("  \"rationale\": \"string\",");
            sb.AppendLine("  \"resourceHints\": []");
            sb.AppendLine("}");
            sb.AppendLine($"QuestionType: {questionType}");
            sb.AppendLine($"OptionCount: {optionCount}");
            sb.AppendLine("Rules:");
            sb.AppendLine("- Use English only.");
            sb.AppendLine("- Keep exactly OptionCount options.");
            sb.AppendLine("- Keep one correct answer.");
            if (isTrueFalse)
            {
                sb.AppendLine("- correctAnswer format: \"A=True; B=False; C=False; D=False\".");
            }
            else
            {
                sb.AppendLine("- correctAnswer format: option letter only, for example \"B\".");
            }
            sb.AppendLine();
            sb.AppendLine("INPUT:");
            sb.AppendLine(snippet);

            var repaired = await TryFetchSmiAnswerAsync(
                sb.ToString(),
                qualificationCode,
                qualificationDescription,
                mode: "questionnaire",
                cancellationToken,
                timeoutOverrideSeconds: SmiRepairTimeoutSeconds);
            if (repaired == null || string.IsNullOrWhiteSpace(repaired.Answer)) return null;
            return TryParseSmiQuestionEnvelope(repaired.Answer);
        }

        private static async Task<bool> IsSmiServiceResponsiveAsync(CancellationToken cancellationToken)
        {
            if (!IsSmiIntegrationEnabled()) return false;

            var baseUrl = GetSmiBaseUrl();
            if (string.IsNullOrWhiteSpace(baseUrl)) return false;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/health");
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(SmiHealthProbeTimeoutSeconds));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
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
                .Select(NormalizeOptionLabelPrefix)
                .Select(o => AssessmentDrivenQuestionGenerator.NormalizeQuestionStatement(o))
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!IsLikelyEnglishText(prompt) || HasNoiseArtifacts(prompt)) return null;
            if (options.Any(o => !IsLikelyEnglishText(o) || HasNoiseArtifacts(o))) return null;

            if (options.Count > optionCount)
            {
                options = options.Take(optionCount).ToList();
            }
            if (options.Count != optionCount) return null;
            if (options.Any(AssessmentDrivenQuestionGenerator.ContainsQuestionAdministrativeReference)) return null;
            if (options.Any(o => o.Contains("all of the above", StringComparison.OrdinalIgnoreCase))) return null;
            if (options.Any(o => o.Contains("none of the above", StringComparison.OrdinalIgnoreCase))) return null;

            var rationale = AssessmentDrivenQuestionGenerator.SanitizeQuestionText(parsed.Rationale);
            if (string.IsNullOrWhiteSpace(rationale))
            {
                rationale = "Generated by SMI using lesson-plan content and qualification context.";
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
                if (correctIndex < 0)
                {
                    var normalizedCorrect = NormalizeOptionLabelPrefix(
                        AssessmentDrivenQuestionGenerator.SanitizeQuestionText(parsed.CorrectAnswer));
                    if (!string.IsNullOrWhiteSpace(normalizedCorrect))
                    {
                        correctIndex = options.FindIndex(o => string.Equals(
                            o,
                            AssessmentDrivenQuestionGenerator.NormalizeQuestionStatement(normalizedCorrect),
                            StringComparison.OrdinalIgnoreCase));
                    }
                }
                if (correctIndex < 0 || correctIndex >= optionCount) return null;
                correctAnswer = $"{OptionLabel(correctIndex)}. {options[correctIndex]}";
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

        private static string BuildSmiQuestionPrompt(
            AssessmentDrivenQuestionGenerator.LessonEvidenceItem item,
            Qualification qualification,
            Subject subject,
            string questionType,
            int optionCount,
            int marks,
            int variantIndex,
            int variantCount,
            IReadOnlyCollection<AssessmentDrivenQuestionGenerator.GeneratedQuestion>? existingQuestions)
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
            sb.AppendLine($"VariantNumber: {Math.Max(1, variantIndex + 1)}");
            sb.AppendLine($"TotalVariantsRequiredForThisCriterion: {Math.Max(1, variantCount)}");
            sb.AppendLine("Rules:");
            sb.AppendLine("- Use English only.");
            sb.AppendLine("- Use clean plain text (no garbled or corrupted characters).");
            sb.AppendLine("- Do not wrap the JSON in markdown code fences.");
            sb.AppendLine("- Apply logical reasoning using only the provided context.");
            sb.AppendLine("- Keep the stem self-contained and practical.");
            sb.AppendLine("- Do not include topic codes, AC numbers, LPN labels, or administrative metadata in the stem/options.");
            sb.AppendLine("- Keep options homogeneous and realistic.");
            sb.AppendLine("- Do not use 'All of the above' or 'None of the above'.");
            sb.AppendLine("- The question must test the stated learning requirement, not generic workshop trivia.");
            if (variantCount > 1)
            {
                sb.AppendLine("- This criterion requires multiple distinct questions.");
                sb.AppendLine("- This question must assess a different angle or fact pattern from the other question(s) for the same criterion.");
                sb.AppendLine("- Do not repeat or lightly paraphrase an earlier stem.");
                sb.AppendLine("- Do not reuse the same correct answer concept as an earlier question.");
            }
            if (isTrueFalse)
            {
                sb.AppendLine("- Build one stem and OptionCount statements.");
                sb.AppendLine("- Exactly one statement must be True.");
                sb.AppendLine("- correctAnswer format: \"A=True; B=False; C=False; D=False\".");
                sb.AppendLine("- Use the stem to frame a specific knowledge check, not a generic instruction only.");
            }
            else
            {
                sb.AppendLine("- Build one stem and OptionCount options.");
                sb.AppendLine("- Exactly one option is correct.");
                sb.AppendLine("- correctAnswer format: option letter only, for example \"B\".");
                sb.AppendLine("- Use a stem that asks for the best description, explanation, identification, or interpretation from the lesson content.");
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
            sb.AppendLine($"LessonContent: {CleanPromptField(TrimForPrompt(item.LessonPlanContent, 1000))}");
            sb.AppendLine($"EvidenceText: {CleanPromptField(TrimForPrompt(item.EvidenceText, 1000))}");
            if (existingQuestions != null && existingQuestions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Avoid repeating these existing question stems or answer concepts for this same criterion:");
                foreach (var row in existingQuestions.Take(4))
                {
                    var priorPrompt = CleanPromptField(TrimForPrompt(row.Prompt, 240));
                    var priorAnswer = CleanPromptField(TrimForPrompt(row.CorrectAnswer, 160));
                    if (!string.IsNullOrWhiteSpace(priorPrompt))
                    {
                        sb.AppendLine($"- ExistingPrompt: {priorPrompt}");
                    }
                    if (!string.IsNullOrWhiteSpace(priorAnswer))
                    {
                        sb.AppendLine($"- ExistingCorrectAnswer: {priorAnswer}");
                    }
                }
            }
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

        private static string NormalizeOptionLabelPrefix(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            text = Regex.Replace(text, @"^\s*[A-H]\s*[\)\.\:\-]\s*", string.Empty, RegexOptions.CultureInvariant);
            text = Regex.Replace(text, @"^\s*(Option|Choice)\s*[A-H]\s*[\)\.\:\-]?\s*", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return text.Trim();
        }

        private static string ReadJsonOptionText(JsonElement option)
        {
            if (option.ValueKind == JsonValueKind.String)
            {
                return (option.GetString() ?? string.Empty).Trim();
            }

            if (option.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "text", "option", "value", "statement", "labelText", "content" })
                {
                    var value = ReadJsonString(option, key);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
            }

            return string.Empty;
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
                        var text = ReadJsonOptionText(option);
                        if (!string.IsNullOrWhiteSpace(text)) options.Add(text);
                    }
                }
                if (options.Count == 0 && root.TryGetProperty("choices", out var choicesEl) && choicesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var option in choicesEl.EnumerateArray())
                    {
                        var text = ReadJsonOptionText(option);
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
            CancellationToken cancellationToken,
            int? timeoutOverrideSeconds = null)
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
                var timeoutSeconds = timeoutOverrideSeconds.HasValue
                    ? Math.Clamp(timeoutOverrideSeconds.Value, 1, 60)
                    : GetSmiTimeoutSeconds();
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
                return Math.Clamp(parsed, 0, 60);
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

        private async Task<List<AssessmentDrivenQuestionGenerator.GeneratedQuestion>> TryBuildQuestionsWithSemanticKernelAsync(
            List<AssessmentDrivenQuestionGenerator.LessonEvidenceItem> items,
            int trueFalseCount,
            int mcqCount,
            int mcqDistractors,
            CancellationToken cancellationToken)
        {
            var result = new List<AssessmentDrivenQuestionGenerator.GeneratedQuestion>();
            if (!_semanticKernelQuestionService.IsAvailable()) return result;
            if (items.Count == 0) return result;

            var tfSeeds = new List<AssessmentDrivenQuestionGenerator.GeneratedQuestion>();
            var mcqSeeds = new List<AssessmentDrivenQuestionGenerator.GeneratedQuestion>();
            var seedNumber = 1;

            for (var i = 0; i < items.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var item = items[i];

                var tf = await _semanticKernelQuestionService.GenerateTrueFalseQuestionAsync(
                    item,
                    seedNumber++,
                    marks: 1,
                    optionCount: TrueFalseOptionCount,
                    cancellationToken);

                if (tf != null) tfSeeds.Add(tf);

                var mcq = await _semanticKernelQuestionService.GenerateMultipleChoiceQuestionAsync(
                    item,
                    seedNumber++,
                    marks: 1,
                    distractorCount: mcqDistractors,
                    cancellationToken);

                if (mcq != null) mcqSeeds.Add(mcq);
            }

            if (tfSeeds.Count == 0 || mcqSeeds.Count == 0)
            {
                return new List<AssessmentDrivenQuestionGenerator.GeneratedQuestion>();
            }

            var qn = 1;
            for (var i = 0; i < trueFalseCount; i++)
            {
                result.Add(CloneQuestion(tfSeeds[i % tfSeeds.Count], qn++));
            }

            for (var i = 0; i < mcqCount; i++)
            {
                result.Add(CloneQuestion(mcqSeeds[i % mcqSeeds.Count], qn++));
            }

            return result;
        }

        private static List<AssessmentDrivenQuestionGenerator.GeneratedQuestion> ExpandAndRenumber(
            List<AssessmentDrivenQuestionGenerator.GeneratedQuestion> source,
            int targetCount)
        {
            if (source.Count == 0 || targetCount <= 0) return new List<AssessmentDrivenQuestionGenerator.GeneratedQuestion>();

            var result = new List<AssessmentDrivenQuestionGenerator.GeneratedQuestion>(targetCount);
            for (var i = 0; i < targetCount; i++)
            {
                var seed = source[i % source.Count];
                result.Add(CloneQuestion(seed, i + 1));
            }
            return result;
        }

        private static AssessmentDrivenQuestionGenerator.GeneratedQuestion CloneQuestion(
            AssessmentDrivenQuestionGenerator.GeneratedQuestion source,
            int questionNumber)
        {
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

        private static AssessmentDrivenQuestionGenerator.GeneratedQuestion BuildTrueFalseSingleCorrectQuestion(
            AssessmentDrivenQuestionGenerator.LessonEvidenceItem item,
            int number,
            int variantIndex = 0)
        {
            _ = variantIndex;
            return AssessmentDrivenQuestionGenerator.BuildTrueFalseQuestion(item, number, 1);
        }

        private static AssessmentDrivenQuestionGenerator.GeneratedQuestion BuildMultipleChoiceSubjectQuestion(
            AssessmentDrivenQuestionGenerator.LessonEvidenceItem item,
            int number,
            int mcqDistractors,
            int variantIndex = 0)
        {
            _ = mcqDistractors;
            _ = variantIndex;
            return AssessmentDrivenQuestionGenerator.BuildMultipleChoiceQuestion(item, number, 1);
        }

        private static (string Correct, List<string> Wrong) ExtractCorrectAndWrongOptions(AssessmentDrivenQuestionGenerator.GeneratedQuestion seed)
        {
            if (seed.Options == null || seed.Options.Count == 0)
            {
                return ("The learner follows safe procedure and verifies quality.", new List<string>
                {
                    "Safety checks may be skipped when the task appears routine.",
                    "Precision can be estimated by eye when production pressure is high.",
                    "Documenting defects is unnecessary if the output appears usable."
                });
            }

            var correctIndex = LabelToIndex(seed.CorrectAnswer);
            if (correctIndex < 0 || correctIndex >= seed.Options.Count) correctIndex = 0;
            var correct = seed.Options[correctIndex];
            var wrong = new List<string>();
            for (var i = 0; i < seed.Options.Count; i++)
            {
                if (i == correctIndex) continue;
                AddUniqueOption(wrong, seed.Options[i]);
            }
            return (correct, wrong);
        }

        private static void AddUniqueOption(List<string> list, string value)
        {
            var cleaned = NormalizeQuestionOption(value);
            if (string.IsNullOrWhiteSpace(cleaned)) return;
            if (list.Any(x => string.Equals(x, cleaned, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(cleaned);
        }

        private static string BuildFallbackDistractor(AssessmentDrivenQuestionGenerator.LessonEvidenceItem item, int index)
        {
            return index switch
            {
                0 => "Safety checks may be skipped when a task appears routine.",
                1 => "Precision can be estimated by eye when production pressure is high.",
                2 => "Documenting faults is optional if the final output appears usable.",
                3 => "Protective equipment is optional for short tasks.",
                4 => "A quick workaround is acceptable even when procedure requires verification.",
                5 => "Peer approval alone is enough evidence that the work meets standard.",
                6 => "Quality checks can be completed after handover if time is limited.",
                _ => "The task can be signed off without recording how the result was achieved."
            };
        }

        private static string NormalizeQuestionOption(string? value, string? fallback = null)
        {
            var cleaned = AssessmentDrivenQuestionGenerator.SanitizeQuestionText(value);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = AssessmentDrivenQuestionGenerator.SanitizeQuestionText(fallback);
            }
            if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
            return AssessmentDrivenQuestionGenerator.NormalizeQuestionStatement(cleaned, cleaned);
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

        private static SectionProperties DefaultSectionProperties()
        {
            return new SectionProperties(
                new PageSize() { Orient = PageOrientationValues.Portrait, Width = PortraitA4PageWidthTwips, Height = PortraitA4PageHeightTwips },
                new PageMargin()
                {
                    Top = 1020,
                    Bottom = 1020,
                    Left = 1020,
                    Right = 1020,
                    Header = 680,
                    Footer = 680,
                    Gutter = 0
                });
        }

        private static TableProperties DefaultTableProperties()
        {
            return new TableProperties(
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

        private static Paragraph QuestionMetaParagraph(string text, int sizeHalfPt, bool bold = false)
        {
            var runProps = new RunProperties
            {
                FontSize = new FontSize() { Val = CompactBodyHalfPt(sizeHalfPt).ToString() },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };
            if (bold) runProps.Bold = new Bold();

            var paraProps = new ParagraphProperties(
                new SpacingBetweenLines() { Before = "80", After = "40", Line = "240", LineRule = LineSpacingRuleValues.Auto });

            return new Paragraph(
                paraProps,
                new Run(runProps, new Text(SanitizeXmlText(text ?? string.Empty))
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
        }

        private static Paragraph BuildPlainQuestionPromptParagraph(PhaseQuestionnaireDocxQuestionRow question)
        {
            var runProps = new RunProperties
            {
                Bold = new Bold(),
                FontSize = new FontSize() { Val = CompactBodyHalfPt(22).ToString() },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };

            var paraProps = new ParagraphProperties(
                new SpacingBetweenLines() { Before = "60", After = "20", Line = "280", LineRule = LineSpacingRuleValues.Auto },
                new Indentation() { Left = "0", Hanging = "360" });

            var marks = Math.Max(1, question?.Marks ?? 1);
            var prompt = $"{Math.Max(1, question?.Number ?? 1)}. {(question?.Prompt ?? string.Empty).Trim()} ({marks} mark{(marks == 1 ? string.Empty : "s")})";
            return new Paragraph(
                paraProps,
                new Run(runProps, new Text(SanitizeXmlText(prompt))
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
        }

        private static Paragraph BuildPlainQuestionOptionParagraph(string label, string text, bool includeTrueFalseTicks)
        {
            var suffix = includeTrueFalseTicks ? "    True [ ]    False [ ]" : string.Empty;
            var line = $"{label}. {text}{suffix}";
            var runProps = new RunProperties
            {
                FontSize = new FontSize() { Val = CompactBodyHalfPt(20).ToString() },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };
            var paraProps = new ParagraphProperties(
                new SpacingBetweenLines() { Before = "0", After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto },
                new Indentation() { Left = "420", Hanging = "240" });

            return new Paragraph(
                paraProps,
                new Run(runProps, new Text(SanitizeXmlText(line))
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
        }

        private static Paragraph BuildMarkerLineParagraph()
        {
            var runProps = new RunProperties
            {
                FontSize = new FontSize() { Val = CompactBodyHalfPt(18).ToString() },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };
            var paraProps = new ParagraphProperties(
                new SpacingBetweenLines() { Before = "20", After = "60", Line = "220", LineRule = LineSpacingRuleValues.Auto },
                new Indentation() { Left = "420" });

            return new Paragraph(
                paraProps,
                new Run(runProps, new Text("Ass.: ____________    Mod.: ____________")
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
        }

        private static Paragraph BulletPara(string text, int sizeHalfPt)
        {
            var rPr = new RunProperties
            {
                FontSize = new FontSize() { Val = CompactBodyHalfPt(sizeHalfPt).ToString() },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };

            var pPr = new ParagraphProperties(
                new SpacingBetweenLines() { Line = "240", LineRule = LineSpacingRuleValues.Auto },
                new Indentation() { Left = "560", Hanging = "220" });

            return new Paragraph(pPr, new Run(rPr, new Text(SanitizeXmlText($"- {text ?? string.Empty}"))));
        }

        private static void AppendTableOfContentsPage(Body body)
        {
            body.Append(StyledHeading("TABLE OF CONTENTS", "Heading1", 22));
            body.Append(BodyPara("If the table is blank, open this file in Microsoft Word and choose Update Field.", 22));
            body.Append(BuildTableOfContentsField());
        }

        private static string BuildPhaseScopeLabel(string? phaseName, string? phaseDescription)
        {
            var parts = new[]
            {
                (phaseName ?? string.Empty).Trim(),
                (phaseDescription ?? string.Empty).Trim()
            }.Where(part => !string.IsNullOrWhiteSpace(part));

            return string.Join(" / ", parts);
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
            body.Append(BodyPara($"(C) {year} by Dr P.C. Wepener, supported by the professional assistance of OpenAI (CODEX).", 22));
            body.Append(BodyPara("Neither Dr P.C. Wepener nor OpenAI is accountable or liable for the correctness, completeness, factual, or academic correctness of this document. This document is generated by the ETDP App. The accredited learning institution should be contacted for content inquiries, sources, references, or citations.", 22));

            body.Append(HeadingPara("NOTICE OF RIGHTS", 14));
            body.Append(BodyPara("No part of this publication may be reproduced, transmitted, transcribed, stored in a retrieval system, or translated into any language or computer language, in any form or by any means, electronic, mechanical, magnetic, optical, chemical, manual, or otherwise, without prior written permission from the branded learning institution that owns the legal and intellectual property rights to the content of this document.", 22));

            body.Append(HeadingPara("TRADEMARK NOTICE", 14));
            body.Append(BodyPara("Throughout this courseware title, trademark names may be used. Rather than placing a trademark symbol at every occurrence, names are used in an editorial manner for the benefit of the trademark owner, with no intention of infringement.", 22));

            body.Append(HeadingPara("NOTICE OF LIABILITY", 14));
            body.Append(BodyPara("The information in this courseware title is distributed on an 'as is' basis, without warranty. While every precaution has been taken in preparation of this courseware, neither Dr P.C. Wepener nor OpenAI shall have any liability to any person or entity for any loss or damage caused, or alleged to be caused, directly or indirectly by the instructions in this document or by the learning design and development processes described in it.", 22));

            body.Append(HeadingPara("DISCLAIMER", 14));
            body.Append(BodyPara("A sincere effort has been made to ensure typology accuracy of the material; however, no warranty, express or implied, is made regarding quality, correctness, reliability, accuracy, or freedom from error of this document or the products it describes. Data used in examples and sample files may be fictional. Any resemblance to real persons or companies is coincidental.", 22));

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
            var coverPath = ResolveKnowledgeQuestionnaireCoverPath(documentTitle);
            var qualificationLine = BuildCoverQualificationLine(qualification);
            var institutionLine = (qualification.LearningInstitutionName ?? "LEARNING INSTITUTION").Trim();
            var appended = DocxCoverPageOverlay.TryAppendStandardPortraitCoverPage(
                body,
                main,
                coverPath,
                qualificationLine,
                subjectLine,
                institutionLine,
                PortraitCoverUsableWidthTwips,
                2001U);

            if (appended) return;

            body.Append(CenterPara(documentTitle, 34, true));
            if (!string.IsNullOrWhiteSpace(institutionLine))
            {
                body.Append(CenterPara(institutionLine, 24, true));
            }
            if (!string.IsNullOrWhiteSpace(qualificationLine))
            {
                body.Append(CenterPara(qualificationLine, 20, false));
            }
            if (!string.IsNullOrWhiteSpace(subjectLine))
            {
                body.Append(CenterPara(subjectLine, 18, false));
            }
        }

        private static Table BuildPhaseQuestionTable(PhaseQuestionnaireDocxQuestionRow? question)
        {
            var isTrueFalse = string.Equals(question?.Type, "TrueFalse", StringComparison.OrdinalIgnoreCase);
            var gridWidths = new[] { "939", "5965", "833", "693", "694", "742" };
            var promptSpanWidth = "7491";
            var choiceSpanWidth = "1526";
            var table = new Table();
            table.Append(BuildPhaseQuestionTableProperties());
            table.Append(new TableGrid(
                new GridColumn() { Width = gridWidths[0] },
                new GridColumn() { Width = gridWidths[1] },
                new GridColumn() { Width = gridWidths[2] },
                new GridColumn() { Width = gridWidths[3] },
                new GridColumn() { Width = gridWidths[4] },
                new GridColumn() { Width = gridWidths[5] }));

            var marks = Math.Max(1, question?.Marks ?? 1);
            var options = (question?.Options ?? new List<string>())
                .Select(option => (option ?? string.Empty).Trim())
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .ToList();

            if (options.Count == 0)
            {
                var fallbackStatement = isTrueFalse
                    ? NormalizeTrueFalseStatement(question?.Prompt)
                    : (question?.Prompt ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(fallbackStatement))
                {
                    options.Add(fallbackStatement);
                }
            }

            table.Append(new TableRow(
                new TableRowProperties(new TableRowHeight { Val = 624U, HeightType = HeightRuleValues.AtLeast }),
                SampleQuestionCell(gridWidths[0], $"Q{Math.Max(1, question?.Number ?? 1)}", bold: true, center: true, fill: "F2F2F2", fontSizeHalfPt: "22"),
                SampleQuestionCell(
                    promptSpanWidth,
                    new OpenXmlElement[] { BuildSampleQuestionPromptParagraph(question, marks) },
                    fill: "F2F2F2",
                    gridSpan: 3),
                SampleQuestionCell(gridWidths[4], "Assessor Decision", bold: true, center: true, verticalText: true, verticalMerge: "restart", fontSizeHalfPt: "18"),
                SampleQuestionCell(gridWidths[5], "Moderator Decision", bold: true, center: true, verticalText: true, verticalMerge: "restart", fontSizeHalfPt: "16")));

            table.Append(new TableRow(
                SampleQuestionCell(gridWidths[0], "Option", bold: true, center: true, fill: "F2F2F2", fontSizeHalfPt: "20"),
                SampleQuestionCell(gridWidths[1], "Statement", bold: true, fill: "F2F2F2", fontSizeHalfPt: "20"),
                SampleQuestionCell(choiceSpanWidth, "ENCIRCLE YOUR CHOISE", bold: true, center: true, fill: "F2F2F2", gridSpan: 2, fontSizeHalfPt: "16"),
                SampleQuestionCell(gridWidths[4], string.Empty, fill: "F2F2F2", verticalMerge: "continue"),
                SampleQuestionCell(gridWidths[5], string.Empty, fill: "F2F2F2", verticalMerge: "continue")));

            for (var i = 0; i < options.Count; i++)
            {
                if (isTrueFalse)
                {
                    table.Append(new TableRow(
                        new TableRowProperties(new TableRowHeight { Val = 520U, HeightType = HeightRuleValues.AtLeast }),
                        SampleQuestionCell(gridWidths[0], OptionLabel(i), center: true, fontSizeHalfPt: "20"),
                        SampleQuestionCell(gridWidths[1], options[i], fontSizeHalfPt: "20"),
                        SampleQuestionCell(gridWidths[2], "T", center: true, fontSizeHalfPt: "20"),
                        SampleQuestionCell(gridWidths[3], "F", center: true, fontSizeHalfPt: "20"),
                        SampleQuestionCell(gridWidths[4], string.Empty, center: true, fontSizeHalfPt: "20"),
                        SampleQuestionCell(gridWidths[5], string.Empty, center: true, fontSizeHalfPt: "20")));
                }
                else
                {
                    table.Append(new TableRow(
                        new TableRowProperties(new TableRowHeight { Val = 520U, HeightType = HeightRuleValues.AtLeast }),
                        SampleQuestionCell(gridWidths[0], OptionLabel(i), center: true, fontSizeHalfPt: "20"),
                        SampleQuestionCell(gridWidths[1], options[i], fontSizeHalfPt: "20"),
                        SampleQuestionCell(choiceSpanWidth, "[   ]", center: true, gridSpan: 2, fontSizeHalfPt: "22"),
                        SampleQuestionCell(gridWidths[4], string.Empty, center: true, fontSizeHalfPt: "20"),
                        SampleQuestionCell(gridWidths[5], string.Empty, center: true, fontSizeHalfPt: "20")));
                }
            }

            return table;
        }

        private static Paragraph BuildSampleQuestionPromptParagraph(PhaseQuestionnaireDocxQuestionRow? question, int marks)
        {
            var prompt = (question?.Prompt ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                prompt = string.Equals(question?.Type, "TrueFalse", StringComparison.OrdinalIgnoreCase)
                    ? "Indicate which of the following statements are True or False? There is only 1 correct answer."
                    : "Choose the correct answer from the options below.";
            }

            var fontSize = "22";
            var marksSize = "18";
            var runProps = new RunProperties
            {
                Bold = new Bold(),
                FontSize = new FontSize() { Val = fontSize },
                FontSizeComplexScript = new FontSizeComplexScript() { Val = fontSize },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };
            var marksRunProps = new RunProperties
            {
                Bold = new Bold(),
                FontSize = new FontSize() { Val = marksSize },
                FontSizeComplexScript = new FontSizeComplexScript() { Val = marksSize },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };

            return new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines() { Line = "240", LineRule = LineSpacingRuleValues.Auto }),
                new Run((RunProperties)runProps.CloneNode(true), new Text(SanitizeXmlText(prompt)) { Space = SpaceProcessingModeValues.Preserve }),
                new Run((RunProperties)marksRunProps.CloneNode(true), new Text($" [MARKS POSSIBLE {Math.Max(1, marks)}]") { Space = SpaceProcessingModeValues.Preserve }));
        }

        private static TableCell SampleQuestionCell(
            string width,
            string text,
            bool bold = false,
            bool center = false,
            string fill = "FFFFFF",
            int gridSpan = 1,
            string fontSizeHalfPt = CompactTableCellHalfPt,
            bool verticalText = false,
            string verticalMerge = "")
        {
            return SampleQuestionCell(
                width,
                new OpenXmlElement[] { BuildQuestionBlockParagraph(text, fontSizeHalfPt, bold: bold, center: center) },
                fill,
                gridSpan,
                verticalText,
                verticalMerge);
        }

        private static TableCell SampleQuestionCell(
            string width,
            IEnumerable<OpenXmlElement> content,
            string fill = "FFFFFF",
            int gridSpan = 1,
            bool verticalText = false,
            string verticalMerge = "")
        {
            var props = new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = width },
                BuildVisibleTableCellBorders(),
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fill },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

            if (gridSpan > 1)
            {
                props.Append(new GridSpan { Val = gridSpan });
            }

            if (string.Equals(verticalMerge, "restart", StringComparison.OrdinalIgnoreCase))
            {
                props.Append(new VerticalMerge { Val = MergedCellValues.Restart });
            }
            else if (string.Equals(verticalMerge, "continue", StringComparison.OrdinalIgnoreCase))
            {
                props.Append(new VerticalMerge());
            }

            if (verticalText)
            {
                props.Append(new TextDirection { Val = TextDirectionValues.BottomToTopLeftToRight });
            }

            var cell = new TableCell(props);
            foreach (var item in content)
            {
                cell.Append((OpenXmlElement)item.CloneNode(true));
            }

            return cell;
        }

        private static TableProperties BuildPhaseQuestionTableProperties()
        {
            return new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Dxa, Width = QuestionnaireUsableWidthTwips },
                new TableLayout { Type = TableLayoutValues.Fixed },
                BuildVisibleTableBorders(),
                new TableCellMarginDefault(
                    new TopMargin { Width = "70", Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "70", Type = TableWidthUnitValues.Dxa },
                    new TableCellLeftMargin { Width = 100, Type = TableWidthValues.Dxa },
                    new TableCellRightMargin { Width = 100, Type = TableWidthValues.Dxa }));
        }

        private static Table BuildPhaseQuestionResponseTable(PhaseQuestionnaireDocxQuestionRow? question)
        {
            var isTrueFalse = string.Equals(question?.Type, "TrueFalse", StringComparison.OrdinalIgnoreCase);
            return isTrueFalse
                ? BuildTrueFalseResponseTable(question)
                : BuildMultipleChoiceResponseTable(question);
        }

        private static Table BuildTrueFalseResponseTable(PhaseQuestionnaireDocxQuestionRow? question)
        {
            var statement = NormalizeTrueFalseStatement(question?.Prompt);
            var table = new Table();
            table.Append(BuildNestedQuestionTableProperties());
            table.Append(new TableGrid(
                new GridColumn() { Width = "6766" },
                new GridColumn() { Width = "1550" },
                new GridColumn() { Width = "1550" }));

            table.Append(new TableRow(
                QuestionBlockCell("6766", "Statement", bold: true, fill: "F2F2F2", fontSizeHalfPt: "20"),
                QuestionBlockCell("1550", "True", bold: true, center: true, fill: "F2F2F2", fontSizeHalfPt: "20"),
                QuestionBlockCell("1550", "False", bold: true, center: true, fill: "F2F2F2", fontSizeHalfPt: "20")));

            table.Append(new TableRow(
                new TableRowProperties(new TableRowHeight { Val = 620U, HeightType = HeightRuleValues.AtLeast }),
                QuestionBlockCell("6766", statement, fontSizeHalfPt: "20"),
                QuestionBlockCell("1550", "[   ]", center: true, fontSizeHalfPt: "22"),
                QuestionBlockCell("1550", "[   ]", center: true, fontSizeHalfPt: "22")));

            return table;
        }

        private static Table BuildMultipleChoiceResponseTable(PhaseQuestionnaireDocxQuestionRow? question)
        {
            var table = new Table();
            table.Append(BuildNestedQuestionTableProperties());
            table.Append(new TableGrid(
                new GridColumn() { Width = "900" },
                new GridColumn() { Width = "7366" },
                new GridColumn() { Width = "1600" }));

            table.Append(new TableRow(
                QuestionBlockCell("900", "Option", bold: true, center: true, fill: "F2F2F2", fontSizeHalfPt: "20"),
                QuestionBlockCell("7366", "Statement", bold: true, fill: "F2F2F2", fontSizeHalfPt: "20"),
                QuestionBlockCell("1600", "Learner Choice", bold: true, center: true, fill: "F2F2F2", fontSizeHalfPt: "20")));

            var options = (question?.Options ?? new List<string>())
                .Select(option => (option ?? string.Empty).Trim())
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .ToList();

            if (options.Count == 0)
            {
                table.Append(new TableRow(
                    QuestionBlockCell("900", "-", center: true),
                    QuestionBlockCell("7366", "No answer options were generated for this question."),
                    QuestionBlockCell("1600", string.Empty)));
                return table;
            }

            for (var i = 0; i < options.Count; i++)
            {
                table.Append(new TableRow(
                    new TableRowProperties(new TableRowHeight { Val = 520U, HeightType = HeightRuleValues.AtLeast }),
                    QuestionBlockCell("900", OptionLabel(i), center: true, fontSizeHalfPt: "20"),
                    QuestionBlockCell("7366", options[i], fontSizeHalfPt: "20"),
                    QuestionBlockCell("1600", "[   ]", center: true, fontSizeHalfPt: "22")));
            }

            return table;
        }

        private static TableProperties BuildNestedQuestionTableProperties()
        {
            return new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Dxa, Width = QuestionnaireUsableWidthTwips },
                new TableLayout { Type = TableLayoutValues.Fixed },
                BuildVisibleTableBorders(),
                new TableCellMarginDefault(
                    new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                    new TableCellLeftMargin { Width = 90, Type = TableWidthValues.Dxa },
                    new TableCellRightMargin { Width = 90, Type = TableWidthValues.Dxa }));
        }

        private static TableCell QuestionBlockCell(
            string width,
            string text,
            bool bold = false,
            bool center = false,
            string fill = "FFFFFF",
            int gridSpan = 1,
            string fontSizeHalfPt = CompactTableCellHalfPt)
        {
            return QuestionBlockCell(
                width,
                new OpenXmlElement[] { BuildQuestionBlockParagraph(text, fontSizeHalfPt, bold: bold, center: center) },
                fill,
                gridSpan);
        }

        private static TableCell QuestionBlockCell(
            string width,
            IEnumerable<OpenXmlElement> content,
            string fill = "FFFFFF",
            int gridSpan = 1)
        {
            var props = new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = width },
                BuildVisibleTableCellBorders(),
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fill },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });
            if (gridSpan > 1)
            {
                props.Append(new GridSpan { Val = gridSpan });
            }

            var cell = new TableCell(props);
            foreach (var item in content)
            {
                cell.Append((OpenXmlElement)item.CloneNode(true));
            }

            return cell;
        }

        private static Paragraph BuildQuestionBlockParagraph(
            string text,
            string fontSizeHalfPt,
            bool bold = false,
            bool center = false,
            bool italic = false,
            string before = "0",
            string after = "30")
        {
            var runProps = new RunProperties
            {
                FontSize = new FontSize() { Val = fontSizeHalfPt },
                FontSizeComplexScript = new FontSizeComplexScript() { Val = fontSizeHalfPt },
                RunFonts = new RunFonts() { Ascii = ExportFont, HighAnsi = ExportFont }
            };
            if (bold) runProps.Bold = new Bold();
            if (italic) runProps.Italic = new Italic();

            return new Paragraph(
                new ParagraphProperties(
                    new Justification() { Val = center ? JustificationValues.Center : JustificationValues.Left },
                    new SpacingBetweenLines() { Before = before, After = after, Line = "280", LineRule = LineSpacingRuleValues.Auto }),
                new Run(runProps, new Text(SanitizeXmlText(text ?? string.Empty))
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
        }

        private static string BuildQuestionBlockContextLine(PhaseQuestionnaireDocxQuestionRow? question)
        {
            var parts = new List<string>();

            var subjectLine = string.Join(" - ", new[]
            {
                (question?.SubjectCode ?? string.Empty).Trim(),
                (question?.SubjectDescription ?? string.Empty).Trim()
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
            if (!string.IsNullOrWhiteSpace(subjectLine))
            {
                parts.Add($"Subject: {subjectLine}");
            }

            var topicLine = string.Join(" - ", new[]
            {
                (question?.TopicCode ?? string.Empty).Trim(),
                (question?.TopicDescription ?? string.Empty).Trim()
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
            if (!string.IsNullOrWhiteSpace(topicLine))
            {
                parts.Add($"Topic: {topicLine}");
            }

            var criterionNumber = (question?.AssessmentCriteriaNumber ?? string.Empty).Trim();
            var criterionText = (question?.AssessmentCriteriaDescription ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(criterionNumber) || !string.IsNullOrWhiteSpace(criterionText))
            {
                var criterionLine = string.Join(" ", new[]
                {
                    !string.IsNullOrWhiteSpace(criterionNumber) ? criterionNumber + ":" : string.Empty,
                    criterionText
                }.Where(part => !string.IsNullOrWhiteSpace(part)));
                parts.Add($"Assessment Criterion: {criterionLine}");
            }

            return parts.Count == 0
                ? "Assessment Criterion: Refer to the aligned learning requirement for this question."
                : string.Join(" | ", parts);
        }

        private static string BuildQuestionBlockInstruction(PhaseQuestionnaireDocxQuestionRow? question)
        {
            return string.Equals(question?.Type, "TrueFalse", StringComparison.OrdinalIgnoreCase)
                ? "Instruction: Read the statement carefully and encircle only one answer in the True or False column."
                : "Instruction: Read the question carefully and make one clear mark in the Learner Choice column.";
        }

        private static string NormalizeTrueFalseStatement(string? prompt)
        {
            var cleaned = (prompt ?? string.Empty).Trim();
            if (cleaned.StartsWith("True or False:", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned["True or False:".Length..].Trim();
            }

            if (cleaned.StartsWith("True/False:", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned["True/False:".Length..].Trim();
            }

            return string.IsNullOrWhiteSpace(cleaned)
                ? "Read the statement and decide whether it is true or false."
                : cleaned;
        }

        private static string BuildCoverQualificationLine(Qualification qualification)
        {
            var qualificationNumber = (qualification.QualificationNumber ?? string.Empty).Trim();
            var qualificationDescription = (qualification.QualificationDescription ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(qualificationNumber)) return qualificationDescription;
            if (string.IsNullOrWhiteSpace(qualificationDescription)) return qualificationNumber;
            return $"{qualificationNumber} {qualificationDescription}".Trim();
        }

        private static string? ResolveKnowledgeQuestionnaireCoverPath(string documentTitle)
        {
            var normalizedTitle = (documentTitle ?? string.Empty).Trim().ToUpperInvariant();
            var candidates = normalizedTitle.Contains("MEMORANDUM", StringComparison.Ordinal)
                ? new[]
                {
                    Path.Combine("Imports", "Coverpages", "Knowlegde Questionnaire Memorandum Cover Page.png"),
                    Path.Combine("ETDP", "Imports", "Coverpages", "Knowlegde Questionnaire Memorandum Cover Page.png"),
                    Path.Combine("Imports", "Coverpages", "Knowlegde Questionnaire Cover Page.png"),
                    Path.Combine("ETDP", "Imports", "Coverpages", "Knowlegde Questionnaire Cover Page.png"),
                    Path.Combine("Imports", "Coverpages", "clean coverpage.jpg"),
                    Path.Combine("ETDP", "Imports", "Coverpages", "clean coverpage.jpg")
                }
                : new[]
                {
                    Path.Combine("Imports", "Coverpages", "Knowlegde Questionnaire Cover Page.png"),
                    Path.Combine("ETDP", "Imports", "Coverpages", "Knowlegde Questionnaire Cover Page.png"),
                    Path.Combine("Imports", "Coverpages", "clean coverpage.jpg"),
                    Path.Combine("ETDP", "Imports", "Coverpages", "clean coverpage.jpg")
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

        private static Table BuildQuestionTable(
            AssessmentDrivenQuestionGenerator.GeneratedQuestion question,
            string typeLabel,
            string instruction)
        {
            var isTrueFalse = string.Equals(question.Type, "TrueFalse", StringComparison.OrdinalIgnoreCase);
            var table = new Table();
            table.Append(DefaultTableProperties());
            table.Append(new TableGrid(
                new GridColumn() { Width = "2600" },
                new GridColumn() { Width = "9500" }));

            table.Append(QuestionRow("Question", $"Question {question.Number} ({question.Marks} mark) - {typeLabel}", emphasizeValue: true));
            table.Append(QuestionRow("Topic", $"{question.TopicCode} — {question.TopicDescription}"));
            table.Append(QuestionRow("Lesson Plan", question.LessonPlanLabel));
            table.Append(QuestionRow("Assessment Criterion", question.AssessmentCriteriaDescription));
            table.Append(QuestionRow("Stem", question.Prompt));

            if (isTrueFalse)
            {
                table.Append(QuestionNestedTableRow(
                    "Options",
                    BuildQuestionOptionsTable(question, includeTrueFalseColumns: false)));
            }
            else
            {
                table.Append(QuestionNestedTableRow(
                    "Options",
                    BuildQuestionOptionsTable(question, includeTrueFalseColumns: false)));
            }

            table.Append(QuestionRow("Instruction", instruction));
            return table;
        }

        private static Table BuildQuestionOptionsTable(
            AssessmentDrivenQuestionGenerator.GeneratedQuestion question,
            bool includeTrueFalseColumns)
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
                    TableTextCell("1100", "Option", bold: true, center: true),
                    TableTextCell("6400", "Statement", bold: true),
                    TableTextCell("1000", "True", bold: true, center: true),
                    TableTextCell("1000", "False", bold: true, center: true)));
            }
            else
            {
                table.Append(new TableRow(
                    TableTextCell("1100", "Option", bold: true, center: true),
                    TableTextCell("8400", "Statement", bold: true)));
            }

            if (question.Options == null || question.Options.Count == 0)
            {
                if (includeTrueFalseColumns)
                {
                    table.Append(new TableRow(
                        TableTextCell("1100", "-", center: true),
                        TableTextCell("6400", "No options available."),
                        TableTextCell("1000", "", center: true),
                        TableTextCell("1000", "", center: true)));
                }
                else
                {
                    table.Append(new TableRow(
                        TableTextCell("1100", "-", center: true),
                        TableTextCell("8400", "No options available.")));
                }
                return table;
            }

            for (var i = 0; i < question.Options.Count; i++)
            {
                var label = OptionLabel(i);
                if (includeTrueFalseColumns)
                {
                    table.Append(new TableRow(
                        TableTextCell("1100", label, center: true),
                        TableTextCell("6400", question.Options[i]),
                        TableTextCell("1000", "[ ]", center: true),
                        TableTextCell("1000", "[ ]", center: true)));
                }
                else
                {
                    table.Append(new TableRow(
                        TableTextCell("1100", label, center: true),
                        TableTextCell("8400", question.Options[i])));
                }
            }

            return table;
        }

        private static TableRow QuestionNestedTableRow(string label, Table nestedTable)
        {
            var labelCell = new TableCell(
                new TableCellProperties(new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = "2600" }),
                TableCellParagraph(label, bold: true));

            var valueCell = new TableCell(
                new TableCellProperties(new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = "9500" }),
                nestedTable);

            return new TableRow(labelCell, valueCell);
        }

        private static TableRow QuestionRow(string label, string value, bool emphasizeValue = false)
        {
            var labelCell = new TableCell(
                new TableCellProperties(new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = "2600" }),
                TableCellParagraph(label, bold: true));

            var valueCell = new TableCell(
                new TableCellProperties(new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = "9500" }),
                TableCellParagraph(value, bold: emphasizeValue));

            return new TableRow(labelCell, valueCell);
        }

        private static TableCell TableTextCell(string width, string text, bool bold = false, bool center = false)
        {
            return new TableCell(
                new TableCellProperties(
                    new TableCellWidth() { Type = TableWidthUnitValues.Dxa, Width = width },
                    BuildVisibleTableCellBorders(),
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
                TableCellParagraph(text, bold: bold, center: center));
        }

        private static Paragraph TableCellParagraph(string text, bool bold = false, bool center = false)
        {
            var runProps = new RunProperties
            {
                FontSize = new FontSize() { Val = CompactTableCellHalfPt },
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
            var compact = Math.Max(1, requestedSizePt);
            return Math.Clamp(compact, 12, 34);
        }

        private static int CompactBodyHalfPt(int requestedSizeHalfPt)
        {
            var compact = Math.Max(1, requestedSizeHalfPt);
            return Math.Clamp(compact, 20, 32);
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

