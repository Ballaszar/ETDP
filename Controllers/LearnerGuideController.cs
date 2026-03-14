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
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LearnerGuideController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private static readonly HttpClient _http = new();
        private const string DefaultModeratorResponsesEndpoint = "";
        private const string AssessmentCriteriaLeadLine = "By the end of this subject the learner will be able to demonstrate competence against the following assessment criteria:";
        private const string ChapterSummaryLeadLine = "In this chapter we have discussed all the tasks and activities to adhere to the assessment criteria applicable to this Chapter namely:";

        public LearnerGuideController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            var items = _context.LearnerGuides.Select(lg => new ETD.Api.DTOs.LearnerGuideDto
            {
                Id = lg.Id,
                SubjectId = lg.SubjectId,
                Title = lg.Title,
                Version = lg.Version,
                Content = lg.Content
            }).ToList();
            return Ok(items);
        }

        [HttpGet("{id}")]
        public IActionResult Get(int id)
        {
            var lg = _context.LearnerGuides.Find(id);
            if (lg == null) return NotFound();
            return Ok(new ETD.Api.DTOs.LearnerGuideDto
            {
                Id = lg.Id,
                SubjectId = lg.SubjectId,
                Title = lg.Title,
                Version = lg.Version,
                Content = lg.Content
            });
        }

        [HttpPost]
        public IActionResult Create(ETD.Api.DTOs.CreateLearnerGuideDto dto)
        {
            var model = new LearnerGuide
            {
                SubjectId = dto.SubjectId,
                Title = dto.Title,
                Version = dto.Version,
                Content = dto.Content
            };
            _context.LearnerGuides.Add(model);
            _context.SaveChanges();
            return CreatedAtAction(nameof(Get), new { id = model.Id }, model);
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, ETD.Api.DTOs.UpdateLearnerGuideDto dto)
        {
            var item = _context.LearnerGuides.Find(id);
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
            var item = _context.LearnerGuides.Find(id);
            if (item == null) return NotFound();
            _context.LearnerGuides.Remove(item);
            _context.SaveChanges();
            return NoContent();
        }

        public class ParaphraseRequest
        {
            public string Text { get; set; } = string.Empty;
            public string Style { get; set; } = "educational";
            public bool PreserveTerminology { get; set; } = true;
        }

        public class ParaphraseWorkflowRequest
        {
            public int? QualificationId { get; set; }
            public string Style { get; set; } = "educational";
            public bool PreserveTerminology { get; set; } = true;
            public bool ForceRefresh { get; set; } = false;
        }

        public class ParaphraseCacheFile
        {
            public int QualificationId { get; set; }
            public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
            public Dictionary<string, ParaphraseCacheEntry> Entries { get; set; } = new(StringComparer.Ordinal);
        }

        public class ParaphraseCacheEntry
        {
            public string ParaphrasedText { get; set; } = string.Empty;
            public string Backend { get; set; } = "unknown";
            public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        }

        public class ParaphraseReviewSaveRequest
        {
            public int? QualificationId { get; set; }
            public List<ParaphraseReviewSaveItem> Entries { get; set; } = new();
        }

        public class ParaphraseReviewSaveItem
        {
            public string SourceText { get; set; } = string.Empty;
            public string ParaphrasedText { get; set; } = string.Empty;
        }

        [HttpPost("paraphrase")]
        public async Task<IActionResult> Paraphrase([FromBody] ParaphraseRequest req)
        {
            var text = (req?.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) return BadRequest("Text is required.");
            var (paraphrased, backend) = await ParaphraseTextWithBackendAsync(text, req?.Style ?? "educational", req?.PreserveTerminology ?? true);
            return Ok(new { paraphrased, backend, changed = !string.Equals(text, paraphrased, StringComparison.Ordinal) });
        }

        [HttpPost("paraphrase-workflow")]
        public async Task<IActionResult> BuildParaphraseWorkflow([FromBody] ParaphraseWorkflowRequest? req)
        {
            req ??= new ParaphraseWorkflowRequest();

            var qualification = req.QualificationId.HasValue && req.QualificationId.Value > 0
                ? _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId.Value)
                : _context.Qualifications.FirstOrDefault();
            if (qualification == null) return BadRequest("No qualification available for paraphrase workflow.");

            var subjects = _context.Subjects.Where(s => s.QualificationId == qualification.Id)
                .OrderBy(s => s.CurriculumPhaseId).ThenBy(s => s.SubjectCode).ToList();
            subjects = subjects.Where(HasSubjectIdentity).ToList();
            if (subjects.Count == 0) return BadRequest("No subjects found for this qualification.");

            var topics = _context.Topics.Where(t => subjects.Select(s => s.Id).Contains(t.SubjectId))
                .OrderBy(t => t.Order ?? int.MaxValue).ThenBy(t => t.TopicCode).ThenBy(t => t.Id).ToList();
            var criteria = _context.AssessmentCriteria.Where(c => topics.Select(t => t.Id).Contains(c.TopicId))
                .OrderBy(c => c.TopicId).ThenBy(c => c.Id).ToList();
            var lessonPlans = _context.LessonPlans.Where(lp => criteria.Select(c => c.Id).Contains(lp.AssessmentCriteriaId))
                .OrderBy(lp => lp.SortOrder).ThenBy(lp => lp.Id).ToList();
            var toolkit = _context.LecturerToolkitEntries
                .Where(e => e.QualificationsId <= 0 || e.QualificationsId == qualification.Id)
                .ToList()
                .OrderBy(e => ParseLpnSort(e.Lpn))
                .ThenBy(e => e.Id)
                .ToList();

            var topicsBySubject = topics.GroupBy(t => t.SubjectId).ToDictionary(g => g.Key, g => g.ToList());
            var criteriaByTopic = criteria.GroupBy(c => c.TopicId).ToDictionary(g => g.Key, g => g.ToList());
            var lessonByCriteria = lessonPlans.GroupBy(lp => lp.AssessmentCriteriaId).ToDictionary(g => g.Key, g => g.ToList());

            var cacheFile = LoadParaphraseCacheFile(qualification.Id);
            if (cacheFile.QualificationId <= 0) cacheFile.QualificationId = qualification.Id;
            if (cacheFile.Entries == null) cacheFile.Entries = new Dictionary<string, ParaphraseCacheEntry>(StringComparer.Ordinal);

            var uniqueTexts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var subject in subjects)
            {
                var purpose = (subject.SubjectPurpose ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(purpose) && purpose.Length >= 32) uniqueTexts.Add(purpose);

                var subjectTopics = topicsBySubject.TryGetValue(subject.Id, out var st) ? st : new List<Topic>();
                foreach (var topic in subjectTopics)
                {
                    var topicCriteria = criteriaByTopic.TryGetValue(topic.Id, out var tc) ? tc : new List<AssessmentCriteria>();
                    foreach (var criterion in topicCriteria)
                    {
                        var tkList = ResolveToolkitRowsForCriterion(toolkit, criterion, subject);
                        if (tkList.Count > 0)
                        {
                            foreach (var tk in tkList)
                            {
                                var label = NormalizeLpn(tk.Lpn);
                                var desc = string.IsNullOrWhiteSpace(tk.LessonPlanDescription) ? "Lesson Plan" : tk.LessonPlanDescription.Trim();
                                var content = (tk.LessonPlanContent ?? string.Empty).Trim();
                                var block = string.IsNullOrWhiteSpace(content) ? $"{label}: {desc}" : $"{label}: {desc}\n{content}";
                                if (!string.IsNullOrWhiteSpace(block) && block.Length >= 32) uniqueTexts.Add(block.Trim());
                            }
                        }
                        else if (lessonByCriteria.TryGetValue(criterion.Id, out var lpList) && lpList.Count > 0)
                        {
                            foreach (var lp in lpList)
                            {
                                var title = string.IsNullOrWhiteSpace(lp.Title) ? "Lesson Plan" : lp.Title.Trim();
                                var content = (lp.Content ?? string.Empty).Trim();
                                var block = string.IsNullOrWhiteSpace(content) ? title : $"{title}\n{content}";
                                if (!string.IsNullOrWhiteSpace(block) && block.Length >= 32) uniqueTexts.Add(block.Trim());
                            }
                        }
                    }
                }
            }

            var created = 0;
            var refreshed = 0;
            var reused = 0;
            var failed = 0;
            var backendCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var text in uniqueTexts)
            {
                if (!req.ForceRefresh &&
                    cacheFile.Entries.TryGetValue(text, out var existing) &&
                    !string.IsNullOrWhiteSpace(existing.ParaphrasedText))
                {
                    reused++;
                    continue;
                }

                var (paraphrased, backend) = await ParaphraseTextWithBackendAsync(text, req.Style, req.PreserveTerminology);
                if (string.IsNullOrWhiteSpace(paraphrased))
                {
                    failed++;
                    continue;
                }

                if (cacheFile.Entries.ContainsKey(text)) refreshed++; else created++;
                cacheFile.Entries[text] = new ParaphraseCacheEntry
                {
                    ParaphrasedText = paraphrased.Trim(),
                    Backend = backend,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                backendCounts[backend] = backendCounts.TryGetValue(backend, out var count) ? count + 1 : 1;
            }

            cacheFile.UpdatedAtUtc = DateTime.UtcNow;
            var cachePath = GetParaphraseCachePath(qualification.Id);
            SaveParaphraseCacheFile(cacheFile);

            return Ok(new
            {
                qualificationId = qualification.Id,
                qualification = qualification.QualificationNumber,
                totalCandidates = uniqueTexts.Count,
                created,
                refreshed,
                reused,
                failed,
                backendCounts,
                cachePath
            });
        }

        [HttpGet("paraphrase-review")]
        public IActionResult GetParaphraseReview([FromQuery] int? qualificationId = null, [FromQuery] int take = 120, [FromQuery] int skip = 0)
        {
            var qualification = qualificationId.HasValue && qualificationId.Value > 0
                ? _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId.Value)
                : _context.Qualifications.FirstOrDefault();
            if (qualification == null) return BadRequest("No qualification available for paraphrase review.");

            var normalizedTake = Math.Max(1, Math.Min(600, take));
            var normalizedSkip = Math.Max(0, skip);
            var file = LoadParaphraseCacheFile(qualification.Id);
            var entries = (file.Entries ?? new Dictionary<string, ParaphraseCacheEntry>(StringComparer.Ordinal))
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                .Select(kv => new
                {
                    sourceText = kv.Key,
                    paraphrasedText = kv.Value?.ParaphrasedText ?? string.Empty,
                    backend = kv.Value?.Backend ?? "unknown",
                    updatedAtUtc = kv.Value?.UpdatedAtUtc ?? file.UpdatedAtUtc
                })
                .OrderByDescending(x => x.updatedAtUtc)
                .ThenBy(x => x.sourceText.Length)
                .Skip(normalizedSkip)
                .Take(normalizedTake)
                .ToList();

            var total = file.Entries?.Count ?? 0;
            var hasMore = normalizedSkip + entries.Count < total;
            return Ok(new
            {
                qualificationId = qualification.Id,
                total,
                skip = normalizedSkip,
                take = normalizedTake,
                hasMore,
                entries
            });
        }

        [HttpPost("paraphrase-review/save")]
        public IActionResult SaveParaphraseReview([FromBody] ParaphraseReviewSaveRequest? req)
        {
            req ??= new ParaphraseReviewSaveRequest();
            var qualification = req.QualificationId.HasValue && req.QualificationId.Value > 0
                ? _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId.Value)
                : _context.Qualifications.FirstOrDefault();
            if (qualification == null) return BadRequest("No qualification available for paraphrase review save.");

            if (req.Entries == null || req.Entries.Count == 0)
                return BadRequest("No review entries provided.");

            var file = LoadParaphraseCacheFile(qualification.Id);
            if (file.QualificationId <= 0) file.QualificationId = qualification.Id;
            file.Entries ??= new Dictionary<string, ParaphraseCacheEntry>(StringComparer.Ordinal);

            var saved = 0;
            foreach (var item in req.Entries)
            {
                var source = (item?.SourceText ?? string.Empty).Trim();
                var paraphrased = (item?.ParaphrasedText ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(paraphrased)) continue;
                file.Entries[source] = new ParaphraseCacheEntry
                {
                    ParaphrasedText = paraphrased,
                    Backend = "manual_review",
                    UpdatedAtUtc = DateTime.UtcNow
                };
                saved++;
            }

            file.UpdatedAtUtc = DateTime.UtcNow;
            SaveParaphraseCacheFile(file);
            return Ok(new
            {
                qualificationId = qualification.Id,
                saved,
                total = file.Entries.Count,
                cachePath = GetParaphraseCachePath(qualification.Id)
            });
        }

        [HttpGet("export-readiness")]
        public IActionResult ExportReadiness(
            [FromQuery] int? qualificationId = null,
            [FromQuery] int? subjectId = null,
            [FromQuery] bool details = false)
        {
            var qualification = qualificationId.HasValue && qualificationId.Value > 0
                ? _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId.Value)
                : _context.Qualifications.FirstOrDefault();
            if (qualification == null) return BadRequest("No qualification available for Learner Guide export.");

            var qualificationSubjects = _context.Subjects
                .Where(s => s.QualificationId == qualification.Id)
                .OrderBy(s => s.CurriculumPhaseId)
                .ThenBy(s => s.SubjectCode)
                .ToList();
            qualificationSubjects = qualificationSubjects.Where(HasSubjectIdentity).ToList();
            if (qualificationSubjects.Count == 0) return BadRequest("No subjects found for this qualification.");

            if ((!subjectId.HasValue || subjectId.Value <= 0) && qualificationSubjects.Count > 1)
            {
                return Ok(new
                {
                    qualificationId = qualification.Id,
                    ready = false,
                    requiresSubjectSelection = true,
                    message = "Select a subjectId for Learner Guide export readiness check.",
                    subjects = qualificationSubjects.Select(s => new
                    {
                        id = s.Id,
                        subjectCode = (s.SubjectCode ?? string.Empty).Trim(),
                        subjectDescription = (s.SubjectDescription ?? string.Empty).Trim()
                    }).ToList()
                });
            }

            var subject = subjectId.HasValue && subjectId.Value > 0
                ? qualificationSubjects.FirstOrDefault(s => s.Id == subjectId.Value)
                : qualificationSubjects[0];
            if (subject == null) return BadRequest("Selected subjectId was not found for this qualification.");

            var rawTopics = _context.Topics
                .Where(t => t.SubjectId == subject.Id)
                .OrderBy(t => t.Order ?? int.MaxValue)
                .ThenBy(t => t.TopicCode)
                .ThenBy(t => t.Id)
                .ToList();
            var topicById = rawTopics.ToDictionary(t => t.Id);
            var topics = rawTopics
                .GroupBy(BuildTopicIdentity)
                .Select(g => g
                    .OrderBy(t => t.Order ?? int.MaxValue)
                    .ThenBy(t => t.TopicCode)
                    .ThenBy(t => t.Id)
                    .First())
                .ToList();

            var topicIds = rawTopics.Select(t => t.Id).ToList();
            var rawCriteria = _context.AssessmentCriteria
                .Where(c => topicIds.Contains(c.TopicId))
                .OrderBy(c => c.TopicId)
                .ThenBy(c => c.Id)
                .ToList();
            var criteriaGroups = rawCriteria
                .GroupBy(c => BuildCriterionIdentity(c, topicById))
                .Select(g => g.OrderBy(c => c.Id).ToList())
                .ToList();
            var criteria = criteriaGroups
                .Select(g => g.First())
                .ToList();
            var criteriaIds = rawCriteria.Select(c => c.Id).ToList();

            var lessonPlans = _context.LessonPlans
                .Where(lp => criteriaIds.Contains(lp.AssessmentCriteriaId))
                .OrderBy(lp => lp.SortOrder)
                .ThenBy(lp => lp.Id)
                .ToList();
            var lessonByCriteria = lessonPlans
                .GroupBy(lp => lp.AssessmentCriteriaId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var toolkit = _context.LecturerToolkitEntries
                .Where(e => e.QualificationsId <= 0 || e.QualificationsId == qualification.Id)
                .ToList()
                .OrderBy(e => ParseLpnSort(e.Lpn))
                .ThenBy(e => e.Id)
                .ToList();

            var toolkitForSubject = toolkit
                .Where(row => ToolkitMatchesSubjectCode(row, subject))
                .ToList();

            var toolkitRowsWithContent = toolkitForSubject
                .Where(HasGuideSourceContent)
                .ToList();
            var toolkitRowsWithContentOtherSubjects = toolkit
                .Where(row => !ToolkitMatchesSubjectCode(row, subject))
                .Where(HasGuideSourceContent)
                .ToList();

            var criteriaById = criteria
                .GroupBy(c => c.Id)
                .ToDictionary(g => g.Key, g => g.First());
            var mappedToolkitRowIds = new HashSet<int>();
            var criteriaDiagnostics = new List<CriteriaReadinessDiagnostic>();
            foreach (var group in criteriaGroups)
            {
                var c = group.First();
                var matchedRows = ResolveToolkitRowsForCriteria(toolkit, group, subject);
                foreach (var row in matchedRows)
                {
                    mappedToolkitRowIds.Add(row.Id);
                }

                var matchedRowsWithContent = matchedRows
                    .Count(HasGuideSourceContent);
                var fallbackPlans = ResolveLessonPlansForCriteria(lessonByCriteria, group);
                var fallbackPlansWithContent = fallbackPlans
                    .Count(HasGuideSourceContent);

                criteriaDiagnostics.Add(new CriteriaReadinessDiagnostic
                {
                    CriteriaId = c.Id,
                    CriteriaDescription = (c.Description ?? string.Empty).Trim(),
                    MatchedRows = matchedRows.Count,
                    MatchedRowsWithContent = matchedRowsWithContent,
                    FallbackPlans = fallbackPlans.Count,
                    FallbackPlansWithContent = fallbackPlansWithContent,
                    HasAnyContent = matchedRowsWithContent > 0 || fallbackPlansWithContent > 0
                });
            }

            var criteriaTotal = criteriaDiagnostics.Count;
            var criteriaMatched = criteriaDiagnostics.Count(x => x.MatchedRows > 0);
            var criteriaWithLessonContent = criteriaDiagnostics.Count(x => x.MatchedRowsWithContent > 0);
            var criteriaWithAnyContent = criteriaDiagnostics.Count(x => x.HasAnyContent);
            var missingCriteria = criteriaDiagnostics
                .Where(x => !x.HasAnyContent)
                .Select(x => new { criteriaId = x.CriteriaId, criteriaDescription = x.CriteriaDescription })
                .Take(20)
                .ToList();

            var totalLessonContentChars = toolkitRowsWithContent
                .Sum(row =>
                    (row.LessonPlanContent ?? string.Empty).Trim().Length +
                    (row.LessonPlanDescription ?? string.Empty).Trim().Length);

            var ready = criteriaWithAnyContent > 0 || toolkitRowsWithContent.Count > 0;
            var coveragePercent = criteriaTotal > 0
                ? Math.Round((double)criteriaWithAnyContent / criteriaTotal * 100, 1)
                : 0.0;

            var unmappedToolkitRowsWithContent = toolkitRowsWithContent
                .Where(row => !mappedToolkitRowIds.Contains(row.Id))
                .OrderBy(row => ParseLpnSort(row.Lpn))
                .ThenBy(row => row.Id)
                .Select(row => new
                {
                    rowId = row.Id,
                    lpn = NormalizeLpn(row.Lpn),
                    subjectCode = (row.SubjectCode ?? string.Empty).Trim(),
                    criteriaId = row.AssessmentCriteriaId,
                    criteriaDescription = (row.AssessmentCriteriaDescription ?? string.Empty).Trim(),
                    lessonPlanDescription = (row.LessonPlanDescription ?? string.Empty).Trim(),
                    lessonContentChars = (row.LessonPlanContent ?? string.Empty).Trim().Length,
                    reason = DescribeUnmappedToolkitRow(row, criteriaById)
                })
                .ToList();

            var subjectMismatchRowsWithContent = toolkitRowsWithContentOtherSubjects
                .OrderBy(row => ParseLpnSort(row.Lpn))
                .ThenBy(row => row.Id)
                .Select(row => new
                {
                    rowId = row.Id,
                    lpn = NormalizeLpn(row.Lpn),
                    rowSubjectCode = (row.SubjectCode ?? string.Empty).Trim(),
                    selectedSubjectCode = (subject.SubjectCode ?? string.Empty).Trim(),
                    criteriaId = row.AssessmentCriteriaId,
                    criteriaDescription = (row.AssessmentCriteriaDescription ?? string.Empty).Trim(),
                    lessonContentChars = (row.LessonPlanContent ?? string.Empty).Trim().Length
                })
                .ToList();

            return Ok(new
            {
                qualificationId = qualification.Id,
                qualificationNumber = (qualification.QualificationNumber ?? string.Empty).Trim(),
                subjectId = subject.Id,
                subjectCode = (subject.SubjectCode ?? string.Empty).Trim(),
                subjectDescription = (subject.SubjectDescription ?? string.Empty).Trim(),
                topics = topics.Count,
                criteria = criteriaTotal,
                toolkitRowsForSubject = toolkitForSubject.Count,
                toolkitRowsWithLessonContent = toolkitRowsWithContent.Count,
                criteriaMatched,
                criteriaWithLessonContent,
                criteriaWithAnyContent,
                missingCriteriaCount = Math.Max(0, criteriaTotal - criteriaWithAnyContent),
                criteriaCoveragePercent = coveragePercent,
                totalLessonContentChars,
                mappedToolkitRowsWithLessonContent = toolkitRowsWithContent.Count - unmappedToolkitRowsWithContent.Count,
                unmappedToolkitRowsWithLessonContent = unmappedToolkitRowsWithContent.Count,
                toolkitRowsWithLessonContentOtherSubjects = subjectMismatchRowsWithContent.Count,
                ready,
                message = ready
                    ? "Learner guide source rows are available for export."
                    : "No mapped learner guide source rows were found yet for the selected subject.",
                missingCriteria,
                detailsIncluded = details,
                criteriaDiagnostics = details
                    ? criteriaDiagnostics.Take(120).Select(x => new
                    {
                        criteriaId = x.CriteriaId,
                        criteriaDescription = x.CriteriaDescription,
                        matchedRows = x.MatchedRows,
                        matchedRowsWithContent = x.MatchedRowsWithContent,
                        fallbackPlans = x.FallbackPlans,
                        fallbackPlansWithContent = x.FallbackPlansWithContent,
                        hasAnyContent = x.HasAnyContent
                    }).Cast<object>().ToList()
                    : new List<object>(),
                unmappedToolkitRows = details
                    ? unmappedToolkitRowsWithContent.Take(80).Cast<object>().ToList()
                    : new List<object>(),
                subjectMismatchToolkitRows = details
                    ? subjectMismatchRowsWithContent.Take(80).Cast<object>().ToList()
                    : new List<object>()
            });
        }

        [HttpGet("download")]
        public async Task<IActionResult> Download(
            [FromQuery] int? qualificationId = null,
            [FromQuery] int? subjectId = null,
            [FromQuery] bool paraphrase = false,
            [FromQuery] bool useWorkflowCache = true,
            [FromQuery] bool includeIllustrations = true,
            [FromQuery] bool generateIllustrations = false,
            [FromQuery] int maxIllustrationsPerTopic = 2)
        {
            var qualification = qualificationId.HasValue && qualificationId.Value > 0
                ? _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId.Value)
                : _context.Qualifications.FirstOrDefault();
            if (qualification == null) return BadRequest("No qualification available for Learner Guide export.");

            var qualificationSubjects = _context.Subjects
                .Where(s => s.QualificationId == qualification.Id)
                .OrderBy(s => s.CurriculumPhaseId)
                .ThenBy(s => s.SubjectCode)
                .ToList();
            qualificationSubjects = qualificationSubjects.Where(HasSubjectIdentity).ToList();
            if (qualificationSubjects.Count == 0) return BadRequest("No subjects found for this qualification.");

            if (subjectId.HasValue && subjectId.Value > 0)
            {
                qualificationSubjects = qualificationSubjects.Where(s => s.Id == subjectId.Value).ToList();
            }
            if (qualificationSubjects.Count == 0) return BadRequest("Selected subjectId was not found for this qualification.");

            var buildResult = await BuildLearnerGuideDocumentAsync(
                qualification,
                qualificationSubjects,
                paraphrase,
                useWorkflowCache,
                includeIllustrations,
                generateIllustrations,
                maxIllustrationsPerTopic,
                HttpContext.RequestAborted);
            if (!buildResult.Success)
            {
                return BadRequest(buildResult.ErrorMessage);
            }

            return File(
                buildResult.FileBytes,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                buildResult.FileName);
        }

        [HttpGet("download-range")]
        public async Task<IActionResult> DownloadRange(
            [FromQuery] int qualificationId,
            [FromQuery] int? subjectFromId = null,
            [FromQuery] int? subjectToId = null,
            [FromQuery] bool paraphrase = false,
            [FromQuery] bool useWorkflowCache = true,
            [FromQuery] bool includeIllustrations = true,
            [FromQuery] bool generateIllustrations = false,
            [FromQuery] int maxIllustrationsPerTopic = 2)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required for range export.");

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId);
            if (qualification == null) return BadRequest("No qualification available for Learner Guide range export.");

            var subjects = _context.Subjects
                .Where(s => s.QualificationId == qualification.Id)
                .OrderBy(s => s.CurriculumPhaseId)
                .ThenBy(s => s.SubjectCode)
                .ToList();
            subjects = subjects.Where(HasSubjectIdentity).ToList();
            if (subjects.Count == 0) return BadRequest("No subjects found for this qualification.");

            var fromIndex = 0;
            var toIndex = subjects.Count - 1;

            if (subjectFromId.HasValue && subjectFromId.Value > 0)
            {
                fromIndex = subjects.FindIndex(s => s.Id == subjectFromId.Value);
                if (fromIndex < 0) return BadRequest("subjectFromId was not found for this qualification.");
            }
            if (subjectToId.HasValue && subjectToId.Value > 0)
            {
                toIndex = subjects.FindIndex(s => s.Id == subjectToId.Value);
                if (toIndex < 0) return BadRequest("subjectToId was not found for this qualification.");
            }

            if (fromIndex > toIndex)
            {
                (fromIndex, toIndex) = (toIndex, fromIndex);
            }

            var scopedSubjects = subjects
                .Skip(fromIndex)
                .Take((toIndex - fromIndex) + 1)
                .ToList();
            if (scopedSubjects.Count == 0) return BadRequest("No subjects were resolved for the selected range.");

            var buildResult = await BuildLearnerGuideDocumentAsync(
                qualification,
                scopedSubjects,
                paraphrase,
                useWorkflowCache,
                includeIllustrations,
                generateIllustrations,
                maxIllustrationsPerTopic,
                HttpContext.RequestAborted);
            if (!buildResult.Success)
            {
                return BadRequest(buildResult.ErrorMessage);
            }

            return File(
                buildResult.FileBytes,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                buildResult.FileName);
        }

        private sealed class LearnerGuideBuildResult
        {
            public bool Success { get; init; }
            public string ErrorMessage { get; init; } = string.Empty;
            public byte[] FileBytes { get; init; } = Array.Empty<byte>();
            public string FileName { get; init; } = string.Empty;

            public static LearnerGuideBuildResult Fail(string message)
                => new() { Success = false, ErrorMessage = message };

            public static LearnerGuideBuildResult Ok(byte[] bytes, string fileName)
                => new() { Success = true, FileBytes = bytes, FileName = fileName };
        }

        private sealed class SubjectGuideChapter
        {
            public CurriculumPhase? Phase { get; init; }
            public Subject Subject { get; init; } = null!;
            public List<TopicGuideSection> Topics { get; init; } = new();
            public Dictionary<int, List<GuideIllustration>> IllustrationsByTopic { get; init; } = new();
            public List<string> SubjectAssessmentCriteria { get; init; } = new();
            public List<string> WorkbookActivities { get; init; } = new();
        }

        private async Task<LearnerGuideBuildResult> BuildLearnerGuideDocumentAsync(
            Qualification qualification,
            IReadOnlyList<Subject> subjects,
            bool paraphrase,
            bool useWorkflowCache,
            bool includeIllustrations,
            bool generateIllustrations,
            int maxIllustrationsPerTopic,
            CancellationToken cancellationToken)
        {
            if (subjects == null || subjects.Count == 0)
            {
                return LearnerGuideBuildResult.Fail("No subjects were resolved for Learner Guide export.");
            }

            var cache = new Dictionary<string, string>(StringComparer.Ordinal);
            if (paraphrase && useWorkflowCache)
            {
                foreach (var kv in LoadParaphraseMap(qualification.Id))
                {
                    if (!cache.ContainsKey(kv.Key))
                    {
                        cache[kv.Key] = kv.Value;
                    }
                }
            }

            var chapters = new List<SubjectGuideChapter>();
            foreach (var subject in subjects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chapter = await BuildSubjectGuideChapterAsync(
                    qualification,
                    subject,
                    paraphrase,
                    cache,
                    includeIllustrations,
                    generateIllustrations,
                    maxIllustrationsPerTopic,
                    cancellationToken);
                if (chapter != null)
                {
                    chapters.Add(chapter);
                }
            }

            if (chapters.Count == 0)
            {
                return LearnerGuideBuildResult.Fail("No topic data found for the selected subject scope.");
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

                var portraitHeaderRelId = EnsureLearnerGuideHeader(main, qualification);

                AppendCoverPage(body, main, qualification, chapters.Count == 1 ? chapters[0].Subject : null);
                body.Append(PageBreak());

                AppendDisclaimerPage(body, qualification);
                body.Append(PageBreak());

                DocumentRevisionQualityControlPage.Append(body, qualification, new DocumentRevisionQualityControlPageOptions
                {
                    DocumentTitle = "Learner Guide",
                    DocumentType = "Learning Material",
                    Phase = "Knowledge Learning"
                });
                body.Append(PageBreak());

                AppendTableOfContentsPage(body);
                body.Append(PageBreak());

                for (var i = 0; i < chapters.Count; i++)
                {
                    var chapter = chapters[i];
                    AppendSubjectChapter(
                        body,
                        main,
                        qualification,
                        chapter.Phase,
                        chapter.Subject,
                        chapter.Topics,
                        chapter.IllustrationsByTopic,
                        chapter.SubjectAssessmentCriteria,
                        chapter.WorkbookActivities,
                        chapterNumber: i + 1);

                    if (i < chapters.Count - 1)
                    {
                        body.Append(PageBreak());
                    }
                }

                body.Append(DefaultPortraitSectionProperties(portraitHeaderRelId));
                main.Document.Save();
            }

            ms.Position = 0;
            var safeQualification = SafeFileNameToken(qualification.QualificationNumber);
            var suffix = chapters.Count > 1 ? "Full" : SafeFileNameToken(chapters[0].Subject.SubjectCode);
            var fileName = $"LearnerGuide_{safeQualification}_{suffix}_{DateTime.Now:yyyyMMdd}.docx";
            return LearnerGuideBuildResult.Ok(ms.ToArray(), fileName);
        }

        private async Task<SubjectGuideChapter?> BuildSubjectGuideChapterAsync(
            Qualification qualification,
            Subject subject,
            bool paraphrase,
            Dictionary<string, string> cache,
            bool includeIllustrations,
            bool generateIllustrations,
            int maxIllustrationsPerTopic,
            CancellationToken cancellationToken)
        {
            var phase = _context.CurriculumPhases.FirstOrDefault(p => p.Id == subject.CurriculumPhaseId);

            var rawTopics = _context.Topics
                .Where(t => t.SubjectId == subject.Id)
                .OrderBy(t => t.Order ?? int.MaxValue)
                .ThenBy(t => t.TopicCode)
                .ThenBy(t => t.Id)
                .ToList();
            if (rawTopics.Count == 0) return null;
            var topicById = rawTopics.ToDictionary(t => t.Id);
            var topics = rawTopics
                .GroupBy(BuildTopicIdentity)
                .Select(g => g
                    .OrderBy(t => t.Order ?? int.MaxValue)
                    .ThenBy(t => t.TopicCode)
                    .ThenBy(t => t.Id)
                    .First())
                .ToList();

            var topicIds = rawTopics.Select(t => t.Id).ToList();
            var rawCriteria = _context.AssessmentCriteria
                .Where(c => topicIds.Contains(c.TopicId))
                .OrderBy(c => c.TopicId)
                .ThenBy(c => c.Id)
                .ToList();
            var criteriaGroups = rawCriteria
                .GroupBy(c => BuildCriterionIdentity(c, topicById))
                .Select(g => new GuideCriteriaGroup
                {
                    TopicKey = topicById.TryGetValue(g.First().TopicId, out var topic)
                        ? BuildTopicIdentity(topic)
                        : $"topic:{g.First().TopicId}",
                    Representative = g.First(),
                    Members = g.OrderBy(c => c.Id).ToList()
                })
                .OrderBy(g => g.Representative.TopicId)
                .ThenBy(g => g.Representative.Id)
                .ToList();
            var criteria = criteriaGroups.Select(g => g.Representative).ToList();
            var criteriaIds = rawCriteria.Select(c => c.Id).ToList();

            var lessonPlans = _context.LessonPlans
                .Where(lp => criteriaIds.Contains(lp.AssessmentCriteriaId))
                .OrderBy(lp => lp.SortOrder)
                .ThenBy(lp => lp.Id)
                .ToList();
            var lessonByCriteria = lessonPlans
                .GroupBy(lp => lp.AssessmentCriteriaId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var toolkit = _context.LecturerToolkitEntries
                .Where(e => e.QualificationsId <= 0 || e.QualificationsId == qualification.Id)
                .ToList()
                .OrderBy(e => ParseLpnSort(e.Lpn))
                .ThenBy(e => e.Id)
                .ToList();

            var criteriaByTopic = criteriaGroups
                .GroupBy(g => g.TopicKey)
                .ToDictionary(g => g.Key, g => g.ToList());
            var topicSections = new List<TopicGuideSection>();
            var consumedToolkitRowIds = new HashSet<int>();
            foreach (var topic in topics)
            {
                var topicKey = BuildTopicIdentity(topic);
                var topicCriteria = criteriaByTopic.TryGetValue(topicKey, out var list)
                    ? list
                    : new List<GuideCriteriaGroup>();

                var criteriaSections = new List<CriteriaGuideSection>();
                foreach (var criterionGroup in topicCriteria)
                {
                    var criterion = criterionGroup.Representative;
                    var lessonBlocks = new List<GuideLessonBlock>();

                    var toolkitRows = ResolveToolkitRowsForCriteria(toolkit, criterionGroup.Members, subject);
                    if (toolkitRows.Count > 0)
                    {
                        var availableRows = toolkitRows
                            .Where(row => consumedToolkitRowIds.Add(row.Id))
                            .OrderBy(x => ParseLpnSort(x.Lpn))
                            .ThenBy(x => x.Id)
                            .ToList();

                        foreach (var row in availableRows)
                        {
                            var lessonDescription = (row.LessonPlanDescription ?? string.Empty).Trim();
                            var fallbackPlans = ResolveLessonPlansForCriteria(lessonByCriteria, criterionGroup.Members);
                            if (string.IsNullOrWhiteSpace(lessonDescription))
                            {
                                lessonDescription = fallbackPlans
                                    .Select(x => (x.Title ?? string.Empty).Trim())
                                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                                    ?? string.Empty;
                            }

                            var lessonContent = (row.LessonPlanContent ?? string.Empty).Trim();
                            if (string.IsNullOrWhiteSpace(lessonContent))
                            {
                                lessonContent = string.Join("\n\n", fallbackPlans
                                    .Select(x => (x.Content ?? string.Empty).Trim())
                                    .Where(x => !string.IsNullOrWhiteSpace(x)));
                            }

                            var lecturerActions = (row.LecturerActions ?? string.Empty).Trim();
                            var learnerActions = (row.LearnerActions ?? string.Empty).Trim();
                            if (paraphrase)
                            {
                                lessonDescription = await ParaphraseForGuideAsync(lessonDescription, true, cache);
                                lecturerActions = await ParaphraseForGuideAsync(lecturerActions, true, cache);
                                learnerActions = await ParaphraseForGuideAsync(learnerActions, true, cache);
                            }

                            lessonBlocks.Add(new GuideLessonBlock
                            {
                                Lpn = NormalizeLpn(row.Lpn),
                                LessonPlanDescription = lessonDescription,
                                LessonContent = lessonContent,
                                LecturerActions = lecturerActions,
                                LearnerActions = learnerActions,
                                LearningAids = (row.LearningAids ?? string.Empty).Trim(),
                                TimeStart = (row.TimeStart ?? string.Empty).Trim(),
                                TimeEnd = (row.TimeEnd ?? string.Empty).Trim()
                            });
                        }
                    }

                    if (lessonBlocks.Count == 0)
                    {
                        var fallbackPlans = ResolveLessonPlansForCriteria(lessonByCriteria, criterionGroup.Members);
                        foreach (var plan in fallbackPlans.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
                        {
                            var lessonDescription = (plan.Title ?? string.Empty).Trim();
                            if (paraphrase)
                            {
                                lessonDescription = await ParaphraseForGuideAsync(lessonDescription, true, cache);
                            }

                            lessonBlocks.Add(new GuideLessonBlock
                            {
                                Lpn = plan.SortOrder > 0 ? $"LPN {plan.SortOrder}" : "LPN 1",
                                LessonPlanDescription = lessonDescription,
                                LessonContent = plan.Content ?? string.Empty,
                                LecturerActions = string.Empty,
                                LearnerActions = string.Empty,
                                LearningAids = string.Empty,
                                TimeStart = string.Empty,
                                TimeEnd = string.Empty
                            });
                        }
                    }

                    lessonBlocks = DeduplicateLessonBlocks(lessonBlocks);

                    criteriaSections.Add(new CriteriaGuideSection
                    {
                        CriteriaId = criterion.Id,
                        CriteriaDescription = (criterion.Description ?? string.Empty).Trim(),
                        Lessons = lessonBlocks.OrderBy(x => ParseLpnSort(x.Lpn)).ToList()
                    });
                }

                topicSections.Add(new TopicGuideSection
                {
                    TopicId = topic.Id,
                    TopicCode = (topic.TopicCode ?? string.Empty).Trim(),
                    TopicDescription = (topic.TopicDescription ?? string.Empty).Trim(),
                    TopicPurpose = (topic.TopicPurpose ?? string.Empty).Trim(),
                    Criteria = criteriaSections
                });
            }

            if (topicSections.Count == 0) return null;

            var subjectAssessmentCriteria = criteria
                .Select(c => (c.Description ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var workbookActivities = ResolveWorkbookActivitiesForSubject(qualification.Id, subject);

            var illustrationsByTopic = includeIllustrations
                ? await ResolveGuideIllustrationsByTopicAsync(
                    qualification,
                    subject,
                    topics,
                    Math.Clamp(maxIllustrationsPerTopic, 1, 4),
                    generateIllustrations,
                    cancellationToken)
                : new Dictionary<int, List<GuideIllustration>>();

            return new SubjectGuideChapter
            {
                Phase = phase,
                Subject = subject,
                Topics = topicSections,
                IllustrationsByTopic = illustrationsByTopic,
                SubjectAssessmentCriteria = subjectAssessmentCriteria,
                WorkbookActivities = workbookActivities
            };
        }

        private List<string> ResolveWorkbookActivitiesForSubject(int qualificationId, Subject subject)
        {
            var topicIds = _context.Topics
                .Where(t => t.SubjectId == subject.Id)
                .Select(t => t.Id)
                .ToList();
            if (topicIds.Count == 0) return new List<string>();

            var subtopicIds = _context.Subtopics
                .Where(st => topicIds.Contains(st.TopicId))
                .Select(st => st.Id)
                .ToList();

            var activities = subtopicIds.Count == 0
                ? new List<string>()
                : _context.Activities
                    .Where(a => subtopicIds.Contains(a.SubtopicId))
                    .OrderBy(a => a.Order ?? int.MaxValue)
                    .ThenBy(a => a.Id)
                    .Select(a => new
                    {
                        Name = (a.Name ?? string.Empty).Trim(),
                        Description = (a.Description ?? string.Empty).Trim()
                    })
                    .ToList()
                    .Select(a =>
                    {
                        if (string.IsNullOrWhiteSpace(a.Name)) return a.Description;
                        if (string.IsNullOrWhiteSpace(a.Description)) return a.Name;
                        return $"{a.Name}: {a.Description}";
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (activities.Count > 0) return activities;

            var subjectCode = (subject.SubjectCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(subjectCode)) return new List<string>();

            var toolkitActions = _context.LecturerToolkitEntries
                .Where(e => e.QualificationsId == qualificationId && e.SubjectCode == subjectCode)
                .ToList()
                .OrderBy(e => ParseLpnSort(e.Lpn))
                .ThenBy(e => e.Id)
                .Select(e => (e.LearnerActions ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var normalized = new List<string>();
            foreach (var block in toolkitActions)
            {
                var lines = block
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x));
                normalized.AddRange(lines);
            }

            return normalized
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        [HttpGet("download-audio")]
        public async Task<IActionResult> DownloadAudio(
            [FromQuery] int? qualificationId = null,
            [FromQuery] int? subjectId = null,
            [FromQuery] bool paraphrase = false,
            [FromQuery] string? model = null,
            [FromQuery] string? voice = null,
            [FromQuery] string? format = "mp3",
            [FromQuery] double? speed = null,
            [FromQuery] int maxChunks = 24)
        {
            if (!AiRuntime.AllowOpenAi())
            {
                return StatusCode(403, "OpenAI TTS is disabled by AI_MODE.");
            }

            var key = (Secrets.GetOpenAIKey() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return StatusCode(503, "OPENAI_API_KEY is not configured.");
            }

            var qualification = qualificationId.HasValue && qualificationId.Value > 0
                ? _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId.Value)
                : _context.Qualifications.FirstOrDefault();
            if (qualification == null) return BadRequest("No qualification available for Learner Guide audio export.");

            var qualificationSubjects = _context.Subjects
                .Where(s => s.QualificationId == qualification.Id)
                .OrderBy(s => s.CurriculumPhaseId)
                .ThenBy(s => s.SubjectCode)
                .ToList();
            qualificationSubjects = qualificationSubjects.Where(HasSubjectIdentity).ToList();
            if (qualificationSubjects.Count == 0) return BadRequest("No subjects found for this qualification.");

            Subject? subject;
            if (subjectId.HasValue && subjectId.Value > 0)
            {
                subject = qualificationSubjects.FirstOrDefault(s => s.Id == subjectId.Value);
                if (subject == null) return BadRequest("Selected subjectId was not found for this qualification.");
            }
            else
            {
                subject = qualificationSubjects.FirstOrDefault();
            }
            if (subject == null) return BadRequest("No subject available for Learner Guide audio export.");

            var phase = _context.CurriculumPhases.FirstOrDefault(p => p.Id == subject.CurriculumPhaseId);
            var topics = _context.Topics
                .Where(t => t.SubjectId == subject.Id)
                .OrderBy(t => t.Order ?? int.MaxValue)
                .ThenBy(t => t.TopicCode)
                .ThenBy(t => t.Id)
                .ToList();
            if (topics.Count == 0) return BadRequest("No topics found for the selected subject.");

            var topicIds = topics.Select(t => t.Id).ToList();
            var criteria = _context.AssessmentCriteria
                .Where(c => topicIds.Contains(c.TopicId))
                .OrderBy(c => c.TopicId)
                .ThenBy(c => c.Id)
                .ToList();
            var criteriaIds = criteria.Select(c => c.Id).ToList();

            var lessonPlans = _context.LessonPlans
                .Where(lp => criteriaIds.Contains(lp.AssessmentCriteriaId))
                .OrderBy(lp => lp.SortOrder)
                .ThenBy(lp => lp.Id)
                .ToList();
            var lessonByCriteria = lessonPlans
                .GroupBy(lp => lp.AssessmentCriteriaId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var toolkit = _context.LecturerToolkitEntries
                .Where(e => e.QualificationsId <= 0 || e.QualificationsId == qualification.Id)
                .ToList()
                .OrderBy(e => ParseLpnSort(e.Lpn))
                .ThenBy(e => e.Id)
                .ToList();

            var cache = new Dictionary<string, string>(StringComparer.Ordinal);
            var criteriaByTopic = criteria.GroupBy(c => c.TopicId).ToDictionary(g => g.Key, g => g.ToList());
            var consumedToolkitRowIds = new HashSet<int>();
            var topicSections = new List<TopicGuideSection>();
            foreach (var topic in topics)
            {
                var topicCriteria = criteriaByTopic.TryGetValue(topic.Id, out var list)
                    ? list
                    : new List<AssessmentCriteria>();

                var criteriaSections = new List<CriteriaGuideSection>();
                foreach (var criterion in topicCriteria)
                {
                    var lessonBlocks = new List<GuideLessonBlock>();
                    var toolkitRows = ResolveToolkitRowsForCriterion(toolkit, criterion, subject);
                    if (toolkitRows.Count > 0)
                    {
                        var availableRows = toolkitRows
                            .Where(row => consumedToolkitRowIds.Add(row.Id))
                            .OrderBy(x => ParseLpnSort(x.Lpn))
                            .ThenBy(x => x.Id)
                            .ToList();

                        foreach (var row in availableRows)
                        {
                            var lessonDescription = (row.LessonPlanDescription ?? string.Empty).Trim();
                            if (string.IsNullOrWhiteSpace(lessonDescription) &&
                                lessonByCriteria.TryGetValue(criterion.Id, out var titleFallbackRows))
                            {
                                lessonDescription = titleFallbackRows
                                    .Select(x => (x.Title ?? string.Empty).Trim())
                                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                                    ?? string.Empty;
                            }

                            var lessonContent = (row.LessonPlanContent ?? string.Empty).Trim();
                            if (string.IsNullOrWhiteSpace(lessonContent) &&
                                lessonByCriteria.TryGetValue(criterion.Id, out var contentFallbackRows))
                            {
                                lessonContent = string.Join("\n\n", contentFallbackRows
                                    .Select(x => (x.Content ?? string.Empty).Trim())
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .Take(3));
                            }

                            if (paraphrase)
                            {
                                lessonDescription = await ParaphraseForGuideAsync(lessonDescription, true, cache);
                                lessonContent = await ParaphraseForGuideAsync(lessonContent, true, cache);
                            }

                            lessonBlocks.Add(new GuideLessonBlock
                            {
                                Lpn = NormalizeLpn(row.Lpn),
                                LessonPlanDescription = lessonDescription,
                                LessonContent = lessonContent,
                                LecturerActions = string.Empty,
                                LearnerActions = string.Empty,
                                LearningAids = (row.LearningAids ?? string.Empty).Trim(),
                                TimeStart = string.Empty,
                                TimeEnd = string.Empty
                            });
                        }
                    }

                    if (lessonBlocks.Count == 0 &&
                        lessonByCriteria.TryGetValue(criterion.Id, out var fallbackPlans))
                    {
                        foreach (var plan in fallbackPlans.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
                        {
                            var lessonDescription = (plan.Title ?? string.Empty).Trim();
                            var lessonContent = (plan.Content ?? string.Empty).Trim();
                            if (paraphrase)
                            {
                                lessonDescription = await ParaphraseForGuideAsync(lessonDescription, true, cache);
                                lessonContent = await ParaphraseForGuideAsync(lessonContent, true, cache);
                            }

                            lessonBlocks.Add(new GuideLessonBlock
                            {
                                Lpn = plan.SortOrder > 0 ? $"LPN {plan.SortOrder}" : "LPN 1",
                                LessonPlanDescription = lessonDescription,
                                LessonContent = lessonContent,
                                LecturerActions = string.Empty,
                                LearnerActions = string.Empty,
                                LearningAids = string.Empty,
                                TimeStart = string.Empty,
                                TimeEnd = string.Empty
                            });
                        }
                    }

                    lessonBlocks = DeduplicateLessonBlocks(lessonBlocks);

                    criteriaSections.Add(new CriteriaGuideSection
                    {
                        CriteriaId = criterion.Id,
                        CriteriaDescription = (criterion.Description ?? string.Empty).Trim(),
                        Lessons = lessonBlocks.OrderBy(x => ParseLpnSort(x.Lpn)).ToList()
                    });
                }

                topicSections.Add(new TopicGuideSection
                {
                    TopicId = topic.Id,
                    TopicCode = (topic.TopicCode ?? string.Empty).Trim(),
                    TopicDescription = (topic.TopicDescription ?? string.Empty).Trim(),
                    TopicPurpose = (topic.TopicPurpose ?? string.Empty).Trim(),
                    Criteria = criteriaSections
                });
            }

            if (topicSections.Count == 0)
            {
                return BadRequest("No learner-guide topic sections found for audio export.");
            }

            var script = BuildLearnerGuideNarrationScript(qualification, phase, subject, topicSections);
            if (string.IsNullOrWhiteSpace(script))
            {
                return BadRequest("No learner-guide text is available to synthesize.");
            }

            var normalizedFormat = NormalizeTtsFormat(format);
            var normalizedModel = string.IsNullOrWhiteSpace(model)
                ? (Environment.GetEnvironmentVariable("OPENAI_TTS_MODEL") ?? "gpt-4o-mini-tts").Trim()
                : model.Trim();
            var normalizedVoice = string.IsNullOrWhiteSpace(voice)
                ? (Environment.GetEnvironmentVariable("OPENAI_TTS_VOICE") ?? "alloy").Trim()
                : voice.Trim();
            var normalizedSpeed = Math.Clamp(speed.GetValueOrDefault(1.0), 0.25, 2.0);
            var chunks = SplitNarrationIntoChunks(script, 3600, Math.Clamp(maxChunks, 1, 60));
            if (chunks.Count == 0) return BadRequest("Narration chunking produced no output.");

            using var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
            {
                var manifest = zip.CreateEntry("manifest.txt", CompressionLevel.Fastest);
                await using (var manifestWriter = new StreamWriter(manifest.Open(), Encoding.UTF8))
                {
                    await manifestWriter.WriteLineAsync("Learner Guide Audio Export");
                    await manifestWriter.WriteLineAsync($"Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
                    await manifestWriter.WriteLineAsync($"Qualification: {(qualification.QualificationNumber ?? "").Trim()} - {(qualification.QualificationDescription ?? "").Trim()}");
                    await manifestWriter.WriteLineAsync($"Subject: {(subject.SubjectCode ?? "").Trim()} - {(subject.SubjectDescription ?? "").Trim()}");
                    await manifestWriter.WriteLineAsync($"Model: {normalizedModel}");
                    await manifestWriter.WriteLineAsync($"Voice: {normalizedVoice}");
                    await manifestWriter.WriteLineAsync($"Format: {normalizedFormat}");
                    await manifestWriter.WriteLineAsync($"Speed: {normalizedSpeed:0.##}");
                    await manifestWriter.WriteLineAsync($"Chunks: {chunks.Count}");
                }

                var scriptEntry = zip.CreateEntry("script.txt", CompressionLevel.Fastest);
                await using (var scriptWriter = new StreamWriter(scriptEntry.Open(), Encoding.UTF8))
                {
                    await scriptWriter.WriteAsync(script);
                }

                var failures = new List<string>();
                var successCount = 0;
                for (var i = 0; i < chunks.Count; i++)
                {
                    var text = chunks[i];
                    var audio = await TrySynthesizeOpenAiTtsChunkAsync(
                        key,
                        normalizedModel,
                        normalizedVoice,
                        normalizedFormat,
                        normalizedSpeed,
                        text,
                        HttpContext.RequestAborted);
                    if (audio.Bytes == null || audio.Bytes.Length == 0)
                    {
                        failures.Add($"Part {i + 1:000}: {audio.Error}");
                        continue;
                    }

                    successCount += 1;
                    var entry = zip.CreateEntry($"part_{i + 1:000}.{normalizedFormat}", CompressionLevel.NoCompression);
                    await using var stream = entry.Open();
                    await stream.WriteAsync(audio.Bytes, 0, audio.Bytes.Length, HttpContext.RequestAborted);
                }

                if (failures.Count > 0)
                {
                    var failEntry = zip.CreateEntry("_errors.txt", CompressionLevel.Fastest);
                    await using var failWriter = new StreamWriter(failEntry.Open(), Encoding.UTF8);
                    await failWriter.WriteLineAsync("Some TTS chunks failed:");
                    foreach (var failure in failures)
                    {
                        await failWriter.WriteLineAsync(failure);
                    }
                }

                if (successCount == 0)
                {
                    return StatusCode(502, failures.Count > 0 ? string.Join(" | ", failures.Take(2)) : "TTS synthesis returned no audio data.");
                }
            }

            zipStream.Position = 0;
            var safeQualification = SafeFileNameToken(qualification.QualificationNumber);
            var safeSubject = SafeFileNameToken(subject.SubjectCode);
            var fileName = $"LearnerGuide_Audio_{safeQualification}_{safeSubject}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            return File(zipStream.ToArray(), "application/zip", fileName);
        }

        private async Task<string> ParaphraseForGuideAsync(string text, bool enabled, Dictionary<string, string> cache)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (!enabled || string.IsNullOrWhiteSpace(trimmed) || trimmed.Length < 32) return trimmed;
            if (cache.TryGetValue(trimmed, out var cached)) return cached;
            var (paraphrased, _) = await ParaphraseTextWithBackendAsync(trimmed, "educational", true);
            var finalText = string.IsNullOrWhiteSpace(paraphrased) ? trimmed : paraphrased.Trim();
            cache[trimmed] = finalText;
            return finalText;
        }

        private async Task<(string text, string backend)> ParaphraseTextWithBackendAsync(string text, string style, bool preserveTerminology)
        {
            var preferLocalFirst = AiRuntime.PreferLocalFirst();
            var cloudEnabled = AiRuntime.AllowCloudProviders();
            var allowOpenAi = AiRuntime.AllowOpenAi();
            var wrapperPriority = (Environment.GetEnvironmentVariable("PARAPHRASE_WRAPPER_PRIORITY") ?? "fallback").Trim();
            var wrapperFirst = preferLocalFirst || wrapperPriority.Equals("first", StringComparison.OrdinalIgnoreCase);
            if (wrapperFirst)
            {
                var wrapperFirstText = await TryParaphraseWithLocalWrapperAsync(text, style, preserveTerminology);
                if (!string.IsNullOrWhiteSpace(wrapperFirstText)) return (wrapperFirstText, "local_wrapper");
            }

            if (cloudEnabled && allowOpenAi)
            {
                var openAi = await TryParaphraseWithOpenAiAsync(text, style, preserveTerminology);
                if (!string.IsNullOrWhiteSpace(openAi)) return (openAi, "openai");
            }

            if (!wrapperFirst)
            {
                var wrapperFallbackText = await TryParaphraseWithLocalWrapperAsync(text, style, preserveTerminology);
                if (!string.IsNullOrWhiteSpace(wrapperFallbackText)) return (wrapperFallbackText, "local_wrapper");
            }

            return (HeuristicParaphrase(text), "heuristic");
        }

        private async Task<string?> TryParaphraseWithFoundryAsync(string text, string style, bool preserveTerminology)
        {
            if (!AiRuntime.AllowFoundry()) return null;
            var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_RESPONSES_ENDPOINT") ?? DefaultModeratorResponsesEndpoint;
            var foundryApiKey = Environment.GetEnvironmentVariable("FOUNDRY_API_KEY") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(foundryApiKey)) return null;

            var instructions = $"Paraphrase the educational text while preserving meaning and technical terms. Style: {style}. Preserve terminology: {(preserveTerminology ? "yes" : "no")}. Return only paraphrased text.";
            var payload = new { input = text, instructions };
            using var msg = new HttpRequestMessage(HttpMethod.Post, endpoint);
            msg.Headers.Add("api-key", foundryApiKey);
            msg.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(msg);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return null;
            return TryExtractResponseOutputText(json);
        }

        private async Task<string?> TryParaphraseWithOpenAiAsync(string text, string style, bool preserveTerminology)
        {
            if (!AiRuntime.AllowOpenAi()) return null;
            var key = Secrets.GetOpenAIKey();
            if (string.IsNullOrWhiteSpace(key)) return null;
            var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
            var prompt = $"Paraphrase the text for a learner guide. Keep meaning and sequence. Style={style}. Preserve terminology={(preserveTerminology ? "yes" : "no")}. Return only paraphrased text.";
            var payload = new
            {
                model,
                messages = new[] { new { role = "system", content = prompt }, new { role = "user", content = text } },
                temperature = 0.35
            };
            using var msg = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            msg.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(msg);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return null;
            return TryExtractChatCompletionText(body);
        }

        private async Task<string?> TryParaphraseWithLocalWrapperAsync(string text, string style, bool preserveTerminology)
        {
            var endpoint = AiRuntime.GetLocalLlmEndpoint();
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                endpoint = Environment.GetEnvironmentVariable("PARAPHRASE_WRAPPER_ENDPOINT")
                    ?? Environment.GetEnvironmentVariable("LOCAL_PARAPHRASE_ENDPOINT")
                    ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(endpoint)) return null;

            var model = AiRuntime.GetLocalLlmModel();
            if (string.IsNullOrWhiteSpace(model))
            {
                model = Environment.GetEnvironmentVariable("PARAPHRASE_WRAPPER_MODEL");
                if (string.IsNullOrWhiteSpace(model)) model = "local-paraphrase";
            }

            var prompt = $"Paraphrase the text for a learner guide. Keep meaning and sequence. Style={style}. Preserve terminology={(preserveTerminology ? "yes" : "no")}. Return only paraphrased text.";
            var payload = new
            {
                model,
                messages = new[] { new { role = "system", content = prompt }, new { role = "user", content = text } },
                temperature = 0.2
            };

            using var msg = new HttpRequestMessage(HttpMethod.Post, endpoint.Trim());
            var wrapperApiKey = AiRuntime.GetLocalLlmApiKey();
            if (string.IsNullOrWhiteSpace(wrapperApiKey))
            {
                wrapperApiKey = Environment.GetEnvironmentVariable("PARAPHRASE_WRAPPER_API_KEY") ?? string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(wrapperApiKey))
            {
                var token = wrapperApiKey.Trim();
                if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    token = token.Substring(7).Trim();
                if (!string.IsNullOrWhiteSpace(token))
                    msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            msg.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(msg);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return null;
            return TryExtractChatCompletionText(body) ?? TryExtractResponseOutputText(body);
        }

        private static string? TryExtractChatCompletionText(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return null;
            var m = choices[0].TryGetProperty("message", out var msgObj) ? msgObj : default;
            if (m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("content", out var c)) return null;
            if (c.ValueKind == JsonValueKind.String) return c.GetString();
            if (c.ValueKind != JsonValueKind.Array) return null;
            foreach (var part in c.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object &&
                    part.TryGetProperty("text", out var txt) &&
                    txt.ValueKind == JsonValueKind.String)
                {
                    var t = txt.GetString();
                    if (!string.IsNullOrWhiteSpace(t)) return t;
                }
            }
            return null;
        }

        private static string? TryExtractResponseOutputText(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var txt))
                    {
                        var t = txt.GetString();
                        if (!string.IsNullOrWhiteSpace(t)) return t;
                    }
                }
            }
            return null;
        }

        private static string HeuristicParaphrase(string text)
        {
            var cleaned = Regex.Replace((text ?? string.Empty).Trim(), @"\s+", " ");
            var repl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "must", "is required to" }, { "should", "is expected to" }, { "demonstrate", "show" }, { "use", "apply" }, { "ensure", "make sure" }
            };
            foreach (var kv in repl)
            {
                cleaned = Regex.Replace(cleaned, $@"\b{Regex.Escape(kv.Key)}\b", kv.Value, RegexOptions.IgnoreCase);
            }
            return cleaned;
        }

        private static string GetParaphraseCachePath(int qualificationId)
        {
            var root = Path.Combine(Directory.GetCurrentDirectory(), "Exports", "paraphrase-cache");
            return Path.Combine(root, $"qualification-{qualificationId}.json");
        }

        private static Dictionary<string, string> LoadParaphraseMap(int qualificationId)
        {
            var file = LoadParaphraseCacheFile(qualificationId);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (file.Entries == null) return map;
            foreach (var kv in file.Entries)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null || string.IsNullOrWhiteSpace(kv.Value.ParaphrasedText)) continue;
                map[kv.Key] = kv.Value.ParaphrasedText.Trim();
            }
            return map;
        }

        private static ParaphraseCacheFile LoadParaphraseCacheFile(int qualificationId)
        {
            var path = GetParaphraseCachePath(qualificationId);
            if (!System.IO.File.Exists(path))
                return new ParaphraseCacheFile { QualificationId = qualificationId };

            try
            {
                var json = System.IO.File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<ParaphraseCacheFile>(json);
                if (loaded == null) return new ParaphraseCacheFile { QualificationId = qualificationId };
                loaded.QualificationId = loaded.QualificationId <= 0 ? qualificationId : loaded.QualificationId;
                loaded.Entries ??= new Dictionary<string, ParaphraseCacheEntry>(StringComparer.Ordinal);
                return loaded;
            }
            catch
            {
                return new ParaphraseCacheFile { QualificationId = qualificationId };
            }
        }

        private static void SaveParaphraseCacheFile(ParaphraseCacheFile file)
        {
            var path = GetParaphraseCachePath(file.QualificationId);
            var dir = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(path, json);
        }

        private static void AppendCoverPage(Body body, MainDocumentPart main, Qualification qualification, Subject? subject)
        {
            var coverPath = ResolveLearnerGuideCoverPath();
            var institutionLine = (qualification.LearningInstitutionName ?? string.Empty).Trim();
            var phaseLine = "Phase: Knowledge Learning";
            var nqfAndCreditsLine = BuildLearnerGuideCoverNqfCreditsLine(qualification);
            var qualificationLine = BuildCoverQualificationLine(qualification);
            var subjectLine = subject == null
                ? string.Empty
                : $"{(subject.SubjectCode ?? string.Empty).Trim()} {(subject.SubjectDescription ?? string.Empty).Trim()}".Trim();
            var coverLines = new List<DocxCoverPageOverlay.CoverTextLine>();
            if (!string.IsNullOrWhiteSpace(institutionLine))
            {
                coverLines.Add(new DocxCoverPageOverlay.CoverTextLine
                {
                    Text = institutionLine.ToUpperInvariant(),
                    FontSizeHalfPt = 52,
                    Bold = true,
                    BeforeTwips = 1200,
                    AfterTwips = 120
                });
            }
            coverLines.Add(new DocxCoverPageOverlay.CoverTextLine
            {
                Text = phaseLine,
                FontSizeHalfPt = 44,
                Bold = true,
                BeforeTwips = 420,
                AfterTwips = 120
            });
            if (!string.IsNullOrWhiteSpace(nqfAndCreditsLine))
            {
                coverLines.Add(new DocxCoverPageOverlay.CoverTextLine
                {
                    Text = nqfAndCreditsLine,
                    FontSizeHalfPt = 40,
                    Bold = true,
                    BeforeTwips = 360,
                    AfterTwips = 120
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
                    AfterTwips = 120
                });
            }
            if (!string.IsNullOrWhiteSpace(subjectLine))
            {
                coverLines.Add(new DocxCoverPageOverlay.CoverTextLine
                {
                    Text = subjectLine.ToUpperInvariant(),
                    FontSizeHalfPt = 28,
                    Bold = true,
                    BeforeTwips = 900,
                    AfterTwips = 0
                });
            }

            var appended = DocxCoverPageOverlay.TryAppendCenteredPortraitCoverPage(
                body,
                main,
                coverPath,
                coverLines,
                PortraitA4PageWidthTwips,
                1U);

            if (appended)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(institutionLine))
            {
                body.Append(CenterPara(institutionLine.ToUpperInvariant(), 36, bold: true));
            }
            body.Append(CenterPara(phaseLine, 32, bold: true));
            if (!string.IsNullOrWhiteSpace(nqfAndCreditsLine))
            {
                body.Append(CenterPara(nqfAndCreditsLine, 30, bold: true));
            }
            if (!string.IsNullOrWhiteSpace(qualificationLine))
            {
                body.Append(CenterPara(qualificationLine, 36, bold: true));
            }
            if (!string.IsNullOrWhiteSpace(subjectLine))
            {
                body.Append(CenterPara(subjectLine.ToUpperInvariant(), 24, bold: true));
            }
            if (string.IsNullOrWhiteSpace(institutionLine) &&
                string.IsNullOrWhiteSpace(nqfAndCreditsLine) &&
                string.IsNullOrWhiteSpace(qualificationLine) &&
                string.IsNullOrWhiteSpace(subjectLine))
            {
                body.Append(StyledHeading("Learner Guide", "Heading1", 30));
            }
        }

        private static void AppendTopicPage(
            Body body,
            Qualification qualification,
            CurriculumPhase? phase,
            Subject subject,
            IReadOnlyList<TopicGuideSection> topics)
        {
            body.Append(StyledHeading("Topic Page", "Heading1", 24));
            body.Append(BodyPara($"Qualification: {qualification.QualificationNumber} - {qualification.QualificationDescription}", 22, 0));
            body.Append(BodyPara($"Phase: {phase?.Name ?? "Not assigned"}", 22, 0));
            body.Append(BodyPara($"Subject: {subject.SubjectCode} - {subject.SubjectDescription}", 22, 0));

            var subjectMeta = new List<string>();
            if (subject.SubjectCredits.HasValue) subjectMeta.Add($"Credits: {subject.SubjectCredits.Value}");
            if (subject.SubjectPercentage.HasValue) subjectMeta.Add($"Percentage: {subject.SubjectPercentage.Value}%");
            if (subject.SubjectNQFLevel.HasValue) subjectMeta.Add($"NQF: {subject.SubjectNQFLevel.Value}");
            if (subjectMeta.Count > 0)
            {
                body.Append(BodyPara(string.Join(" | ", subjectMeta), 22, 0));
            }

            var purpose = (subject.SubjectPurpose ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(purpose))
            {
                body.Append(BodyPara($"Purpose: {purpose}", 22, 0));
            }

            body.Append(StyledHeading("Topics Included", "Heading2", 18));
            for (var i = 0; i < topics.Count; i++)
            {
                var t = topics[i];
                var criteriaCount = t.Criteria.Count;
                var lessonCount = t.Criteria.Sum(x => x.Lessons.Count);
                body.Append(BodyPara($"{i + 1}. {t.TopicCode} - {t.TopicDescription} (Criteria: {criteriaCount}, LPN sections: {lessonCount})", 22, 0));
            }
        }

        private static void AppendTableOfContentsPage(Body body)
        {
            body.Append(StyledHeading("TABLE OF CONTENT", "Heading1", 18));
            body.Append(BodyPara("If the table is blank, right-click inside it in Word and choose Update Field.", 20, 0));
            body.Append(BuildTableOfContentsField());
        }

        private static void AppendDisclaimerPage(Body body, Qualification qualification)
        {
            var year = DateTime.Now.Year;
            var institution = (qualification.LearningInstitutionName ?? string.Empty).Trim();

            body.Append(StyledHeading("DISCLAIMER", "Heading1", 18));
            body.Append(BodyPara("ETDP Courseware Release ETDP RSA PATENT 004/026785", 22, 0));
            body.Append(BodyPara($"(C) {year} by Dr P.C. Wepener, supported by the professional assistance of OpenAI (CODEX).", 22));
            body.Append(BodyPara("Neither Dr P.C. Wepener nor OpenAI is accountable or liable for the correctness, completeness, factual, or academic correctness of this document. This document is generated by the ETDP App. The accredited learning institution should be contacted for content inquiries, sources, references, or citations.", 22));

            body.Append(StyledHeading("NOTICE OF RIGHTS", "Heading2", 14));
            body.Append(BodyPara("No part of this publication may be reproduced, transmitted, transcribed, stored in a retrieval system, or translated into any language or computer language, in any form or by any means, electronic, mechanical, magnetic, optical, chemical, manual, or otherwise, without prior written permission from the branded learning institution that owns the legal and intellectual property rights to the content of this document.", 22));

            body.Append(StyledHeading("TRADEMARK NOTICE", "Heading2", 14));
            body.Append(BodyPara("Throughout this courseware title, trademark names may be used. Rather than placing a trademark symbol at every occurrence, names are used in an editorial manner for the benefit of the trademark owner, with no intention of infringement.", 22));

            body.Append(StyledHeading("NOTICE OF LIABILITY", "Heading2", 14));
            body.Append(BodyPara("The information in this courseware title is distributed on an 'as is' basis, without warranty. While every precaution has been taken in preparation of this courseware, neither Dr P.C. Wepener nor OpenAI shall have any liability to any person or entity for any loss or damage caused, or alleged to be caused, directly or indirectly by the instructions in this document or by the learning design and development processes described in it.", 22));

            body.Append(StyledHeading("DISCLAIMER", "Heading2", 14));
            body.Append(BodyPara("A sincere effort has been made to ensure typology accuracy of the material; however, no warranty, express or implied, is made regarding quality, correctness, reliability, accuracy, or freedom from error of this document or the products it describes. Data used in examples and sample files may be fictional. Any resemblance to real persons or companies is coincidental.", 22));

            body.Append(StyledHeading("TERMS AND CONDITIONS", "Heading2", 14));
            body.Append(BodyPara("This document is developed for the learning institution holding a legal permit and may not be resold by the learning institution. Sample versions may be shared but may not be resold to a third party. For licensed users, this document may only be used under the terms of the license agreement between the learning institution and Dr P.C. Wepener.", 22));

            if (!string.IsNullOrWhiteSpace(institution))
            {
                body.Append(BodyPara($"Learning Institution: {institution}", 22, 0, bold: true));
            }
            body.Append(BodyPara("PC WEPENER (Ph.D.) BUSINESS MANAGEMENT UJ 2005", 22, 0));
            body.Append(BodyPara($"Pretoria, South Africa, {year}.", 22, 0));
        }

        private static void AppendSubjectChapter(
            Body body,
            MainDocumentPart main,
            Qualification qualification,
            CurriculumPhase? phase,
            Subject subject,
            IReadOnlyList<TopicGuideSection> topics,
            IReadOnlyDictionary<int, List<GuideIllustration>> illustrationsByTopic,
            IReadOnlyList<string> subjectAssessmentCriteria,
            IReadOnlyList<string> workbookActivities,
            int chapterNumber)
        {
            _ = qualification;
            _ = phase;

            body.Append(BuildChapterHeading($"CHAPTER {chapterNumber}"));
            body.Append(BuildSubjectHeading($"{(subject.SubjectCode ?? string.Empty).Trim()}: {(subject.SubjectDescription ?? string.Empty).Trim()}"));
            var drawingId = 100U;

            if (!string.IsNullOrWhiteSpace(subject.SubjectPurpose))
            {
                body.Append(BuildPurposeHeading("Purpose of the Subject"));
                AppendMultilineBody(body, subject.SubjectPurpose, 22);
            }

            body.Append(BuildAssessmentCriteriaHeading("Subject Assessment Criteria"));
            body.Append(BodyPara(AssessmentCriteriaLeadLine, 22, 0));
            if (subjectAssessmentCriteria == null || subjectAssessmentCriteria.Count == 0)
            {
                body.Append(BodyPara("No assessment criteria captured for this chapter.", 22, 0));
            }
            else
            {
                foreach (var criterion in subjectAssessmentCriteria)
                {
                    body.Append(BodyPara(criterion, 22, 0));
                }
            }

            foreach (var topic in topics)
            {
                body.Append(BuildTopicHeading($"TOPIC {topic.TopicCode}: {topic.TopicDescription}"));

                if (illustrationsByTopic.TryGetValue(topic.TopicId, out var topicIllustrations) &&
                    topicIllustrations != null &&
                    topicIllustrations.Count > 0)
                {
                    AppendTopicIllustrations(body, main, topicIllustrations, ref drawingId);
                }

                if (topic.Criteria.Count == 0)
                {
                    body.Append(BodyPara("No assessment criteria captured for this topic.", 22, 0));
                }
                else
                {
                    foreach (var criterion in topic.Criteria)
                    {
                        if (criterion.Lessons.Count == 0)
                        {
                            body.Append(BodyPara("No lesson-plan text available for this criterion yet.", 22, 0));
                            continue;
                        }

                        foreach (var lesson in criterion.Lessons)
                        {
                            var lpnDescription = (lesson.LessonPlanDescription ?? string.Empty).Trim();
                            if (string.IsNullOrWhiteSpace(lpnDescription))
                            {
                                lpnDescription = (topic.TopicDescription ?? string.Empty).Trim();
                            }

                            var lpnHeading = BuildLessonPlanHeadingText(lesson.Lpn, lpnDescription);
                            body.Append(BuildLessonPlanHeading(lpnHeading));

                            if (!string.IsNullOrWhiteSpace(lesson.LessonContent))
                            {
                                AppendExactLessonPlanContent(body, lesson.LessonContent, 22);
                            }
                        }
                    }
                }
            }

            body.Append(BuildSubjectHeading($"Summary of Chapter {chapterNumber}"));
            body.Append(BodyPara(ChapterSummaryLeadLine, 22, 0));
            if (subjectAssessmentCriteria == null || subjectAssessmentCriteria.Count == 0)
            {
                body.Append(BodyPara("No assessment criteria captured for this chapter.", 22, 0));
            }
            else
            {
                foreach (var criterion in subjectAssessmentCriteria)
                {
                    body.Append(BodyPara(criterion, 22, 0));
                }
            }

            body.Append(BuildWorkbookActivitiesHeading("Workbook Activities to complete"));
            var activityCount = ResolveWorkbookActivityCount(topics, workbookActivities);
            if (activityCount <= 0)
            {
                body.Append(BodyPara("No workbook activities captured for this chapter.", 22, 0));
            }
            else
            {
                body.Append(BodyPara(BuildWorkbookActivitiesSummaryLine(activityCount), 22, 0));
            }
        }

        private static void AppendTopicIllustrations(
            Body body,
            MainDocumentPart main,
            IReadOnlyList<GuideIllustration> illustrations,
            ref uint drawingId)
        {
            if (illustrations == null || illustrations.Count == 0) return;

            body.Append(StyledHeading("TOPIC ILLUSTRATIONS", "Heading3", 14));
            var contentWidthTwips = PortraitA4PageWidthTwips - 2040U;
            var contentHeightTwips = 4200U;

            foreach (var item in illustrations
                .Where(i => i != null && !string.IsNullOrWhiteSpace(i.ImagePath) && System.IO.File.Exists(i.ImagePath))
                .Take(4))
            {
                try
                {
                    using var stream = System.IO.File.OpenRead(item.ImagePath);
                    var imagePart = main.AddImagePart(ResolveImagePartType(item.ImagePath));
                    imagePart.FeedData(stream);
                    var relId = main.GetIdOfPart(imagePart);
                    var drawing = BuildInlineImage(
                        relId,
                        TwipsToEmu(contentWidthTwips),
                        TwipsToEmu(contentHeightTwips),
                        drawingId++,
                        Path.GetFileName(item.ImagePath));

                    var centered = new ParagraphProperties(
                        new SpacingBetweenLines { Before = "80", After = "80" },
                        new Justification { Val = JustificationValues.Center });
                    body.Append(new Paragraph(centered, new Run(drawing)));

                    var sourceText = string.IsNullOrWhiteSpace(item.Source) ? string.Empty : $" ({item.Source})";
                    var caption = string.IsNullOrWhiteSpace(item.Caption)
                        ? Path.GetFileNameWithoutExtension(item.ImagePath)
                        : item.Caption;
                    body.Append(BodyPara($"Illustration{sourceText}: {caption}", 20, 0));
                }
                catch
                {
                    // Skip unreadable image files without breaking export.
                }
            }
        }

        private async Task<Dictionary<int, List<GuideIllustration>>> ResolveGuideIllustrationsByTopicAsync(
            Qualification qualification,
            Subject subject,
            IReadOnlyList<Topic> topics,
            int maxIllustrationsPerTopic,
            bool generateIllustrations,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<int, List<GuideIllustration>>();
            if (topics == null || topics.Count == 0) return result;

            var qualificationCode = (qualification.QualificationNumber ?? string.Empty).Trim();
            var subjectCode = (subject.SubjectCode ?? string.Empty).Trim();
            var subjectDescription = (subject.SubjectDescription ?? string.Empty).Trim();
            var maxPerTopic = Math.Clamp(maxIllustrationsPerTopic, 1, 4);

            var materials = _context.SourceMaterials
                .AsNoTracking()
                .OrderByDescending(s => s.KnowledgeUploadedAtUtc ?? s.CreatedAt)
                .Take(1800)
                .ToList();

            var openAiAllowed = generateIllustrations && AiRuntime.AllowOpenAi();
            var openAiKey = openAiAllowed ? (Secrets.GetOpenAIKey() ?? string.Empty).Trim() : string.Empty;
            var imageModel = (Environment.GetEnvironmentVariable("OPENAI_IMAGE_MODEL") ?? "gpt-image-1").Trim();
            if (string.IsNullOrWhiteSpace(imageModel))
            {
                imageModel = "gpt-image-1";
            }

            foreach (var topic in topics)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var local = ResolveGuideIllustrationsFromSourceMaterials(
                    materials,
                    qualificationCode,
                    subjectCode,
                    subjectDescription,
                    topic)
                    .Take(maxPerTopic)
                    .ToList();

                var combined = new List<GuideIllustration>(local);
                var remaining = maxPerTopic - combined.Count;
                if (remaining > 0 &&
                    openAiAllowed &&
                    !string.IsNullOrWhiteSpace(openAiKey))
                {
                    var outputDir = Path.Combine("C:\\ETDP\\ETDP", "Exports", "LearnerGuide", "GeneratedImages", $"topic_{topic.Id}");
                    Directory.CreateDirectory(outputDir);

                    for (var i = 0; i < remaining; i += 1)
                    {
                        var prompt = BuildGuideGeneratedImagePrompt(qualification, subject, topic, i + 1);
                        var generated = await TryGenerateGuideImageWithOpenAiAsync(
                            openAiKey,
                            imageModel,
                            "1024x1024",
                            prompt,
                            cancellationToken);
                        if (generated.Bytes == null || generated.Bytes.Length == 0)
                        {
                            continue;
                        }

                        var safeTopic = SafeFileNameToken(topic.TopicCode);
                        var generatedName = $"{safeTopic}_guide_{i + 1:00}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                        var fullPath = GetUniquePath(outputDir, generatedName);
                        await System.IO.File.WriteAllBytesAsync(fullPath, generated.Bytes, cancellationToken);

                        combined.Add(new GuideIllustration
                        {
                            ImagePath = fullPath,
                            Caption = $"AI generated learning illustration for {(topic.TopicDescription ?? topic.TopicCode)}",
                            Source = "generated",
                            Score = 120
                        });
                    }
                }

                if (combined.Count > 0)
                {
                    result[topic.Id] = combined
                        .Where(x => !string.IsNullOrWhiteSpace(x.ImagePath) && System.IO.File.Exists(x.ImagePath))
                        .OrderByDescending(x => x.Score)
                        .ThenBy(x => x.ImagePath, StringComparer.OrdinalIgnoreCase)
                        .Take(maxPerTopic)
                        .ToList();
                }
            }

            return result;
        }

        private static List<GuideIllustration> ResolveGuideIllustrationsFromSourceMaterials(
            IReadOnlyList<SourceMaterial> materials,
            string qualificationCode,
            string subjectCode,
            string subjectDescription,
            Topic topic)
        {
            var topicCode = (topic.TopicCode ?? string.Empty).Trim();
            var topicDescription = (topic.TopicDescription ?? string.Empty).Trim();

            var keywordTokens = Regex.Matches(
                    $"{topicCode} {topicDescription} {subjectCode} {subjectDescription}".ToLowerInvariant(),
                    @"[a-z0-9]{3,}")
                .Select(m => m.Value)
                .Distinct(StringComparer.Ordinal)
                .Take(14)
                .ToList();

            var picked = new List<GuideIllustration>();
            foreach (var row in materials ?? Array.Empty<SourceMaterial>())
            {
                var imagePath = ResolveSourceMaterialImagePath(row);
                if (string.IsNullOrWhiteSpace(imagePath) || !System.IO.File.Exists(imagePath)) continue;

                var rowQualificationCode = (row.QualificationCode ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(qualificationCode) &&
                    !string.IsNullOrWhiteSpace(rowQualificationCode) &&
                    !string.Equals(rowQualificationCode, qualificationCode, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var score = 0;
                if (ContainsLoose((row.TopicDescription ?? string.Empty), topicDescription)) score += 120;
                if (ContainsLoose((row.TopicDescription ?? string.Empty), topicCode)) score += 90;
                if (ContainsLoose((row.AssessmentCriteriaDescription ?? string.Empty), topicDescription)) score += 40;
                if (ContainsLoose((row.SubjectDescription ?? string.Empty), subjectDescription)) score += 20;
                if (ContainsLoose((row.SubjectDescription ?? string.Empty), subjectCode)) score += 16;

                var searchable = $"{row.Title} {row.FileName} {row.ExtractedText}".ToLowerInvariant();
                score += keywordTokens.Count(token => searchable.Contains(token, StringComparison.Ordinal)) * 4;
                if (score < 18) continue;

                var caption = (row.Title ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(caption))
                {
                    caption = (row.TopicDescription ?? string.Empty).Trim();
                }
                if (string.IsNullOrWhiteSpace(caption))
                {
                    caption = Path.GetFileNameWithoutExtension(imagePath);
                }

                picked.Add(new GuideIllustration
                {
                    ImagePath = imagePath,
                    Caption = caption,
                    Source = "source_material",
                    Score = score
                });
            }

            return picked
                .GroupBy(x => x.ImagePath.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.Score).First())
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.ImagePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool ContainsLoose(string haystack, string needle)
        {
            var h = (haystack ?? string.Empty).Trim();
            var n = (needle ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(h) || string.IsNullOrWhiteSpace(n)) return false;
            return h.Contains(n, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveSourceMaterialImagePath(SourceMaterial row)
        {
            if (row == null) return string.Empty;

            var filePath = (row.FilePath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                if (Path.IsPathRooted(filePath) && System.IO.File.Exists(filePath) && IsImageFile(filePath))
                {
                    return filePath;
                }

                var rooted = Path.GetFullPath(filePath);
                if (System.IO.File.Exists(rooted) && IsImageFile(rooted))
                {
                    return rooted;
                }

                var viaParents = ResolveFromCurrentOrParents(filePath, 6);
                if (!string.IsNullOrWhiteSpace(viaParents) && IsImageFile(viaParents))
                {
                    return viaParents;
                }
            }

            var fileName = (row.FileName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(fileName) && IsImageFile(fileName))
            {
                var resolvedName = ResolveFromCurrentOrParents(fileName, 6);
                if (!string.IsNullOrWhiteSpace(resolvedName))
                {
                    return resolvedName;
                }
            }

            var url = (row.Url ?? string.Empty).Trim();
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                var localPath = uri.LocalPath;
                if (System.IO.File.Exists(localPath) && IsImageFile(localPath))
                {
                    return localPath;
                }
            }

            return string.Empty;
        }

        private static bool IsImageFile(string path)
        {
            var ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".webp";
        }

        private static string BuildGuideGeneratedImagePrompt(
            Qualification qualification,
            Subject subject,
            Topic topic,
            int variant)
        {
            var topicTitle = string.IsNullOrWhiteSpace(topic?.TopicDescription)
                ? "vocational training concept"
                : topic.TopicDescription.Trim();
            var topicCode = (topic?.TopicCode ?? string.Empty).Trim();
            var qualificationLabel = $"{qualification?.QualificationNumber} {qualification?.QualificationDescription}".Trim();
            var subjectLabel = $"{subject?.SubjectCode} {subject?.SubjectDescription}".Trim();

            var prompt = new StringBuilder();
            prompt.Append("Create a clean educational technical illustration for a learner guide. ");
            prompt.Append("Use textbook style, white background, high clarity labels, and realistic proportions. ");
            prompt.Append("No logos, no watermarks, no trademarks, and no brand names. ");
            prompt.Append("Avoid dense paragraphs of text in the image. ");
            prompt.Append($"Topic: {topicTitle}. ");
            if (!string.IsNullOrWhiteSpace(topicCode))
            {
                prompt.Append($"Topic code: {topicCode}. ");
            }
            if (!string.IsNullOrWhiteSpace(subjectLabel))
            {
                prompt.Append($"Subject context: {subjectLabel}. ");
            }
            if (!string.IsNullOrWhiteSpace(qualificationLabel))
            {
                prompt.Append($"Qualification context: {qualificationLabel}. ");
            }
            prompt.Append($"Variation index: {variant}. ");
            prompt.Append("Style direction: vocational training diagram that helps learners identify machine parts and working principles.");
            return prompt.ToString();
        }

        private async Task<OpenAiGeneratedGuideImageResult> TryGenerateGuideImageWithOpenAiAsync(
            string apiKey,
            string model,
            string size,
            string prompt,
            CancellationToken cancellationToken)
        {
            var payload = new
            {
                model,
                prompt,
                size
            };

            using var msg = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/generations");
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            msg.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await _http.SendAsync(msg, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return OpenAiGeneratedGuideImageResult.Fail($"HTTP {(int)response.StatusCode}: {TrimTail(body, 320)}");
                }

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Array ||
                    data.GetArrayLength() == 0)
                {
                    return OpenAiGeneratedGuideImageResult.Fail("Image response did not include any data items.");
                }

                var first = data[0];
                if (first.TryGetProperty("b64_json", out var b64Node) && b64Node.ValueKind == JsonValueKind.String)
                {
                    var b64 = (b64Node.GetString() ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(b64))
                    {
                        try
                        {
                            return OpenAiGeneratedGuideImageResult.Success(Convert.FromBase64String(b64));
                        }
                        catch
                        {
                            return OpenAiGeneratedGuideImageResult.Fail("Image base64 payload could not be decoded.");
                        }
                    }
                }

                if (first.TryGetProperty("url", out var urlNode) && urlNode.ValueKind == JsonValueKind.String)
                {
                    var url = (urlNode.GetString() ?? string.Empty).Trim();
                    if (Uri.TryCreate(url, UriKind.Absolute, out var _))
                    {
                        var bytes = await _http.GetByteArrayAsync(url, cancellationToken);
                        return OpenAiGeneratedGuideImageResult.Success(bytes);
                    }
                }

                return OpenAiGeneratedGuideImageResult.Fail("Image response did not include b64_json or url.");
            }
            catch (Exception ex)
            {
                return OpenAiGeneratedGuideImageResult.Fail(ex.Message);
            }
        }

        private static string GetUniquePath(string directory, string fileName)
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var path = Path.Combine(directory, fileName);
            var index = 1;
            while (System.IO.File.Exists(path))
            {
                path = Path.Combine(directory, $"{baseName}_{index:00}{ext}");
                index += 1;
            }
            return path;
        }

        private static string BuildTopicSummary(TopicGuideSection topic)
        {
            var contentBlocks = topic.Criteria
                .SelectMany(c => c.Lessons)
                .Select(l => (l.LessonContent ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(10)
                .ToList();

            if (contentBlocks.Count == 0)
            {
                return $"This topic covered {topic.TopicCode} with {topic.Criteria.Count} assessment criteria. Add lesson content to generate richer summaries.";
            }

            return BuildSummary(contentBlocks, $"{topic.TopicCode} - {topic.TopicDescription}");
        }

        private static string BuildLearnerGuideNarrationScript(
            Qualification qualification,
            CurriculumPhase? phase,
            Subject subject,
            IReadOnlyList<TopicGuideSection> topics)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Learner guide narration for {(subject.SubjectCode ?? string.Empty).Trim()} {(subject.SubjectDescription ?? string.Empty).Trim()}.");
            sb.AppendLine($"Qualification {(qualification.QualificationNumber ?? string.Empty).Trim()} {(qualification.QualificationDescription ?? string.Empty).Trim()}.");
            if (!string.IsNullOrWhiteSpace(phase?.Name))
            {
                sb.AppendLine($"Phase: {phase.Name.Trim()}.");
            }
            sb.AppendLine();

            var purpose = (subject.SubjectPurpose ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(purpose))
            {
                sb.AppendLine("Subject purpose.");
                sb.AppendLine(purpose);
                sb.AppendLine();
            }

            foreach (var topic in topics ?? Array.Empty<TopicGuideSection>())
            {
                var topicTitle = $"{(topic.TopicCode ?? string.Empty).Trim()} {(topic.TopicDescription ?? string.Empty).Trim()}".Trim();
                if (string.IsNullOrWhiteSpace(topicTitle))
                {
                    topicTitle = "Topic";
                }

                sb.AppendLine($"Topic: {topicTitle}.");
                if (!string.IsNullOrWhiteSpace(topic.TopicPurpose))
                {
                    sb.AppendLine($"Purpose: {topic.TopicPurpose.Trim()}");
                }

                foreach (var criterion in topic.Criteria ?? new List<CriteriaGuideSection>())
                {
                    var criterionText = (criterion.CriteriaDescription ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(criterionText))
                    {
                        sb.AppendLine($"Assessment criterion: {criterionText}");
                    }

                    foreach (var lesson in criterion.Lessons ?? new List<GuideLessonBlock>())
                    {
                        if (!string.IsNullOrWhiteSpace(lesson.Lpn))
                        {
                            sb.AppendLine($"{lesson.Lpn}.");
                        }
                        if (!string.IsNullOrWhiteSpace(lesson.LessonPlanDescription))
                        {
                            sb.AppendLine($"Lesson focus: {lesson.LessonPlanDescription.Trim()}");
                        }
                        if (!string.IsNullOrWhiteSpace(lesson.LessonContent))
                        {
                            sb.AppendLine(lesson.LessonContent.Trim());
                        }
                    }
                }

                sb.AppendLine();
            }

            return Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n").Trim();
        }

        private static List<string> SplitNarrationIntoChunks(string script, int maxCharsPerChunk, int maxChunks)
        {
            var paragraphs = Regex.Split((script ?? string.Empty).Trim(), @"\r?\n\r?\n")
                .Select(p => Regex.Replace((p ?? string.Empty).Trim(), @"\s+", " "))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            var chunks = new List<string>();
            if (paragraphs.Count == 0) return chunks;
            var maxChars = Math.Clamp(maxCharsPerChunk, 1000, 3900);
            var hardMaxChunks = Math.Clamp(maxChunks, 1, 60);
            var current = new StringBuilder();

            foreach (var paragraph in paragraphs)
            {
                if (chunks.Count >= hardMaxChunks) break;

                if (paragraph.Length > maxChars)
                {
                    var sentences = Regex.Split(paragraph, @"(?<=[.!?])\s+")
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                    foreach (var sentence in sentences)
                    {
                        if (chunks.Count >= hardMaxChunks) break;
                        if (current.Length > 0 && current.Length + sentence.Length + 1 > maxChars)
                        {
                            chunks.Add(current.ToString().Trim());
                            current.Clear();
                            if (chunks.Count >= hardMaxChunks) break;
                        }

                        if (sentence.Length > maxChars)
                        {
                            var trimmed = sentence[..Math.Min(maxChars, sentence.Length)].Trim();
                            if (!string.IsNullOrWhiteSpace(trimmed))
                            {
                                chunks.Add(trimmed);
                            }
                            continue;
                        }

                        if (current.Length > 0) current.Append(' ');
                        current.Append(sentence);
                    }
                    continue;
                }

                if (current.Length > 0 && current.Length + paragraph.Length + 2 > maxChars)
                {
                    chunks.Add(current.ToString().Trim());
                    current.Clear();
                    if (chunks.Count >= hardMaxChunks) break;
                }

                if (current.Length > 0) current.Append("\n\n");
                current.Append(paragraph);
            }

            if (current.Length > 0 && chunks.Count < hardMaxChunks)
            {
                chunks.Add(current.ToString().Trim());
            }

            return chunks
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();
        }

        private async Task<OpenAiTtsResult> TrySynthesizeOpenAiTtsChunkAsync(
            string apiKey,
            string model,
            string voice,
            string format,
            double speed,
            string text,
            CancellationToken cancellationToken)
        {
            var payload = new
            {
                model,
                voice,
                input = text,
                format,
                speed
            };

            using var msg = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech");
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            msg.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                using var response = await _http.SendAsync(msg, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    return OpenAiTtsResult.Fail($"HTTP {(int)response.StatusCode}: {TrimTail(body, 320)}");
                }

                var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (audio.Length == 0)
                {
                    return OpenAiTtsResult.Fail("OpenAI returned empty audio output.");
                }

                return OpenAiTtsResult.Success(audio);
            }
            catch (Exception ex)
            {
                return OpenAiTtsResult.Fail(ex.Message);
            }
        }

        private static string NormalizeTtsFormat(string? raw)
        {
            var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "aac" => "aac",
                "wav" => "wav",
                "flac" => "flac",
                "opus" => "opus",
                _ => "mp3"
            };
        }

        private static string TrimTail(string? input, int maxChars)
        {
            var s = (input ?? string.Empty).Trim();
            if (s.Length <= maxChars) return s;
            return s.Substring(s.Length - maxChars);
        }

        private static string NormalizeDocumentText(string? value)
        {
            var normalized = (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Trim();
            return SanitizeXmlText(normalized);
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
                // Drop invalid XML characters.
            }
            return sb.ToString();
        }

        private static int ParseLpnSort(string? value)
        {
            var raw = (value ?? string.Empty).Trim();
            if (int.TryParse(raw, out var direct)) return direct;
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var parsed) ? parsed : int.MaxValue - 1;
        }

        private static string NormalizeLpn(string? value)
        {
            var raw = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return "LPN 1";
            return raw.StartsWith("LPN", StringComparison.OrdinalIgnoreCase) ? raw.ToUpperInvariant() : $"LPN {raw}";
        }

        private static string NormalizeLooseText(string? value)
        {
            return Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        }

        private static bool HasSubjectIdentity(Subject subject)
        {
            return !string.IsNullOrWhiteSpace((subject.SubjectCode ?? string.Empty).Trim()) ||
                   !string.IsNullOrWhiteSpace((subject.SubjectDescription ?? string.Empty).Trim());
        }

        private static bool ToolkitMatchesSubjectCode(LecturerToolkitEntry row, Subject subject)
        {
            var rowSubjectCode = (row.SubjectCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rowSubjectCode)) return true;
            var subjectCode = (subject.SubjectCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(subjectCode)) return true;
            return string.Equals(rowSubjectCode, subjectCode, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildTopicIdentity(Topic topic)
        {
            return string.Join("|", new[]
            {
                NormalizeLooseText(topic.TopicCode),
                NormalizeLooseText(topic.TopicDescription)
            });
        }

        private static string BuildCriterionIdentity(
            AssessmentCriteria criterion,
            IReadOnlyDictionary<int, Topic> topicsById)
        {
            if (topicsById.TryGetValue(criterion.TopicId, out var topic))
            {
                return $"{BuildTopicIdentity(topic)}|{NormalizeLooseText(criterion.Description)}";
            }

            return $"topic:{criterion.TopicId}|{NormalizeLooseText(criterion.Description)}";
        }

        private static bool HasGuideSourceContent(LecturerToolkitEntry? row)
        {
            if (row == null) return false;
            return !string.IsNullOrWhiteSpace((row.LessonPlanContent ?? string.Empty).Trim()) ||
                   !string.IsNullOrWhiteSpace((row.LessonPlanDescription ?? string.Empty).Trim()) ||
                   !string.IsNullOrWhiteSpace((row.LecturerActions ?? string.Empty).Trim()) ||
                   !string.IsNullOrWhiteSpace((row.LearnerActions ?? string.Empty).Trim());
        }

        private static bool HasGuideSourceContent(LessonPlan? row)
        {
            if (row == null) return false;
            return !string.IsNullOrWhiteSpace((row.Content ?? string.Empty).Trim()) ||
                   !string.IsNullOrWhiteSpace((row.Title ?? string.Empty).Trim());
        }

        private static string DescribeUnmappedToolkitRow(
            LecturerToolkitEntry row,
            IReadOnlyDictionary<int, AssessmentCriteria> criteriaById)
        {
            var criteriaId = row.AssessmentCriteriaId.GetValueOrDefault();
            if (criteriaId > 0)
            {
                if (!criteriaById.ContainsKey(criteriaId))
                {
                    return "AssessmentCriteriaId is not part of the selected subject/topics.";
                }

                return "AssessmentCriteriaId exists but row did not map. Verify criteria text and subject selection.";
            }

            var criteriaDescription = (row.AssessmentCriteriaDescription ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(criteriaDescription))
            {
                return "Missing AssessmentCriteriaId and AssessmentCriteriaDescription.";
            }

            return "Criteria description did not match selected subject criteria.";
        }

        private static List<LecturerToolkitEntry> ResolveToolkitRowsForCriterion(
            IEnumerable<LecturerToolkitEntry> toolkitRows,
            AssessmentCriteria criterion,
            Subject subject)
        {
            var allRows = (toolkitRows ?? Enumerable.Empty<LecturerToolkitEntry>())
                .Where(row => ToolkitMatchesSubjectCode(row, subject))
                .ToList();

            var matches = new List<LecturerToolkitEntry>();
            var byId = allRows
                .Where(row => row.AssessmentCriteriaId.HasValue && row.AssessmentCriteriaId.Value == criterion.Id)
                .ToList();
            if (byId.Count > 0)
            {
                matches.AddRange(byId);
            }

            var criterionDescriptionNorm = NormalizeLooseText(criterion.Description);
            if (!string.IsNullOrWhiteSpace(criterionDescriptionNorm))
            {
                var byDescription = allRows
                    .Where(row =>
                        string.Equals(
                            NormalizeLooseText(row.AssessmentCriteriaDescription),
                            criterionDescriptionNorm,
                            StringComparison.Ordinal))
                    .ToList();
                if (byDescription.Count > 0)
                {
                    foreach (var row in byDescription)
                    {
                        if (matches.Any(existing => existing.Id == row.Id)) continue;
                        matches.Add(row);
                    }
                }
            }

            return matches
                .OrderBy(row => ParseLpnSort(row.Lpn))
                .ThenBy(row => row.Id)
                .ToList();
        }

        private static List<LecturerToolkitEntry> ResolveToolkitRowsForCriteria(
            IEnumerable<LecturerToolkitEntry> toolkitRows,
            IEnumerable<AssessmentCriteria> criteria,
            Subject subject)
        {
            var merged = new Dictionary<int, LecturerToolkitEntry>();
            foreach (var criterion in criteria ?? Enumerable.Empty<AssessmentCriteria>())
            {
                foreach (var row in ResolveToolkitRowsForCriterion(toolkitRows, criterion, subject))
                {
                    if (!merged.ContainsKey(row.Id))
                    {
                        merged[row.Id] = row;
                    }
                }
            }

            return merged.Values
                .OrderBy(row => ParseLpnSort(row.Lpn))
                .ThenBy(row => row.Id)
                .ToList();
        }

        private static List<LessonPlan> ResolveLessonPlansForCriteria(
            IReadOnlyDictionary<int, List<LessonPlan>> lessonByCriteria,
            IEnumerable<AssessmentCriteria> criteria)
        {
            var merged = new Dictionary<int, LessonPlan>();
            foreach (var criterion in criteria ?? Enumerable.Empty<AssessmentCriteria>())
            {
                if (!lessonByCriteria.TryGetValue(criterion.Id, out var rows) || rows == null) continue;
                foreach (var row in rows)
                {
                    if (!merged.ContainsKey(row.Id))
                    {
                        merged[row.Id] = row;
                    }
                }
            }

            return merged.Values
                .OrderBy(row => row.SortOrder)
                .ThenBy(row => row.Id)
                .ToList();
        }

        private static List<GuideLessonBlock> DeduplicateLessonBlocks(IEnumerable<GuideLessonBlock>? lessons)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var deduped = new List<GuideLessonBlock>();
            foreach (var lesson in lessons ?? Enumerable.Empty<GuideLessonBlock>())
            {
                var signature = BuildLessonBlockSignature(lesson);
                if (!seen.Add(signature)) continue;
                deduped.Add(lesson);
            }
            return deduped;
        }

        private static string BuildLessonBlockSignature(GuideLessonBlock lesson)
        {
            return string.Join("|", new[]
            {
                NormalizeLooseText(lesson.Lpn),
                NormalizeLooseText(lesson.LessonPlanDescription),
                NormalizeLooseText(lesson.LessonContent)
            });
        }

        private static string BuildSummary(List<string> blocks, string criteriaDescription)
        {
            if (blocks.Count == 0) return $"No lesson content available for {criteriaDescription}.";
            var flattened = string.Join(" ", blocks);
            var sentences = Regex.Split(flattened, @"(?<=[.!?])\s+")
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Take(4)
                .ToList();
            if (sentences.Count == 0)
            {
                var fallback = flattened.Trim();
                return fallback.Length <= 600 ? fallback : $"{fallback.Substring(0, 600)}...";
            }
            return string.Join(" ", sentences);
        }

        private sealed class TopicGuideSection
        {
            public int TopicId { get; init; }
            public string TopicCode { get; init; } = string.Empty;
            public string TopicDescription { get; init; } = string.Empty;
            public string TopicPurpose { get; init; } = string.Empty;
            public List<CriteriaGuideSection> Criteria { get; init; } = new();
        }

        private sealed class GuideCriteriaGroup
        {
            public string TopicKey { get; init; } = string.Empty;
            public AssessmentCriteria Representative { get; init; } = null!;
            public List<AssessmentCriteria> Members { get; init; } = new();
        }

        private sealed class GuideIllustration
        {
            public string ImagePath { get; init; } = string.Empty;
            public string Caption { get; init; } = string.Empty;
            public string Source { get; init; } = string.Empty;
            public int Score { get; init; }
        }

        private sealed class OpenAiGeneratedGuideImageResult
        {
            public byte[]? Bytes { get; init; }
            public string Error { get; init; } = string.Empty;

            public static OpenAiGeneratedGuideImageResult Success(byte[] bytes) => new()
            {
                Bytes = bytes ?? Array.Empty<byte>(),
                Error = string.Empty
            };

            public static OpenAiGeneratedGuideImageResult Fail(string error) => new()
            {
                Bytes = null,
                Error = error ?? string.Empty
            };
        }

        private sealed class OpenAiTtsResult
        {
            public byte[]? Bytes { get; init; }
            public string Error { get; init; } = string.Empty;

            public static OpenAiTtsResult Success(byte[] bytes) => new()
            {
                Bytes = bytes ?? Array.Empty<byte>(),
                Error = string.Empty
            };

            public static OpenAiTtsResult Fail(string error) => new()
            {
                Bytes = null,
                Error = error ?? string.Empty
            };
        }

        private sealed class CriteriaReadinessDiagnostic
        {
            public int CriteriaId { get; init; }
            public string CriteriaDescription { get; init; } = string.Empty;
            public int MatchedRows { get; init; }
            public int MatchedRowsWithContent { get; init; }
            public int FallbackPlans { get; init; }
            public int FallbackPlansWithContent { get; init; }
            public bool HasAnyContent { get; init; }
        }

        private sealed class CriteriaGuideSection
        {
            public int CriteriaId { get; init; }
            public string CriteriaDescription { get; init; } = string.Empty;
            public List<GuideLessonBlock> Lessons { get; init; } = new();
        }

        private sealed class GuideLessonBlock
        {
            public string Lpn { get; init; } = string.Empty;
            public string LessonPlanDescription { get; init; } = string.Empty;
            public string LessonContent { get; init; } = string.Empty;
            public string LecturerActions { get; init; } = string.Empty;
            public string LearnerActions { get; init; } = string.Empty;
            public string LearningAids { get; init; } = string.Empty;
            public string TimeStart { get; init; } = string.Empty;
            public string TimeEnd { get; init; } = string.Empty;
        }

        private static Paragraph PageBreak() => new(new Run(new Break() { Type = BreakValues.Page }));

        private static Paragraph StyledHeading(string text, string styleId, int sizePt)
        {
            var rp = new RunProperties
            {
                Bold = new Bold(),
                FontSize = new FontSize { Val = (sizePt * 2).ToString() },
                RunFonts = new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }
            };
            var pp = new ParagraphProperties(
                new ParagraphStyleId { Val = styleId },
                new SpacingBetweenLines { Before = "120", After = "120" });
            return new Paragraph(pp, new Run(rp, new Text(SanitizeXmlText(text ?? string.Empty))));
        }

        private static Paragraph BuildChapterHeading(string text)
        {
            return BuildGuideHeadingParagraph(
                text,
                "Heading1",
                56,
                bold: true,
                allCaps: true,
                beforeTwips: 120,
                afterTwips: 240,
                justification: JustificationValues.Left);
        }

        private static Paragraph BuildSubjectHeading(string text)
        {
            return BuildGuideHeadingParagraph(
                text,
                "Heading2",
                32,
                bold: false,
                allCaps: true,
                beforeTwips: 240,
                afterTwips: 240,
                justification: JustificationValues.Left,
                bottomBorderSize: 12);
        }

        private static Paragraph BuildPurposeHeading(string text)
        {
            return BuildGuideHeadingParagraph(
                text,
                null,
                32,
                bold: true,
                allCaps: true,
                beforeTwips: 240,
                afterTwips: 240,
                justification: JustificationValues.Left);
        }

        private static Paragraph BuildAssessmentCriteriaHeading(string text)
        {
            return BuildGuideHeadingParagraph(
                text,
                "Heading3",
                28,
                bold: true,
                allCaps: true,
                beforeTwips: 160,
                afterTwips: 160,
                justification: JustificationValues.Left,
                topBorderSize: 12,
                bottomBorderSize: 12,
                shadingFill: "D9D9D9");
        }

        private static Paragraph BuildTopicHeading(string text)
        {
            return BuildGuideHeadingParagraph(
                text,
                "Heading4",
                28,
                bold: true,
                allCaps: true,
                beforeTwips: 160,
                afterTwips: 160,
                justification: JustificationValues.Left,
                bottomBorderSize: 12);
        }

        private static Paragraph BuildLessonPlanHeading(string text)
        {
            return BuildGuideHeadingParagraph(
                text,
                "Heading5",
                28,
                bold: true,
                allCaps: false,
                beforeTwips: 0,
                afterTwips: 240,
                justification: JustificationValues.Right);
        }

        private static Paragraph BuildLessonContentTitleHeading(string text)
        {
            return BuildGuideHeadingParagraph(
                text,
                "Heading6",
                24,
                bold: true,
                allCaps: false,
                beforeTwips: 0,
                afterTwips: 240,
                justification: JustificationValues.Left,
                topBorderSize: 8);
        }

        private static Paragraph BuildWorkbookActivitiesHeading(string text)
        {
            return BuildGuideHeadingParagraph(
                text,
                "Heading3",
                28,
                bold: true,
                allCaps: false,
                beforeTwips: 160,
                afterTwips: 160,
                justification: JustificationValues.Left);
        }

        private static Paragraph BuildGuideHeadingParagraph(
            string text,
            string? styleId,
            int sizeHalfPt,
            bool bold,
            bool allCaps,
            int beforeTwips,
            int afterTwips,
            JustificationValues justification,
            uint? topBorderSize = null,
            uint? bottomBorderSize = null,
            string? shadingFill = null)
        {
            var runProperties = new RunProperties
            {
                FontSize = new FontSize { Val = sizeHalfPt.ToString() },
                RunFonts = new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }
            };
            if (bold) runProperties.Bold = new Bold();
            if (allCaps) runProperties.Caps = new Caps();

            var paragraphProperties = new ParagraphProperties(
                new SpacingBetweenLines
                {
                    Before = beforeTwips.ToString(),
                    After = afterTwips.ToString(),
                    Line = "360",
                    LineRule = LineSpacingRuleValues.Auto
                },
                new Justification { Val = justification });

            if (!string.IsNullOrWhiteSpace(styleId))
            {
                paragraphProperties.ParagraphStyleId = new ParagraphStyleId { Val = styleId };
            }

            if (topBorderSize.HasValue || bottomBorderSize.HasValue)
            {
                var borders = new ParagraphBorders();
                if (topBorderSize.HasValue)
                {
                    borders.TopBorder = new TopBorder
                    {
                        Val = BorderValues.Single,
                        Size = topBorderSize.Value,
                        Space = 0U,
                        Color = "000000"
                    };
                }
                if (bottomBorderSize.HasValue)
                {
                    borders.BottomBorder = new BottomBorder
                    {
                        Val = BorderValues.Single,
                        Size = bottomBorderSize.Value,
                        Space = 0U,
                        Color = "000000"
                    };
                }
                paragraphProperties.Append(borders);
            }

            if (!string.IsNullOrWhiteSpace(shadingFill))
            {
                paragraphProperties.Shading = new Shading
                {
                    Val = ShadingPatternValues.Clear,
                    Color = "auto",
                    Fill = shadingFill
                };
            }

            return new Paragraph(
                paragraphProperties,
                new Run(runProperties, new Text(SanitizeXmlText(text ?? string.Empty)) { Space = SpaceProcessingModeValues.Preserve }));
        }

        private static Paragraph CenterPara(string text, int sizeHalfPt, bool bold = false)
        {
            var rp = new RunProperties
            {
                FontSize = new FontSize { Val = sizeHalfPt.ToString() },
                RunFonts = new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }
            };
            if (bold) rp.Bold = new Bold();
            var pp = new ParagraphProperties(
                new SpacingBetweenLines { Line = "320", LineRule = LineSpacingRuleValues.Auto },
                new Justification { Val = JustificationValues.Center });
            return new Paragraph(pp, new Run(rp, new Text(SanitizeXmlText(text ?? string.Empty))));
        }

        private static Paragraph BodyPara(string text, int sizeHalfPt, int firstLineTwips = 720, bool bold = false)
        {
            var rp = new RunProperties
            {
                FontSize = new FontSize { Val = sizeHalfPt.ToString() },
                RunFonts = new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }
            };
            if (bold) rp.Bold = new Bold();
            var pp = new ParagraphProperties(
                new SpacingBetweenLines { Line = "360", LineRule = LineSpacingRuleValues.Auto },
                new Indentation { FirstLine = firstLineTwips.ToString() });
            return new Paragraph(pp, new Run(rp, new Text(SanitizeXmlText(text ?? string.Empty))));
        }

        private static void AppendMultilineBody(Body body, string text, int sizeHalfPt)
        {
            var normalized = NormalizeDocumentText(text);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            var blocks = normalized
                .Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();

            foreach (var block in blocks)
            {
                body.Append(BodyPara(block, sizeHalfPt, 0));
            }
        }

        private static void AppendExactLessonPlanContent(Body body, string text, int sizeHalfPt)
        {
            var raw = (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            if (string.IsNullOrWhiteSpace(raw)) return;

            var buffer = new List<string>();
            foreach (var rawLine in raw.Split('\n', StringSplitOptions.None))
            {
                var line = NormalizeDocumentText(rawLine);
                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushLessonContentParagraph(body, buffer, sizeHalfPt);
                    continue;
                }

                if (LooksLikeLessonContentTitle(line))
                {
                    FlushLessonContentParagraph(body, buffer, sizeHalfPt);
                    body.Append(BuildLessonContentTitleHeading(line));
                    continue;
                }

                buffer.Add(line);
            }

            FlushLessonContentParagraph(body, buffer, sizeHalfPt);
        }

        private static void FlushLessonContentParagraph(Body body, List<string> buffer, int sizeHalfPt)
        {
            if (buffer.Count == 0) return;
            body.Append(BodyPara(string.Join(" ", buffer), sizeHalfPt, 0));
            buffer.Clear();
        }

        private static bool LooksLikeLessonContentTitle(string line)
        {
            var text = NormalizeDocumentText(line);
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Length > 80) return false;
            if (text.EndsWith(".") || text.EndsWith(":") || text.EndsWith(";")) return false;
            if (text.StartsWith("LPN ", StringComparison.OrdinalIgnoreCase)) return false;
            if (text.StartsWith("Topic ", StringComparison.OrdinalIgnoreCase)) return false;
            if (Regex.IsMatch(text, @"^\d+[\.\)]")) return false;

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words.Length > 8) return false;

            return words.Count(word => char.IsLetter(word.FirstOrDefault())) >= 2;
        }

        private static string BuildLessonPlanHeadingText(string? lpn, string? description)
        {
            var normalizedLpn = NormalizeLpn(lpn);
            var normalizedDescription = NormalizeDocumentText(description);
            if (string.IsNullOrWhiteSpace(normalizedDescription)) return normalizedLpn;
            if (string.IsNullOrWhiteSpace(normalizedLpn)) return normalizedDescription;
            return $"{normalizedLpn}: {normalizedDescription}";
        }

        private static int ResolveWorkbookActivityCount(
            IReadOnlyList<TopicGuideSection> topics,
            IReadOnlyList<string> workbookActivities)
        {
            if (workbookActivities != null && workbookActivities.Count > 0)
            {
                return workbookActivities.Count;
            }

            return topics?
                .SelectMany(topic => topic.Criteria)
                .SelectMany(criteria => criteria.Lessons)
                .Count() ?? 0;
        }

        private static string BuildWorkbookActivitiesSummaryLine(int activityCount)
        {
            if (activityCount <= 1)
            {
                return "Complete all Workbook activities based on the topic and Lesson Plan Numbers (LPN) for this Chapter which is activity 1.";
            }

            return $"Complete all Workbook activities based on the topic and Lesson Plan Numbers (LPN) for this Chapter which are activities 1 – {activityCount}.";
        }

        private static Paragraph BuildTableOfContentsField()
        {
            return new Paragraph(
                new Run(new FieldChar() { FieldCharType = FieldCharValues.Begin }),
                new Run(new FieldCode(" TOC \\o \"1-6\" \\h \\z \\u ") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new FieldChar() { FieldCharType = FieldCharValues.Separate }),
                new Run(new Text("Table of contents will populate after field update.") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new FieldChar() { FieldCharType = FieldCharValues.End }));
        }

        private const uint PortraitA4PageWidthTwips = 11906U;
        private const uint PortraitA4PageHeightTwips = 16838U;
        private const uint PortraitCoverUsableWidthTwips = 9866U;

        private static long TwipsToEmu(uint twips) => twips * 635L;

        private static string EnsureLearnerGuideHeader(MainDocumentPart main, Qualification qualification)
        {
            var institution = (qualification.LearningInstitutionName ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(institution)) institution = "LEARNING INSTITUTION";

            var lecturer = (qualification.SeniorLecturer ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(lecturer))
            {
                lecturer = (qualification.DeanPrincipalCEO ?? string.Empty).Trim().ToUpperInvariant();
            }
            if (string.IsNullOrWhiteSpace(lecturer)) lecturer = "LECTURER";

            var headerPart = main.AddNewPart<HeaderPart>();
            var header = new Header();

            var paragraph = new Paragraph(
                new ParagraphProperties(
                    new Tabs(
                        new TabStop { Val = TabStopValues.Center, Position = 5233 },
                        new TabStop { Val = TabStopValues.Right, Position = 10466 }),
                    new SpacingBetweenLines { Before = "0", After = "0" }),
                HeaderRun(institution, bold: true),
                new Run(new TabChar()),
                HeaderRun(lecturer, bold: true),
                new Run(new TabChar()),
                HeaderRun("Page "),
                new SimpleField { Instruction = " PAGE " },
                HeaderRun(" of "),
                new SimpleField { Instruction = " NUMPAGES " });

            header.Append(paragraph);
            headerPart.Header = header;
            headerPart.Header.Save();
            return main.GetIdOfPart(headerPart);
        }

        private static Run HeaderRun(string text, bool bold = false)
        {
            var rp = new RunProperties
            {
                FontSize = new FontSize { Val = "20" },
                RunFonts = new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }
            };
            if (bold) rp.Bold = new Bold();
            return new Run(rp, new Text(SanitizeXmlText(text ?? string.Empty)) { Space = SpaceProcessingModeValues.Preserve });
        }

        private static SectionProperties DefaultPortraitSectionProperties(string? headerRelId = null)
        {
            var section = new SectionProperties();
            if (!string.IsNullOrWhiteSpace(headerRelId))
            {
                section.Append(new HeaderReference { Type = HeaderFooterValues.Default, Id = headerRelId });
            }

            section.Append(
                new PageSize() { Orient = PageOrientationValues.Portrait, Width = PortraitA4PageWidthTwips, Height = PortraitA4PageHeightTwips },
                new PageMargin() { Top = 1020, Bottom = 1020, Left = 1020, Right = 1020, Header = 720, Footer = 720, Gutter = 0 });

            return section;
        }

        private static string? ResolveLearnerGuideCoverPath()
        {
            var candidates = new[]
            {
                Path.Combine("Imports", "Coverpages", "clean coverpage.jpg"),
                Path.Combine("ETDP", "Imports", "Coverpages", "clean coverpage.jpg"),
                Path.Combine("Imports", "Coverpages", "Learner Guide Coverpage.png"),
                Path.Combine("ETDP", "Imports", "Coverpages", "Learner Guide Coverpage.png")
            };

            foreach (var relative in candidates)
            {
                var path = ResolveFromCurrentOrParents(relative, 6);
                if (!string.IsNullOrWhiteSpace(path)) return path;
            }

            return null;
        }

        private static string BuildCoverQualificationLine(Qualification qualification)
        {
            var qualificationNumber = (qualification.QualificationNumber ?? string.Empty).Trim();
            var qualificationDescription = (qualification.QualificationDescription ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(qualificationNumber)) return qualificationDescription;
            if (string.IsNullOrWhiteSpace(qualificationDescription)) return qualificationNumber;
            return $"{qualificationNumber} {qualificationDescription}".Trim();
        }

        private static string BuildLearnerGuideCoverNqfCreditsLine(Qualification qualification)
        {
            var nqfLevel = (qualification.NqfLevel ?? string.Empty).Trim();
            var credits = (qualification.Credits ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(nqfLevel) && string.IsNullOrWhiteSpace(credits))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(nqfLevel))
            {
                return $"{credits} Credits".Trim();
            }

            if (string.IsNullOrWhiteSpace(credits))
            {
                return $"NQF Level {nqfLevel}".Trim();
            }

            return $"NQF Level {nqfLevel} - {credits} Credits".Trim();
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
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks() { NoChangeAspect = true }),
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

        private static string SafeFileNameToken(string? value)
        {
            var raw = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return "NA";
            var safe = Regex.Replace(raw, @"[^A-Za-z0-9._-]+", "_");
            safe = Regex.Replace(safe, @"_+", "_").Trim('_');
            return string.IsNullOrWhiteSpace(safe) ? "NA" : safe;
        }
    }
}
