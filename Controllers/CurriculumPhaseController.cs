using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Utils;
using System.Globalization;
using System.IO;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CurriculumPhaseController : ControllerBase
    {
        private static readonly string[] TemplateRoots =
        {
            @"C:\ETDP\ETDP\Imports\ExcelCSVTemplates"
        };

        private readonly ApplicationDbContext _context;

        public CurriculumPhaseController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var items = _context.CurriculumPhases.Select(cp => new ETD.Api.DTOs.CurriculumPhaseDto
                {
                    Id = cp.Id,
                    Name = cp.Name,
                    Description = cp.Description,
                    Sequence = cp.Sequence
                }).ToList();
                return Ok(items);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("byQualification")]
        public IActionResult GetByQualification([FromQuery] int qualificationId)
        {
            var resolvedQualificationId = NormalizeQualificationId(qualificationId);
            if (resolvedQualificationId <= 0) return Ok(Array.Empty<object>());

            var qps = _context.QualificationPhases
                .Where(qp => qp.QualificationId == resolvedQualificationId)
                .ToList();
            var result = new List<object>();
            foreach (var qp in qps)
            {
                var cp = _context.CurriculumPhases.Find(qp.CurriculumPhaseId);
                if (cp != null)
                    result.Add(new { id = cp.Id, qualificationPhaseId = qp.Id, name = cp.Name, description = cp.Description, sequence = cp.Sequence });
            }
            return Ok(result);
        }

        [HttpGet("{id}")]
        public IActionResult Get(int id)
        {
            var cp = _context.CurriculumPhases.Find(id);
            if (cp == null) return NotFound();
            var dto = new ETD.Api.DTOs.CurriculumPhaseDto
            {
                Id = cp.Id,
                Name = cp.Name,
                Description = cp.Description,
                Sequence = cp.Sequence
            };
            return Ok(dto);
        }

        [HttpPost]
        public IActionResult Create(CurriculumPhase model)
        {
            _context.CurriculumPhases.Add(model);
            _context.SaveChanges();
            return CreatedAtAction(nameof(Get), new { id = model.Id }, model);
        }

        [HttpPost("import-csv")]
        public IActionResult ImportCsv([FromQuery] int? qualificationId, [FromQuery] string? csvPath)
        {
            var path = ResolveCsvPath(csvPath, "Phases.csv", "CurriculumPhases.csv");
            if (path == null)
            {
                if (!string.IsNullOrWhiteSpace(csvPath))
                {
                    return NotFound($"CSV file not found: {csvPath}");
                }
                return NotFound("Template not found: Phases.csv or CurriculumPhases.csv");
            }

            var rows = Csv.ReadSemicolonCsv(path);
            if (rows.Count == 0) return BadRequest("CSV is empty");

            var header = rows[0];

            int Col(params string[] names)
            {
                foreach (var name in names)
                {
                    var idx = Array.FindIndex(header, h => string.Equals(h?.Trim(), name, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) return idx;
                }

                var normalized = names.Select(NormalizeHeader).Where(v => v.Length > 0).ToHashSet();
                for (var i = 0; i < header.Length; i++)
                {
                    if (normalized.Contains(NormalizeHeader(header[i]))) return i;
                }

                return -1;
            }

            var cQualificationId = Col("QualificationId");
            var cQualificationCode = Col("Qualification Code", "Qaulification Code", "Qualification Number");
            var cLearningPhases = Col("Learning Phases", "Phase Name", "Name");
            var cPhasesCode = Col("Phases Code", "Phase Code");
            var cPhasesDescription = Col("Phases Description", "Description");
            var cSequence = Col("Sequence", "Order");

            var created = 0;
            var updated = 0;
            var linked = 0;
            var failed = 0;
            var details = new List<object>();
            var requestedQualificationId = NormalizeQualificationId(qualificationId.GetValueOrDefault());

            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Length == 0 || row.All(string.IsNullOrWhiteSpace)) continue;

                int resolvedQualificationId = requestedQualificationId;
                if (resolvedQualificationId <= 0 && cQualificationId >= 0 && cQualificationId < row.Length)
                {
                    var parsed = 0;
                    int.TryParse((row[cQualificationId] ?? "").Trim(), out parsed);
                    resolvedQualificationId = NormalizeQualificationId(parsed);
                }

                if (resolvedQualificationId <= 0 && cQualificationCode >= 0 && cQualificationCode < row.Length)
                {
                    var qCode = (row[cQualificationCode] ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(qCode))
                    {
                        var qualification = _context.Qualifications.FirstOrDefault(q => q.QualificationNumber == qCode);
                        if (qualification != null) resolvedQualificationId = qualification.Id;
                    }
                }

                if (resolvedQualificationId <= 0)
                {
                    failed++;
                    details.Add(new { row = i, reason = "Qualification not resolved" });
                    continue;
                }

                var learningPhase = cLearningPhases >= 0 && cLearningPhases < row.Length ? (row[cLearningPhases] ?? "").Trim() : "";
                var phaseCode = cPhasesCode >= 0 && cPhasesCode < row.Length ? (row[cPhasesCode] ?? "").Trim() : "";
                var phaseDescription = cPhasesDescription >= 0 && cPhasesDescription < row.Length ? (row[cPhasesDescription] ?? "").Trim() : "";
                var phaseName = !string.IsNullOrWhiteSpace(learningPhase) ? learningPhase : phaseCode;
                if (string.IsNullOrWhiteSpace(phaseName))
                {
                    failed++;
                    details.Add(new { row = i, reason = "Learning Phases/Phases Code missing" });
                    continue;
                }

                var sequenceRaw = cSequence >= 0 && cSequence < row.Length ? (row[cSequence] ?? "").Trim() : "";
                var sequence = ParseSequence(sequenceRaw, i);

                var phase = _context.CurriculumPhases.FirstOrDefault(p => p.Name == phaseName);
                var isCreate = phase == null;
                if (phase == null)
                {
                    phase = new CurriculumPhase
                    {
                        Name = phaseName,
                        Description = phaseDescription,
                        Sequence = sequence
                    };
                    _context.CurriculumPhases.Add(phase);
                }
                else
                {
                    phase.Description = phaseDescription;
                    phase.Sequence = sequence;
                }
                _context.SaveChanges();

                if (isCreate) created++;
                else updated++;

                var hasLink = _context.QualificationPhases.Any(qp => qp.QualificationId == resolvedQualificationId && qp.CurriculumPhaseId == phase.Id);
                if (!hasLink)
                {
                    _context.QualificationPhases.Add(new QualificationPhase
                    {
                        QualificationId = resolvedQualificationId,
                        CurriculumPhaseId = phase.Id
                    });
                    _context.SaveChanges();
                    linked++;
                }

                details.Add(new
                {
                    row = i,
                    status = isCreate ? "created" : "updated",
                    qualificationId = resolvedQualificationId,
                    phaseId = phase.Id,
                    phaseName = phase.Name
                });
            }

            return Ok(new { created, updated, linked, failed, details });
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, CurriculumPhase updated)
        {
            var item = _context.CurriculumPhases.Find(id);
            if (item == null) return NotFound();

            item.Name = updated.Name;
            item.Description = updated.Description;
            item.Sequence = updated.Sequence;
            _context.SaveChanges();
            return Ok(item);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.CurriculumPhases.Find(id);
            if (item == null) return NotFound();

            _context.CurriculumPhases.Remove(item);
            _context.SaveChanges();
            return NoContent();
        }

        private int NormalizeQualificationId(int qualificationRef)
        {
            if (qualificationRef <= 0) return 0;

            var byId = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationRef);
            if (byId != null) return byId.Id;

            var key = qualificationRef.ToString();
            var byNumber = _context.Qualifications.FirstOrDefault(q => q.QualificationNumber == key);
            return byNumber?.Id ?? 0;
        }

        private static string? ResolveTemplate(params string[] names)
        {
            foreach (var root in TemplateRoots)
            {
                foreach (var name in names)
                {
                    var path = Path.Combine(root, name);
                    if (System.IO.File.Exists(path)) return path;
                }
            }
            return null;
        }

        private static string? ResolveCsvPath(string? csvPath, params string[] templateNames)
        {
            if (!string.IsNullOrWhiteSpace(csvPath))
            {
                var fullPath = Path.GetFullPath(csvPath.Trim());
                if (System.IO.File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return ResolveTemplate(templateNames);
        }

        private static string NormalizeHeader(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var chars = value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }

        private static int ParseSequence(string raw, int fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            if (int.TryParse(raw.Trim(), out var n)) return n;

            var normalized = raw.Trim().Replace(" ", string.Empty);
            if (normalized.Contains(',') && !normalized.Contains('.'))
            {
                normalized = normalized.Replace(',', '.');
            }
            if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            {
                return Math.Max(1, (int)Math.Round(d, MidpointRounding.AwayFromZero));
            }

            return fallback;
        }
    }
}
