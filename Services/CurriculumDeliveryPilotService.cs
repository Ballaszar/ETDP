using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Utils;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Services
{
    public sealed class CurriculumDeliveryPilotService
    {
        private const string AutoDraftMarker = "[AUTO_CURRICULUM_EVIDENCE_DRAFT]";
        private const string AutoDraftCoverageGapMarker = "[AUTO_CURRICULUM_INSUFFICIENT_COVERAGE]";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".docx", ".pdf", ".pptx", ".csv", ".json", ".jsonl", ".xml", ".yml", ".yaml", ".html", ".htm",
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".svg"
        };

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".svg"
        };

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "for", "with", "from", "that", "this", "into", "within", "also", "shall", "must", "are",
            "was", "were", "have", "has", "had", "can", "could", "should", "will", "would", "about", "their", "them",
            "they", "then", "than", "your", "you", "our", "ours", "its", "which", "what", "when", "where", "while",
            "there", "these", "those", "been", "being", "over", "under", "through", "such", "using", "used", "use",
            "topic", "subject", "phase", "unit", "module", "outcome", "criteria", "criterion", "assessment", "learning",
            "programme", "program", "qualification", "code", "description", "level", "credits", "nqf", "learner",
            "learners", "lecturer", "lecturers", "content", "guide", "chapter", "section"
        };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CurriculumDeliveryPilotService> _logger;

        public CurriculumDeliveryPilotService(
            IServiceScopeFactory scopeFactory,
            ILogger<CurriculumDeliveryPilotService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<DeliveryPilotResult> ExecuteQualificationPilotAsync(
            DeliveryPilotRequest request,
            CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var knowledgeHierarchy = scope.ServiceProvider.GetRequiredService<KnowledgeHierarchyService>();

            var qualification = await db.Qualifications
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == request.QualificationId, cancellationToken);

            if (qualification == null)
            {
                throw new InvalidOperationException("Qualification not found for delivery pilot.");
            }

            var qualificationCode = (qualification.QualificationNumber ?? string.Empty).Trim();
            var qualificationDescription = (qualification.QualificationDescription ?? string.Empty).Trim();
            var structure = knowledgeHierarchy.EnsureQualificationStructure(qualificationCode, qualificationDescription);

            var artifactsDirectory = Path.Combine(request.JobFolder, "delivery-pilot");
            Directory.CreateDirectory(artifactsDirectory);

            var result = new DeliveryPilotResult
            {
                QualificationId = qualification.Id,
                QualificationCode = qualificationCode,
                QualificationDescription = qualificationDescription,
                QualificationRootPath = structure.QualificationRootPath,
                ArtifactsDirectory = artifactsDirectory
            };

            var importResult = await ImportExternalResourcesAsync(
                db,
                knowledgeHierarchy,
                structure,
                qualificationCode,
                qualificationDescription,
                cancellationToken);
            result.Import = importResult;
            result.DetectedExternalResourceFolder = importResult.DetectedExternalFolder;

            var sourceMaterials = await LoadSourceMaterialsAsync(db, qualificationCode, qualificationDescription, cancellationToken);
            result.SourceMaterialCount = sourceMaterials.Count;
            if (sourceMaterials.Count == 0)
            {
                result.Warnings.Add("No qualification-linked source material is available yet for detailed topic mapping.");
                return result;
            }

            var chunks = BuildSourceMaterialChunks(sourceMaterials);
            result.SourceChunkCount = chunks.Count;
            result.SourceChunksPath = await WriteJsonArtifactAsync(
                artifactsDirectory,
                "source-chunks.json",
                chunks.Select(BuildChunkArtifact).ToList(),
                cancellationToken);

            var topics = await LoadTopicTargetsAsync(db, qualification.Id, cancellationToken);
            var criteria = await LoadCriteriaTargetsAsync(db, qualification.Id, cancellationToken);
            result.TopicCount = topics.Count;
            result.CriteriaCount = criteria.Count;

            if (chunks.Count == 0)
            {
                result.Warnings.Add("Source material was found, but no clean content chunks survived sanitation and TOC filtering.");
                return result;
            }

            var tokenIndex = BuildChunkTokenIndex(chunks);
            var topicMaps = BuildTopicSourceMap(topics, chunks, tokenIndex);
            var criteriaMaps = BuildCriteriaSourceMap(criteria, chunks, tokenIndex);

            result.TopicsMappedCount = topicMaps.Count(x => x.Matches.Count > 0);
            result.CriteriaMappedCount = criteriaMaps.Count(x => x.Matches.Count > 0);
            result.TopicSourceMapPath = await WriteJsonArtifactAsync(artifactsDirectory, "topic-source-map.json", topicMaps, cancellationToken);
            result.CriteriaSourceMapPath = await WriteJsonArtifactAsync(artifactsDirectory, "criteria-source-map.json", criteriaMaps, cancellationToken);

            var draftWriteResult = await SeedLessonPlanDraftsAsync(
                db,
                qualification.Id,
                criteriaMaps,
                request.PopulateLessonPlanDrafts,
                cancellationToken);

            result.LessonPlanDraftsCreated = draftWriteResult.Created;
            result.LessonPlanDraftsUpdated = draftWriteResult.Updated;
            result.LessonPlanDraftsSkipped = draftWriteResult.Skipped;
            result.LessonPlanDraftsPath = await WriteJsonArtifactAsync(
                artifactsDirectory,
                "lesson-plan-drafts.json",
                draftWriteResult.Entries,
                cancellationToken);

            foreach (var warning in importResult.Warnings
                .Concat(draftWriteResult.Warnings)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                result.Warnings.Add(warning);
            }

            return result;
        }

        public async Task<TopicEvidenceSummary> BuildTopicEvidenceSummaryAsync(
            int qualificationId,
            CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var qualification = await db.Qualifications
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == qualificationId, cancellationToken);

            if (qualification == null)
            {
                throw new InvalidOperationException("Qualification not found for topic evidence summary.");
            }

            var qualificationCode = (qualification.QualificationNumber ?? string.Empty).Trim();
            var qualificationDescription = (qualification.QualificationDescription ?? string.Empty).Trim();
            var topics = await LoadTopicTargetsAsync(db, qualification.Id, cancellationToken);
            var duplicateCriteriaGroups = await LoadDuplicateCriteriaGroupsAsync(db, qualification.Id, cancellationToken);

            var summary = new TopicEvidenceSummary
            {
                QualificationId = qualification.Id,
                QualificationNumber = qualificationCode,
                QualificationDescription = qualificationDescription,
                TopicCount = topics.Count,
                DuplicateCriteriaGroups = duplicateCriteriaGroups
            };

            if (duplicateCriteriaGroups.Count > 0)
            {
                summary.Warnings.Add(
                    $"Detected {duplicateCriteriaGroups.Count} duplicated assessment-criteria cluster(s) across topics, so ETDP is showing topic evidence coverage as the primary measure.");
            }

            var sourceMaterials = await LoadSourceMaterialsAsync(db, qualificationCode, qualificationDescription, cancellationToken);
            summary.SourceMaterialCount = sourceMaterials.Count;
            if (sourceMaterials.Count == 0)
            {
                summary.Warnings.Add("No qualification-linked subject matter has been uploaded yet for this qualification.");
                summary.Topics = topics.Select(BuildEmptyTopicEvidenceItem).ToList();
                FinalizeTopicEvidenceSummary(summary);
                return summary;
            }

            var chunks = BuildSourceMaterialChunks(sourceMaterials);
            summary.SourceChunkCount = chunks.Count;
            if (chunks.Count == 0)
            {
                summary.Warnings.Add("Subject matter exists, but no clean content chunks survived sanitation and table-of-contents filtering.");
                summary.Topics = topics.Select(BuildEmptyTopicEvidenceItem).ToList();
                FinalizeTopicEvidenceSummary(summary);
                return summary;
            }

            var tokenIndex = BuildChunkTokenIndex(chunks);
            var topicMaps = BuildTopicSourceMap(topics, chunks, tokenIndex);
            summary.Topics = topicMaps
                .Select(BuildTopicEvidenceItem)
                .OrderBy(item => item.SubjectCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TopicCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TopicDescription, StringComparer.OrdinalIgnoreCase)
                .ToList();

            FinalizeTopicEvidenceSummary(summary);
            return summary;
        }

        private async Task<ImportResult> ImportExternalResourcesAsync(
            ApplicationDbContext db,
            KnowledgeHierarchyService knowledgeHierarchy,
            KnowledgeHierarchyService.StructureInfo structure,
            string qualificationCode,
            string qualificationDescription,
            CancellationToken cancellationToken)
        {
            var result = new ImportResult();
            var detectedFolder = ResolveExternalResourceFolder(qualificationCode, qualificationDescription);
            result.DetectedExternalFolder = detectedFolder;

            var hasInboxFiles = Directory.Exists(structure.DeveloperInboxPath) &&
                Directory.GetFiles(structure.DeveloperInboxPath, "*", SearchOption.TopDirectoryOnly)
                    .Any(path => SupportedExtensions.Contains(Path.GetExtension(path)) && !IsImageSidecarFile(path));

            if (string.IsNullOrWhiteSpace(detectedFolder) || !Directory.Exists(detectedFolder))
            {
                if (!hasInboxFiles)
                {
                    result.Warnings.Add("No external vocational source folder was auto-detected; using only already indexed qualification resources.");
                    return result;
                }
            }
            else
            {
                var existingSourceNames = await db.SourceMaterials
                    .AsNoTracking()
                    .Where(s => (s.QualificationCode ?? string.Empty) == qualificationCode &&
                                (s.KnowledgeSourceType ?? string.Empty) == "developer_knowledge_base")
                    .Select(s => new { s.Title, s.FileName, s.AssessmentCriteriaDescription })
                    .ToListAsync(cancellationToken);

                var knownSourceNames = existingSourceNames
                    .Select(x => ResolveOriginalSourceName(x.Title, x.FileName, x.AssessmentCriteriaDescription))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var copiedInRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var candidateFiles = Directory.GetFiles(detectedFolder, "*", SearchOption.AllDirectories)
                    .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                    .Where(path => !IsImageSidecarFile(path))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var sourcePath in candidateFiles)
                {
                    var originalName = Path.GetFileName(sourcePath);
                    if (string.IsNullOrWhiteSpace(originalName))
                    {
                        continue;
                    }

                    if (!copiedInRun.Add(originalName))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    if (knownSourceNames.Contains(originalName))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    var destination = EnsureUniquePath(Path.Combine(structure.DeveloperInboxPath, originalName));
                    File.Copy(sourcePath, destination, overwrite: false);
                    result.CopiedToInboxCount++;
                }

                hasInboxFiles = hasInboxFiles || result.CopiedToInboxCount > 0;
            }

            if (!hasInboxFiles)
            {
                return result;
            }

            var sync = knowledgeHierarchy.SyncKnowledgeHierarchy(new KnowledgeHierarchyService.SyncOptions
            {
                QualificationCode = qualificationCode,
                QualificationDescription = qualificationDescription,
                IncludeLocalSourceUploads = false,
                IncludeDeveloperKnowledgeBase = true,
                RebuildUploadReadme = false,
                ConsolidateLegacyFolders = false
            });

            result.SyncCreatedCount = sync.Created;
            result.SyncSkippedCount = sync.Skipped;
            result.SyncFailedCount = sync.Failed;
            result.CoverageReportPath = sync.CoverageReports.FirstOrDefault()?.MarkdownPath ?? string.Empty;
            return result;
        }

        private static string ResolveExternalResourceFolder(string qualificationCode, string qualificationDescription)
        {
            foreach (var root in FindVocationalDisciplineRoots())
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var target = NormalizeMatchToken($"{qualificationCode} {qualificationDescription}");
                var best = Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly)
                    .Select(path => new
                    {
                        Path = path,
                        Score = ScoreResourceFolder(path, target, qualificationDescription, qualificationCode)
                    })
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (best != null && best.Score >= 4)
                {
                    return best.Path;
                }
            }

            return string.Empty;
        }

        private static IEnumerable<string> FindVocationalDisciplineRoots()
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddIfPresent(string? candidate)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    candidates.Add(candidate.Trim());
                }
            }

            var current = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); i++)
            {
                AddIfPresent(Path.Combine(current, "VocationalLLM", "data", "knowledge_taxonomy", "vocational_disciplines"));
                current = Directory.GetParent(current)?.FullName ?? string.Empty;
            }

            var cwd = Directory.GetCurrentDirectory();
            for (var i = 0; i < 8 && !string.IsNullOrWhiteSpace(cwd); i++)
            {
                AddIfPresent(Path.Combine(cwd, "VocationalLLM", "data", "knowledge_taxonomy", "vocational_disciplines"));
                cwd = Directory.GetParent(cwd)?.FullName ?? string.Empty;
            }

            return candidates;
        }

        private static int ScoreResourceFolder(string folderPath, string target, string qualificationDescription, string qualificationCode)
        {
            var folderName = Path.GetFileName(folderPath) ?? string.Empty;
            var normalizedFolder = NormalizeMatchToken(folderName);
            var score = 0;

            if (!string.IsNullOrWhiteSpace(target))
            {
                if (string.Equals(normalizedFolder, target, StringComparison.OrdinalIgnoreCase))
                {
                    score += 8;
                }
                else if (normalizedFolder.Contains(target, StringComparison.OrdinalIgnoreCase) ||
                         target.Contains(normalizedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    score += 6;
                }
            }

            foreach (var token in BuildTargetTokens($"{qualificationDescription} {qualificationCode}"))
            {
                if (normalizedFolder.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    score += 1;
                }
            }

            return score;
        }

        private async Task<List<SourceMaterialRow>> LoadSourceMaterialsAsync(
            ApplicationDbContext db,
            string qualificationCode,
            string qualificationDescription,
            CancellationToken cancellationToken)
        {
            var rawRows = await db.SourceMaterials
                .AsNoTracking()
                .Where(s => ((s.QualificationCode ?? string.Empty) == qualificationCode ||
                             (string.IsNullOrWhiteSpace(qualificationCode) &&
                              (s.QualificationDescription ?? string.Empty) == qualificationDescription)) &&
                            ((s.KnowledgeSourceType ?? string.Empty) == "developer_knowledge_base" ||
                             (s.KnowledgeSourceType ?? string.Empty) == "local_source_upload"))
                .OrderByDescending(s => s.KnowledgeUploadedAtUtc ?? s.CreatedAt)
                .Select(s => new
                {
                    s.Id,
                    s.Title,
                    s.FileName,
                    s.FilePath,
                    s.FileType,
                    s.Url,
                    s.KnowledgeSourceType,
                    s.KnowledgeNumber,
                    UploadedAtUtc = s.KnowledgeUploadedAtUtc ?? s.CreatedAt,
                    s.AssessmentCriteriaDescription,
                    s.KnowledgeLabel,
                    s.ExtractedText
                })
                .ToListAsync(cancellationToken);

            return rawRows
                .Select(x => new SourceMaterialRow
                {
                    Id = x.Id,
                    Title = x.Title ?? string.Empty,
                    FileName = x.FileName ?? string.Empty,
                    FilePath = x.FilePath ?? string.Empty,
                    FileType = x.FileType ?? string.Empty,
                    Url = x.Url ?? string.Empty,
                    KnowledgeSourceType = x.KnowledgeSourceType ?? string.Empty,
                    KnowledgeNumber = x.KnowledgeNumber ?? 0,
                    UploadedAtUtc = x.UploadedAtUtc,
                    AssessmentCriteriaDescription = x.AssessmentCriteriaDescription ?? string.Empty,
                    KnowledgeLabel = x.KnowledgeLabel ?? string.Empty,
                    ExtractedText = x.ExtractedText ?? string.Empty,
                    OriginalSourceName = ResolveOriginalSourceName(x.Title, x.FileName, x.AssessmentCriteriaDescription)
                })
                .ToList();
        }

        private async Task<List<DuplicateCriteriaGroup>> LoadDuplicateCriteriaGroupsAsync(
            ApplicationDbContext db,
            int qualificationId,
            CancellationToken cancellationToken)
        {
            var rows =
                await (from criteria in db.AssessmentCriteria.AsNoTracking()
                       join topic in db.Topics.AsNoTracking() on criteria.TopicId equals topic.Id
                       join subject in db.Subjects.AsNoTracking() on topic.SubjectId equals subject.Id
                       where subject.QualificationId == qualificationId
                       select new
                       {
                           topic.Id,
                           topic.TopicCode,
                           topic.TopicDescription,
                           criteria.Description
                       })
                    .ToListAsync(cancellationToken);

            return rows
                .Select(x => new
                {
                    x.Id,
                    x.TopicCode,
                    x.TopicDescription,
                    Description = NormalizeLine(x.Description),
                    Key = NormalizeDuplicateCriteriaKey(x.Description)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Key.Length >= 24)
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(x => x.Id).Distinct().Count() > 1)
                .OrderByDescending(group => group.Select(x => x.Id).Distinct().Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .Select(group => new DuplicateCriteriaGroup
                {
                    CriteriaDescription = TruncateWithEllipsis(group.First().Description, 220),
                    TopicCount = group.Select(x => x.Id).Distinct().Count(),
                    Topics = group
                        .Select(x => $"{x.TopicCode} {x.TopicDescription}".Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(8)
                        .ToList()
                })
                .ToList();
        }

        private static string ResolveOriginalSourceName(string? title, string? fileName, string? assessmentCriteriaDescription)
        {
            var assessmentDescription = (assessmentCriteriaDescription ?? string.Empty).Trim();
            var sourceIdx = assessmentDescription.IndexOf("Source:", StringComparison.OrdinalIgnoreCase);
            if (sourceIdx >= 0)
            {
                var value = assessmentDescription[(sourceIdx + "Source:".Length)..].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            var titleText = (title ?? string.Empty).Trim();
            var separatorIndex = titleText.LastIndexOf("::", StringComparison.Ordinal);
            if (separatorIndex >= 0 && separatorIndex < titleText.Length - 2)
            {
                var value = titleText[(separatorIndex + 2)..].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return (fileName ?? string.Empty).Trim();
        }

        private static List<SourceMaterialChunk> BuildSourceMaterialChunks(IReadOnlyList<SourceMaterialRow> materials)
        {
            var chunks = new List<SourceMaterialChunk>();
            foreach (var material in materials)
            {
                chunks.AddRange(BuildSourceMaterialChunks(material));
            }

            return chunks;
        }

        private static List<SourceMaterialChunk> BuildSourceMaterialChunks(SourceMaterialRow material)
        {
            var chunks = new List<SourceMaterialChunk>();
            var rawText = (material.ExtractedText ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return chunks;
            }

            var lines = rawText.Split('\n', StringSplitOptions.None);
            var navigationHints = new List<string>();
            var pageNumber = 0;
            var chunkIndex = 0;
            var currentHeading = string.Empty;
            var buffer = new List<string>();
            var bufferWordCount = 0;
            var inTableOfContents = false;
            var contentEvidenceAfterToc = 0;

            void Flush()
            {
                if (buffer.Count == 0 || bufferWordCount < 45)
                {
                    buffer.Clear();
                    bufferWordCount = 0;
                    return;
                }

                var text = CleanGeneratedText(string.Join(" ", buffer));
                if (DocumentTextCleaner.WordCount(text) < 45)
                {
                    buffer.Clear();
                    bufferWordCount = 0;
                    return;
                }

                var excerpt = BuildExcerpt(text);
                var chapterTitle = CleanHeading(currentHeading);
                var citation = BuildCitation(material, pageNumber, chapterTitle);
                var searchText = BuildSearchText(material, text, chapterTitle, navigationHints);
                var indexTokens = BuildIndexTokens(searchText);

                chunks.Add(new SourceMaterialChunk
                {
                    Id = $"{material.Id}:{chunkIndex:D4}",
                    MaterialId = material.Id,
                    MaterialTitle = material.Title,
                    FileName = material.OriginalSourceName,
                    FileType = material.FileType,
                    KnowledgeNumber = material.KnowledgeNumber,
                    KnowledgeSourceType = material.KnowledgeSourceType,
                    Url = material.Url,
                    PageNumber = pageNumber,
                    ChapterTitle = chapterTitle,
                    Citation = citation,
                    Text = text,
                    Excerpt = excerpt,
                    SearchText = searchText,
                    IndexTokens = indexTokens
                });

                chunkIndex++;
                buffer.Clear();
                bufferWordCount = 0;
            }

            foreach (var rawLine in lines)
            {
                var line = NormalizeLine(rawLine);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var pageMatch = Regex.Match(line, @"^\[Page\s+(\d+)\]$", RegexOptions.IgnoreCase);
                if (pageMatch.Success)
                {
                    if (bufferWordCount >= 180)
                    {
                        Flush();
                    }

                    int.TryParse(pageMatch.Groups[1].Value, out pageNumber);
                    if (inTableOfContents && contentEvidenceAfterToc > 0)
                    {
                        inTableOfContents = false;
                        contentEvidenceAfterToc = 0;
                    }

                    continue;
                }

                if (DocumentTextCleaner.IsNoiseLine(line))
                {
                    continue;
                }

                if (IsTableOfContentsHeading(line))
                {
                    inTableOfContents = true;
                    contentEvidenceAfterToc = 0;
                    continue;
                }

                if (LooksLikeTableOfContentsLine(line))
                {
                    var hint = ExtractNavigationHint(line);
                    if (!string.IsNullOrWhiteSpace(hint))
                    {
                        AddUnique(navigationHints, hint, 24);
                    }

                    continue;
                }

                if (inTableOfContents)
                {
                    if (DocumentTextCleaner.WordCount(line) >= 6)
                    {
                        contentEvidenceAfterToc++;
                    }

                    if (contentEvidenceAfterToc < 2)
                    {
                        continue;
                    }

                    inTableOfContents = false;
                }

                if (IsLikelyHeading(line))
                {
                    if (bufferWordCount >= 120)
                    {
                        Flush();
                    }

                    currentHeading = line;
                    continue;
                }

                var cleaned = CleanGeneratedText(line);
                var words = DocumentTextCleaner.WordCount(cleaned);
                if (words < 3)
                {
                    continue;
                }

                buffer.Add(cleaned);
                bufferWordCount += words;
                if (bufferWordCount >= 210)
                {
                    Flush();
                }
            }

            Flush();
            return chunks;
        }

        private static Dictionary<string, List<int>> BuildChunkTokenIndex(IReadOnlyList<SourceMaterialChunk> chunks)
        {
            var index = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < chunks.Count; i++)
            {
                foreach (var token in chunks[i].IndexTokens)
                {
                    if (!index.TryGetValue(token, out var list))
                    {
                        list = new List<int>();
                        index[token] = list;
                    }

                    list.Add(i);
                }
            }

            return index;
        }

        private async Task<List<CurriculumTopicTarget>> LoadTopicTargetsAsync(
            ApplicationDbContext db,
            int qualificationId,
            CancellationToken cancellationToken)
        {
            var rows =
                await (from topic in db.Topics.AsNoTracking()
                       join subject in db.Subjects.AsNoTracking() on topic.SubjectId equals subject.Id
                       where subject.QualificationId == qualificationId
                       orderby subject.SubjectCode, topic.TopicCode, topic.Id
                       select new
                       {
                           topic.Id,
                           topic.TopicCode,
                           topic.TopicDescription,
                           topic.TopicPurpose,
                           subject.SubjectCode,
                           subject.SubjectDescription,
                           subject.SubjectPurpose
                       })
                    .ToListAsync(cancellationToken);

            return rows.Select(x => new CurriculumTopicTarget
            {
                TopicId = x.Id,
                TopicCode = x.TopicCode ?? string.Empty,
                TopicDescription = x.TopicDescription ?? string.Empty,
                TopicPurpose = x.TopicPurpose ?? string.Empty,
                SubjectCode = x.SubjectCode ?? string.Empty,
                SubjectDescription = x.SubjectDescription ?? string.Empty,
                SubjectPurpose = x.SubjectPurpose ?? string.Empty,
                TopicPhrase = NormalizeSearchPhrase(x.TopicDescription ?? string.Empty),
                SubjectPhrase = NormalizeSearchPhrase(x.SubjectDescription ?? string.Empty),
                TopicTokens = BuildTargetTokens($"{x.TopicCode} {x.TopicDescription} {x.TopicPurpose}"),
                SubjectTokens = BuildTargetTokens($"{x.SubjectCode} {x.SubjectDescription} {x.SubjectPurpose}")
            }).ToList();
        }

        private async Task<List<CurriculumCriteriaTarget>> LoadCriteriaTargetsAsync(
            ApplicationDbContext db,
            int qualificationId,
            CancellationToken cancellationToken)
        {
            var rows =
                await (from criteria in db.AssessmentCriteria.AsNoTracking()
                       join topic in db.Topics.AsNoTracking() on criteria.TopicId equals topic.Id
                       join subject in db.Subjects.AsNoTracking() on topic.SubjectId equals subject.Id
                       where subject.QualificationId == qualificationId
                       orderby subject.SubjectCode, topic.TopicCode, criteria.Id
                       select new
                       {
                           CriteriaId = criteria.Id,
                           CriteriaDescription = criteria.Description,
                           topic.Id,
                           topic.TopicCode,
                           topic.TopicDescription,
                           topic.TopicPurpose,
                           subject.SubjectCode,
                           subject.SubjectDescription,
                           subject.SubjectPurpose
                       })
                    .ToListAsync(cancellationToken);

            return rows.Select(x => new CurriculumCriteriaTarget
            {
                CriteriaId = x.CriteriaId,
                CriteriaDescription = x.CriteriaDescription ?? string.Empty,
                TopicId = x.Id,
                TopicCode = x.TopicCode ?? string.Empty,
                TopicDescription = x.TopicDescription ?? string.Empty,
                TopicPurpose = x.TopicPurpose ?? string.Empty,
                SubjectCode = x.SubjectCode ?? string.Empty,
                SubjectDescription = x.SubjectDescription ?? string.Empty,
                SubjectPurpose = x.SubjectPurpose ?? string.Empty,
                CriteriaPhrase = NormalizeSearchPhrase(x.CriteriaDescription ?? string.Empty),
                TopicPhrase = NormalizeSearchPhrase(x.TopicDescription ?? string.Empty),
                SubjectPhrase = NormalizeSearchPhrase(x.SubjectDescription ?? string.Empty),
                CriteriaTokens = BuildTargetTokens(x.CriteriaDescription ?? string.Empty),
                TopicTokens = BuildTargetTokens($"{x.TopicCode} {x.TopicDescription} {x.TopicPurpose}"),
                SubjectTokens = BuildTargetTokens($"{x.SubjectCode} {x.SubjectDescription} {x.SubjectPurpose}")
            }).ToList();
        }

        private static TopicEvidenceItem BuildEmptyTopicEvidenceItem(CurriculumTopicTarget topic)
        {
            return new TopicEvidenceItem
            {
                TopicId = topic.TopicId,
                TopicCode = topic.TopicCode,
                TopicDescription = topic.TopicDescription,
                SubjectCode = topic.SubjectCode,
                SubjectDescription = topic.SubjectDescription,
                CoverageBand = "gap",
                CoverageBandLabel = "Gap"
            };
        }

        private static List<TopicSourceMapItem> BuildTopicSourceMap(
            IReadOnlyList<CurriculumTopicTarget> topics,
            IReadOnlyList<SourceMaterialChunk> chunks,
            IReadOnlyDictionary<string, List<int>> chunkIndex)
        {
            var results = new List<TopicSourceMapItem>();
            foreach (var topic in topics)
            {
                var candidateIds = ResolveCandidateChunkIds(topic.TopicTokens.Concat(topic.SubjectTokens), chunks, chunkIndex);
                var matches = candidateIds
                    .Select(id => ScoreTopicMatch(topic, chunks[id]))
                    .Where(match => match.Score >= 6)
                    .OrderByDescending(match => match.Score)
                    .ThenByDescending(match => match.Confidence)
                    .ThenBy(match => match.Citation, StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList();

                results.Add(new TopicSourceMapItem
                {
                    TopicId = topic.TopicId,
                    TopicCode = topic.TopicCode,
                    TopicDescription = topic.TopicDescription,
                    SubjectCode = topic.SubjectCode,
                    SubjectDescription = topic.SubjectDescription,
                    Matches = matches
                });
            }

            return results;
        }

        private static TopicEvidenceItem BuildTopicEvidenceItem(TopicSourceMapItem map)
        {
            var matches = (map.Matches ?? new List<ChunkMatch>())
                .OrderByDescending(match => match.Confidence)
                .ThenByDescending(match => match.Score)
                .ThenBy(match => match.Citation, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var distinctSourceCount = matches
                .Select(match => match.MaterialId)
                .Where(id => id > 0)
                .Distinct()
                .Count();

            var topMatches = matches.Take(3).ToList();
            var bestConfidencePercent = matches.Count == 0
                ? 0
                : ClampPercent(matches.Max(match => match.Confidence * 100d));
            var averageConfidencePercent = topMatches.Count == 0
                ? 0
                : ClampPercent(topMatches.Average(match => match.Confidence * 100d));
            var evidenceDepthPercent = ClampPercent((Math.Min(5, matches.Count) / 5d) * 100d);
            var sourceDiversityPercent = ClampPercent((Math.Min(3, distinctSourceCount) / 3d) * 100d);
            var coveragePercent = matches.Count == 0
                ? 0
                : ClampPercent(
                    (averageConfidencePercent * 0.50d) +
                    (evidenceDepthPercent * 0.30d) +
                    (sourceDiversityPercent * 0.20d));

            var coverageBand = DetermineCoverageBand(coveragePercent);

            return new TopicEvidenceItem
            {
                TopicId = map.TopicId,
                TopicCode = map.TopicCode,
                TopicDescription = map.TopicDescription,
                SubjectCode = map.SubjectCode,
                SubjectDescription = map.SubjectDescription,
                CoveragePercent = coveragePercent,
                CoverageBand = coverageBand,
                CoverageBandLabel = DetermineCoverageBandLabel(coverageBand),
                EvidenceCount = matches.Count,
                DistinctSourceCount = distinctSourceCount,
                BestConfidencePercent = bestConfidencePercent,
                AverageConfidencePercent = averageConfidencePercent,
                TopCitations = matches
                    .Select(match => match.Citation)
                    .Where(citation => !string.IsNullOrWhiteSpace(citation))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList(),
                TopEvidence = topMatches
                    .Select(match => new TopicEvidenceSource
                    {
                        Citation = match.Citation,
                        ConfidencePercent = ClampPercent(match.Confidence * 100d),
                        KnowledgeSourceType = match.KnowledgeSourceType,
                        MaterialTitle = match.MaterialTitle,
                        PageNumber = match.PageNumber,
                        Excerpt = match.Excerpt
                    })
                    .ToList()
            };
        }

        private static List<CriteriaSourceMapItem> BuildCriteriaSourceMap(
            IReadOnlyList<CurriculumCriteriaTarget> criteriaTargets,
            IReadOnlyList<SourceMaterialChunk> chunks,
            IReadOnlyDictionary<string, List<int>> chunkIndex)
        {
            var results = new List<CriteriaSourceMapItem>();
            foreach (var criteria in criteriaTargets)
            {
                var candidateIds = ResolveCandidateChunkIds(criteria.CriteriaTokens.Concat(criteria.TopicTokens).Concat(criteria.SubjectTokens), chunks, chunkIndex);
                var matches = candidateIds
                    .Select(id => ScoreCriteriaMatch(criteria, chunks[id]))
                    .Where(match => match.Score >= 8)
                    .OrderByDescending(match => match.Score)
                    .ThenByDescending(match => match.Confidence)
                    .ThenBy(match => match.Citation, StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .ToList();

                results.Add(new CriteriaSourceMapItem
                {
                    CriteriaId = criteria.CriteriaId,
                    CriteriaDescription = criteria.CriteriaDescription,
                    TopicId = criteria.TopicId,
                    TopicCode = criteria.TopicCode,
                    TopicDescription = criteria.TopicDescription,
                    SubjectCode = criteria.SubjectCode,
                    SubjectDescription = criteria.SubjectDescription,
                    Matches = matches
                });
            }

            return results;
        }

        private static List<int> ResolveCandidateChunkIds(
            IEnumerable<string> targetTokens,
            IReadOnlyList<SourceMaterialChunk> chunks,
            IReadOnlyDictionary<string, List<int>> chunkIndex)
        {
            var candidates = new Dictionary<int, int>();
            foreach (var token in targetTokens
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24))
            {
                if (!chunkIndex.TryGetValue(token, out var ids))
                {
                    continue;
                }

                foreach (var id in ids.Take(240))
                {
                    candidates[id] = candidates.TryGetValue(id, out var count) ? count + 1 : 1;
                }
            }

            if (candidates.Count == 0)
            {
                return chunks.Count <= 180
                    ? Enumerable.Range(0, chunks.Count).ToList()
                    : new List<int>();
            }

            return candidates
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .Take(140)
                .Select(x => x.Key)
                .ToList();
        }

        private static ChunkMatch ScoreTopicMatch(CurriculumTopicTarget target, SourceMaterialChunk chunk)
        {
            var tokenSet = chunk.IndexTokens;
            var score = 0;

            if (ContainsPhrase(chunk.SearchText, target.TopicPhrase))
            {
                score += 12;
            }

            if (ContainsPhrase(chunk.SearchText, target.SubjectPhrase))
            {
                score += 6;
            }

            if (!string.IsNullOrWhiteSpace(target.TopicCode) &&
                chunk.SearchText.Contains($" {NormalizeSearchPhrase(target.TopicCode)} ", StringComparison.Ordinal))
            {
                score += 4;
            }

            var topicOverlap = CountOverlap(target.TopicTokens, tokenSet);
            var subjectOverlap = CountOverlap(target.SubjectTokens, tokenSet);
            score += Math.Min(12, topicOverlap * 2);
            score += Math.Min(6, subjectOverlap);

            return BuildChunkMatch(chunk, score, topicOverlap, subjectOverlap, 0);
        }

        private static ChunkMatch ScoreCriteriaMatch(CurriculumCriteriaTarget target, SourceMaterialChunk chunk)
        {
            var tokenSet = chunk.IndexTokens;
            var score = 0;

            if (ContainsPhrase(chunk.SearchText, target.CriteriaPhrase))
            {
                score += 12;
            }

            if (ContainsPhrase(chunk.SearchText, target.TopicPhrase))
            {
                score += 9;
            }

            if (ContainsPhrase(chunk.SearchText, target.SubjectPhrase))
            {
                score += 5;
            }

            var criteriaOverlap = CountOverlap(target.CriteriaTokens, tokenSet);
            var topicOverlap = CountOverlap(target.TopicTokens, tokenSet);
            var subjectOverlap = CountOverlap(target.SubjectTokens, tokenSet);

            score += Math.Min(14, criteriaOverlap * 2);
            score += Math.Min(8, topicOverlap * 2);
            score += Math.Min(5, subjectOverlap);

            return BuildChunkMatch(chunk, score, topicOverlap, subjectOverlap, criteriaOverlap);
        }

        private static ChunkMatch BuildChunkMatch(
            SourceMaterialChunk chunk,
            int score,
            int topicOverlap,
            int subjectOverlap,
            int criteriaOverlap)
        {
            return new ChunkMatch
            {
                MaterialId = chunk.MaterialId,
                ChunkId = chunk.Id,
                MaterialTitle = chunk.MaterialTitle,
                FileName = chunk.FileName,
                KnowledgeNumber = chunk.KnowledgeNumber,
                KnowledgeSourceType = chunk.KnowledgeSourceType,
                Url = chunk.Url,
                Citation = chunk.Citation,
                ChapterTitle = chunk.ChapterTitle,
                PageNumber = chunk.PageNumber,
                Score = score,
                Confidence = ScoreToConfidence(score),
                TopicTokenMatches = topicOverlap,
                SubjectTokenMatches = subjectOverlap,
                CriteriaTokenMatches = criteriaOverlap,
                Excerpt = chunk.Excerpt
            };
        }

        private async Task<DraftWriteResult> SeedLessonPlanDraftsAsync(
            ApplicationDbContext db,
            int qualificationId,
            IReadOnlyList<CriteriaSourceMapItem> criteriaMaps,
            bool populateLessonPlanDrafts,
            CancellationToken cancellationToken)
        {
            var result = new DraftWriteResult();
            if (!populateLessonPlanDrafts)
            {
                result.Warnings.Add("Lesson-plan draft seeding was disabled for this pipeline run.");
                return result;
            }

            var authoringRules = LearningMaterialAuthoringRulesStore.Load();

            var existingRows = await db.LecturerToolkitEntries
                .Where(x => x.QualificationsId == qualificationId)
                .ToListAsync(cancellationToken);

            foreach (var map in criteriaMaps)
            {
                var matches = map.Matches.Where(x => x.Confidence >= 0.65d).Take(3).ToList();
                if (matches.Count == 0)
                {
                    result.Skipped++;
                    result.Entries.Add(new LessonPlanDraftArtifact
                    {
                        CriteriaId = map.CriteriaId,
                        CriteriaDescription = map.CriteriaDescription,
                        SubjectCode = map.SubjectCode,
                        TopicCode = map.TopicCode,
                        Status = "skipped_no_confident_match",
                        ConfidencePercent = 0
                    });
                    continue;
                }

                var manualRows = existingRows
                    .Where(x => x.AssessmentCriteriaId == map.CriteriaId && !ContainsAutoDraftMarker(x.LearningAids))
                    .ToList();
                if (manualRows.Count > 0)
                {
                    result.Skipped++;
                    result.Entries.Add(new LessonPlanDraftArtifact
                    {
                        CriteriaId = map.CriteriaId,
                        CriteriaDescription = map.CriteriaDescription,
                        SubjectCode = map.SubjectCode,
                        TopicCode = map.TopicCode,
                        Status = "skipped_manual_row_present",
                        ConfidencePercent = (int)Math.Round(matches[0].Confidence * 100d),
                        Citations = matches.Select(x => x.Citation).ToList()
                    });
                    continue;
                }

                var generatedRow = existingRows
                    .FirstOrDefault(x => x.AssessmentCriteriaId == map.CriteriaId && ContainsAutoDraftMarker(x.LearningAids));

                var lessonContent = BuildLessonPlanContent(map, matches, authoringRules);
                var hasGroundedLessonContent = HasGroundedLessonPlanContent(lessonContent);
                var learningAids = BuildLearningAids(map, matches, hasGroundedLessonContent);
                if (!hasGroundedLessonContent)
                {
                    if (generatedRow != null)
                    {
                        generatedRow.SubjectCode = map.SubjectCode;
                        generatedRow.SubjectDescription = map.SubjectDescription;
                        generatedRow.AssessmentCriteriaDescription = map.CriteriaDescription;
                        generatedRow.Lpn = BuildAutoLpn(map);
                        generatedRow.LessonPlanDescription = BuildLessonPlanDescription(map);
                        generatedRow.LessonPlanContent = lessonContent;
                        generatedRow.LecturerActions = string.Empty;
                        generatedRow.LearnerActions = string.Empty;
                        generatedRow.LearningAids = learningAids;

                        result.Updated++;
                        result.Entries.Add(BuildDraftArtifact(map, matches, "updated_insufficient_grounding"));
                    }
                    else
                    {
                        result.Skipped++;
                        result.Entries.Add(BuildDraftArtifact(map, matches, "skipped_insufficient_grounding"));
                    }

                    continue;
                }

                if (generatedRow == null)
                {
                    generatedRow = new LecturerToolkitEntry
                    {
                        QualificationsId = qualificationId,
                        SubjectCode = map.SubjectCode,
                        SubjectDescription = map.SubjectDescription,
                        AssessmentCriteriaId = map.CriteriaId,
                        AssessmentCriteriaDescription = map.CriteriaDescription,
                        Lpn = BuildAutoLpn(map),
                        LessonPlanDescription = BuildLessonPlanDescription(map),
                        LessonPlanContent = lessonContent,
                        LecturerActions = BuildLecturerActions(map, matches, authoringRules),
                        LearnerActions = BuildLearnerActions(map, matches, authoringRules),
                        LearningAids = learningAids
                    };

                    db.LecturerToolkitEntries.Add(generatedRow);
                    existingRows.Add(generatedRow);
                    result.Created++;
                    result.Entries.Add(BuildDraftArtifact(map, matches, "created"));
                }
                else
                {
                    generatedRow.SubjectCode = map.SubjectCode;
                    generatedRow.SubjectDescription = map.SubjectDescription;
                    generatedRow.AssessmentCriteriaDescription = map.CriteriaDescription;
                    generatedRow.Lpn = BuildAutoLpn(map);
                    generatedRow.LessonPlanDescription = BuildLessonPlanDescription(map);
                    generatedRow.LessonPlanContent = lessonContent;
                    generatedRow.LecturerActions = BuildLecturerActions(map, matches, authoringRules);
                    generatedRow.LearnerActions = BuildLearnerActions(map, matches, authoringRules);
                    generatedRow.LearningAids = learningAids;

                    result.Updated++;
                    result.Entries.Add(BuildDraftArtifact(map, matches, "updated"));
                }
            }

            if (result.Created > 0 || result.Updated > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            if (result.Created == 0 && result.Updated == 0 && result.Skipped > 0)
            {
                result.Warnings.Add("All lesson-plan draft candidates were skipped because they already have manual rows or lacked confident, grounded subject-matter coverage.");
            }

            return result;
        }

        private static LessonPlanDraftArtifact BuildDraftArtifact(CriteriaSourceMapItem map, IReadOnlyList<ChunkMatch> matches, string status)
        {
            return new LessonPlanDraftArtifact
            {
                CriteriaId = map.CriteriaId,
                CriteriaDescription = map.CriteriaDescription,
                SubjectCode = map.SubjectCode,
                TopicCode = map.TopicCode,
                Status = status,
                ConfidencePercent = (int)Math.Round(matches[0].Confidence * 100d),
                Citations = matches.Select(x => x.Citation).ToList()
            };
        }

        private static string BuildLessonPlanDescription(CriteriaSourceMapItem map)
        {
            if (!string.IsNullOrWhiteSpace(map.TopicDescription))
            {
                return $"{map.TopicCode} - {map.TopicDescription}".Trim(' ', '-');
            }

            return map.CriteriaDescription;
        }

        private static string BuildAutoLpn(CriteriaSourceMapItem map)
        {
            var topicCode = Regex.Replace(map.TopicCode ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty);
            if (string.IsNullOrWhiteSpace(topicCode))
            {
                topicCode = $"AC{map.CriteriaId}";
            }

            return $"AUTO-{topicCode}-{map.CriteriaId:D4}";
        }

        private static string BuildLessonPlanContent(
            CriteriaSourceMapItem map,
            IReadOnlyList<ChunkMatch> matches,
            LearningMaterialAuthoringRules authoringRules)
        {
            _ = authoringRules;
            var blocks = new List<string>();
            var evidenceContent = BuildEvidenceBackedNarrative(map, matches, includeLead: false);
            if (!string.IsNullOrWhiteSpace(evidenceContent))
            {
                blocks.Add(evidenceContent);
            }

            var compiled = CleanGeneratedText(string.Join("\n\n", blocks.Where(x => !string.IsNullOrWhiteSpace(x))));
            if (HasGroundedLessonPlanContent(compiled))
            {
                return compiled;
            }

            return BuildInsufficientCoverageLessonContent(map, matches);
        }

        private static string BuildKeyConceptSummary(CriteriaSourceMapItem map, IReadOnlyList<ChunkMatch> matches)
        {
            var keywords = ExtractWeightedKeywords(map, matches)
                .Where(IsUsableEvidenceKeyword)
                .Take(5)
                .ToList();
            if (keywords.Count < 2)
            {
                return string.Empty;
            }

            return $"{BuildTopicLabel(map)} covers {JoinAsPhrase(keywords)}.";
        }

        private static string BuildEvidenceBackedNarrative(
            CriteriaSourceMapItem map,
            IReadOnlyList<ChunkMatch> matches,
            bool includeLead = true)
        {
            var selectedSentences = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targetTokens = BuildTargetTokens($"{map.SubjectDescription} {map.TopicDescription} {map.CriteriaDescription}");
            foreach (var match in matches)
            {
                foreach (var sentence in SelectEvidenceSentences(match.Excerpt))
                {
                    var normalized = NormalizeSearchPhrase(sentence);
                    if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
                    {
                        continue;
                    }

                    var overlap = BuildTargetTokens(sentence)
                        .Count(token => targetTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
                    if (overlap < 2)
                    {
                        continue;
                    }

                    if (LooksLikeGenericEvidenceInstruction(sentence))
                    {
                        continue;
                    }

                    selectedSentences.Add(sentence);
                    if (selectedSentences.Count >= 6)
                    {
                        break;
                    }
                }

                if (selectedSentences.Count >= 6)
                {
                    break;
                }
            }

            if (selectedSentences.Count == 0)
            {
                return string.Empty;
            }

            return CleanGeneratedText(string.Join(" ", selectedSentences));
        }

        private static IReadOnlyList<string> ExtractWeightedKeywords(CriteriaSourceMapItem map, IReadOnlyList<ChunkMatch> matches)
        {
            var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in BuildTargetTokens($"{map.SubjectDescription} {map.TopicDescription} {map.CriteriaDescription}"))
            {
                weights[token] = weights.TryGetValue(token, out var value) ? value + 2 : 2;
            }

            foreach (var match in matches)
            {
                foreach (var token in BuildTargetTokens(match.Excerpt).Take(24))
                {
                    weights[token] = weights.TryGetValue(token, out var value) ? value + 1 : 1;
                }
            }

            return weights
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Key)
                .Take(8)
                .ToList();
        }

        private static IEnumerable<string> SelectEvidenceSentences(string excerpt)
        {
            return Regex.Split(excerpt ?? string.Empty, @"(?<=[\.\!\?])\s+")
                .Select(CleanGeneratedText)
                .Where(sentence => DocumentTextCleaner.WordCount(sentence) >= 7)
                .Where(sentence => sentence.Length <= 260)
                .Where(sentence => !LooksLikeGenericEvidenceInstruction(sentence))
                .Take(4)
                .ToList();
        }

        private static string BuildCriteriaFocus(CriteriaSourceMapItem map)
        {
            _ = map;
            return string.Empty;
        }

        private static string BuildLecturerActions(
            CriteriaSourceMapItem map,
            IReadOnlyList<ChunkMatch> matches,
            LearningMaterialAuthoringRules authoringRules)
        {
            if (authoringRules.DisableRigidLessonTemplate)
            {
                return string.Empty;
            }

            return CleanGeneratedText(
                $"Introduce {BuildTopicLabel(map)} as a practical vocational lesson. Demonstrate the correct concepts, sequence, tools, safety requirements, and quality checks, then connect the explanation back to the full assessment criteria that the learner must meet.");
        }

        private static string BuildLearnerActions(
            CriteriaSourceMapItem map,
            IReadOnlyList<ChunkMatch> matches,
            LearningMaterialAuthoringRules authoringRules)
        {
            _ = matches;
            if (authoringRules.DisableRigidLessonTemplate)
            {
                return CleanGeneratedText(
                    $"Use the grounded explanation for {BuildTopicLabel(map)} and be ready to {NormalizeSentence(map.CriteriaDescription)}.");
            }

            return CleanGeneratedText(
                $"Use the explained subject matter for {BuildTopicLabel(map)}, identify the key technical concepts, practise the correct sequence or decision points, and explain how the topic helps you {NormalizeSentence(map.CriteriaDescription)} in real workplace conditions.");
        }

        private static string BuildLearningAids(CriteriaSourceMapItem map, IReadOnlyList<ChunkMatch> matches, bool hasGroundedLessonContent)
        {
            var sb = new StringBuilder();
            sb.AppendLine(AutoDraftMarker);
            if (!hasGroundedLessonContent)
            {
                sb.AppendLine(AutoDraftCoverageGapMarker);
                sb.AppendLine("Coverage status: insufficient_grounded_content");
            }
            sb.AppendLine($"Topic: {BuildTopicLabel(map)}");
            sb.AppendLine($"Confidence: {Math.Round(matches[0].Confidence * 100d)}%");
            sb.AppendLine("Rule: table-of-contents text is reference-only and must not be imported as lesson content.");
            sb.AppendLine("Mapped sources:");
            foreach (var match in matches.Select((value, index) => $"[{index + 1}] {value.Citation}"))
            {
                sb.AppendLine(match);
            }

            return CleanGeneratedText(sb.ToString());
        }

        private static string BuildTopicLabel(CriteriaSourceMapItem map)
        {
            return $"{map.TopicCode} {map.TopicDescription}".Trim();
        }

        private static bool ContainsAutoDraftMarker(string? value)
        {
            return (value ?? string.Empty).IndexOf(AutoDraftMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasGroundedLessonPlanContent(string? value)
        {
            var cleaned = CleanGeneratedText(value ?? string.Empty)
                .Replace(AutoDraftMarker, string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(AutoDraftCoverageGapMarker, string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
            return !string.IsNullOrWhiteSpace(cleaned);
        }

        private static string BuildInsufficientCoverageLessonContent(CriteriaSourceMapItem map, IReadOnlyList<ChunkMatch> matches)
        {
            var sb = new StringBuilder();
            sb.AppendLine(AutoDraftMarker);
            sb.AppendLine(AutoDraftCoverageGapMarker);
            sb.Append($"Insufficient grounded subject-matter coverage is available to answer {NormalizeSentence(map.CriteriaDescription)} directly for {BuildTopicLabel(map)}.");

            var citations = matches
                .Select(x => x.Citation)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            if (citations.Count > 0)
            {
                sb.Append(' ');
                sb.Append("Closest mapped sources: ");
                sb.Append(string.Join("; ", citations));
                sb.Append('.');
            }

            return CleanGeneratedText(sb.ToString());
        }

        private static bool IsUsableEvidenceKeyword(string? value)
        {
            var token = NormalizeLine(value);
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (token.Length < 4 || token.Length > 32)
            {
                return false;
            }

            return token.Count(char.IsLetter) >= 3;
        }

        private static bool LooksLikeGenericEvidenceInstruction(string? value)
        {
            var normalized = NormalizeSearchPhrase(value ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return normalized.StartsWith("focus your study of", StringComparison.Ordinal)
                || normalized.StartsWith("build your understanding of", StringComparison.Ordinal)
                || normalized.StartsWith("learn what each term means", StringComparison.Ordinal)
                || normalized.StartsWith("you must understand", StringComparison.Ordinal)
                || normalized.StartsWith("study the explanation below", StringComparison.Ordinal)
                || normalized.StartsWith("work through the topic", StringComparison.Ordinal)
                || normalized.StartsWith("study the mapped source content", StringComparison.Ordinal)
                || normalized.StartsWith("study the lesson material", StringComparison.Ordinal)
                || normalized.StartsWith("this lesson develops the learner s ability to", StringComparison.Ordinal);
        }

        private static ChunkArtifact BuildChunkArtifact(SourceMaterialChunk chunk)
        {
            return new ChunkArtifact
            {
                ChunkId = chunk.Id,
                MaterialId = chunk.MaterialId,
                MaterialTitle = chunk.MaterialTitle,
                FileName = chunk.FileName,
                KnowledgeNumber = chunk.KnowledgeNumber,
                KnowledgeSourceType = chunk.KnowledgeSourceType,
                Citation = chunk.Citation,
                ChapterTitle = chunk.ChapterTitle,
                PageNumber = chunk.PageNumber,
                Excerpt = chunk.Excerpt
            };
        }

        private static async Task<string> WriteJsonArtifactAsync<T>(
            string directory,
            string fileName,
            T value,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await File.WriteAllTextAsync(path, json, cancellationToken);
            return path;
        }

        private static string BuildExcerpt(string text)
        {
            var trimmed = CleanGeneratedText(text);
            if (trimmed.Length <= 520)
            {
                return trimmed;
            }

            var excerpt = trimmed[..520].TrimEnd();
            var lastStop = excerpt.LastIndexOfAny(new[] { '.', '!', '?' });
            if (lastStop >= 180)
            {
                excerpt = excerpt[..(lastStop + 1)];
            }

            return excerpt.Trim();
        }

        private static string BuildCitation(SourceMaterialRow material, int pageNumber, string chapterTitle)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(material.OriginalSourceName))
            {
                parts.Add(material.OriginalSourceName);
            }
            else if (!string.IsNullOrWhiteSpace(material.Title))
            {
                parts.Add(material.Title);
            }

            if (pageNumber > 0)
            {
                parts.Add($"p. {pageNumber}");
            }

            if (!string.IsNullOrWhiteSpace(chapterTitle))
            {
                parts.Add(chapterTitle);
            }

            return string.Join(" | ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static string BuildSearchText(
            SourceMaterialRow material,
            string text,
            string chapterTitle,
            IReadOnlyList<string> navigationHints)
        {
            var composed = string.Join("\n", new[]
            {
                material.Title,
                material.KnowledgeLabel,
                material.OriginalSourceName,
                chapterTitle,
                string.Join(" ", navigationHints.Take(8)),
                text
            });

            var normalized = NormalizeSearchPhrase(composed);
            return string.IsNullOrWhiteSpace(normalized) ? string.Empty : $" {normalized} ";
        }

        private static HashSet<string> BuildIndexTokens(string searchText)
        {
            return searchText
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length >= 4 || token.Any(char.IsDigit))
                .Where(token => !StopWords.Contains(token))
                .Where(token => !IsAdministrativeToken(token))
                .Take(48)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static List<string> BuildTargetTokens(string value)
        {
            return NormalizeSearchPhrase(value)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length >= 4 || token.Any(char.IsDigit))
                .Where(token => !StopWords.Contains(token))
                .Where(token => !IsAdministrativeToken(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToList();
        }

        private static string NormalizeSearchPhrase(string value)
        {
            var source = (value ?? string.Empty).ToLowerInvariant();
            var normalized = Regex.Replace(source, @"[^a-z0-9]+", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        private static bool ContainsPhrase(string searchText, string phrase)
        {
            if (string.IsNullOrWhiteSpace(searchText) || string.IsNullOrWhiteSpace(phrase))
            {
                return false;
            }

            return searchText.Contains($" {phrase} ", StringComparison.Ordinal);
        }

        private static int CountOverlap(IEnumerable<string> targetTokens, IReadOnlySet<string> chunkTokens)
        {
            var count = 0;
            foreach (var token in targetTokens)
            {
                if (chunkTokens.Contains(token))
                {
                    count++;
                }
            }

            return count;
        }

        private static double ScoreToConfidence(int score)
        {
            return score switch
            {
                >= 32 => 0.97d,
                >= 28 => 0.93d,
                >= 24 => 0.88d,
                >= 20 => 0.81d,
                >= 16 => 0.73d,
                >= 12 => 0.66d,
                >= 8 => 0.56d,
                _ => 0.45d
            };
        }

        private static void FinalizeTopicEvidenceSummary(TopicEvidenceSummary summary)
        {
            summary.TopicsWithEvidenceCount = summary.Topics.Count(topic => topic.EvidenceCount > 0);
            summary.MappedTopicsCount = summary.Topics.Count(topic => string.Equals(topic.CoverageBand, "mapped", StringComparison.OrdinalIgnoreCase));
            summary.DevelopingTopicsCount = summary.Topics.Count(topic => string.Equals(topic.CoverageBand, "developing", StringComparison.OrdinalIgnoreCase));
            summary.GapTopicsCount = Math.Max(0, summary.Topics.Count - summary.MappedTopicsCount - summary.DevelopingTopicsCount);
            summary.CoveragePercent = summary.Topics.Count == 0
                ? 0
                : ClampPercent(summary.Topics.Average(topic => topic.CoveragePercent));
        }

        private static int ClampPercent(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            return (int)Math.Max(0d, Math.Min(100d, Math.Round(value)));
        }

        private static string DetermineCoverageBand(int coveragePercent)
        {
            if (coveragePercent >= 75)
            {
                return "mapped";
            }

            if (coveragePercent >= 40)
            {
                return "developing";
            }

            return "gap";
        }

        private static string DetermineCoverageBandLabel(string coverageBand)
        {
            return coverageBand switch
            {
                "mapped" => "Mapped",
                "developing" => "Developing",
                _ => "Gap"
            };
        }

        private static string NormalizeSentence(string value)
        {
            var text = CleanGeneratedText(value).Trim();
            text = Regex.Replace(text, @"\b(?:IAC|AC|ELO)\d+[A-Za-z0-9]*\b", string.Empty, RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\(\s*Weight\s*\d+%?\s*\)", string.Empty, RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "demonstrate the mapped outcome";
            }

            return (char.ToLowerInvariant(text[0]) + text[1..]).TrimEnd('.', ';');
        }

        private static bool IsAdministrativeToken(string token)
        {
            var value = (token ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return Regex.IsMatch(value, @"^(?:iac|ac|elo)\d+[a-z0-9]*$", RegexOptions.IgnoreCase)
                || value.Equals("weight", StringComparison.OrdinalIgnoreCase);
        }

        private static string JoinAsPhrase(IReadOnlyList<string> values)
        {
            if (values.Count == 0) return string.Empty;
            if (values.Count == 1) return values[0];
            if (values.Count == 2) return $"{values[0]} and {values[1]}";
            return string.Join(", ", values.Take(values.Count - 1)) + $", and {values[^1]}";
        }

        private static string CleanGeneratedText(string value)
        {
            var cleaned = DocumentTextCleaner.Clean(value, preservePdfPageMarkers: false);
            cleaned = Regex.Replace(cleaned, @"\[(?:page|ocr)[^\]]+\]", " ", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<[^>]+>", " ");
            cleaned = Regex.Replace(cleaned, @"[^\S\r\n]{2,}", " ");
            cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n");
            return cleaned.Trim();
        }

        private static string NormalizeLine(string? value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        }

        private static string NormalizeDuplicateCriteriaKey(string? value)
        {
            return Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"\s+", " ").Trim();
        }

        private static string TruncateWithEllipsis(string value, int maxLength)
        {
            var normalized = NormalizeLine(value);
            if (normalized.Length <= maxLength)
            {
                return normalized;
            }

            return normalized[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
        }

        private static string CleanHeading(string value)
        {
            var cleaned = CleanGeneratedText(value);
            return cleaned.Length > 90 ? cleaned[..90].Trim() : cleaned;
        }

        private static bool IsLikelyHeading(string line)
        {
            var text = NormalizeLine(line);
            if (string.IsNullOrWhiteSpace(text) || text.Length > 110)
            {
                return false;
            }

            if (Regex.IsMatch(text, @"^(chapter|section|module|unit|part)\s+[a-z0-9ivx]+(?:[\.\-:]\s*|\s+).{3,}$", RegexOptions.IgnoreCase))
            {
                return true;
            }

            if (text.EndsWith(".") || text.EndsWith(";"))
            {
                return false;
            }

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2 || words.Length > 10)
            {
                return false;
            }

            return words.Count(word => word.All(ch => !char.IsLetter(ch) || char.IsUpper(ch))) >= Math.Max(2, words.Length - 1);
        }

        private static bool IsTableOfContentsHeading(string line)
        {
            var normalized = NormalizeSearchPhrase(line);
            return normalized == "table of contents" ||
                   normalized == "contents" ||
                   normalized == "list of figures" ||
                   normalized == "list of tables" ||
                   normalized == "index";
        }

        private static bool LooksLikeTableOfContentsLine(string line)
        {
            if (Regex.IsMatch(line, @"^.{0,180}\.{2,}\s*\d{1,4}\s*$"))
            {
                return true;
            }

            if (Regex.IsMatch(line, @"^(chapter|section|module|unit)\s+.+\s+\d{1,4}$", RegexOptions.IgnoreCase))
            {
                return true;
            }

            return Regex.IsMatch(line, @"^[A-Za-z].{3,120}\s+\d{1,4}$") &&
                   DocumentTextCleaner.WordCount(line) <= 12;
        }

        private static string ExtractNavigationHint(string line)
        {
            var value = Regex.Replace(line ?? string.Empty, @"\.{2,}\s*\d{1,4}\s*$", " ");
            value = Regex.Replace(value, @"\s+\d{1,4}\s*$", " ");
            return CleanGeneratedText(value);
        }

        private static void AddUnique(List<string> list, string value, int maxCount)
        {
            if (string.IsNullOrWhiteSpace(value) || list.Count >= maxCount)
            {
                return;
            }

            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(value);
            }
        }

        private static string EnsureUniquePath(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return path;
            }

            var directory = Path.GetDirectoryName(path) ?? ".";
            var stem = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (var i = 1; i < 10000; i++)
            {
                var candidate = Path.Combine(directory, $"{stem}_{i}{ext}");
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(directory, $"{stem}_{Guid.NewGuid():N}{ext}");
        }

        private static bool IsImageSidecarFile(string path)
        {
            var fileName = Path.GetFileName(path) ?? string.Empty;
            if (!fileName.Contains(".caption.", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var stem = fileName[..fileName.IndexOf(".caption.", StringComparison.OrdinalIgnoreCase)];
            return ImageExtensions.Contains(Path.GetExtension(stem));
        }

        private static string NormalizeMatchToken(string value)
        {
            return Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
        }

        public sealed class DeliveryPilotRequest
        {
            public int QualificationId { get; set; }
            public string JobFolder { get; set; } = string.Empty;
            public bool PopulateLessonPlanDrafts { get; set; } = true;
        }

        public sealed class DeliveryPilotResult
        {
            public int QualificationId { get; set; }
            public string QualificationCode { get; set; } = string.Empty;
            public string QualificationDescription { get; set; } = string.Empty;
            public string QualificationRootPath { get; set; } = string.Empty;
            public string ArtifactsDirectory { get; set; } = string.Empty;
            public string DetectedExternalResourceFolder { get; set; } = string.Empty;
            public int SourceMaterialCount { get; set; }
            public int SourceChunkCount { get; set; }
            public int TopicCount { get; set; }
            public int CriteriaCount { get; set; }
            public int TopicsMappedCount { get; set; }
            public int CriteriaMappedCount { get; set; }
            public int LessonPlanDraftsCreated { get; set; }
            public int LessonPlanDraftsUpdated { get; set; }
            public int LessonPlanDraftsSkipped { get; set; }
            public string SourceChunksPath { get; set; } = string.Empty;
            public string TopicSourceMapPath { get; set; } = string.Empty;
            public string CriteriaSourceMapPath { get; set; } = string.Empty;
            public string LessonPlanDraftsPath { get; set; } = string.Empty;
            public ImportResult Import { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
        }

        public sealed class ImportResult
        {
            public string DetectedExternalFolder { get; set; } = string.Empty;
            public int CopiedToInboxCount { get; set; }
            public int SkippedCount { get; set; }
            public int SyncCreatedCount { get; set; }
            public int SyncSkippedCount { get; set; }
            public int SyncFailedCount { get; set; }
            public string CoverageReportPath { get; set; } = string.Empty;
            public List<string> Warnings { get; set; } = new();
        }

        private sealed class SourceMaterialRow
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public string FileType { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string KnowledgeSourceType { get; set; } = string.Empty;
            public int KnowledgeNumber { get; set; }
            public DateTime UploadedAtUtc { get; set; }
            public string AssessmentCriteriaDescription { get; set; } = string.Empty;
            public string KnowledgeLabel { get; set; } = string.Empty;
            public string ExtractedText { get; set; } = string.Empty;
            public string OriginalSourceName { get; set; } = string.Empty;
        }

        private sealed class SourceMaterialChunk
        {
            public string Id { get; set; } = string.Empty;
            public int MaterialId { get; set; }
            public string MaterialTitle { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public string FileType { get; set; } = string.Empty;
            public int KnowledgeNumber { get; set; }
            public string KnowledgeSourceType { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public int PageNumber { get; set; }
            public string ChapterTitle { get; set; } = string.Empty;
            public string Citation { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
            public string Excerpt { get; set; } = string.Empty;
            public string SearchText { get; set; } = string.Empty;
            public HashSet<string> IndexTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ChunkArtifact
        {
            public string ChunkId { get; set; } = string.Empty;
            public int MaterialId { get; set; }
            public string MaterialTitle { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public int KnowledgeNumber { get; set; }
            public string KnowledgeSourceType { get; set; } = string.Empty;
            public string Citation { get; set; } = string.Empty;
            public string ChapterTitle { get; set; } = string.Empty;
            public int PageNumber { get; set; }
            public string Excerpt { get; set; } = string.Empty;
        }

        private sealed class CurriculumTopicTarget
        {
            public int TopicId { get; set; }
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string TopicPurpose { get; set; } = string.Empty;
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public string SubjectPurpose { get; set; } = string.Empty;
            public string TopicPhrase { get; set; } = string.Empty;
            public string SubjectPhrase { get; set; } = string.Empty;
            public List<string> TopicTokens { get; set; } = new();
            public List<string> SubjectTokens { get; set; } = new();
        }

        private sealed class CurriculumCriteriaTarget
        {
            public int CriteriaId { get; set; }
            public string CriteriaDescription { get; set; } = string.Empty;
            public int TopicId { get; set; }
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string TopicPurpose { get; set; } = string.Empty;
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public string SubjectPurpose { get; set; } = string.Empty;
            public string CriteriaPhrase { get; set; } = string.Empty;
            public string TopicPhrase { get; set; } = string.Empty;
            public string SubjectPhrase { get; set; } = string.Empty;
            public List<string> CriteriaTokens { get; set; } = new();
            public List<string> TopicTokens { get; set; } = new();
            public List<string> SubjectTokens { get; set; } = new();
        }

        public sealed class TopicSourceMapItem
        {
            public int TopicId { get; set; }
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public List<ChunkMatch> Matches { get; set; } = new();
        }

        public sealed class CriteriaSourceMapItem
        {
            public int CriteriaId { get; set; }
            public string CriteriaDescription { get; set; } = string.Empty;
            public int TopicId { get; set; }
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public List<ChunkMatch> Matches { get; set; } = new();
        }

        public sealed class ChunkMatch
        {
            public int MaterialId { get; set; }
            public string ChunkId { get; set; } = string.Empty;
            public string MaterialTitle { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public int KnowledgeNumber { get; set; }
            public string KnowledgeSourceType { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string Citation { get; set; } = string.Empty;
            public string ChapterTitle { get; set; } = string.Empty;
            public int PageNumber { get; set; }
            public int Score { get; set; }
            public double Confidence { get; set; }
            public int TopicTokenMatches { get; set; }
            public int SubjectTokenMatches { get; set; }
            public int CriteriaTokenMatches { get; set; }
            public string Excerpt { get; set; } = string.Empty;
        }

        private sealed class DraftWriteResult
        {
            public int Created { get; set; }
            public int Updated { get; set; }
            public int Skipped { get; set; }
            public List<LessonPlanDraftArtifact> Entries { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
        }

        public sealed class LessonPlanDraftArtifact
        {
            public int CriteriaId { get; set; }
            public string CriteriaDescription { get; set; } = string.Empty;
            public string SubjectCode { get; set; } = string.Empty;
            public string TopicCode { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public int ConfidencePercent { get; set; }
            public List<string> Citations { get; set; } = new();
        }

        public sealed class TopicEvidenceSummary
        {
            public int QualificationId { get; set; }
            public string QualificationNumber { get; set; } = string.Empty;
            public string QualificationDescription { get; set; } = string.Empty;
            public int SourceMaterialCount { get; set; }
            public int SourceChunkCount { get; set; }
            public int TopicCount { get; set; }
            public int TopicsWithEvidenceCount { get; set; }
            public int MappedTopicsCount { get; set; }
            public int DevelopingTopicsCount { get; set; }
            public int GapTopicsCount { get; set; }
            public int CoveragePercent { get; set; }
            public List<TopicEvidenceItem> Topics { get; set; } = new();
            public List<DuplicateCriteriaGroup> DuplicateCriteriaGroups { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
        }

        public sealed class TopicEvidenceItem
        {
            public int TopicId { get; set; }
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public int CoveragePercent { get; set; }
            public string CoverageBand { get; set; } = string.Empty;
            public string CoverageBandLabel { get; set; } = string.Empty;
            public int EvidenceCount { get; set; }
            public int DistinctSourceCount { get; set; }
            public int BestConfidencePercent { get; set; }
            public int AverageConfidencePercent { get; set; }
            public List<string> TopCitations { get; set; } = new();
            public List<TopicEvidenceSource> TopEvidence { get; set; } = new();
        }

        public sealed class TopicEvidenceSource
        {
            public string Citation { get; set; } = string.Empty;
            public int ConfidencePercent { get; set; }
            public string KnowledgeSourceType { get; set; } = string.Empty;
            public string MaterialTitle { get; set; } = string.Empty;
            public int PageNumber { get; set; }
            public string Excerpt { get; set; } = string.Empty;
        }

        public sealed class DuplicateCriteriaGroup
        {
            public string CriteriaDescription { get; set; } = string.Empty;
            public int TopicCount { get; set; }
            public List<string> Topics { get; set; } = new();
        }
    }
}
