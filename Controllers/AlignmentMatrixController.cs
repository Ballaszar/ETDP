using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Services;
using ETD.Api.Utils;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class AlignmentMatrixController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly Regex TopicCodeRegex = new(@"\bKT\d{4}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex IacCodeRegex = new(@"\bIAC\d{4}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex CodePrefixRegex = new(@"(KM|PM|WM)-\d{2}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly ConcurrentDictionary<int, CachedAlignmentMatrix> SummaryCache = new();
        private static readonly TimeSpan SummaryCacheTtl = TimeSpan.FromMinutes(20);
        private static readonly string[] CurriculumFileNames = { "QC_CurriculumSpecification.pdf" };
        private static readonly string[] AssessmentFileNames = { "QC_AssessmentSpecification.pdf" };
        private static readonly string[] CrossCuttingSignals =
        {
            "employment",
            "organisation of work",
            "employer-employee",
            "ethics",
            "communication",
            "workplace",
            "information and communication technology",
            "current trends",
            "fundamentals"
        };
        private static readonly Dictionary<int, string[]> OutcomeKeywordBoosters = new()
        {
            [1] = new[]
            {
                "maintenance", "maintain", "maintained", "service", "servicing", "scheduled", "preventative",
                "preventive", "routine", "safely", "emergencies", "work safely", "scheduled services"
            },
            [2] = new[]
            {
                "dismantle", "inspect", "assess", "assemble", "reassemble", "remove", "install", "refit",
                "cooling", "brake", "steering", "suspension", "hydraulic", "pneumatic", "component"
            },
            [3] = new[]
            {
                "diagnose", "diagnosis", "fault", "faults", "repair", "troubleshoot", "electrical",
                "electronic", "air conditioning", "optimisation", "optimization", "diagnostic"
            }
        };

        private readonly ApplicationDbContext _context;
        private readonly CurriculumDeliveryPilotService _curriculumDeliveryPilotService;

        public AlignmentMatrixController(
            ApplicationDbContext context,
            CurriculumDeliveryPilotService curriculumDeliveryPilotService)
        {
            _context = context;
            _curriculumDeliveryPilotService = curriculumDeliveryPilotService;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> Summary([FromQuery] int qualificationId, CancellationToken cancellationToken)
        {
            if (qualificationId <= 0)
            {
                return BadRequest("qualificationId is required.");
            }

            if (SummaryCache.TryGetValue(qualificationId, out var cached) &&
                cached.GeneratedAtUtc + SummaryCacheTtl > DateTime.UtcNow)
            {
                return Ok(cached.Report);
            }

            var report = await BuildReportAsync(qualificationId, cancellationToken);
            SummaryCache[qualificationId] = new CachedAlignmentMatrix
            {
                GeneratedAtUtc = DateTime.UtcNow,
                Report = report
            };
            return Ok(report);
        }

        [HttpGet("curriculum-digestion-assessment")]
        public async Task<IActionResult> CurriculumDigestionAssessment(
            [FromQuery] int qualificationId,
            [FromQuery] bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (qualificationId <= 0)
            {
                return BadRequest("qualificationId is required.");
            }

            var qualification = await _context.Qualifications
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == qualificationId, cancellationToken);
            if (qualification == null)
            {
                return NotFound($"Qualification {qualificationId} was not found.");
            }

            var subjects = await _context.Subjects
                .AsNoTracking()
                .Where(s => s.QualificationId == qualificationId)
                .OrderBy(s => s.SubjectCode)
                .ThenBy(s => s.SubjectDescription)
                .ToListAsync(cancellationToken);
            var subjectIds = subjects.Select(s => s.Id).ToList();
            var topics = await _context.Topics
                .AsNoTracking()
                .Where(t => subjectIds.Contains(t.SubjectId))
                .OrderBy(t => t.SubjectId)
                .ThenBy(t => t.Order ?? int.MaxValue)
                .ThenBy(t => t.TopicCode)
                .ToListAsync(cancellationToken);
            var topicIds = topics.Select(t => t.Id).ToList();
            var criteriaCount = await _context.AssessmentCriteria
                .AsNoTracking()
                .CountAsync(c => topicIds.Contains(c.TopicId), cancellationToken);

            CurriculumDeliveryPilotService.TopicEvidenceSummary evidence;
            try
            {
                evidence = await _curriculumDeliveryPilotService.BuildTopicEvidenceSummaryAsync(
                    qualificationId,
                    forceRefresh: forceRefresh,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    error = "Topic evidence assessment failed.",
                    detail = ex.Message
                });
            }

            var evidenceByTopicId = evidence.Topics
                .GroupBy(t => t.TopicId)
                .ToDictionary(g => g.Key, g => g.First());

            var topicRows = topics.Select(topic =>
            {
                evidenceByTopicId.TryGetValue(topic.Id, out var item);
                var status = item == null || item.EvidenceCount <= 0
                    ? "not_available"
                    : item.CoverageBand;
                var readiness = item == null
                    ? 0
                    : Math.Clamp((item.CoveragePercent + item.BestConfidencePercent + Math.Min(100, item.DistinctSourceCount * 20)) / 3, 0, 100);
                return new
                {
                    topicId = topic.Id,
                    topicCode = topic.TopicCode ?? string.Empty,
                    topicDescription = topic.TopicDescription ?? string.Empty,
                    subjectId = topic.SubjectId,
                    subjectCode = subjects.FirstOrDefault(s => s.Id == topic.SubjectId)?.SubjectCode ?? string.Empty,
                    evidenceStatus = status,
                    learnerGuideReadyPercent = readiness,
                    coveragePercent = item?.CoveragePercent ?? 0,
                    evidenceCount = item?.EvidenceCount ?? 0,
                    distinctSourceCount = item?.DistinctSourceCount ?? 0,
                    bestConfidencePercent = item?.BestConfidencePercent ?? 0,
                    averageConfidencePercent = item?.AverageConfidencePercent ?? 0,
                    citations = item?.TopCitations?.Take(5).ToList() ?? new List<string>(),
                    evidence = item?.TopEvidence?.Take(3).Select(source => new
                    {
                        source.MaterialTitle,
                        source.KnowledgeSourceType,
                        source.Citation,
                        source.PageNumber,
                        source.ConfidencePercent,
                        Excerpt = TrimForApi(source.Excerpt, 520)
                    }).ToList()
                };
            }).ToList();

            var readyTopics = topicRows.Count(row => row.evidenceCount > 0 && row.learnerGuideReadyPercent >= 55);
            var understandingPercent = topics.Count == 0
                ? 0
                : (int)Math.Round(topicRows.Sum(row => row.learnerGuideReadyPercent) / Math.Max(1.0, topicRows.Count));

            var requiredUploadPaths = new[]
            {
                new
                {
                    purpose = "ETDP curriculum and assessment specifications",
                    path = $@"D:\ETDP\Imports\{qualification.QualificationNumber}",
                    files = "QC_CurriculumSpecification.pdf and QC_AssessmentSpecification.pdf"
                },
                new
                {
                    purpose = "VocationalLLM subject-matter source PDFs for Diesel Mechanic",
                    path = @"D:\ETDP\VocationalLLM\data\knowledge_taxonomy\vocational_disciplines\Diesel Mechanic",
                    files = "PDF, DOCX, PPTX, TXT source documents"
                },
                new
                {
                    purpose = "Additional Diesel Mechanic source pool currently present",
                    path = @"D:\ETDP\VocationalLLM\data\knowledge_taxonomy\vocational_disciplines\Diesel Mechanic 2",
                    files = "PDF, DOCX, PPTX, TXT source documents"
                },
                new
                {
                    purpose = "ETDP app upload route for subject matter",
                    path = @"D:\ETDP\ETDP\Imports\SubjectMatterUploads",
                    files = "Uploaded through the ETDP subject-matter upload page or API"
                }
            };

            return Ok(new
            {
                ok = true,
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                qualification = new
                {
                    qualification.Id,
                    qualification.QualificationNumber,
                    qualification.QualificationDescription
                },
                summary = new
                {
                    subjectCount = subjects.Count,
                    topicCount = topics.Count,
                    criteriaCount,
                    sourceMaterialCount = evidence.SourceMaterialCount,
                    sourceChunkCount = evidence.SourceChunkCount,
                    topicsWithEvidence = evidence.TopicsWithEvidenceCount,
                    mappedTopics = evidence.MappedTopicsCount,
                    developingTopics = evidence.DevelopingTopicsCount,
                    gapTopics = evidence.GapTopicsCount,
                    coveragePercent = evidence.CoveragePercent,
                    learnerGuideReadyTopics = readyTopics,
                    understandingPercent,
                    llmLearningMode = "retrieval_augmented_generation",
                    llmLearnsNewSubjectMatter = evidence.SourceChunkCount > 0,
                    forceRefresh
                },
                interpretation = new
                {
                    databaseCanBeCleaned = true,
                    recommendedBeforeCleaning = "Export a backup, run this assessment with forceRefresh=true, and only reset source/curriculum data if the report still shows wrong or duplicated source pools.",
                    doesTheLlmAbsorbIntoWeights = false,
                    howItLearns = "The LLM learns operationally through indexed source chunks and retrieval at generation time. Fine-tuning the model weights is a separate training step and is not required for learner-guide generation."
                },
                requiredUploadPaths,
                topics = topicRows,
                gaps = topicRows.Where(row => row.evidenceCount <= 0).Take(100).ToList(),
                warnings = evidence.Warnings
            });
        }

        [HttpGet("subject-matter-digestion-status")]
        public IActionResult SubjectMatterDigestionStatus(
            [FromQuery] string discipline = "Diesel Mechanic",
            [FromQuery] int qualificationId = 0)
        {
            var normalizedDiscipline = string.IsNullOrWhiteSpace(discipline)
                ? "Diesel Mechanic"
                : discipline.Trim();
            var root = ResolveVocationalLlmRoot();
            var uploadPath = Path.Combine(root, "data", "knowledge_taxonomy", "vocational_disciplines", normalizedDiscipline);
            var dbPath = Path.Combine(root, "data", "vocational_llm.db");
            var files = Directory.Exists(uploadPath)
                ? Directory.EnumerateFiles(uploadPath, "*.*", SearchOption.AllDirectories)
                    .Where(path => IsSupportedSubjectMatterFile(path))
                    .ToList()
                : new List<string>();

            var status = new SubjectMatterDigestionSnapshot
            {
                Discipline = normalizedDiscipline,
                QualificationId = qualificationId,
                UploadPath = uploadPath,
                DatabasePath = dbPath,
                UploadFolderExists = Directory.Exists(uploadPath),
                DatabaseExists = System.IO.File.Exists(dbPath),
                FileCount = files.Count,
                PdfCount = files.Count(path => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)),
                DocxCount = files.Count(path => string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase))
            };

            if (status.DatabaseExists)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Cache=Shared");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
