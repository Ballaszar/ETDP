using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ETD.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AssessmentComplianceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        private const string ExportsRoot = "C:\\ETDP\\ETDP\\Exports";
        private static readonly string GuidelinesPath = Path.Combine(ExportsRoot, "Assessment Guidelines", "ADHERING TO THE CURRICULUM ASSESSMENT SPECIFICATION DOCUMENT.docx");
        private static readonly string PoePath = Path.Combine(ExportsRoot, "poe", "Example POE.docx");
        private static readonly string RubricExamplePath = Path.Combine(ExportsRoot, "Rubrics", "Example_Rubric.docx");
        private static readonly string RubricsOutputDir = Path.Combine(ExportsRoot, "Rubrics");

        public AssessmentComplianceController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("status")]
        public IActionResult Status()
        {
            var guidelinePreview = System.IO.File.Exists(GuidelinesPath)
                ? ExtractText(GuidelinesPath, 2800)
                : string.Empty;
            var rubricHeaders = System.IO.File.Exists(RubricExamplePath)
                ? ExtractFirstTableHeaders(RubricExamplePath)
                : new List<string>();

            return Ok(new
            {
                guidelinesExists = System.IO.File.Exists(GuidelinesPath),
                poeExists = System.IO.File.Exists(PoePath),
                rubricExampleExists = System.IO.File.Exists(RubricExamplePath),
                guidelinePreview,
                rubricHeaders
            });
        }

        [HttpGet("download/{docType}")]
        public IActionResult Download([FromRoute] string docType)
        {
            string? path = docType.ToLowerInvariant() switch
            {
                "guidelines" => GuidelinesPath,
                "poe" => PoePath,
                "rubric-example" => RubricExamplePath,
                _ => null
            };

            if (path == null) return BadRequest("Unsupported document type.");
            if (!System.IO.File.Exists(path)) return NotFound("Document not found.");

            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Path.GetFileName(path));
        }

        [HttpPost("rubric/generate")]
        public IActionResult GenerateRubric([FromQuery] int qualificationId)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required.");

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var criteria = _context.AssessmentCriteria
                .Where(c => c.Topic != null && c.Topic.Subject != null && c.Topic.Subject.QualificationId == qualificationId)
                .Select(c => new
                {
                    AssessmentCriteriaId = c.Id,
                    AssessmentCriteriaDescription = c.Description,
                    TopicCode = c.Topic != null ? c.Topic.TopicCode : string.Empty,
                    TopicDescription = c.Topic != null ? c.Topic.TopicDescription : string.Empty,
                    SubjectCode = c.Topic != null && c.Topic.Subject != null ? c.Topic.Subject.SubjectCode : string.Empty,
                    SubjectDescription = c.Topic != null && c.Topic.Subject != null ? c.Topic.Subject.SubjectDescription : string.Empty
                })
                .OrderBy(x => x.SubjectCode)
                .ThenBy(x => x.TopicCode)
                .ThenBy(x => x.AssessmentCriteriaId)
                .ToList();

            if (criteria.Count == 0) return BadRequest("No assessment criteria found for this qualification.");

            var levelHeaders = ExtractCompetencyLevels(RubricExamplePath);
            if (levelHeaders.Count < 3)
            {
                levelHeaders = new List<string> { "Not Yet Competent", "Competent", "Highly Competent" };
            }

            Directory.CreateDirectory(RubricsOutputDir);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var qualificationSafe = MakeSafeFilePart(qualification.QualificationNumber, $"Q{qualificationId}");
            var fileName = $"Generated_Rubric_{qualificationSafe}_{timestamp}.csv";
            var filePath = Path.Combine(RubricsOutputDir, fileName);

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(";",
                "QualificationId",
                "QualificationNumber",
                "SubjectCode",
                "SubjectDescription",
                "TopicCode",
                "TopicDescription",
                "AssessmentCriteriaNumber",
                "AssessmentCriteriaDescription",
                EscapeCsv(levelHeaders[0]),
                EscapeCsv(levelHeaders[1]),
                EscapeCsv(levelHeaders[2])
            ));

            var topicCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in criteria)
            {
                var topicKey = string.IsNullOrWhiteSpace(c.TopicCode) ? "__TOPIC__" : c.TopicCode.Trim();
                var nextCounter = topicCounters.TryGetValue(topicKey, out var current) ? current + 1 : 1;
                topicCounters[topicKey] = nextCounter;
                var criteriaNumber = ResolveAssessmentCriteriaNumber(c.AssessmentCriteriaDescription, c.TopicCode, nextCounter);

                var nyc = $"Evidence is incomplete for AC {criteriaNumber}; major support still required.";
                var comp = $"Evidence meets AC {criteriaNumber} requirements to the expected standard.";
                var high = $"Evidence exceeds AC {criteriaNumber}; performance is independent, accurate, and consistent.";

                sb.AppendLine(string.Join(";",
                    qualificationId.ToString(),
                    EscapeCsv(qualification.QualificationNumber),
                    EscapeCsv(c.SubjectCode),
                    EscapeCsv(c.SubjectDescription),
                    EscapeCsv(c.TopicCode),
                    EscapeCsv(c.TopicDescription ?? string.Empty),
                    EscapeCsv(criteriaNumber),
                    EscapeCsv(c.AssessmentCriteriaDescription),
                    EscapeCsv(nyc),
                    EscapeCsv(comp),
                    EscapeCsv(high)
                ));
            }

            System.IO.File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

            return Ok(new
            {
                qualificationId,
                qualificationNumber = qualification.QualificationNumber,
                generatedFile = fileName,
                generatedPath = filePath,
                rows = criteria.Count,
                downloadUrl = $"/api/AssessmentCompliance/rubric/download?fileName={Uri.EscapeDataString(fileName)}",
                levelHeaders
            });
        }

        [HttpGet("rubric/download")]
        public IActionResult DownloadGeneratedRubric([FromQuery] string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return BadRequest("fileName is required.");
            var safeName = Path.GetFileName(fileName);
            var path = Path.Combine(RubricsOutputDir, safeName);
            if (!System.IO.File.Exists(path)) return NotFound("Generated rubric not found.");
            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, "text/csv", safeName);
        }

        private static string ExtractText(string docxPath, int maxChars)
        {
            try
            {
                using var doc = WordprocessingDocument.Open(docxPath, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return string.Empty;
                var text = string.Join("\n",
                    body.Descendants<Paragraph>()
                        .Select(p => p.InnerText?.Trim())
                        .Where(t => !string.IsNullOrWhiteSpace(t)));
                if (text.Length > maxChars)
                {
                    return text.Substring(0, maxChars) + "...";
                }
                return text;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static List<string> ExtractFirstTableHeaders(string docxPath)
        {
            try
            {
                using var doc = WordprocessingDocument.Open(docxPath, false);
                var table = doc.MainDocumentPart?.Document?.Body?.Descendants<Table>().FirstOrDefault();
                var firstRow = table?.Descendants<TableRow>().FirstOrDefault();
                var headers = firstRow?.Descendants<TableCell>()
                    .Select(c => c.InnerText?.Trim() ?? string.Empty)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList() ?? new List<string>();
                return headers;
            }
            catch
            {
                return new List<string>();
            }
        }

        private static List<string> ExtractCompetencyLevels(string docxPath)
        {
            var headers = ExtractFirstTableHeaders(docxPath);
            if (headers.Count == 0) return new List<string>();

            var lower = headers.Select(h => h.ToLowerInvariant()).ToList();
            var levelIndices = new List<int>();
            for (var i = 0; i < lower.Count; i++)
            {
                if (lower[i].Contains("competent") || lower[i].Contains("not yet") || lower[i].Contains("excellent") || lower[i].Contains("high"))
                {
                    levelIndices.Add(i);
                }
            }
            if (levelIndices.Count >= 3)
            {
                return levelIndices.Take(3).Select(i => headers[i]).ToList();
            }

            // fallback: use last three columns from template first row
            if (headers.Count >= 3)
            {
                return headers.Skip(headers.Count - 3).Take(3).ToList();
            }
            return new List<string>();
        }

        private static string EscapeCsv(string? value)
        {
            var s = value ?? string.Empty;
            if (s.Contains(";") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

        private static string MakeSafeFilePart(string? value, string fallback)
        {
            var v = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(v)) return fallback;
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                v = v.Replace(c, '_');
            }
            v = v.Replace(" ", "");
            return string.IsNullOrWhiteSpace(v) ? fallback : v;
        }

        private static string ResolveAssessmentCriteriaNumber(string? criteriaDescription, string? topicCode, int topicOrdinal)
        {
            var raw = (criteriaDescription ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var acToken = Regex.Match(raw, @"\bAC\s*[:\-]?\s*([A-Za-z0-9\.\-]+)\b", RegexOptions.IgnoreCase);
                if (acToken.Success)
                {
                    return acToken.Groups[1].Value.Trim();
                }

                var leadingNumber = Regex.Match(raw, @"^\s*([0-9]+(?:\.[0-9]+)*)\b");
                if (leadingNumber.Success)
                {
                    return leadingNumber.Groups[1].Value.Trim();
                }

                var anyNumber = Regex.Match(raw, @"\b([0-9]{2,}(?:\.[0-9]+)*)\b");
                if (anyNumber.Success)
                {
                    return anyNumber.Groups[1].Value.Trim();
                }
            }

            var topic = string.IsNullOrWhiteSpace(topicCode) ? "TOPIC" : topicCode.Trim();
            return $"{topic}-AC{topicOrdinal}";
        }
    }
}
