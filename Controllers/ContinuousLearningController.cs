using ETD.Api.Services;
using ETD.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class ContinuousLearningController : ControllerBase
    {
        private readonly ContinuousLearningIngestionWorker _worker;
        private readonly ApplicationDbContext _context;

        public ContinuousLearningController(ContinuousLearningIngestionWorker worker, ApplicationDbContext context)
        {
            _worker = worker;
            _context = context;
        }

        [HttpGet("status")]
        public IActionResult Status()
        {
            return Ok(_worker.GetStatus());
        }

        [HttpGet("config")]
        public async Task<IActionResult> Config(CancellationToken ct)
        {
            return Ok(await _worker.GetConfigAsync(ct));
        }

        [HttpPut("config")]
        public async Task<IActionResult> SaveConfig([FromBody] ContinuousLearningConfig config, CancellationToken ct)
        {
            if (config == null)
            {
                return BadRequest("Continuous learning config is required.");
            }

            if (config.IntervalHours < 1)
            {
                config.IntervalHours = 1;
            }

            if (config.MaxGitHubFilesPerSourcePerRun < 1)
            {
                config.MaxGitHubFilesPerSourcePerRun = 1;
            }

            if (config.MaxHuggingFaceRowsPerSourcePerRun < 1)
            {
                config.MaxHuggingFaceRowsPerSourcePerRun = 1;
            }

            if (config.MaxTextCharsPerMaterial < 2000)
            {
                config.MaxTextCharsPerMaterial = 2000;
            }

            await _worker.SaveConfigAsync(config, ct);
            return Ok(await _worker.GetConfigAsync(ct));
        }

        [HttpPost("run-now")]
        public async Task<IActionResult> RunNow()
        {
            await _worker.RequestRunAsync();
            return Ok(new
            {
                accepted = true,
                message = "Continuous learning run requested. The worker should move from queued to running on the next background tick.",
                status = _worker.GetStatus()
            });
        }

        [HttpGet("metrics")]
        public async Task<IActionResult> Metrics([FromQuery] int? qualificationId = null, CancellationToken ct = default)
        {
            var materialQuery = _context.SourceMaterials.AsNoTracking()
                .Where(x => !string.IsNullOrWhiteSpace(x.ExtractedText));
            var subjectQuery = _context.Subjects.AsNoTracking().AsQueryable();
            var topicQuery = _context.Topics.AsNoTracking().Include(x => x.Subject).AsQueryable();

            var requestedQualificationId = qualificationId.GetValueOrDefault();
            if (requestedQualificationId > 0)
            {
                var qid = requestedQualificationId;
                var qualification = await _context.Qualifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == qid, ct);
                subjectQuery = subjectQuery.Where(x => x.QualificationId == qid);
                topicQuery = topicQuery.Where(x => x.Subject != null && x.Subject.QualificationId == qid);
                if (qualification != null)
                {
                    var code = (qualification.QualificationNumber ?? string.Empty).Trim();
                    var description = (qualification.QualificationDescription ?? string.Empty).Trim();
                    materialQuery = materialQuery.Where(x =>
                        (!string.IsNullOrWhiteSpace(code) && x.QualificationCode == code) ||
                        (!string.IsNullOrWhiteSpace(description) && x.QualificationDescription == description) ||
                        string.IsNullOrWhiteSpace(x.QualificationCode));
                }
            }

            var materials = await materialQuery
                .Select(x => new
                {
                    x.Id,
                    x.Title,
                    x.KnowledgeSourceType,
                    x.QualificationCode,
                    x.QualificationDescription,
                    x.SubjectDescription,
                    x.TopicDescription,
                    CharCount = x.ExtractedText.Length,
                    x.CreatedAt,
                    x.KnowledgeUploadedAtUtc
                })
                .ToListAsync(ct);

            var subjects = await subjectQuery
                .Select(x => new { x.Id, x.SubjectCode, x.SubjectDescription, x.SubjectCredits, x.SubjectNQFLevel })
                .ToListAsync(ct);
            var topics = await topicQuery
                .Select(x => new
                {
                    x.Id,
                    x.TopicCode,
                    x.TopicDescription,
                    SubjectId = x.SubjectId,
                    SubjectDescription = x.Subject == null ? string.Empty : x.Subject.SubjectDescription
                })
                .ToListAsync(ct);

            var materialDtos = materials.Select(x => new
            {
                x.Id,
                x.Title,
                SourceType = string.IsNullOrWhiteSpace(x.KnowledgeSourceType) ? "local_source_upload" : x.KnowledgeSourceType,
                x.SubjectDescription,
                x.TopicDescription,
                x.CharCount,
                WordCount = EstimateWords(x.CharCount),
                CreatedAtUtc = x.KnowledgeUploadedAtUtc ?? x.CreatedAt
            }).ToList();

            var totalWords = materialDtos.Sum(x => x.WordCount);
            var sourceTypes = materialDtos
                .GroupBy(x => x.SourceType)
                .Select(g => new
                {
                    sourceType = g.Key,
                    materials = g.Count(),
                    words = g.Sum(x => x.WordCount),
                    percentage = Percent(g.Sum(x => x.WordCount), Math.Max(1, totalWords))
                })
                .OrderByDescending(x => x.words)
                .ToList();

            var subjectCoverage = subjects
                .Select(subject =>
                {
                    var subjectText = $"{subject.SubjectCode} {subject.SubjectDescription}".Trim();
                    var matched = materialDtos.Where(m =>
                        ContainsAny(m.SubjectDescription, subject.SubjectDescription, subject.SubjectCode) ||
                        ContainsAny(m.TopicDescription, subject.SubjectDescription, subject.SubjectCode) ||
                        ContainsAny(m.Title, subject.SubjectDescription, subject.SubjectCode)).ToList();
                    var topicCount = topics.Count(t => t.SubjectId == subject.Id);
                    var coveredTopics = topics
                        .Where(t => t.SubjectId == subject.Id)
                        .Count(t => materialDtos.Any(m =>
                            ContainsAny(m.TopicDescription, t.TopicDescription, t.TopicCode) ||
                            ContainsAny(m.Title, t.TopicDescription, t.TopicCode) ||
                            ContainsAny(m.SubjectDescription, subject.SubjectDescription, subject.SubjectCode)));
                    var topicPercent = topicCount == 0 ? 0 : Percent(coveredTopics, topicCount);
                    var evidencePercent = Math.Min(100, matched.Sum(x => x.WordCount) / 2500);
                    var mastery = Math.Min(100, (int)Math.Round((topicPercent * 0.65) + (evidencePercent * 0.35)));
                    return new
                    {
                        subject.SubjectCode,
                        subject.SubjectDescription,
                        topicCount,
                        coveredTopics,
                        evidenceMaterials = matched.Count,
                        words = matched.Sum(x => x.WordCount),
                        masteryPercentage = mastery,
                        signal = MasterySignal(mastery)
                    };
                })
                .OrderByDescending(x => x.masteryPercentage)
                .ThenBy(x => x.SubjectCode)
                .ToList();

            var scientificFields = materials
                .SelectMany(x => ExtractFields(x.SubjectDescription, x.TopicDescription, x.Title))
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    field = g.Key,
                    signals = g.Count(),
                    percentage = Percent(g.Count(), Math.Max(1, materials.Count))
                })
                .OrderByDescending(x => x.signals)
                .Take(12)
                .ToList();

            var avgMastery = subjectCoverage.Count == 0 ? 0 : (int)Math.Round(subjectCoverage.Average(x => x.masteryPercentage));
            return Ok(new
            {
                generatedAtUtc = DateTime.UtcNow,
                totals = new
                {
                    materials = materials.Count,
                    words = totalWords,
                    subjects = subjects.Count,
                    topics = topics.Count,
                    averageMasteryPercentage = avgMastery
                },
                sourceTypes,
                subjectCoverage,
                scientificFields,
                recentMaterials = materialDtos
                    .OrderByDescending(x => x.CreatedAtUtc)
                    .Take(12)
                    .Select(x => new { x.Title, x.SourceType, x.WordCount, x.CreatedAtUtc })
                    .ToList()
            });
        }

        private static int EstimateWords(int charCount)
            => charCount <= 0 ? 0 : Math.Max(1, (int)Math.Round(charCount / 6.0));

        private static int Percent(int value, int total)
            => total <= 0 ? 0 : Math.Clamp((int)Math.Round(value * 100.0 / total), 0, 100);

        private static bool ContainsAny(string? haystack, params string?[] needles)
        {
            var text = (haystack ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) return false;
            return needles.Any(needle =>
            {
                var value = (needle ?? string.Empty).Trim();
                return value.Length >= 3 && text.Contains(value, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static string MasterySignal(int percentage)
        {
            if (percentage >= 80) return "strong";
            if (percentage >= 50) return "developing";
            if (percentage >= 20) return "thin";
            return "not-yet-evidenced";
        }

        private static IEnumerable<string> ExtractFields(params string?[] values)
        {
            var text = string.Join(" ", values.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();
            var fields = new Dictionary<string, string[]>
            {
                ["Mechanical Engineering"] = new[] { "engine", "motor", "diesel", "mechanic", "vehicle", "hydraulic", "pneumatic", "machining", "fitter", "turner" },
                ["Electrical Systems"] = new[] { "electrical", "circuit", "voltage", "current", "battery", "alternator", "starter", "sensor" },
                ["Mathematics"] = new[] { "math", "algebra", "calculus", "geometry", "statistics", "equation", "measurement" },
                ["Science"] = new[] { "physics", "chemistry", "biology", "scientific", "experiment", "energy", "force" },
                ["Education Design"] = new[] { "curriculum", "outcome", "assessment", "instruction", "learner", "pedagogy", "lesson" },
                ["Information Technology"] = new[] { "software", "programming", "javascript", "react", "database", "api", "python", "code" },
                ["Safety and Compliance"] = new[] { "safety", "risk", "hazard", "compliance", "standard", "procedure", "inspection" },
                ["Communication"] = new[] { "communication", "report", "writing", "presentation", "language", "explain" }
            };

            foreach (var field in fields)
            {
                if (field.Value.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return field.Key;
                }
            }
        }
    }
}