SELECT
  (SELECT COUNT(*) FROM documents
   WHERE COALESCE(source_path, '') LIKE $pathLike
      OR COALESCE(vocational_discipline, '') = $discipline) AS documents_count,
  (SELECT COALESCE(SUM(raw_char_count), 0) FROM documents
   WHERE COALESCE(source_path, '') LIKE $pathLike
      OR COALESCE(vocational_discipline, '') = $discipline) AS raw_char_count,
  (SELECT COUNT(*) FROM chunks
   WHERE document_id IN (
      SELECT id FROM documents
      WHERE COALESCE(source_path, '') LIKE $pathLike
         OR COALESCE(vocational_discipline, '') = $discipline
   )) AS chunk_count,
  (SELECT COUNT(*) FROM chunks
   WHERE embedding_json IS NOT NULL
     AND LENGTH(embedding_json) > 10
     AND document_id IN (
      SELECT id FROM documents
      WHERE COALESCE(source_path, '') LIKE $pathLike
         OR COALESCE(vocational_discipline, '') = $discipline
   )) AS embedded_chunk_count,
  (SELECT COUNT(*) FROM ingest_events
   WHERE status = 'ok'
     AND (COALESCE(source_path, '') LIKE $pathLike
       OR COALESCE(vocational_discipline, '') = $discipline)) AS ok_events,
  (SELECT COUNT(*) FROM ingest_events
   WHERE status = 'failed'
     AND (COALESCE(source_path, '') LIKE $pathLike
       OR COALESCE(vocational_discipline, '') = $discipline)) AS failed_events;";
                    cmd.Parameters.AddWithValue("$pathLike", $"%vocational_disciplines%{normalizedDiscipline}%");
                    cmd.Parameters.AddWithValue("$discipline", normalizedDiscipline);
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        status.DocumentCount = reader.GetInt32(0);
                        status.RawCharCount = reader.GetInt64(1);
                        status.ChunkCount = reader.GetInt32(2);
                        status.EmbeddedChunkCount = reader.GetInt32(3);
                        status.OkIngestEvents = reader.GetInt32(4);
                        status.FailedIngestEvents = reader.GetInt32(5);
                    }

                    status.RecentFailures = ReadRecentIngestFailures(conn, normalizedDiscipline);
                }
                catch (Exception ex)
                {
                    status.DatabaseError = ex.Message;
                }
            }

            status.FileDigestionPercent = status.FileCount <= 0
                ? 0
                : Math.Clamp((int)Math.Round((double)status.DocumentCount / Math.Max(1, status.FileCount) * 100), 0, 100);
            status.EmbeddingPercent = status.ChunkCount <= 0
                ? 0
                : Math.Clamp((int)Math.Round((double)status.EmbeddedChunkCount / Math.Max(1, status.ChunkCount) * 100), 0, 100);
            status.Stage = DetermineDigestionStage(status);
            status.EstimatedMessage = BuildDigestionEstimate(status);

            return Ok(status);
        }

        private static string TrimForApi(string? value, int maxChars)
        {
            var text = WhitespaceRegex.Replace(value ?? string.Empty, " ").Trim();
            if (text.Length <= maxChars) return text;
            return text.Substring(0, Math.Max(0, maxChars - 3)).TrimEnd() + "...";
        }

        [HttpGet("report")]
        public IActionResult Report([FromQuery] int qualificationId)
        {
            if (qualificationId <= 0)
            {
                return BadRequest("qualificationId is required.");
            }

            return Content(BuildReportShell(qualificationId), "text/html; charset=utf-8");
        }

        private async Task<AlignmentMatrixReport> BuildReportAsync(int qualificationId, CancellationToken cancellationToken)
        {
            var qualification = await _context.Qualifications
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == qualificationId, cancellationToken);

            if (qualification == null)
            {
                throw new InvalidOperationException($"Qualification {qualificationId} was not found.");
            }

            var subjects = await _context.Subjects
                .AsNoTracking()
                .Where(s => s.QualificationId == qualificationId)
                .OrderBy(s => s.SubjectCode)
                .ThenBy(s => s.SubjectDescription)
                .ToListAsync(cancellationToken);

            var subjectIds = subjects.Select(s => s.Id).ToList();

            var topics = await _context.Topics
                .AsNoTracking()
                .Where(t => subjectIds.Contains(t.SubjectId))
                .OrderBy(t => t.SubjectId)
                .ThenBy(t => t.Order ?? int.MaxValue)
                .ThenBy(t => t.TopicCode)
                .ToListAsync(cancellationToken);

            var topicIds = topics.Select(t => t.Id).ToList();

            var criteria = await _context.AssessmentCriteria
                .AsNoTracking()
                .Where(c => topicIds.Contains(c.TopicId))
                .OrderBy(c => c.TopicId)
                .ThenBy(c => c.Id)
                .ToListAsync(cancellationToken);

            var criteriaIds = criteria.Select(c => c.Id).ToList();

            var lessonPlans = await _context.LessonPlans
                .AsNoTracking()
                .Where(lp => criteriaIds.Contains(lp.AssessmentCriteriaId))
                .ToListAsync(cancellationToken);

            var lecturerToolkitEntries = await _context.LecturerToolkitEntries
                .AsNoTracking()
                .Where(e => e.QualificationsId == qualificationId)
                .ToListAsync(cancellationToken);

            var outcomes = await _context.Outcomes
                .AsNoTracking()
                .Where(o => subjectIds.Contains(o.SubjectId))
                .OrderBy(o => o.SubjectId)
                .ThenBy(o => o.Order ?? int.MaxValue)
                .ThenBy(o => o.Id)
                .ToListAsync(cancellationToken);

            var knowledgeQuestionnaireCount = await _context.KnowledgeQuestionnaires
                .AsNoTracking()
                .CountAsync(q => subjectIds.Contains(q.SubjectId), cancellationToken);

            var workbookCount = await _context.Workbooks
                .AsNoTracking()
                .CountAsync(w => subjectIds.Contains(w.SubjectId), cancellationToken);

            var learnerGuideCount = await _context.LearnerGuides
                .AsNoTracking()
                .CountAsync(g => subjectIds.Contains(g.SubjectId), cancellationToken);

            CurriculumDeliveryPilotService.TopicEvidenceSummary? topicEvidence;
            try
            {
                topicEvidence = await _curriculumDeliveryPilotService.BuildTopicEvidenceSummaryAsync(qualificationId, cancellationToken: cancellationToken);
            }
            catch
            {
                topicEvidence = null;
            }

            var sourceDocuments = ResolveSourceDocuments(qualification);
            var curriculumText = LoadCurriculumText(qualification, sourceDocuments.CurriculumSpecPath);
            var assessmentText = LoadAssessmentText(sourceDocuments.AssessmentSpecPath);
            sourceDocuments.CurriculumExtractLength = curriculumText.Length;
            sourceDocuments.AssessmentExtractLength = assessmentText.Length;

            var extractedOutcomes = ExtractExitLevelOutcomes(curriculumText, assessmentText);
            var nqfDescriptors = LoadNqfDescriptors(qualification.NqfLevel, sourceDocuments.AlignmentWorkbookPath);
            var seedArtifacts = ResolveSeedArtifacts(qualification);
            sourceDocuments.SubjectSeedCsvPath = seedArtifacts.SubjectCsvPath;
            sourceDocuments.TopicSeedCsvPath = seedArtifacts.TopicCsvPath;

            var seedSubjects = ReadSeedSubjects(seedArtifacts.SubjectCsvPath);
            var seedTopics = ReadSeedTopics(seedArtifacts.TopicCsvPath);

            var dbSubjectsByCode = subjects
                .GroupBy(subject => NormalizeKey(subject.SubjectCode))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var topicsBySubjectId = topics
                .GroupBy(topic => topic.SubjectId)
                .ToDictionary(group => group.Key, group => group.ToList());

            var criteriaByTopicId = criteria
                .GroupBy(item => item.TopicId)
                .ToDictionary(group => group.Key, group => group.ToList());

            var lessonPlansByCriteriaId = lessonPlans
                .GroupBy(item => item.AssessmentCriteriaId)
                .ToDictionary(group => group.Key, group => group.ToList());

            var toolkitByCriteriaId = lecturerToolkitEntries
                .Where(entry => entry.AssessmentCriteriaId.HasValue)
                .GroupBy(entry => entry.AssessmentCriteriaId!.Value)
                .ToDictionary(group => group.Key, group => group.ToList());

            var toolkitBySubjectCode = lecturerToolkitEntries
                .GroupBy(entry => NormalizeKey(entry.SubjectCode))
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            var topicEvidenceByTopicId = topicEvidence?.Topics
                .GroupBy(item => item.TopicId)
                .ToDictionary(group => group.Key, group => group.First())
                ?? new Dictionary<int, CurriculumDeliveryPilotService.TopicEvidenceItem>();

            var topicEvidenceByKey = topicEvidence?.Topics
                .GroupBy(item => BuildTopicKey(item.SubjectCode, item.TopicCode, item.TopicDescription))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, CurriculumDeliveryPilotService.TopicEvidenceItem>(StringComparer.OrdinalIgnoreCase);

            var modules = BuildModules(
                seedSubjects,
                seedTopics,
                dbSubjectsByCode,
                topicsBySubjectId,
                criteriaByTopicId,
                lessonPlansByCriteriaId,
                toolkitByCriteriaId,
                toolkitBySubjectCode,
                topicEvidenceByTopicId,
                topicEvidenceByKey,
                extractedOutcomes);

            var moduleTypeBreakdown = modules
                .GroupBy(module => module.ModuleType)
                .OrderBy(group => group.Key)
                .Select(group => new ModuleTypeBreakdown
                {
                    ModuleType = group.Key,
                    ModuleTypeLabel = group.First().ModuleTypeLabel,
                    ModuleCount = group.Count(),
                    SubjectCount = group.Sum(item => item.SubjectCount),
                    TopicCount = group.Sum(item => item.TopicCount),
                    LessonPlanRowCount = group.Sum(item => item.LessonPlanRowCount)
                })
                .ToList();

            var duplicateCriteriaRisks = (topicEvidence?.DuplicateCriteriaGroups ?? new List<CurriculumDeliveryPilotService.DuplicateCriteriaGroup>())
                .OrderByDescending(group => group.TopicCount)
                .Take(20)
                .Select(group => new DuplicateCriteriaRisk
                {
                    CriteriaDescription = group.CriteriaDescription,
                    TopicCount = group.TopicCount,
                    Topics = group.Topics
                })
                .ToList();

            var topicOutcomeMappedCount = topics.Count(topic => topic.OutcomeId.HasValue);
            var sourceMaterialCount = await _context.SourceMaterials
                .AsNoTracking()
                .CountAsync(item =>
                    item.QualificationCode == qualification.QualificationNumber ||
                    item.QualificationCode == qualification.QualificationNumber.Replace("-", string.Empty) ||
                    item.QualificationDescription == qualification.QualificationDescription,
                    cancellationToken);

            sourceDocuments.SourceMaterialCount = sourceMaterialCount;

            var report = new AlignmentMatrixReport
            {
                QualificationId = qualification.Id,
                QualificationNumber = qualification.QualificationNumber,
                QualificationDescription = qualification.QualificationDescription,
                QualificationNqfLevel = qualification.NqfLevel,
                QualificationCredits = qualification.Credits,
                UsesOutcomes = qualification.UsesOutcomes,
                GeneratedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                SourceDocuments = sourceDocuments,
                LayerStatus = new MatrixLayerStatus
                {
                    SubjectCount = subjects.Count,
                    TopicCount = topics.Count,
                    CriteriaCount = criteria.Count,
                    SourceMaterialCount = sourceMaterialCount,
                    ExtractedOutcomeCount = extractedOutcomes.Count,
                    DatabaseOutcomeCount = outcomes.Count,
                    TopicOutcomeMappedCount = topicOutcomeMappedCount,
                    LecturerToolkitRowCount = lecturerToolkitEntries.Count,
                    LessonPlanCount = lessonPlans.Count,
                    KnowledgeQuestionnaireCount = knowledgeQuestionnaireCount,
                    WorkbookCount = workbookCount,
                    LearnerGuideCount = learnerGuideCount
                },
                TopicEvidence = new TopicEvidenceSnapshot
                {
                    SourceMaterialCount = topicEvidence?.SourceMaterialCount ?? 0,
                    SourceChunkCount = topicEvidence?.SourceChunkCount ?? 0,
                    TopicCount = topicEvidence?.TopicCount ?? topics.Count,
                    TopicsWithEvidenceCount = topicEvidence?.TopicsWithEvidenceCount ?? 0,
                    MappedTopicsCount = topicEvidence?.MappedTopicsCount ?? 0,
                    DevelopingTopicsCount = topicEvidence?.DevelopingTopicsCount ?? 0,
                    GapTopicsCount = topicEvidence?.GapTopicsCount ?? 0,
                    CoveragePercent = topicEvidence?.CoveragePercent ?? 0,
                    DuplicateCriteriaGroupCount = topicEvidence?.DuplicateCriteriaGroups?.Count ?? 0
                },
                ExtractedExitLevelOutcomes = extractedOutcomes,
                NqfDescriptors = nqfDescriptors,
                ModuleTypeBreakdown = moduleTypeBreakdown,
                Modules = modules,
                DuplicateCriteriaRisks = duplicateCriteriaRisks
            };

            report.Observations = BuildObservations(report);
            report.Caveats = BuildCaveats(report);

            return report;
        }

        private static List<ModuleSummary> BuildModules(
            IReadOnlyList<SeedSubjectRow> seedSubjects,
            IReadOnlyList<SeedTopicRow> seedTopics,
            IReadOnlyDictionary<string, Subject> dbSubjectsByCode,
            IReadOnlyDictionary<int, List<ETD.Api.Models.Topic>> topicsBySubjectId,
            IReadOnlyDictionary<int, List<AssessmentCriteria>> criteriaByTopicId,
            IReadOnlyDictionary<int, List<LessonPlan>> lessonPlansByCriteriaId,
            IReadOnlyDictionary<int, List<LecturerToolkitEntry>> toolkitByCriteriaId,
            IReadOnlyDictionary<string, List<LecturerToolkitEntry>> toolkitBySubjectCode,
            IReadOnlyDictionary<int, CurriculumDeliveryPilotService.TopicEvidenceItem> topicEvidenceByTopicId,
            IReadOnlyDictionary<string, CurriculumDeliveryPilotService.TopicEvidenceItem> topicEvidenceByKey,
            IReadOnlyList<ExtractedOutcome> extractedOutcomes)
        {
            var modules = new List<ModuleSummary>();

            foreach (var moduleGroup in seedSubjects.GroupBy(row => NormalizeKey(row.PhasesCode)).OrderBy(group => group.First().PhasesCode))
            {
                var firstSubject = moduleGroup.First();
                var subjectSummaries = new List<SubjectAlignmentRow>();

                foreach (var seedSubject in moduleGroup.OrderBy(row => row.SubjectCode))
                {
                    dbSubjectsByCode.TryGetValue(NormalizeKey(seedSubject.SubjectCode), out var dbSubject);
                    var subjectTopicRows = seedTopics
                        .Where(row => NormalizeKey(row.SubjectCode) == NormalizeKey(seedSubject.SubjectCode))
                        .OrderBy(row => row.TopicCode)
                        .ToList();

                    var dbTopicRows = dbSubject != null && topicsBySubjectId.TryGetValue(dbSubject.Id, out var items)
                        ? items
                        : new List<ETD.Api.Models.Topic>();

                    var topicRows = new List<TopicAlignmentRow>();
                    var outcomeHintText = new StringBuilder();

                    foreach (var topicRow in subjectTopicRows)
                    {
                        var dbTopic = ResolveTopic(dbTopicRows, topicRow.TopicCode, topicRow.TopicDescription);
                        var topicKey = BuildTopicKey(seedSubject.SubjectCode, topicRow.TopicCode, topicRow.TopicDescription);
                        var evidence = dbTopic != null && topicEvidenceByTopicId.TryGetValue(dbTopic.Id, out var evidenceById)
                            ? evidenceById
                            : (topicEvidenceByKey.TryGetValue(topicKey, out var evidenceByKey) ? evidenceByKey : null);

                        var dbCriteriaRows = dbTopic != null && criteriaByTopicId.TryGetValue(dbTopic.Id, out var topicCriteria)
                            ? topicCriteria
                            : new List<AssessmentCriteria>();

                        var toolkitCount = dbCriteriaRows
                            .Where(c => toolkitByCriteriaId.ContainsKey(c.Id))
                            .Sum(c => toolkitByCriteriaId[c.Id].Count);

                        var lessonPlanCount = dbCriteriaRows
                            .Where(c => lessonPlansByCriteriaId.ContainsKey(c.Id))
                            .Sum(c => lessonPlansByCriteriaId[c.Id].Count);

                        outcomeHintText.Append(' ')
                            .Append(topicRow.TopicDescription)
                            .Append(' ')
                            .Append(topicRow.AssessmentCriteriaDescription);

                        topicRows.Add(new TopicAlignmentRow
                        {
                            TopicCode = topicRow.TopicCode,
                            TopicDescription = topicRow.TopicDescription,
                            AssessmentCriteriaNumber = topicRow.AssessmentCriteriaNumber,
                            AssessmentCriteriaDescription = topicRow.AssessmentCriteriaDescription,
                            AssociatedAssessmentCriteriaCodes = ExtractIacCodes(topicRow.AssessmentCriteriaDescription),
                            CoveragePercent = ClampPercent(evidence?.CoveragePercent ?? 0),
                            CoverageBand = evidence?.CoverageBand ?? "gap",
                            CoverageBandLabel = evidence?.CoverageBandLabel ?? "Gap",
                            EvidenceCount = evidence?.EvidenceCount ?? 0,
                            DistinctSourceCount = evidence?.DistinctSourceCount ?? 0,
                            BestConfidencePercent = ClampPercent(evidence?.BestConfidencePercent ?? 0),
                            LessonPlanRowCount = toolkitCount,
                            LessonPlanCount = lessonPlanCount,
                            TopicOutcomeId = dbTopic?.OutcomeId,
                            TopCitations = evidence?.TopCitations?.Take(3).ToList() ?? new List<string>()
                        });
                    }

                    var uniqueCriteriaClusters = subjectTopicRows
                        .Select(row => NormalizeKey(row.AssessmentCriteriaDescription))
                        .Where(text => !string.IsNullOrWhiteSpace(text))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                    var criteriaClusterCount = subjectTopicRows.Count(row => !string.IsNullOrWhiteSpace(row.AssessmentCriteriaDescription));
                    var copyPasteRiskPercent = criteriaClusterCount <= 1
                        ? 0
                        : ClampPercent((1d - (uniqueCriteriaClusters / (double)Math.Max(1, criteriaClusterCount))) * 100d);

                    var toolkitRowsForSubject = toolkitBySubjectCode.TryGetValue(NormalizeKey(seedSubject.SubjectCode), out var toolkitRows)
                        ? toolkitRows.Count
                        : 0;

                    var likelyOutcome = DetermineLikelyOutcome(
                        seedSubject.SubjectCode,
                        seedSubject.SubjectDescription,
                        seedSubject.PhasesDescription,
                        outcomeHintText.ToString(),
                        extractedOutcomes);

                    subjectSummaries.Add(new SubjectAlignmentRow
                    {
                        SubjectCode = seedSubject.SubjectCode,
                        SubjectDescription = seedSubject.SubjectDescription,
                        SubjectNqfLevel = seedSubject.SubjectNqfLevel,
                        SubjectCredits = seedSubject.SubjectCredits,
                        SubjectPercentage = seedSubject.SubjectPercentage,
                        TopicCount = topicRows.Count,
                        CriteriaClusterCount = criteriaClusterCount,
                        UniqueCriteriaClusterCount = uniqueCriteriaClusters,
                        CopyPasteRiskPercent = copyPasteRiskPercent,
                        LessonPlanRowCount = toolkitRowsForSubject,
                        AverageCoveragePercent = topicRows.Count == 0 ? 0 : ClampPercent(topicRows.Average(row => row.CoveragePercent)),
                        LikelyOutcomeCode = likelyOutcome.Code,
                        LikelyOutcomeLabel = likelyOutcome.Label,
                        LikelyOutcomeConfidencePercent = likelyOutcome.ConfidencePercent,
                        SupportMode = likelyOutcome.SupportMode,
                        SampleAssociatedCodes = topicRows
                            .SelectMany(row => row.AssociatedAssessmentCriteriaCodes)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(8)
                            .ToList(),
                        Topics = topicRows
                    });
                }

                modules.Add(new ModuleSummary
                {
                    ModuleCode = ExtractModuleCode(firstSubject.PhasesCode),
                    PhaseCode = firstSubject.PhasesCode,
                    PhaseDescription = firstSubject.PhasesDescription,
                    ModuleType = ResolveModuleType(firstSubject.PhasesCode),
                    ModuleTypeLabel = ResolveModuleTypeLabel(firstSubject.PhasesCode),
                    SubjectCount = subjectSummaries.Count,
                    TopicCount = subjectSummaries.Sum(item => item.TopicCount),
                    LessonPlanRowCount = subjectSummaries.Sum(item => item.LessonPlanRowCount),
                    NqfLevels = subjectSummaries
                        .Where(item => item.SubjectNqfLevel.HasValue)
                        .Select(item => item.SubjectNqfLevel!.Value)
                        .Distinct()
                        .OrderBy(value => value)
                        .ToList(),
                    LikelyOutcomeLabels = subjectSummaries
                        .Select(item => item.LikelyOutcomeLabel)
                        .Where(label => !string.IsNullOrWhiteSpace(label))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(3)
                        .ToList(),
                    Subjects = subjectSummaries
                });
            }

            return modules;
        }

        private static string BuildReportShell(int qualificationId)
        {
            return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Alignment Matrix Audit</title>
  <style>
    :root {
      color-scheme: light;
      --ink: #20364d;
      --muted: #607a94;
      --line: #d8e4ef;
      --card: #fbfdff;
      --bg: #eef4f9;
      --accent: #1d4f80;
      --green: #2f7a55;
      --amber: #b88418;
      --red: #bd4343;
      --shadow: 0 18px 46px rgba(25, 48, 72, 0.12);
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      background: linear-gradient(180deg, #edf3f9 0%, #f6f9fc 100%);
      color: var(--ink);
      font: 14px/1.45 "Segoe UI", Arial, sans-serif;
    }
    .shell {
      max-width: 1380px;
      margin: 0 auto;
      padding: 28px;
    }
    .report {
      background: #fff;
      border-radius: 24px;
      box-shadow: var(--shadow);
      overflow: hidden;
      border: 1px solid rgba(216, 228, 239, 0.9);
    }
    .hero {
      padding: 28px 30px 24px;
      background:
        radial-gradient(circle at top right, rgba(83, 136, 196, 0.22), transparent 32%),
        linear-gradient(135deg, #103d69 0%, #1a557f 42%, #2e6f94 100%);
      color: #fff;
    }
    .eyebrow {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 6px 12px;
      border-radius: 999px;
      background: rgba(255, 255, 255, 0.14);
      border: 1px solid rgba(255, 255, 255, 0.18);
      font-size: 12px;
      letter-spacing: .05em;
      text-transform: uppercase;
      font-weight: 700;
    }
    .hero h1 {
      margin: 14px 0 10px;
      font-size: 32px;
      line-height: 1.12;
    }
    .hero p {
      margin: 0;
      max-width: 980px;
      color: rgba(255, 255, 255, 0.88);
    }
    .toolbar {
      margin-top: 18px;
      display: flex;
      flex-wrap: wrap;
      gap: 10px;
    }
    button, .toolbar a {
      appearance: none;
      border: none;
      border-radius: 12px;
      padding: 10px 14px;
      background: #163f66;
      color: #fff;
      text-decoration: none;
      font: inherit;
      font-weight: 700;
      cursor: pointer;
      box-shadow: inset 0 0 0 1px rgba(255,255,255,.12);
    }
    button.secondary, .toolbar a.secondary {
      background: rgba(255,255,255,.12);
    }
    .content {
      padding: 26px 28px 32px;
    }
    .status {
      padding: 18px 22px;
      color: var(--muted);
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 14px;
    }
    .card, details.module, details.subject {
      border: 1px solid var(--line);
      border-radius: 18px;
      background: var(--card);
    }
    .card {
      padding: 16px 18px;
    }
    .metric-label {
      color: var(--muted);
      font-size: 12px;
      text-transform: uppercase;
      letter-spacing: .05em;
      font-weight: 700;
    }
    .metric-value {
      margin-top: 6px;
      font-size: 28px;
      font-weight: 800;
      color: #1e3f63;
    }
    .metric-note {
      margin-top: 6px;
      color: var(--muted);
      font-size: 12px;
    }
    .section {
      margin-top: 20px;
    }
    .section h2 {
      margin: 0 0 10px;
      font-size: 20px;
    }
    .section-copy {
      margin: 0 0 12px;
      color: var(--muted);
      max-width: 980px;
    }
    .two-col {
      display: grid;
      grid-template-columns: minmax(0, 1.15fr) minmax(320px, .85fr);
      gap: 16px;
    }
    .mini-list {
      margin: 0;
      padding-left: 18px;
    }
    .mini-list li + li {
      margin-top: 6px;
    }
    .bar-stack {
      display: grid;
      gap: 10px;
    }
    .bar-row {
      display: grid;
      grid-template-columns: 230px minmax(0, 1fr) 86px;
      gap: 12px;
      align-items: center;
    }
    .bar-track {
      position: relative;
      height: 12px;
      border-radius: 999px;
      background: #e7eef6;
      overflow: hidden;
    }
    .bar-fill {
      height: 100%;
      border-radius: inherit;
    }
    .bar-fill.green { background: linear-gradient(90deg, #2f7a55, #50a170); }
    .bar-fill.amber { background: linear-gradient(90deg, #b88418, #d1a444); }
    .bar-fill.red { background: linear-gradient(90deg, #bd4343, #d76565); }
    .bar-fill.blue { background: linear-gradient(90deg, #245788, #3f7db6); }
    .pills {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin-top: 10px;
    }
    .pill {
      padding: 6px 10px;
      border-radius: 999px;
      font-size: 12px;
      font-weight: 700;
      border: 1px solid transparent;
    }
    .pill.green { background: #e8f5ee; color: #1f5b3d; border-color: #cde9d7; }
    .pill.amber { background: #fff4d9; color: #7a5600; border-color: #efd6a0; }
    .pill.red { background: #fdeaea; color: #7c2727; border-color: #f2c6c6; }
    .pill.blue { background: #edf5ff; color: #0f5d9d; border-color: #cfe0f7; }
    .source-table, .topic-table {
      width: 100%;
      border-collapse: collapse;
    }
    .source-table td, .source-table th, .topic-table td, .topic-table th {
      border-bottom: 1px solid #e8eef5;
      padding: 9px 10px;
      text-align: left;
      vertical-align: top;
    }
    .source-table th, .topic-table th {
      font-size: 12px;
      color: var(--muted);
      text-transform: uppercase;
      letter-spacing: .04em;
    }
    details.module summary, details.subject summary {
      list-style: none;
      cursor: pointer;
    }
    details.module summary::-webkit-details-marker,
    details.subject summary::-webkit-details-marker {
      display: none;
    }
    details.module summary {
      padding: 16px 18px;
      display: grid;
      grid-template-columns: minmax(0, 1.25fr) repeat(4, auto);
      gap: 12px;
      align-items: center;
    }
    details.subject {
      margin-top: 10px;
      background: #fff;
    }
    details.subject summary {
      padding: 14px 16px;
      display: grid;
      grid-template-columns: minmax(0, 1.4fr) repeat(5, auto);
      gap: 12px;
      align-items: center;
    }
    .module-body, .subject-body {
      padding: 0 18px 18px;
    }
    .subject-kpis {
      display: grid;
      grid-template-columns: repeat(5, minmax(0, 1fr));
      gap: 10px;
      margin-bottom: 12px;
    }
    .tiny-card {
      border: 1px solid #e2eaf2;
      border-radius: 12px;
      padding: 10px 12px;
      background: #fbfdff;
    }
    .tiny-label {
      color: var(--muted);
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: .04em;
      font-weight: 700;
    }
    .tiny-value {
      margin-top: 5px;
      font-weight: 800;
      color: #1e3f63;
    }
    .search-box {
      width: 100%;
      margin: 0 0 12px;
      padding: 12px 14px;
      border-radius: 12px;
      border: 1px solid var(--line);
      font: inherit;
    }
    code.path {
      display: inline-block;
      padding: 2px 6px;
      border-radius: 8px;
      background: #eef4fb;
      color: #244a73;
      word-break: break-all;
    }
    .small {
      color: var(--muted);
      font-size: 12px;
    }
    .warn {
      color: #7a5600;
      background: #fff8e9;
      border-color: #f0d6a4;
    }
    @media print {
      body { background: #fff; }
      .shell { padding: 0; }
      .report { border: none; box-shadow: none; border-radius: 0; }
      .toolbar { display: none !important; }
      details.module, details.subject { break-inside: avoid; }
    }
    @media (max-width: 1180px) {
      .grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .two-col { grid-template-columns: 1fr; }
      details.module summary, details.subject summary { grid-template-columns: 1fr; }
      .subject-kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .bar-row { grid-template-columns: 1fr; }
    }
  </style>
</head>
<body>
  <div class="shell">
    <div class="report">
      <div class="hero">
        <div class="eyebrow">Alignment Matrix Audit</div>
        <h1>Curriculum, Assessment, Topic, and ELO Alignment</h1>
        <p>
          This report is ETDP's first-pass visual blueprint of how curriculum modules, subjects, topics,
          assessment criteria, associated assessment-criteria signals, lesson-plan rows, and NQF level
          descriptors relate inside one qualification.
        </p>
        <div class="toolbar">
          <button type="button" onclick="window.print()">Print / Save PDF</button>
          <a class="secondary" id="json-link" href="#">Open JSON</a>
          <a class="secondary" id="topic-link" href="/topics">Back To Topics</a>
        </div>
      </div>
      <div class="content">
        <div id="status" class="status">Loading alignment matrix...</div>
        <div id="app" style="display:none"></div>
      </div>
    </div>
  </div>
  <script>
    const qid = {{qualificationId}};
    const esc = (value) => String(value ?? '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
    const pct = (value) => {
      const n = Number(value || 0);
      if (!Number.isFinite(n)) return 0;
      return Math.max(0, Math.min(100, Math.round(n)));
    };
    const toneForValue = (value) => {
      const score = pct(value);
      if (score >= 75) return 'green';
      if (score >= 40) return 'amber';
      return 'red';
    };
    const listHtml = (items, emptyText = 'None recorded.') => {
      const list = Array.isArray(items) ? items.filter(Boolean) : [];
      if (!list.length) return `<div class="small">${esc(emptyText)}</div>`;
      return `<ul class="mini-list">${list.map((item) => `<li>${esc(item)}</li>`).join('')}</ul>`;
    };
    const barRow = (label, value, total, tone = 'blue', suffix = '') => {
      const safeTotal = Math.max(1, Number(total || 0));
      const percent = Math.max(0, Math.min(100, Math.round((Number(value || 0) / safeTotal) * 100)));
      return `
        <div class="bar-row">
          <div>${esc(label)}</div>
          <div class="bar-track"><div class="bar-fill ${tone}" style="width:${percent}%"></div></div>
          <div><strong>${esc(value)}</strong>${suffix ? ` ${esc(suffix)}` : ''}</div>
        </div>`;
    };
    const sourceState = (present, length) => present
      ? (Number(length || 0) > 0 ? '<span class="pill green">Available</span>' : '<span class="pill amber">File Found</span>')
      : '<span class="pill red">Missing</span>';

    async function render() {
      const status = document.getElementById('status');
      const app = document.getElementById('app');
      document.getElementById('json-link').href = `/api/AlignmentMatrix/summary?qualificationId=${qid}`;
      document.getElementById('topic-link').href = `/topics`;

      try {
        const res = await fetch(`/api/AlignmentMatrix/summary?qualificationId=${qid}`);
        if (!res.ok) {
          throw new Error(await res.text() || `HTTP ${res.status}`);
        }

        const data = await res.json();
        status.style.display = 'none';
        app.style.display = '';

        const modules = Array.isArray(data.modules) ? data.modules : [];
        const extractedOutcomes = Array.isArray(data.extractedExitLevelOutcomes) ? data.extractedExitLevelOutcomes : [];
        const nqfDescriptors = Array.isArray(data.nqfDescriptors) ? data.nqfDescriptors : [];
        const duplicateRisks = Array.isArray(data.duplicateCriteriaRisks) ? data.duplicateCriteriaRisks : [];
        const moduleTypeBreakdown = Array.isArray(data.moduleTypeBreakdown) ? data.moduleTypeBreakdown : [];
        const observations = Array.isArray(data.observations) ? data.observations : [];
        const caveats = Array.isArray(data.caveats) ? data.caveats : [];
        const source = data.sourceDocuments || {};
        const layer = data.layerStatus || {};
        const topicEvidence = data.topicEvidence || {};

        app.innerHTML = `
          <div class="grid">
            <div class="card">
              <div class="metric-label">Qualification</div>
              <div class="metric-value" style="font-size:24px">${esc(data.qualificationNumber || '#')}</div>
              <div class="metric-note">${esc(data.qualificationDescription || '')}</div>
            </div>
            <div class="card">
              <div class="metric-label">Curriculum Spine</div>
              <div class="metric-value">${esc(layer.topicCount || 0)}</div>
              <div class="metric-note">${esc(layer.subjectCount || 0)} subjects | ${esc(layer.criteriaCount || 0)} criteria rows</div>
            </div>
            <div class="card">
              <div class="metric-label">Topic Evidence Fit</div>
              <div class="metric-value">${pct(topicEvidence.coveragePercent)}%</div>
              <div class="metric-note">${esc(topicEvidence.mappedTopicsCount || 0)} mapped | ${esc(topicEvidence.gapTopicsCount || 0)} gap</div>
            </div>
            <div class="card">
              <div class="metric-label">Outcome Layer</div>
              <div class="metric-value">${esc(layer.extractedOutcomeCount || 0)}</div>
              <div class="metric-note">Extracted ELOs | DB outcomes: ${esc(layer.databaseOutcomeCount || 0)}</div>
            </div>
          </div>

          <div class="section two-col">
            <div class="card">
              <h2>Audit Readout</h2>
              <p class="section-copy">This summary is deliberately honest about what is present, what is inferred, and what is still missing from the formal assessment layer.</p>
              ${listHtml(observations, 'No observations recorded.')}
            </div>
            <div class="card warn">
              <h2>Caveats</h2>
              <p class="section-copy">These cautions protect the report from overstating certainty.</p>
              ${listHtml(caveats, 'No caveats recorded.')}
            </div>
          </div>

          <div class="section two-col">
            <div class="card">
              <h2>Source Document State</h2>
              <p class="section-copy">The matrix distinguishes files discovered on disk from text that ETDP can actively inspect and reason over.</p>
              <table class="source-table">
                <thead>
                  <tr><th>Source</th><th>Status</th><th>Path</th></tr>
                </thead>
                <tbody>
                  <tr>
                    <td>Curriculum Specification</td>
                    <td>${sourceState(source.curriculumSpecPresent, source.curriculumExtractLength)}</td>
                    <td>${source.curriculumSpecPath ? `<code class="path">${esc(source.curriculumSpecPath)}</code>` : '-'}</td>
                  </tr>
                  <tr>
                    <td>Assessment Specification</td>
                    <td>${sourceState(source.assessmentSpecPresent, source.assessmentExtractLength)}</td>
                    <td>${source.assessmentSpecPath ? `<code class="path">${esc(source.assessmentSpecPath)}</code>` : '-'}</td>
                  </tr>
                  <tr>
                    <td>Reference Alignment Workbook</td>
                    <td>${source.alignmentWorkbookPresent ? '<span class="pill green">Available</span>' : '<span class="pill red">Missing</span>'}</td>
                    <td>${source.alignmentWorkbookPath ? `<code class="path">${esc(source.alignmentWorkbookPath)}</code>` : '-'}</td>
                  </tr>
                  <tr>
                    <td>Subject Seed CSV</td>
                    <td>${source.subjectSeedCsvPath ? '<span class="pill green">Loaded</span>' : '<span class="pill red">Missing</span>'}</td>
                    <td>${source.subjectSeedCsvPath ? `<code class="path">${esc(source.subjectSeedCsvPath)}</code>` : '-'}</td>
                  </tr>
                  <tr>
                    <td>Topic Seed CSV</td>
                    <td>${source.topicSeedCsvPath ? '<span class="pill green">Loaded</span>' : '<span class="pill red">Missing</span>'}</td>
                    <td>${source.topicSeedCsvPath ? `<code class="path">${esc(source.topicSeedCsvPath)}</code>` : '-'}</td>
                  </tr>
                </tbody>
              </table>
              <div class="pills">
                <span class="pill blue">Source materials indexed: ${esc(layer.sourceMaterialCount || 0)}</span>
                <span class="pill blue">Curriculum text length: ${esc(source.curriculumExtractLength || 0)}</span>
                <span class="pill blue">Assessment text length: ${esc(source.assessmentExtractLength || 0)}</span>
              </div>
            </div>
            <div class="card">
              <h2>Matrix Layer Status</h2>
              <p class="section-copy">These counts show which layers of the full alignment stack are already populated.</p>
              <div class="bar-stack">
                ${barRow('Subjects', layer.subjectCount || 0, Math.max(1, layer.subjectCount || 1), 'blue')}
                ${barRow('Topics', layer.topicCount || 0, Math.max(1, layer.topicCount || 1), 'blue')}
                ${barRow('Criteria Rows', layer.criteriaCount || 0, Math.max(1, layer.criteriaCount || 1), 'blue')}
                ${barRow('LPN Rows', layer.lecturerToolkitRowCount || 0, Math.max(1, layer.topicCount || 1), 'green')}
                ${barRow('LessonPlans Table', layer.lessonPlanCount || 0, Math.max(1, layer.topicCount || 1), toneForValue((layer.lessonPlanCount || 0) * 100 / Math.max(1, layer.topicCount || 1)))}
                ${barRow('DB Outcomes', layer.databaseOutcomeCount || 0, Math.max(1, layer.extractedOutcomeCount || 1), toneForValue((layer.databaseOutcomeCount || 0) * 100 / Math.max(1, layer.extractedOutcomeCount || 1)))}
                ${barRow('Topic -> Outcome Links', layer.topicOutcomeMappedCount || 0, Math.max(1, layer.topicCount || 1), toneForValue((layer.topicOutcomeMappedCount || 0) * 100 / Math.max(1, layer.topicCount || 1)))}
                ${barRow('Summative Questionnaires', layer.knowledgeQuestionnaireCount || 0, Math.max(1, layer.subjectCount || 1), toneForValue((layer.knowledgeQuestionnaireCount || 0) * 100 / Math.max(1, layer.subjectCount || 1)))}
                ${barRow('Workbooks', layer.workbookCount || 0, Math.max(1, layer.subjectCount || 1), toneForValue((layer.workbookCount || 0) * 100 / Math.max(1, layer.subjectCount || 1)))}
              </div>
            </div>
          </div>

          <div class="section two-col">
            <div class="card">
              <h2>Extracted Exit Level Outcomes</h2>
              <p class="section-copy">These are the qualification-level applied competence statements currently extracted from the source documents.</p>
              ${extractedOutcomes.length ? extractedOutcomes.map((outcome) => `
                <div class="card" style="margin-top:10px;padding:14px 16px">
                  <div class="metric-label">${esc(outcome.code || 'ELO')}</div>
                  <div style="font-weight:800;font-size:16px;margin-top:5px">${esc(outcome.label || '')}</div>
                  <div class="metric-note" style="margin-top:8px">Source: ${esc(outcome.source || 'document extraction')}</div>
                </div>
              `).join('') : '<div class="small">No exit level outcomes were extracted.</div>'}
            </div>
            <div class="card">
              <h2>NQF Level Descriptor Fit</h2>
              <p class="section-copy">The qualification is currently marked as NQF level ${esc(data.qualificationNqfLevel || '-')}.</p>
              ${nqfDescriptors.length ? nqfDescriptors.map((item) => `
                <div class="tiny-card" style="margin-top:10px">
                  <div class="tiny-label">${esc(item.dimension || 'Descriptor')}</div>
                  <div class="small" style="margin-top:6px;color:#304b67">${esc(item.descriptor || '')}</div>
                </div>
              `).join('') : '<div class="small">No NQF descriptors were loaded from the workbook reference.</div>'}
            </div>
          </div>

          <div class="section two-col">
            <div class="card">
              <h2>Module Type Footprint</h2>
              <p class="section-copy">This mirrors the broad workbook logic: Knowledge, Practical Skill, and Work Experience modules contributing to one qualification picture.</p>
              <div class="bar-stack">
                ${moduleTypeBreakdown.map((item) => barRow(`${item.moduleTypeLabel} modules`, item.moduleCount || 0, Math.max(1, modules.length || 1), 'blue')).join('')}
                ${moduleTypeBreakdown.map((item) => barRow(`${item.moduleTypeLabel} topics`, item.topicCount || 0, Math.max(1, layer.topicCount || 1), 'green')).join('')}
              </div>
            </div>
            <div class="card">
              <h2>Topic Evidence Distribution</h2>
              <p class="section-copy">Topic evidence is currently the safest quantitative measure because repeated assessment-criteria text can hide real topic gaps.</p>
              <div class="pills">
                <span class="pill green">Mapped ${esc(topicEvidence.mappedTopicsCount || 0)}</span>
                <span class="pill amber">Developing ${esc(topicEvidence.developingTopicsCount || 0)}</span>
                <span class="pill red">Gap ${esc(topicEvidence.gapTopicsCount || 0)}</span>
                <span class="pill blue">Duplicate clusters ${esc(topicEvidence.duplicateCriteriaGroupCount || 0)}</span>
              </div>
              <div style="margin-top:14px" class="bar-stack">
                ${barRow('Topics with evidence', topicEvidence.topicsWithEvidenceCount || 0, Math.max(1, topicEvidence.topicCount || 1), 'green')}
                ${barRow('Mapped topics', topicEvidence.mappedTopicsCount || 0, Math.max(1, topicEvidence.topicCount || 1), 'green')}
                ${barRow('Developing topics', topicEvidence.developingTopicsCount || 0, Math.max(1, topicEvidence.topicCount || 1), 'amber')}
                ${barRow('Gap topics', topicEvidence.gapTopicsCount || 0, Math.max(1, topicEvidence.topicCount || 1), 'red')}
              </div>
            </div>
          </div>

          <div class="section">
            <div class="card">
              <h2>Criteria Duplication Risk</h2>
              <p class="section-copy">These are the most repeated assessment-criteria clusters currently appearing across multiple topics.</p>
              ${duplicateRisks.length ? duplicateRisks.map((risk) => `
                <div class="tiny-card" style="margin-top:10px">
                  <div class="tiny-label">Used in ${esc(risk.topicCount || 0)} topics</div>
                  <div style="margin-top:6px;font-weight:700;color:#304b67">${esc(risk.criteriaDescription || '')}</div>
                  <div class="small" style="margin-top:6px">${esc((risk.topics || []).slice(0, 12).join(' | '))}</div>
                </div>
              `).join('') : '<div class="small">No repeated criteria clusters were reported.</div>'}
            </div>
          </div>

          <div class="section">
            <h2>Module / Subject / Topic Matrix</h2>
            <p class="section-copy">Open a module, then a subject, to inspect the actual topic rows, criteria signals, associated assessment-criteria codes, LPN coverage, and topic-evidence score.</p>
            <input id="matrix-search" class="search-box" type="search" placeholder="Search by module, subject, topic, criteria, IAC code, or outcome..." />
            <div id="module-host">
              ${modules.map((module, moduleIndex) => `
                <details class="module" ${moduleIndex < 2 ? 'open' : ''} data-search="${esc([
                  module.moduleCode, module.phaseDescription, module.moduleTypeLabel,
                  ...(module.likelyOutcomeLabels || [])
                ].join(' ')).toLowerCase()}">
                  <summary>
                    <div>
                      <div style="font-size:12px;color:var(--muted);text-transform:uppercase;letter-spacing:.05em;font-weight:700">${esc(module.moduleTypeLabel || 'Module')}</div>
                      <div style="font-weight:800;font-size:18px;margin-top:4px">${esc(module.moduleCode || module.phaseCode || '')} - ${esc(module.phaseDescription || '')}</div>
                      <div class="small" style="margin-top:6px">${esc((module.likelyOutcomeLabels || []).join(' | ') || 'No dominant ELO signal detected')}</div>
                    </div>
                    <div><strong>${esc(module.subjectCount || 0)}</strong><div class="small">Subjects</div></div>
                    <div><strong>${esc(module.topicCount || 0)}</strong><div class="small">Topics</div></div>
                    <div><strong>${esc(module.lessonPlanRowCount || 0)}</strong><div class="small">LPN Rows</div></div>
                    <div><strong>${esc((module.nqfLevels || []).join(', ') || '-')}</strong><div class="small">NQF</div></div>
                  </summary>
                  <div class="module-body">
                    ${(module.subjects || []).map((subject) => `
                      <details class="subject" data-search="${esc([
                        subject.subjectCode,
                        subject.subjectDescription,
                        subject.likelyOutcomeLabel,
                        ...(subject.sampleAssociatedCodes || []),
                        ...((subject.topics || []).flatMap((topic) => [topic.topicCode, topic.topicDescription, topic.assessmentCriteriaDescription].filter(Boolean)))
                      ].join(' ')).toLowerCase()}">
                        <summary>
                          <div>
                            <div style="font-weight:800;font-size:16px">${esc(subject.subjectCode || '')} - ${esc(subject.subjectDescription || '')}</div>
                            <div class="small" style="margin-top:6px">
                              ${esc(subject.supportMode || 'Support')} | ${esc(subject.likelyOutcomeLabel || 'Cross-cutting / foundation')}
                              ${subject.likelyOutcomeConfidencePercent ? ` | confidence ${esc(subject.likelyOutcomeConfidencePercent)}%` : ''}
                            </div>
                          </div>
                          <div><strong>${esc(subject.topicCount || 0)}</strong><div class="small">Topics</div></div>
                          <div><strong>${esc(subject.criteriaClusterCount || 0)}</strong><div class="small">Criteria</div></div>
                          <div><strong>${esc(subject.lessonPlanRowCount || 0)}</strong><div class="small">LPN</div></div>
                          <div><strong>${esc(subject.averageCoveragePercent || 0)}%</strong><div class="small">Evidence</div></div>
                          <div><strong>${esc(subject.copyPasteRiskPercent || 0)}%</strong><div class="small">Duplication Risk</div></div>
                        </summary>
                        <div class="subject-body">
                          <div class="subject-kpis">
                            <div class="tiny-card"><div class="tiny-label">NQF Level</div><div class="tiny-value">${esc(subject.subjectNqfLevel || '-')}</div></div>
                            <div class="tiny-card"><div class="tiny-label">Credits</div><div class="tiny-value">${esc(subject.subjectCredits || '-')}</div></div>
                            <div class="tiny-card"><div class="tiny-label">Unique Criteria Clusters</div><div class="tiny-value">${esc(subject.uniqueCriteriaClusterCount || 0)}</div></div>
                            <div class="tiny-card"><div class="tiny-label">Support Mode</div><div class="tiny-value">${esc(subject.supportMode || '-')}</div></div>
                            <div class="tiny-card"><div class="tiny-label">IAC Codes</div><div class="tiny-value">${esc((subject.sampleAssociatedCodes || []).slice(0, 5).join(', ') || '-')}</div></div>
                          </div>
                          <table class="topic-table">
                            <thead>
                              <tr>
                                <th>Topic</th>
                                <th>Assessment Criteria</th>
                                <th>IAC Codes</th>
                                <th>Evidence</th>
                                <th>LPN</th>
                              </tr>
                            </thead>
                            <tbody>
                              ${(subject.topics || []).map((topic) => `
                                <tr>
                                  <td>
                                    <strong>${esc(topic.topicCode || '')}</strong><br />
                                    ${esc(topic.topicDescription || '')}
                                  </td>
                                  <td>
                                    <div><strong>${esc(topic.assessmentCriteriaNumber || '-')}</strong></div>
                                    <div class="small" style="margin-top:4px">${esc(topic.assessmentCriteriaDescription || '')}</div>
                                  </td>
                                  <td>${esc((topic.associatedAssessmentCriteriaCodes || []).join(', ') || '-')}</td>
                                  <td>
                                    <span class="pill ${esc(toneForValue(topic.coveragePercent || 0))}">${esc(topic.coverageBandLabel || 'Gap')} ${esc(topic.coveragePercent || 0)}%</span>
                                    <div class="small" style="margin-top:6px">${esc(topic.evidenceCount || 0)} evidence | ${esc(topic.distinctSourceCount || 0)} sources</div>
                                    ${Array.isArray(topic.topCitations) && topic.topCitations.length ? `<div class="small" style="margin-top:6px">${esc(topic.topCitations.join(' | '))}</div>` : ''}
                                  </td>
                                  <td>
                                    <strong>${esc(topic.lessonPlanRowCount || 0)}</strong> row(s)
                                    <div class="small" style="margin-top:6px">${esc(topic.lessonPlanCount || 0)} lesson-plan table row(s)</div>
                                  </td>
                                </tr>
                              `).join('')}
                            </tbody>
                          </table>
                        </div>
                      </details>
                    `).join('')}
                  </div>
                </details>
              `).join('')}
            </div>
          </div>
        `;

        const searchBox = document.getElementById('matrix-search');
        const subjectDetails = Array.from(document.querySelectorAll('details.subject'));
        const moduleDetails = Array.from(document.querySelectorAll('details.module'));
        searchBox?.addEventListener('input', () => {
          const query = String(searchBox.value || '').trim().toLowerCase();
          subjectDetails.forEach((node) => {
            node.style.display = !query || String(node.dataset.search || '').includes(query) ? '' : 'none';
          });
          moduleDetails.forEach((node) => {
            const moduleMatch = !query || String(node.dataset.search || '').includes(query);
            const visibleSubjects = Array.from(node.querySelectorAll('details.subject')).some((child) => child.style.display !== 'none');
            node.style.display = moduleMatch || visibleSubjects ? '' : 'none';
            if (query && (moduleMatch || visibleSubjects)) {
              node.open = true;
            }
          });
        });
      } catch (error) {
        status.innerHTML = `<span style="color:#b00020"><strong>Alignment Matrix failed:</strong> ${esc(error && error.message ? error.message : error)}</span>`;
      }
    }

    render();
  </script>
</body>
</html>
""";
        }

        private SourceDocumentState ResolveSourceDocuments(Qualification qualification)
        {
            var importsRoot = EtdpPaths.GetImportsRoot();
            var workspaceRoot = Directory.GetParent(EtdpPaths.GetProjectRoot())?.FullName ?? EtdpPaths.GetProjectRoot();
            var qualificationFolder = Path.Combine(importsRoot, qualification.QualificationNumber);
            var digits = ExtractDigits(qualification.QualificationNumber);
            var digitsFolder = Path.Combine(importsRoot, digits.Length >= 6 ? digits[..6] + "000" : digits);

            var curriculumSpecPath = ResolveFirstExistingFile(qualificationFolder, CurriculumFileNames)
                ?? ResolveFirstExistingFile(digitsFolder, CurriculumFileNames);
            var assessmentSpecPath = ResolveFirstExistingFile(qualificationFolder, AssessmentFileNames)
                ?? ResolveFirstExistingFile(digitsFolder, AssessmentFileNames);
            var alignmentWorkbookPath = Path.Combine(workspaceRoot, "VocationalLLM", "data", "knowledge_taxonomy", "Alignment_Matrix", "alignment_matrix.xlsx");

            return new SourceDocumentState
            {
                CurriculumSpecPath = curriculumSpecPath ?? string.Empty,
                CurriculumSpecPresent = !string.IsNullOrWhiteSpace(curriculumSpecPath) && System.IO.File.Exists(curriculumSpecPath),
                AssessmentSpecPath = assessmentSpecPath ?? string.Empty,
                AssessmentSpecPresent = !string.IsNullOrWhiteSpace(assessmentSpecPath) && System.IO.File.Exists(assessmentSpecPath),
                AlignmentWorkbookPath = alignmentWorkbookPath,
                AlignmentWorkbookPresent = System.IO.File.Exists(alignmentWorkbookPath)
            };
        }

        private static string ResolveFirstExistingFile(string directory, IEnumerable<string> fileNames)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return string.Empty;
            }

            foreach (var fileName in fileNames)
            {
                var path = Path.Combine(directory, fileName);
                if (System.IO.File.Exists(path))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        private string LoadCurriculumText(Qualification qualification, string curriculumPath)
        {
            var extracted = _context.SourceMaterials
                .AsNoTracking()
                .Where(item =>
                    item.FileName == "QC_CurriculumSpecification.pdf" &&
                    (item.QualificationCode == qualification.QualificationNumber ||
                     item.QualificationDescription == qualification.QualificationDescription))
                .OrderByDescending(item => item.Id)
                .Select(item => item.ExtractedText)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return CleanText(extracted);
            }

            if (!string.IsNullOrWhiteSpace(curriculumPath) && System.IO.File.Exists(curriculumPath))
            {
                return CleanText(ExtractTextFromPdf(curriculumPath));
            }

            return string.Empty;
        }

        private static string LoadAssessmentText(string assessmentPath)
        {
            if (string.IsNullOrWhiteSpace(assessmentPath) || !System.IO.File.Exists(assessmentPath))
            {
                return string.Empty;
            }

            return CleanText(ExtractTextFromPdf(assessmentPath));
        }

        private static string ExtractTextFromPdf(string path)
        {
            var builder = new StringBuilder();
            using var reader = new PdfReader(path);
            using var document = new PdfDocument(reader);
            for (var page = 1; page <= document.GetNumberOfPages(); page++)
            {
                builder.AppendLine(PdfTextExtractor.GetTextFromPage(document.GetPage(page)));
            }

            return builder.ToString();
        }

        private static string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text.Replace('\r', '\n');
            normalized = normalized.Replace("\u2022", " ");
            normalized = normalized.Replace("\u00a0", " ");
            normalized = Regex.Replace(normalized, @"[ \t]+", " ");
            normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
            return normalized.Trim();
        }

        private static List<ExtractedOutcome> ExtractExitLevelOutcomes(string curriculumText, string assessmentText)
        {
            var results = new List<ExtractedOutcome>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddRange(string text, string source)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                var lines = text
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.Length >= 28 && line.Length <= 220)
                    .Where(line => !Regex.IsMatch(line, @"\b(KM|PM|WM)-\d{2}\b", RegexOptions.IgnoreCase))
                    .Where(line => !line.Contains("Credits", StringComparison.OrdinalIgnoreCase))
                    .Where(line => !line.Contains("Table of content", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var line in lines)
                {
                    var candidate = line;
                    if (candidate.Contains("Exit Level Outcome", StringComparison.OrdinalIgnoreCase))
                    {
                        candidate = Regex.Replace(candidate, @"(?i)^.*?Exit Level Outcome\s*\d+\s*:\s*", string.Empty).Trim();
                    }

                    if (!Regex.IsMatch(candidate, @"^(Perform|Dismantle|Diagnose|Remove|Install)\b", RegexOptions.IgnoreCase))
                    {
                        continue;
                    }

                    candidate = Regex.Replace(candidate, @"\(NQF\s*Level.*?\)", string.Empty, RegexOptions.IgnoreCase).Trim();
                    candidate = Regex.Replace(candidate, @"\d{3,}$", string.Empty).Trim();
                    candidate = CanonicalizeExitLevelOutcome(candidate);
                    if (string.IsNullOrWhiteSpace(candidate))
                    {
                        continue;
                    }

                    var dedupeKey = NormalizeKey(candidate);
                    if (!seen.Add(dedupeKey))
                    {
                        continue;
                    }

                    results.Add(new ExtractedOutcome
                    {
                        Code = $"ELO {results.Count + 1}",
                        Label = candidate,
                        Source = source
                    });
                }
            }

            AddRange(assessmentText, "Assessment specification extraction");
            AddRange(curriculumText, "Curriculum extraction");

            return results
                .Take(3)
                .Select((item, index) =>
                {
                    item.Code = $"ELO {index + 1}";
                    return item;
                })
                .ToList();
        }

        private static string CanonicalizeExitLevelOutcome(string candidate)
        {
            var normalized = NormalizeKey(candidate);

            if (normalized.Contains("perform preventative and scheduled maintenance"))
            {
                return "Perform preventative and scheduled maintenance on diesel vehicles";
            }

            if (normalized.Contains("dismantle, inspect") && normalized.Contains("repair and assemble"))
            {
                return "Dismantle, inspect, assess, repair and assemble diesel engine and vehicle system components";
            }

            if (normalized.Contains("diagnose and repair faults"))
            {
                return "Diagnose and repair faults in diesel engine and vehicle systems and their components";
            }

            return string.Empty;
        }

        private static List<NqfDescriptor> LoadNqfDescriptors(string nqfLevel, string workbookPath)
        {
            if (string.IsNullOrWhiteSpace(workbookPath) || !System.IO.File.Exists(workbookPath))
            {
                return new List<NqfDescriptor>();
            }

            var levelNumber = ParseFirstInteger(nqfLevel);
            if (levelNumber <= 0)
            {
                return new List<NqfDescriptor>();
            }

            try
            {
                using var document = SpreadsheetDocument.Open(workbookPath, false);
                var workbookPart = document.WorkbookPart;
                if (workbookPart?.Workbook?.Sheets == null)
                {
                    return new List<NqfDescriptor>();
                }

                var sheet = workbookPart.Workbook.Sheets
                    .Elements<Sheet>()
                    .FirstOrDefault(item => string.Equals(item.Name?.Value, "NQF_Level_Measurement", StringComparison.OrdinalIgnoreCase));

                if (sheet == null)
                {
                    return new List<NqfDescriptor>();
                }

                var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
                var rows = worksheetPart.Worksheet.Descendants<Row>().ToList();
                if (rows.Count == 0)
                {
                    return new List<NqfDescriptor>();
                }

                var headerRow = rows.First();
                var targetCell = headerRow
                    .Elements<Cell>()
                    .Select(cell => new
                    {
                        Column = GetColumnName(cell.CellReference?.Value),
                        Value = GetCellText(workbookPart, cell)
                    })
                    .FirstOrDefault(item => HeaderMatchesLevel(item.Value, levelNumber));

                if (targetCell == null || string.IsNullOrWhiteSpace(targetCell.Column))
                {
                    return new List<NqfDescriptor>();
                }

                var descriptors = new List<NqfDescriptor>();
                foreach (var row in rows.Skip(1))
                {
                    var cell = row.Elements<Cell>().FirstOrDefault(item =>
                        string.Equals(GetColumnName(item.CellReference?.Value), targetCell.Column, StringComparison.OrdinalIgnoreCase));
                    if (cell == null)
                    {
                        continue;
                    }

                    var text = GetCellText(workbookPart, cell);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var dimension = text.Split(',', 2, StringSplitOptions.TrimEntries)[0];
                    descriptors.Add(new NqfDescriptor
                    {
                        Dimension = dimension,
                        Descriptor = text.Trim()
                    });
                }

                return descriptors;
            }
            catch
            {
                return new List<NqfDescriptor>();
            }
        }

        private static bool HeaderMatchesLevel(string header, int levelNumber)
        {
            var normalized = NormalizeKey(header);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (normalized.Contains($"level {levelNumber}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return levelNumber switch
            {
                1 => normalized.Contains("level one", StringComparison.OrdinalIgnoreCase),
                2 => normalized.Contains("level two", StringComparison.OrdinalIgnoreCase),
                3 => normalized.Contains("level three", StringComparison.OrdinalIgnoreCase),
                4 => normalized.Contains("level four", StringComparison.OrdinalIgnoreCase),
                5 => normalized.Contains("level five", StringComparison.OrdinalIgnoreCase),
                6 => normalized.Contains("level six", StringComparison.OrdinalIgnoreCase),
                7 => normalized.Contains("level seven", StringComparison.OrdinalIgnoreCase),
                8 => normalized.Contains("level eight", StringComparison.OrdinalIgnoreCase),
                9 => normalized.Contains("level nine", StringComparison.OrdinalIgnoreCase),
                10 => normalized.Contains("level ten", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private SeedArtifactPaths ResolveSeedArtifacts(Qualification qualification)
        {
            var importsRoot = EtdpPaths.GetImportsRoot();
            var qualificationFolder = Path.Combine(importsRoot, qualification.QualificationNumber, "CognitiveScan", "PipelineJobs");
            if (Directory.Exists(qualificationFolder))
            {
                foreach (var folder in Directory.GetDirectories(qualificationFolder).OrderByDescending(Path.GetFileName))
                {
                    var subjectCsv = Path.Combine(folder, "artifacts", "normalized_KnowledgeSubjects.csv");
                    var topicCsv = Path.Combine(folder, "artifacts", "normalized_KnowledgeTopics.csv");
                    if (System.IO.File.Exists(subjectCsv) && System.IO.File.Exists(topicCsv))
                    {
                        return new SeedArtifactPaths
                        {
                            SubjectCsvPath = subjectCsv,
                            TopicCsvPath = topicCsv
                        };
                    }
                }
            }

            return new SeedArtifactPaths();
        }

        private static List<SeedSubjectRow> ReadSeedSubjects(string path)
        {
            var rows = ReadDelimitedRows(path);
            if (rows.Count <= 1)
            {
                return new List<SeedSubjectRow>();
            }

            var header = rows[0];
            var cPhasesCode = FindColumn(header, "Phases Code", "PhasesCode");
            var cPhasesDescription = FindColumn(header, "Phases Description");
            var cSubjectCode = FindColumn(header, "SubjectCode", "Subject Code");
            var cSubjectDescription = FindColumn(header, "Subject Description");
            var cSubjectCredits = FindColumn(header, "Subject Credits");
            var cSubjectNqfLevel = FindColumn(header, "Subject NQF Level");
            var cSubjectPercentage = FindColumn(header, "Subject Percentage");

            return rows.Skip(1)
                .Where(row => row.Count > 0)
                .Select(row => new SeedSubjectRow
                {
                    PhasesCode = Cell(row, cPhasesCode),
                    PhasesDescription = Cell(row, cPhasesDescription),
                    SubjectCode = Cell(row, cSubjectCode),
                    SubjectDescription = Cell(row, cSubjectDescription),
                    SubjectCredits = ParseNullableDouble(Cell(row, cSubjectCredits)),
                    SubjectNqfLevel = ParseNullableInt(Cell(row, cSubjectNqfLevel)),
                    SubjectPercentage = ParseNullableInt(Cell(row, cSubjectPercentage))
                })
                .Where(row => !string.IsNullOrWhiteSpace(row.SubjectCode))
                .ToList();
        }

        private static List<SeedTopicRow> ReadSeedTopics(string path)
        {
            var rows = ReadDelimitedRows(path);
            if (rows.Count <= 1)
            {
                return new List<SeedTopicRow>();
            }

            var header = rows[0];
            var cPhasesCode = FindColumn(header, "Phases Code", "PhasesCode");
            var cPhasesDescription = FindColumn(header, "Phases Description");
            var cSubjectCode = FindColumn(header, "Subject Code", "SubjectCode");
            var cSubjectDescription = FindColumn(header, "Subject Decription", "Subject Description");
            var cTopicCode = FindColumn(header, "Topic Code");
            var cTopicDescription = FindColumn(header, "Topic Description");
            var cCriteriaNumber = FindColumn(header, "Assessment Criteria Number", "Assessment Criteria Id");
            var cCriteriaDescription = FindColumn(header, "Assesment Criteria Description", "Assessment Criteria Description");

            return rows.Skip(1)
                .Where(row => row.Count > 0)
                .Select(row => new SeedTopicRow
                {
                    PhasesCode = Cell(row, cPhasesCode),
                    PhasesDescription = Cell(row, cPhasesDescription),
                    SubjectCode = Cell(row, cSubjectCode),
                    SubjectDescription = Cell(row, cSubjectDescription),
                    TopicCode = Cell(row, cTopicCode),
                    TopicDescription = Cell(row, cTopicDescription),
                    AssessmentCriteriaNumber = Cell(row, cCriteriaNumber),
                    AssessmentCriteriaDescription = Cell(row, cCriteriaDescription)
                })
                .Where(row => !string.IsNullOrWhiteSpace(row.SubjectCode) && !string.IsNullOrWhiteSpace(row.TopicCode))
                .ToList();
        }

        private static List<List<string>> ReadDelimitedRows(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                return new List<List<string>>();
            }

            return System.IO.File.ReadAllLines(path)
                .Select(ParseDelimitedLine)
                .ToList();
        }

        private static List<string> ParseDelimitedLine(string line)
        {
            var values = new List<string>();
            if (line == null)
            {
                values.Add(string.Empty);
                return values;
            }

            var builder = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (ch == ';' && !inQuotes)
                {
                    values.Add(builder.ToString().Trim());
                    builder.Clear();
                    continue;
                }

                builder.Append(ch);
            }

            values.Add(builder.ToString().Trim());
            return values;
        }

        private static int FindColumn(IReadOnlyList<string> header, params string[] names)
        {
            for (var i = 0; i < header.Count; i++)
            {
                var normalized = NormalizeKey(header[i]);
                if (names.Any(name => NormalizeKey(name) == normalized))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string Cell(IReadOnlyList<string> row, int index)
        {
            if (index < 0 || index >= row.Count)
            {
                return string.Empty;
            }

            return row[index]?.Trim() ?? string.Empty;
        }

        private static string GetCellText(WorkbookPart workbookPart, Cell cell)
        {
            if (cell.CellValue == null)
            {
                return cell.InnerText?.Trim() ?? string.Empty;
            }

            var value = cell.CellValue.InnerText;
            if (cell.DataType == null)
            {
                return value.Trim();
            }

            if (cell.DataType.Value == CellValues.SharedString && int.TryParse(value, out var index))
            {
                return workbookPart.SharedStringTablePart?.SharedStringTable?.ElementAtOrDefault(index)?.InnerText?.Trim() ?? string.Empty;
            }

            return value.Trim();
        }

        private static string GetColumnName(string? cellReference)
        {
            if (string.IsNullOrWhiteSpace(cellReference))
            {
                return string.Empty;
            }

            return new string(cellReference.Where(char.IsLetter).ToArray());
        }

        private static int? ParseNullableInt(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private static double? ParseNullableDouble(string value)
        {
            return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private static ETD.Api.Models.Topic? ResolveTopic(IEnumerable<ETD.Api.Models.Topic> dbTopicRows, string topicCode, string topicDescription)
        {
            return dbTopicRows.FirstOrDefault(topic =>
                string.Equals(NormalizeKey(topic.TopicCode), NormalizeKey(topicCode), StringComparison.OrdinalIgnoreCase)) ??
                   dbTopicRows.FirstOrDefault(topic =>
                       string.Equals(NormalizeKey(topic.TopicDescription), NormalizeKey(topicDescription), StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildTopicKey(string subjectCode, string topicCode, string topicDescription)
        {
            return $"{NormalizeKey(subjectCode)}|{NormalizeKey(topicCode)}|{NormalizeKey(topicDescription)}";
        }

        private static List<string> ExtractIacCodes(string description)
        {
            return IacCodeRegex
                .Matches(description ?? string.Empty)
                .Select(match => match.Value.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static LikelyOutcomeResult DetermineLikelyOutcome(
            string subjectCode,
            string subjectDescription,
            string moduleDescription,
            string combinedTopicText,
            IReadOnlyList<ExtractedOutcome> extractedOutcomes)
        {
            var combined = NormalizeKey($"{subjectCode} {subjectDescription} {moduleDescription} {combinedTopicText}");

            if (CrossCuttingSignals.Any(signal => combined.Contains(NormalizeKey(signal), StringComparison.OrdinalIgnoreCase)))
            {
                return new LikelyOutcomeResult
                {
                    Code = string.Empty,
                    Label = "Cross-cutting / foundation support",
                    ConfidencePercent = 30,
                    SupportMode = "Indirect support"
                };
            }

            if (extractedOutcomes.Count == 0)
            {
                return new LikelyOutcomeResult
                {
                    Code = string.Empty,
                    Label = "Outcome mapping pending",
                    ConfidencePercent = 0,
                    SupportMode = "Unmapped"
                };
            }

            var scored = extractedOutcomes
                .Select((outcome, index) =>
                {
                    var boosterScore = OutcomeKeywordBoosters.TryGetValue(index + 1, out var boosters)
                        ? boosters.Count(boost => combined.Contains(NormalizeKey(boost), StringComparison.OrdinalIgnoreCase))
                        : 0;
                    var tokenScore = NormalizeKey(outcome.Label)
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(token => token.Length > 4)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(token => combined.Contains(token, StringComparison.OrdinalIgnoreCase));
                    return new
                    {
                        Outcome = outcome,
                        Score = boosterScore * 3 + tokenScore
                    };
                })
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Outcome.Code)
                .ToList();

            var best = scored.FirstOrDefault();
            var second = scored.Skip(1).FirstOrDefault();
            if (best == null || best.Score <= 0)
            {
                return new LikelyOutcomeResult
                {
                    Code = string.Empty,
                    Label = "Cross-cutting / foundation support",
                    ConfidencePercent = 25,
                    SupportMode = "Indirect support"
                };
            }

            var confidence = Math.Min(95, 45 + (best.Score * 8) + Math.Max(0, best.Score - (second?.Score ?? 0)) * 4);
            return new LikelyOutcomeResult
            {
                Code = best.Outcome.Code,
                Label = best.Outcome.Label,
                ConfidencePercent = confidence,
                SupportMode = confidence >= 72 ? "Primary support" : "Likely support"
            };
        }

        private static string ExtractModuleCode(string phasesCode)
        {
            var match = CodePrefixRegex.Match(phasesCode ?? string.Empty);
            return match.Success ? match.Value.ToUpperInvariant() : phasesCode ?? string.Empty;
        }

        private static string ResolveModuleType(string phasesCode)
        {
            var code = ExtractModuleCode(phasesCode);
            if (code.StartsWith("KM", StringComparison.OrdinalIgnoreCase)) return "KM";
            if (code.StartsWith("PM", StringComparison.OrdinalIgnoreCase)) return "PM";
            if (code.StartsWith("WM", StringComparison.OrdinalIgnoreCase)) return "WM";
            return "OTHER";
        }

        private static string ResolveModuleTypeLabel(string phasesCode)
        {
            return ResolveModuleType(phasesCode) switch
            {
                "KM" => "Knowledge",
                "PM" => "Practical Skill",
                "WM" => "Work Experience",
                _ => "Module"
            };
        }

        private static int ClampPercent(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            return Math.Max(0, Math.Min(100, (int)Math.Round(value)));
        }

        private static string NormalizeKey(string value)
        {
            var normalized = WhitespaceRegex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), " ");
            return normalized.Replace("’", "'").Replace("`", "'");
        }

        private static int ParseFirstInteger(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            var digits = new string(value.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var parsed) ? parsed : 0;
        }

        private static string ExtractDigits(string value)
        {
            return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        private static List<string> BuildObservations(AlignmentMatrixReport report)
        {
            var observations = new List<string>
            {
                $"{report.QualificationNumber} currently contains {report.LayerStatus.SubjectCount} subjects, {report.LayerStatus.TopicCount} topics, and {report.LayerStatus.CriteriaCount} assessment-criteria rows in ETDP.",
                $"ETDP already holds {report.LayerStatus.LecturerToolkitRowCount} schedule-ready lesson-plan LPN rows for this qualification, even though the formal LessonPlans table currently contains {report.LayerStatus.LessonPlanCount} row(s).",
                $"The outcome layer is incomplete in the database: {report.LayerStatus.DatabaseOutcomeCount} outcome row(s) and {report.LayerStatus.TopicOutcomeMappedCount} topic-to-outcome links are stored, so the report currently relies on extracted or inferred outcome alignment.",
                $"Summative and formative assessment banks are still missing from ETDP's structured tables: KnowledgeQuestionnaires = {report.LayerStatus.KnowledgeQuestionnaireCount}, Workbooks = {report.LayerStatus.WorkbookCount}, LearnerGuides = {report.LayerStatus.LearnerGuideCount}.",
                $"Topic evidence coverage is currently {report.TopicEvidence.CoveragePercent}%, with {report.TopicEvidence.MappedTopicsCount} mapped topics, {report.TopicEvidence.DevelopingTopicsCount} developing topics, and {report.TopicEvidence.GapTopicsCount} gap topics.",
                $"Detected {report.TopicEvidence.DuplicateCriteriaGroupCount} duplicated assessment-criteria cluster group(s), supporting the audit concern that repeated criteria text can mask topic-level curriculum risk."
            };

            if (report.SourceDocuments.AssessmentSpecPresent && report.SourceDocuments.AssessmentExtractLength > 0)
            {
                observations.Add("The assessment specification file is present and text-readable, so ETDP can now surface that source in the same audit picture instead of relying only on curriculum rows.");
            }

            return observations;
        }

        private static List<string> BuildCaveats(AlignmentMatrixReport report)
        {
            var caveats = new List<string>();

            if (report.LayerStatus.DatabaseOutcomeCount == 0)
            {
                caveats.Add("Exit Level Outcome alignment is currently extracted and heuristically inferred, not yet persisted as a formal database relationship for this qualification.");
            }

            if (report.LayerStatus.TopicOutcomeMappedCount == 0)
            {
                caveats.Add("No topic rows are explicitly linked to outcome IDs in the database, so subject and module alignment to ELOs should be treated as provisional until the outcome layer is loaded.");
            }

            if (report.LayerStatus.KnowledgeQuestionnaireCount == 0 || report.LayerStatus.WorkbookCount == 0)
            {
                caveats.Add("The summative knowledge-question and formative workbook-activity layers are still structurally absent, so this matrix cannot yet prove full assessment-instrument coverage.");
            }

            if (!report.SourceDocuments.AssessmentSpecPresent)
            {
                caveats.Add("The separate assessment specification file was not found in the expected qualification import folder.");
            }

            if (report.SourceDocuments.AssessmentSpecPresent && report.SourceDocuments.AssessmentExtractLength == 0)
            {
                caveats.Add("The assessment specification file is present, but ETDP could not confidently extract inspectable text from it for this run.");
            }

            if (!report.NqfDescriptors.Any())
            {
                caveats.Add("NQF level descriptor rows were not loaded from the workbook reference, so the NQF measurement panel is incomplete.");
            }

            return caveats;
        }

        private sealed class SeedArtifactPaths
        {
            public string SubjectCsvPath { get; set; } = string.Empty;
            public string TopicCsvPath { get; set; } = string.Empty;
        }

        private sealed class SeedSubjectRow
        {
            public string PhasesCode { get; set; } = string.Empty;
            public string PhasesDescription { get; set; } = string.Empty;
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public double? SubjectCredits { get; set; }
            public int? SubjectNqfLevel { get; set; }
            public int? SubjectPercentage { get; set; }
        }

        private sealed class SeedTopicRow
        {
            public string PhasesCode { get; set; } = string.Empty;
            public string PhasesDescription { get; set; } = string.Empty;
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string AssessmentCriteriaNumber { get; set; } = string.Empty;
            public string AssessmentCriteriaDescription { get; set; } = string.Empty;
        }

        private sealed class LikelyOutcomeResult
        {
            public string Code { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
            public int ConfidencePercent { get; set; }
            public string SupportMode { get; set; } = string.Empty;
        }

        private sealed class CachedAlignmentMatrix
        {
            public DateTime GeneratedAtUtc { get; set; }
            public AlignmentMatrixReport Report { get; set; } = new();
        }

        public sealed class AlignmentMatrixReport
        {
            public int QualificationId { get; set; }
            public string QualificationNumber { get; set; } = string.Empty;
            public string QualificationDescription { get; set; } = string.Empty;
            public string QualificationNqfLevel { get; set; } = string.Empty;
            public string QualificationCredits { get; set; } = string.Empty;
            public bool UsesOutcomes { get; set; }
            public string GeneratedAtUtc { get; set; } = string.Empty;
            public SourceDocumentState SourceDocuments { get; set; } = new();
            public MatrixLayerStatus LayerStatus { get; set; } = new();
            public TopicEvidenceSnapshot TopicEvidence { get; set; } = new();
            public List<ExtractedOutcome> ExtractedExitLevelOutcomes { get; set; } = new();
            public List<NqfDescriptor> NqfDescriptors { get; set; } = new();
            public List<ModuleTypeBreakdown> ModuleTypeBreakdown { get; set; } = new();
            public List<ModuleSummary> Modules { get; set; } = new();
            public List<DuplicateCriteriaRisk> DuplicateCriteriaRisks { get; set; } = new();
            public List<string> Observations { get; set; } = new();
            public List<string> Caveats { get; set; } = new();
        }

        public sealed class SourceDocumentState
        {
            public bool CurriculumSpecPresent { get; set; }
            public string CurriculumSpecPath { get; set; } = string.Empty;
            public int CurriculumExtractLength { get; set; }
            public bool AssessmentSpecPresent { get; set; }
            public string AssessmentSpecPath { get; set; } = string.Empty;
            public int AssessmentExtractLength { get; set; }
            public bool AlignmentWorkbookPresent { get; set; }
            public string AlignmentWorkbookPath { get; set; } = string.Empty;
            public string SubjectSeedCsvPath { get; set; } = string.Empty;
            public string TopicSeedCsvPath { get; set; } = string.Empty;
            public int SourceMaterialCount { get; set; }
        }

        public sealed class MatrixLayerStatus
        {
            public int SubjectCount { get; set; }
            public int TopicCount { get; set; }
            public int CriteriaCount { get; set; }
            public int SourceMaterialCount { get; set; }
            public int ExtractedOutcomeCount { get; set; }
            public int DatabaseOutcomeCount { get; set; }
            public int TopicOutcomeMappedCount { get; set; }
            public int LecturerToolkitRowCount { get; set; }
            public int LessonPlanCount { get; set; }
            public int KnowledgeQuestionnaireCount { get; set; }
            public int WorkbookCount { get; set; }
            public int LearnerGuideCount { get; set; }
        }

        public sealed class TopicEvidenceSnapshot
        {
            public int SourceMaterialCount { get; set; }
            public int SourceChunkCount { get; set; }
            public int TopicCount { get; set; }
            public int TopicsWithEvidenceCount { get; set; }
            public int MappedTopicsCount { get; set; }
            public int DevelopingTopicsCount { get; set; }
            public int GapTopicsCount { get; set; }
            public int CoveragePercent { get; set; }
            public int DuplicateCriteriaGroupCount { get; set; }
        }

        public sealed class ExtractedOutcome
        {
            public string Code { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
            public string Source { get; set; } = string.Empty;
        }

        public sealed class NqfDescriptor
        {
            public string Dimension { get; set; } = string.Empty;
            public string Descriptor { get; set; } = string.Empty;
        }

        public sealed class ModuleTypeBreakdown
        {
            public string ModuleType { get; set; } = string.Empty;
            public string ModuleTypeLabel { get; set; } = string.Empty;
            public int ModuleCount { get; set; }
            public int SubjectCount { get; set; }
            public int TopicCount { get; set; }
            public int LessonPlanRowCount { get; set; }
        }

        public sealed class ModuleSummary
        {
            public string ModuleCode { get; set; } = string.Empty;
            public string PhaseCode { get; set; } = string.Empty;
            public string PhaseDescription { get; set; } = string.Empty;
            public string ModuleType { get; set; } = string.Empty;
            public string ModuleTypeLabel { get; set; } = string.Empty;
            public int SubjectCount { get; set; }
            public int TopicCount { get; set; }
            public int LessonPlanRowCount { get; set; }
            public List<int> NqfLevels { get; set; } = new();
            public List<string> LikelyOutcomeLabels { get; set; } = new();
            public List<SubjectAlignmentRow> Subjects { get; set; } = new();
        }

        public sealed class SubjectAlignmentRow
        {
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public int? SubjectNqfLevel { get; set; }
            public double? SubjectCredits { get; set; }
            public int? SubjectPercentage { get; set; }
            public int TopicCount { get; set; }
            public int CriteriaClusterCount { get; set; }
            public int UniqueCriteriaClusterCount { get; set; }
            public int CopyPasteRiskPercent { get; set; }
            public int LessonPlanRowCount { get; set; }
            public int AverageCoveragePercent { get; set; }
            public string LikelyOutcomeCode { get; set; } = string.Empty;
            public string LikelyOutcomeLabel { get; set; } = string.Empty;
            public int LikelyOutcomeConfidencePercent { get; set; }
            public string SupportMode { get; set; } = string.Empty;
            public List<string> SampleAssociatedCodes { get; set; } = new();
            public List<TopicAlignmentRow> Topics { get; set; } = new();
        }

        public sealed class TopicAlignmentRow
        {
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string AssessmentCriteriaNumber { get; set; } = string.Empty;
            public string AssessmentCriteriaDescription { get; set; } = string.Empty;
            public List<string> AssociatedAssessmentCriteriaCodes { get; set; } = new();
            public int CoveragePercent { get; set; }
            public string CoverageBand { get; set; } = string.Empty;
            public string CoverageBandLabel { get; set; } = string.Empty;
            public int EvidenceCount { get; set; }
            public int DistinctSourceCount { get; set; }
            public int BestConfidencePercent { get; set; }
            public int LessonPlanRowCount { get; set; }
            public int LessonPlanCount { get; set; }
            public int? TopicOutcomeId { get; set; }
            public List<string> TopCitations { get; set; } = new();
        }

        public sealed class DuplicateCriteriaRisk
        {
            public string CriteriaDescription { get; set; } = string.Empty;
            public int TopicCount { get; set; }
            public List<string> Topics { get; set; } = new();
        }

        private static string ResolveVocationalLlmRoot()
        {
            var current = Directory.GetCurrentDirectory();
            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(current, "..", "VocationalLLM")),
                Path.GetFullPath(Path.Combine(current, "VocationalLLM")),
                @"D:\ETDP\VocationalLLM"
            };

            return candidates.FirstOrDefault(path =>
                Directory.Exists(path) &&
                Directory.Exists(Path.Combine(path, "data", "knowledge_taxonomy"))) ?? candidates[0];
        }

        private static bool IsSupportedSubjectMatterFile(string path)
        {
            var ext = Path.GetExtension(path);
            return ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".docx", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase);
        }

        private static List<RecentIngestFailure> ReadRecentIngestFailures(SqliteConnection conn, string discipline)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT source_path, error, created_at
FROM ingest_events
WHERE status = 'failed'
  AND (COALESCE(source_path, '') LIKE $pathLike
    OR COALESCE(vocational_discipline, '') = $discipline)
ORDER BY id DESC
LIMIT 5;";
            cmd.Parameters.AddWithValue("$pathLike", $"%vocational_disciplines%{discipline}%");
            cmd.Parameters.AddWithValue("$discipline", discipline);
            using var reader = cmd.ExecuteReader();
            var rows = new List<RecentIngestFailure>();
            while (reader.Read())
            {
                rows.Add(new RecentIngestFailure
                {
                    SourcePath = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    Error = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    CreatedAt = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                });
            }

            return rows;
        }

        private static string DetermineDigestionStage(SubjectMatterDigestionSnapshot status)
        {
            if (!status.UploadFolderExists)
            {
                return "missing_upload_folder";
            }
            if (status.FileCount <= 0)
            {
                return "waiting_for_upload";
            }
            if (status.DocumentCount <= 0)
            {
                return "waiting_for_text_ingestion";
            }
            if (status.DocumentCount < Math.Max(1, status.FileCount - status.FailedIngestEvents))
            {
                return "text_ingestion_in_progress";
            }
            if (status.ChunkCount <= 0)
            {
                return "chunking_pending";
            }
            if (status.EmbeddedChunkCount <= 0)
            {
                return "text_index_ready_embeddings_pending";
            }
            if (status.EmbeddedChunkCount < status.ChunkCount)
            {
                return "embedding_in_progress";
            }
            return "ready_for_topic_evidence";
        }

        private static string BuildDigestionEstimate(SubjectMatterDigestionSnapshot status)
        {
            return status.Stage switch
            {
                "missing_upload_folder" => "The upload folder is missing. Create it before adding subject matter.",
                "waiting_for_upload" => "No supported source files are in the upload folder yet.",
                "waiting_for_text_ingestion" => "Files are present but no text has been digested yet. Auto-ingest usually checks every 20 seconds after the VocationalLLM service is running.",
                "text_ingestion_in_progress" => "Text extraction is still running. Large technical PDFs can take several minutes; refresh to see the document and chunk counts rise.",
                "chunking_pending" => "Documents exist but chunks have not been written yet. This should normally move quickly after text extraction completes.",
                "text_index_ready_embeddings_pending" => "Text digestion is complete enough for keyword retrieval. Semantic vector embedding is still pending, so topic evidence may stay low until embeddings or the evidence scan catches up.",
                "embedding_in_progress" => "Semantic embeddings are being populated. This can take longer than text chunking because every chunk must be sent through nomic-embed-text.",
                _ => "Subject matter is digested and ready for topic evidence refresh."
            };
        }

        public sealed class SubjectMatterDigestionSnapshot
        {
            public string Discipline { get; set; } = string.Empty;
            public int QualificationId { get; set; }
            public string UploadPath { get; set; } = string.Empty;
            public string DatabasePath { get; set; } = string.Empty;
            public bool UploadFolderExists { get; set; }
            public bool DatabaseExists { get; set; }
            public string DatabaseError { get; set; } = string.Empty;
            public int FileCount { get; set; }
            public int PdfCount { get; set; }
            public int DocxCount { get; set; }
            public int DocumentCount { get; set; }
            public int ChunkCount { get; set; }
            public int EmbeddedChunkCount { get; set; }
            public long RawCharCount { get; set; }
            public int OkIngestEvents { get; set; }
            public int FailedIngestEvents { get; set; }
            public int FileDigestionPercent { get; set; }
            public int EmbeddingPercent { get; set; }
            public string Stage { get; set; } = string.Empty;
            public string EstimatedMessage { get; set; } = string.Empty;
            public List<RecentIngestFailure> RecentFailures { get; set; } = new();
        }

        public sealed class RecentIngestFailure
        {
            public string SourcePath { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
            public string CreatedAt { get; set; } = string.Empty;
        }
    }
}
