using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ETD.Api.Data;
using ETD.Api.Utils;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Services
{
    public sealed class CurriculumPipelineService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".txt", ".md"
        };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OcrExtractionService _ocrExtractionService;
        private readonly CurriculumKnowledgeScanService _curriculumKnowledgeScanService;
        private readonly CurriculumDeliveryPilotService _curriculumDeliveryPilotService;
        private readonly ILogger<CurriculumPipelineService> _logger;
        private readonly ConcurrentDictionary<string, CurriculumPipelineJob> _jobs = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<int, string> _latestJobByQualification = new();

        public CurriculumPipelineService(
            IServiceScopeFactory scopeFactory,
            OcrExtractionService ocrExtractionService,
            CurriculumKnowledgeScanService curriculumKnowledgeScanService,
            CurriculumDeliveryPilotService curriculumDeliveryPilotService,
            ILogger<CurriculumPipelineService> logger)
        {
            _scopeFactory = scopeFactory;
            _ocrExtractionService = ocrExtractionService;
            _curriculumKnowledgeScanService = curriculumKnowledgeScanService;
            _curriculumDeliveryPilotService = curriculumDeliveryPilotService;
            _logger = logger;
        }

        public async Task<CurriculumPipelineJob> QueueQualificationAsync(int qualificationId, int? startPage, bool forceRestart, CancellationToken cancellationToken = default)
        {
            if (qualificationId <= 0)
            {
                throw new InvalidOperationException("QualificationId is required.");
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var qualification = await db.Qualifications
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == qualificationId, cancellationToken);
            if (qualification == null)
            {
                throw new InvalidOperationException("Qualification not found.");
            }

            if (!forceRestart &&
                _latestJobByQualification.TryGetValue(qualificationId, out var existingJobId) &&
                _jobs.TryGetValue(existingJobId, out var existingJob) &&
                string.Equals(existingJob.Status, "running", StringComparison.OrdinalIgnoreCase))
            {
                return existingJob;
            }

            var safeQualificationFolder = MakeSafeFolderName(qualification.QualificationNumber, $"Qualification_{qualificationId}");
            var qualificationFolder = Path.Combine(EtdpPaths.GetImportsRoot(), safeQualificationFolder);
            var jobsFolder = Path.Combine(qualificationFolder, "CognitiveScan", "PipelineJobs");
            Directory.CreateDirectory(jobsFolder);

            var createdAt = DateTime.UtcNow;
            var jobId = $"cp-{qualificationId}-{createdAt:yyyyMMddHHmmssfff}";
            var jobFolder = Path.Combine(jobsFolder, jobId);
            Directory.CreateDirectory(jobFolder);

            var requestedStartPage = Math.Max(1, startPage.GetValueOrDefault(1));
            var job = new CurriculumPipelineJob
            {
                Id = jobId,
                QualificationId = qualification.Id,
                QualificationNumber = qualification.QualificationNumber ?? string.Empty,
                QualificationDescription = qualification.QualificationDescription ?? string.Empty,
                Status = "queued",
                CurrentStage = "queued",
                ProgressPercent = 0,
                RequestedStartPage = requestedStartPage,
                CreatedAtUtc = createdAt,
                UpdatedAtUtc = createdAt,
                JobFolder = jobFolder,
                QualificationFolder = qualificationFolder
            };

            SeedStages(job);
            _jobs[jobId] = job;
            _latestJobByQualification[qualificationId] = jobId;
            await SaveJobAsync(job, cancellationToken);

            _ = Task.Run(() => RunJobAsync(jobId), CancellationToken.None);
            return job;
        }

        public async Task<CurriculumPipelineJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(jobId)) return null;
            if (_jobs.TryGetValue(jobId, out var inMemory))
            {
                return inMemory;
            }

            var job = await LoadJobFromDiskAsync(jobId, cancellationToken);
            if (job != null)
            {
                _jobs[jobId] = job;
                if (job.QualificationId > 0)
                {
                    _latestJobByQualification[job.QualificationId] = job.Id;
                }
            }
            return job;
        }

        public async Task<CurriculumPipelineJob?> GetLatestJobAsync(int qualificationId, CancellationToken cancellationToken = default)
        {
            if (qualificationId <= 0) return null;

            if (_latestJobByQualification.TryGetValue(qualificationId, out var jobId))
            {
                return await GetJobAsync(jobId, cancellationToken);
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var qualification = await db.Qualifications
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == qualificationId, cancellationToken);
            if (qualification == null)
            {
                return null;
            }

            var safeQualificationFolder = MakeSafeFolderName(qualification.QualificationNumber, $"Qualification_{qualificationId}");
            var jobsFolder = Path.Combine(EtdpPaths.GetImportsRoot(), safeQualificationFolder, "CognitiveScan", "PipelineJobs");
            if (!Directory.Exists(jobsFolder))
            {
                return null;
            }

            var candidate = Directory.GetDirectories(jobsFolder)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => Path.Combine(path, "job.json"))
                .FirstOrDefault(File.Exists);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(candidate, cancellationToken);
                var job = JsonSerializer.Deserialize<CurriculumPipelineJob>(json, JsonOptions);
                if (job == null) return null;
                _jobs[job.Id] = job;
                _latestJobByQualification[qualificationId] = job.Id;
                return job;
            }
            catch
            {
                return null;
            }
        }

        private async Task RunJobAsync(string jobId)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
            {
                return;
            }

            try
            {
                job.Status = "running";
                job.StartedAtUtc = DateTime.UtcNow;
                await MarkStageAsync(job, "locate-source", "running", 8, "Locating uploaded curriculum source.");

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var qualification = await db.Qualifications
                    .AsNoTracking()
                    .FirstOrDefaultAsync(q => q.Id == job.QualificationId);
                if (qualification == null)
                {
                    throw new InvalidOperationException("Qualification not found.");
                }

                var sourcePath = ResolveCurriculumSourcePath(job.QualificationFolder);
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    throw new InvalidOperationException("Curriculum Specification document is required before pipeline execution.");
                }

                job.SourcePath = sourcePath;
                var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
                if (!AllowedExt.Contains(ext))
                {
                    throw new InvalidOperationException($"Unsupported curriculum source extension: {ext}");
                }

                await MarkStageAsync(job, "locate-source", "completed", 14, Path.GetFileName(sourcePath) ?? sourcePath);

                await MarkStageAsync(job, "normalize-source", "running", 24, "Preparing normalized working copy.");
                var normalizedPath = await CreateNormalizedWorkingCopyAsync(job, sourcePath, ext);
                job.NormalizedSourcePath = normalizedPath;
                await MarkStageAsync(job, "normalize-source", "completed", 30, Path.GetFileName(normalizedPath));

                await MarkStageAsync(job, "extract-text", "running", 46, "Extracting baseline text.");
                var extractedText = await ExtractTextForPipelineAsync(normalizedPath, ext, job.RequestedStartPage);
                job.BaselineExtractLength = extractedText.Length;
                await WriteTextArtifactAsync(job, "baseline_extract.txt", extractedText);
                await MarkStageAsync(job, "extract-text", "completed", 52, $"Baseline text length: {extractedText.Length:n0} chars.");

                await MarkStageAsync(job, "ocr-enrichment", "running", 66, "Applying OCR enrichment.");
                var enrichedText = await _ocrExtractionService.EnhanceExtractedTextAsync(normalizedPath, ext, extractedText);
                job.EnrichedExtractLength = enrichedText.Length;
                await WriteTextArtifactAsync(job, "ocr_enriched_extract.txt", enrichedText);
                await MarkStageAsync(job, "ocr-enrichment", "completed", 72, $"Enriched text length: {enrichedText.Length:n0} chars.");

                await MarkStageAsync(job, "template-detect", "running", 82, "Detecting standard curriculum template markers.");
                var templateDetection = DetectTemplate(enrichedText);
                var templatePath = Path.Combine(job.JobFolder, "template-detection.json");
                await File.WriteAllTextAsync(templatePath, JsonSerializer.Serialize(templateDetection, JsonOptions));
                job.TemplateDetectionPath = templatePath;
                job.TemplateLikelyStandard = templateDetection.LikelyStandardTemplate;
                job.TemplateKey = templateDetection.TemplateKey;
                job.TemplateVersionHint = templateDetection.VersionHint;
                job.TemplateConfidencePercent = templateDetection.ConfidencePercent;
                job.TemplateMatchedAnchors = templateDetection.MatchedAnchors;
                job.TemplateNotes = templateDetection.Notes;
                await MarkStageAsync(
                    job,
                    "template-detect",
                    "completed",
                    88,
                    $"Template confidence: {templateDetection.ConfidencePercent}% ({templateDetection.MatchedAnchors.Count} anchors, key {templateDetection.TemplateKey}).");

                await MarkStageAsync(job, "generate-artifacts", "running", 94, "Generating ETDP curriculum artifacts.");
                var outputDir = Path.Combine(job.JobFolder, "artifacts");
                Directory.CreateDirectory(outputDir);
                var artifacts = _curriculumKnowledgeScanService.GenerateArtifacts(
                    normalizedPath,
                    ext,
                    enrichedText,
                    qualification.QualificationNumber ?? string.Empty,
                    outputDir,
                    ext == ".pdf" ? job.RequestedStartPage : null);

                job.OutputDir = outputDir;
                job.Artifacts = new CurriculumPipelineArtifactSummary
                {
                    ExtractTextPath = artifacts.ExtractTextPath,
                    PhasesCsvPath = artifacts.PhasesCsvPath,
                    SubjectCsvPath = artifacts.SubjectCsvPath,
                    TopicCsvPath = artifacts.TopicCsvPath,
                    ReportJsonPath = artifacts.ReportJsonPath,
                    ModuleCount = artifacts.ModuleCount,
                    CurriculumPhaseCount = artifacts.CurriculumPhaseCount,
                    KnowledgeSubjectCount = artifacts.KnowledgeSubjectCount,
                    TopicCount = artifacts.TopicCount,
                    Warnings = artifacts.Warnings?.ToList() ?? new List<string>()
                };
                job.Warnings = job.Artifacts.Warnings.ToList();

                await MarkStageAsync(job, "generate-artifacts", "completed", 82, $"Artifacts ready in {outputDir}");

                await MarkStageAsync(job, "resource-import", "running", 88, "Importing qualification-linked source material.");
                var deliveryPilot = await _curriculumDeliveryPilotService.ExecuteQualificationPilotAsync(
                    new CurriculumDeliveryPilotService.DeliveryPilotRequest
                    {
                        QualificationId = qualification.Id,
                        JobFolder = job.JobFolder,
                        PopulateLessonPlanDrafts = true
                    });

                job.Artifacts.DeliveryPilot = new CurriculumDeliveryPilotArtifactSummary
                {
                    ArtifactsDirectory = deliveryPilot.ArtifactsDirectory,
                    DetectedExternalResourceFolder = deliveryPilot.DetectedExternalResourceFolder,
                    SourceChunksPath = deliveryPilot.SourceChunksPath,
                    TopicSourceMapPath = deliveryPilot.TopicSourceMapPath,
                    CriteriaSourceMapPath = deliveryPilot.CriteriaSourceMapPath,
                    LessonPlanDraftsPath = deliveryPilot.LessonPlanDraftsPath,
                    SourceMaterialCount = deliveryPilot.SourceMaterialCount,
                    SourceChunkCount = deliveryPilot.SourceChunkCount,
                    TopicCount = deliveryPilot.TopicCount,
                    CriteriaCount = deliveryPilot.CriteriaCount,
                    TopicsMappedCount = deliveryPilot.TopicsMappedCount,
                    CriteriaMappedCount = deliveryPilot.CriteriaMappedCount,
                    LessonPlanDraftsCreated = deliveryPilot.LessonPlanDraftsCreated,
                    LessonPlanDraftsUpdated = deliveryPilot.LessonPlanDraftsUpdated,
                    LessonPlanDraftsSkipped = deliveryPilot.LessonPlanDraftsSkipped,
                    ImportedToInboxCount = deliveryPilot.Import.CopiedToInboxCount,
                    IndexedCount = deliveryPilot.Import.SyncCreatedCount,
                    ImportSkippedCount = deliveryPilot.Import.SyncSkippedCount,
                    ImportFailedCount = deliveryPilot.Import.SyncFailedCount,
                    CoverageReportPath = deliveryPilot.Import.CoverageReportPath,
                    Warnings = deliveryPilot.Warnings.ToList()
                };

                await MarkStageAsync(
                    job,
                    "resource-import",
                    "completed",
                    90,
                    $"Resources: {deliveryPilot.SourceMaterialCount} indexed | inbox copied {deliveryPilot.Import.CopiedToInboxCount}.");

                await MarkStageAsync(
                    job,
                    "topic-source-map",
                    deliveryPilot.SourceMaterialCount > 0 ? "completed" : "skipped",
                    96,
                    deliveryPilot.SourceMaterialCount > 0
                        ? $"Mapped {deliveryPilot.TopicsMappedCount}/{deliveryPilot.TopicCount} topics and {deliveryPilot.CriteriaMappedCount}/{deliveryPilot.CriteriaCount} criteria."
                        : "No qualification-linked source material was available for mapping.");

                await MarkStageAsync(
                    job,
                    "lesson-plan-drafts",
                    deliveryPilot.LessonPlanDraftsCreated > 0 || deliveryPilot.LessonPlanDraftsUpdated > 0
                        ? "completed"
                        : "skipped",
                    100,
                    $"Lesson drafts created: {deliveryPilot.LessonPlanDraftsCreated}, updated: {deliveryPilot.LessonPlanDraftsUpdated}, skipped: {deliveryPilot.LessonPlanDraftsSkipped}.");

                job.Warnings = job.Warnings
                    .Concat(deliveryPilot.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                job.Status = "completed";
                job.CurrentStage = "completed";
                job.ProgressPercent = 100;
                job.CompletedAtUtc = DateTime.UtcNow;
                job.UpdatedAtUtc = DateTime.UtcNow;
                await SaveJobAsync(job);
            }
            catch (Exception ex)
            {
                job.Status = "failed";
                job.CurrentStage = "failed";
                job.Error = ex.Message;
                job.ProgressPercent = Math.Max(job.ProgressPercent, 1);
                job.CompletedAtUtc = DateTime.UtcNow;
                job.UpdatedAtUtc = DateTime.UtcNow;
                MarkCurrentStageFailed(job, ex.Message);
                await SaveJobAsync(job);
                _logger.LogError(ex, "Curriculum pipeline job {JobId} failed for qualification {QualificationId}", job.Id, job.QualificationId);
            }
        }

        private async Task<string> CreateNormalizedWorkingCopyAsync(CurriculumPipelineJob job, string sourcePath, string ext)
        {
            var normalizedDir = Path.Combine(job.JobFolder, "normalized");
            Directory.CreateDirectory(normalizedDir);

            var targetPath = Path.Combine(normalizedDir, $"normalized{ext}");
            await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(target);

            return targetPath;
        }

        private static void SeedStages(CurriculumPipelineJob job)
        {
            job.Stages = new List<CurriculumPipelineStage>
            {
                new() { Key = "locate-source", Label = "Locate source", Status = "pending" },
                new() { Key = "normalize-source", Label = "Normalize source", Status = "pending" },
                new() { Key = "extract-text", Label = "Baseline extract", Status = "pending" },
                new() { Key = "ocr-enrichment", Label = "OCR enrich", Status = "pending" },
                new() { Key = "template-detect", Label = "Template detect", Status = "pending" },
                new() { Key = "generate-artifacts", Label = "Generate artifacts", Status = "pending" },
                new() { Key = "resource-import", Label = "Import resources", Status = "pending" },
                new() { Key = "topic-source-map", Label = "Map subject matter", Status = "pending" },
                new() { Key = "lesson-plan-drafts", Label = "Seed lesson drafts", Status = "pending" }
            };
        }

        private async Task MarkStageAsync(CurriculumPipelineJob job, string key, string status, int progressPercent, string detail)
        {
            var stage = job.Stages.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
            if (stage == null)
            {
                stage = new CurriculumPipelineStage { Key = key, Label = key, Status = "pending" };
                job.Stages.Add(stage);
            }

            if (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase) && stage.StartedAtUtc == null)
            {
                stage.StartedAtUtc = DateTime.UtcNow;
            }

            if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "skipped", StringComparison.OrdinalIgnoreCase))
            {
                stage.CompletedAtUtc = DateTime.UtcNow;
            }

            stage.Status = status;
            stage.Detail = detail;
            job.CurrentStage = key;
            job.ProgressPercent = progressPercent;
            job.UpdatedAtUtc = DateTime.UtcNow;
            await SaveJobAsync(job);
        }

        private static void MarkCurrentStageFailed(CurriculumPipelineJob job, string error)
        {
            var stage = job.Stages
                .FirstOrDefault(s => string.Equals(s.Key, job.CurrentStage, StringComparison.OrdinalIgnoreCase) && !string.Equals(s.Status, "completed", StringComparison.OrdinalIgnoreCase))
                ?? job.Stages.LastOrDefault();
            if (stage == null) return;

            stage.Status = "failed";
            stage.Detail = error;
            stage.CompletedAtUtc = DateTime.UtcNow;
        }

        private async Task SaveJobAsync(CurriculumPipelineJob job, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(job.JobFolder);
            var path = Path.Combine(job.JobFolder, "job.json");
            var json = JsonSerializer.Serialize(job, JsonOptions);
            await File.WriteAllTextAsync(path, json, cancellationToken);
        }

        private async Task<CurriculumPipelineJob?> LoadJobFromDiskAsync(string jobId, CancellationToken cancellationToken)
        {
            var root = EtdpPaths.GetImportsRoot();
            if (!Directory.Exists(root)) return null;

            var candidates = Directory.GetFiles(root, "job.json", SearchOption.AllDirectories)
                .Where(path => path.Contains(Path.DirectorySeparatorChar + "PipelineJobs" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var candidate in candidates)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(candidate, cancellationToken);
                    var job = JsonSerializer.Deserialize<CurriculumPipelineJob>(json, JsonOptions);
                    if (job != null && string.Equals(job.Id, jobId, StringComparison.OrdinalIgnoreCase))
                    {
                        return job;
                    }
                }
                catch
                {
                    // Ignore malformed job files while scanning.
                }
            }

            return null;
        }

        private static string ResolveCurriculumSourcePath(string qualificationFolder)
        {
            if (!Directory.Exists(qualificationFolder)) return string.Empty;
            return Directory.GetFiles(qualificationFolder, "QC_*.*")
                .Where(path => AllowedExt.Contains(Path.GetExtension(path)))
                .FirstOrDefault(path =>
                    (Path.GetFileName(path) ?? string.Empty).StartsWith("QC_CurriculumSpecification", StringComparison.OrdinalIgnoreCase))
                ?? string.Empty;
        }

        private static string MakeSafeFolderName(string? rawValue, string fallback)
        {
            var value = Regex.Replace(rawValue ?? string.Empty, @"[^\w\- ]+", string.Empty).Trim().Replace(" ", "_");
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static async Task WriteTextArtifactAsync(CurriculumPipelineJob job, string fileName, string text)
        {
            var path = Path.Combine(job.JobFolder, fileName);
            await File.WriteAllTextAsync(path, text ?? string.Empty);
        }

        private static CurriculumTemplateDetection DetectTemplate(string text)
        {
            var normalized = Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
            var blankTemplateAnchors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["section_1_curriculum_summary"] = @"section\s*1\s*:\s*curriculum\s+summary",
                ["section_2_profile"] = @"section\s*2\s*:\s*occupational.*skills?\s+programme\s+profile",
                ["section_3_component_specs"] = @"section\s*3\s*:\s*curriculum\s+component\s+specifications",
                ["section_4_work_experience"] = @"section\s*4\.?\s*statement\s+of\s+work\s+experience",
                ["curriculum_structure"] = @"1\.3\s*curriculum\s+structure",
                ["knowledge_module_specs"] = @"3\.1\s*knowledge\s+module\s+specifications",
                ["knowledge_module_detail"] = @"3\.1\.1\s*detailing\s+knowledge\s+module\s*\(km\)\s*contents",
                ["knowledge_topics"] = @"list\s+of\s+knowledge\s+topics",
                ["topic_elements"] = @"topic\s+elements",
                ["iac_weight"] = @"internal\s+assessment\s+criteria\s*\(iac\)\s*(and\s+weight)?",
                ["practical_module_specs"] = @"3\.2\s*practical\s+skill\s+module\s*\(pm\)\s*specifications",
                ["practical_module_detail"] = @"3\.2\.1\s*detailing\s+practical\s*module\s*\(pm\)\s*contents",
                ["practical_activities"] = @"list\s+of\s+practical\s+skill\s+activities",
                ["work_module_specs"] = @"3\.3\s*work\s+experience\s+module\s*\(wm\)\s*specifications",
                ["work_module_detail"] = @"3\.3\.1\s*detailing\s+work\s+experience\s+module\s*\(wm\)\s*contents",
                ["sequencing_integration"] = @"3\.4\s*possible\s+sequencing\s+and\s+integration"
            };

            var publishedSpecAnchors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["section_1_curriculum_summary"] = @"section\s*1\s*:\s*curriculum\s+summary",
                ["section_2_profile"] = @"section\s*2\s*:\s*occupational\s+profile",
                ["section_3_component_specs"] = @"section\s*3\s*:\s*curriculum\s+component\s+specifications",
                ["section_3a_knowledge_specs"] = @"section\s*3a\s*:\s*knowledge\s+module\s+specifications",
                ["knowledge_module_list"] = @"list\s+of\s+knowledge\s+modules\s+for\s+which\s+specifications\s+are\s+included",
                ["knowledge_module_purpose"] = @"purpose\s+of\s+the\s+knowledge\s+modules?",
                ["guidelines_for_topics"] = @"guidelines\s+for\s+topics",
                ["topic_elements"] = @"topic\s+elements",
                ["iac_weight"] = @"internal\s+assessment\s+criteria(?:\s+and\s+weight)?",
                ["section_3b_practical_specs"] = @"section\s*3b\s*:\s*practical\s+skill\s+module\s+specifications",
                ["section_3c_work_specs"] = @"section\s*3c\s*:\s*work\s+experience\s+module\s+specifications"
            };

            static List<string> MatchAnchors(string normalizedText, Dictionary<string, string> anchors) => anchors
                .Where(entry => Regex.IsMatch(normalizedText, entry.Value, RegexOptions.IgnoreCase))
                .Select(entry => entry.Key)
                .ToList();

            var blankMatched = MatchAnchors(normalized, blankTemplateAnchors);
            var publishedMatched = MatchAnchors(normalized, publishedSpecAnchors);
            var blankConfidence = (int)Math.Round((blankMatched.Count / (double)blankTemplateAnchors.Count) * 100d);
            var publishedConfidence = (int)Math.Round((publishedMatched.Count / (double)publishedSpecAnchors.Count) * 100d);

            var blankLikely = blankMatched.Count >= 6 &&
                blankMatched.Contains("section_3_component_specs") &&
                (blankMatched.Contains("knowledge_module_specs") || blankMatched.Contains("practical_module_specs") || blankMatched.Contains("work_module_specs"));
            var publishedLikely = publishedMatched.Count >= 5 &&
                publishedMatched.Contains("section_3_component_specs") &&
                publishedMatched.Contains("section_3a_knowledge_specs") &&
                publishedMatched.Contains("iac_weight");

            var usePublished = publishedLikely || (!blankLikely && publishedConfidence > blankConfidence);
            var matched = usePublished ? publishedMatched : blankMatched;
            var confidence = usePublished ? publishedConfidence : blankConfidence;
            var likelyStandard = usePublished ? publishedLikely : blankLikely;
            var notes = new List<string>();

            if (usePublished)
            {
                notes.Add("Published QCTO curriculum specification layout detected (SECTION 3A/3B/3C).");
            }
            else
            {
                notes.Add("Authoring/template-style QCTO curriculum layout detected.");
            }

            if (!likelyStandard)
            {
                notes.Add("QCTO curriculum-template anchors are incomplete. Template-specific parsing should remain cautious.");
            }
            if (!matched.Contains("section_3_component_specs"))
            {
                notes.Add("SECTION 3 curriculum-component anchor not found.");
            }
            if (!matched.Contains("topic_elements"))
            {
                notes.Add("Topic-elements anchor not found.");
            }

            return new CurriculumTemplateDetection
            {
                TemplateKey = likelyStandard
                    ? (usePublished ? "qcto_curriculum_published_spec_v1" : "qcto_curriculum_template_v1_2")
                    : "unknown",
                VersionHint = likelyStandard
                    ? (usePublished ? "published" : "1.2")
                    : string.Empty,
                ConfidencePercent = confidence,
                LikelyStandardTemplate = likelyStandard,
                MatchedAnchors = matched,
                Notes = notes
            };
        }

        private static async Task<string> ExtractTextForPipelineAsync(string path, string ext, int startPage)
        {
            if (ext == ".txt" || ext == ".md")
            {
                return CleanExtractedText(await File.ReadAllTextAsync(path));
            }

            if (ext == ".docx")
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return await ExtractTextFromDocxStreamAsync(stream);
            }

            if (ext == ".pdf")
            {
                var start = Math.Max(1, startPage);
                var text = ExtractTextFromPdf(path, start);
                if (string.IsNullOrWhiteSpace(text) && start > 1)
                {
                    text = ExtractTextFromPdf(path, 1);
                }
                return CleanExtractedText(text);
            }

            throw new InvalidOperationException($"Unsupported curriculum source extension: {ext}");
        }

        private static async Task<string> ExtractTextFromDocxStreamAsync(Stream stream)
        {
            if (stream.CanSeek) stream.Position = 0;
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            using var doc = WordprocessingDocument.Open(ms, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null) return string.Empty;

            var sb = new StringBuilder();
            foreach (var para in body.Descendants<Paragraph>())
            {
                var line = string.Join("", para
                    .Descendants<Text>()
                    .Select(t => t.Text));
                if (!string.IsNullOrWhiteSpace(line))
                {
                    sb.AppendLine(line.Trim());
                }
            }

            return CleanExtractedText(sb.ToString());
        }

        private static List<(int Number, string Text)> ReadPdfPages(string path)
        {
            using var reader = new PdfReader(path);
            using var doc = new PdfDocument(reader);
            var pages = new List<(int Number, string Text)>();
            var totalPages = doc.GetNumberOfPages();
            for (var pageNumber = 1; pageNumber <= totalPages; pageNumber++)
            {
                var text = PdfTextExtractor.GetTextFromPage(doc.GetPage(pageNumber)) ?? string.Empty;
                pages.Add((pageNumber, text));
            }
            return pages;
        }

        private static string ExtractTextFromPdf(string path, int startPage)
        {
            var pdfPages = ReadPdfPages(path);
            var pages = new List<(int Number, List<string> Lines)>();
            var sb = new StringBuilder();
            foreach (var page in pdfPages)
            {
                if (page.Number < startPage) continue;

                var pageText = DocumentTextCleaner.CleanPdfPageText(page.Text ?? string.Empty);
                if (string.IsNullOrWhiteSpace(pageText)) continue;

                var lines = pageText
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => Regex.Replace(x ?? string.Empty, @"\s+", " ").Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Where(x => !DocumentTextCleaner.IsNoiseLine(x))
                    .ToList();
                if (lines.Count == 0) continue;

                pages.Add((page.Number, lines));
            }

            var repeatedBoundaryKeys = DocumentTextCleaner.DetectRepeatedBoundaryLineKeys(
                pages.Select(x => (IReadOnlyList<string>)x.Lines).ToList());

            foreach (var page in pages)
            {
                var filteredLines = page.Lines
                    .Where(line => !repeatedBoundaryKeys.Contains(DocumentTextCleaner.NormalizeLineKey(line)))
                    .ToList();
                if (filteredLines.Count == 0) continue;

                var normalizedPageText = DocumentTextCleaner.CleanPdfPageText(string.Join("\n", filteredLines));
                if (string.IsNullOrWhiteSpace(normalizedPageText)) continue;
                if (DocumentTextCleaner.IsLikelyBoilerplateParagraph(normalizedPageText) &&
                    DocumentTextCleaner.WordCount(normalizedPageText) < 220)
                {
                    continue;
                }

                sb.AppendLine(normalizedPageText);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string CleanExtractedText(string text)
        {
            return DocumentTextCleaner.Clean(text, preservePdfPageMarkers: true);
        }

        public sealed class CurriculumPipelineJob
        {
            public string Id { get; set; } = string.Empty;
            public int QualificationId { get; set; }
            public string QualificationNumber { get; set; } = string.Empty;
            public string QualificationDescription { get; set; } = string.Empty;
            public string Status { get; set; } = "queued";
            public string CurrentStage { get; set; } = "queued";
            public int ProgressPercent { get; set; }
            public int RequestedStartPage { get; set; } = 1;
            public DateTime CreatedAtUtc { get; set; }
            public DateTime UpdatedAtUtc { get; set; }
            public DateTime? StartedAtUtc { get; set; }
            public DateTime? CompletedAtUtc { get; set; }
            public string QualificationFolder { get; set; } = string.Empty;
            public string JobFolder { get; set; } = string.Empty;
            public string SourcePath { get; set; } = string.Empty;
            public string NormalizedSourcePath { get; set; } = string.Empty;
            public string OutputDir { get; set; } = string.Empty;
            public int BaselineExtractLength { get; set; }
            public int EnrichedExtractLength { get; set; }
            public string TemplateDetectionPath { get; set; } = string.Empty;
            public bool TemplateLikelyStandard { get; set; }
            public string TemplateKey { get; set; } = string.Empty;
            public string TemplateVersionHint { get; set; } = string.Empty;
            public int TemplateConfidencePercent { get; set; }
            public List<string> TemplateMatchedAnchors { get; set; } = new();
            public List<string> TemplateNotes { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
            public string? Error { get; set; }
            public List<CurriculumPipelineStage> Stages { get; set; } = new();
            public CurriculumPipelineArtifactSummary? Artifacts { get; set; }
        }

        public sealed class CurriculumPipelineStage
        {
            public string Key { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
            public string Status { get; set; } = "pending";
            public string Detail { get; set; } = string.Empty;
            public DateTime? StartedAtUtc { get; set; }
            public DateTime? CompletedAtUtc { get; set; }
        }

        public sealed class CurriculumPipelineArtifactSummary
        {
            public string ExtractTextPath { get; set; } = string.Empty;
            public string PhasesCsvPath { get; set; } = string.Empty;
            public string SubjectCsvPath { get; set; } = string.Empty;
            public string TopicCsvPath { get; set; } = string.Empty;
            public string ReportJsonPath { get; set; } = string.Empty;
            public int ModuleCount { get; set; }
            public int CurriculumPhaseCount { get; set; }
            public int KnowledgeSubjectCount { get; set; }
            public int TopicCount { get; set; }
            public List<string> Warnings { get; set; } = new();
            public CurriculumDeliveryPilotArtifactSummary? DeliveryPilot { get; set; }
        }

        public sealed class CurriculumDeliveryPilotArtifactSummary
        {
            public string ArtifactsDirectory { get; set; } = string.Empty;
            public string DetectedExternalResourceFolder { get; set; } = string.Empty;
            public string SourceChunksPath { get; set; } = string.Empty;
            public string TopicSourceMapPath { get; set; } = string.Empty;
            public string CriteriaSourceMapPath { get; set; } = string.Empty;
            public string LessonPlanDraftsPath { get; set; } = string.Empty;
            public string CoverageReportPath { get; set; } = string.Empty;
            public int ImportedToInboxCount { get; set; }
            public int IndexedCount { get; set; }
            public int ImportSkippedCount { get; set; }
            public int ImportFailedCount { get; set; }
            public int SourceMaterialCount { get; set; }
            public int SourceChunkCount { get; set; }
            public int TopicCount { get; set; }
            public int CriteriaCount { get; set; }
            public int TopicsMappedCount { get; set; }
            public int CriteriaMappedCount { get; set; }
            public int LessonPlanDraftsCreated { get; set; }
            public int LessonPlanDraftsUpdated { get; set; }
            public int LessonPlanDraftsSkipped { get; set; }
            public List<string> Warnings { get; set; } = new();
        }

        public sealed class CurriculumTemplateDetection
        {
            public string TemplateKey { get; set; } = string.Empty;
            public string VersionHint { get; set; } = string.Empty;
            public bool LikelyStandardTemplate { get; set; }
            public int ConfidencePercent { get; set; }
            public List<string> MatchedAnchors { get; set; } = new();
            public List<string> Notes { get; set; } = new();
        }
    }
}
