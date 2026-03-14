using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Utils;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubjectController : ControllerBase
    {
        private static readonly string[] TemplateRoots =
        {
            @"C:\ETDP\ETDP\Imports\ExcelCSVTemplates"
        };

        private readonly ApplicationDbContext _context;

        public SubjectController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("import-csv")]
        public IActionResult ImportCsv([FromQuery] int? qualificationId, [FromQuery] string? csvPath)
        {
            var path = ResolveCsvPath(csvPath, "Subjects.csv", "SubjectsV2.csv");
            if (path == null)
            {
                if (!string.IsNullOrWhiteSpace(csvPath))
                {
                    return NotFound($"CSV file not found: {csvPath}");
                }
                return NotFound("Template not found: Subjects.csv or SubjectsV2.csv");
            }

            var rows = Csv.ReadSemicolonCsv(path);
            if (rows.Count == 0) return BadRequest("CSV is empty");

            var header = rows[0];
            var cQualificationId = FindColumn(header, "QualificationId");
            var cQualificationCode = FindColumn(header, "Qualification Code", "Qualification Number", "Qaulification Code");
            var cLearningPhases = FindColumn(header, "Learning Phases", "Curriculum Phase");
            var cPhasesCode = FindColumn(header, "Phases Code", "PhasesCode");
            var cPhasesDescription = FindColumn(header, "Phases Description", "Phase Description");
            var cPhasesPurpose = FindColumn(header, "Phases Purpose");
            var cSubjectPurpose = FindColumn(header, "Subject Purpose", "Phases Purpose");
            var cSubjectCode = FindColumn(header, "SubjectCode", "Subject Code", "PhasesCode");
            var cSubjectDescription = FindColumn(header, "Subject Description");
            var cSubjectCredits = FindColumn(header, "Subject Credits");
            var cSubjectNqfLevel = FindColumn(header, "Subject NQF Level");
            var cSubjectPercentage = FindColumn(header, "Subject Percentage");
            var cCurriculumPhaseId = FindColumn(header, "Curriculum Phase Id", "CurriculumPhaseId");

            var created = 0;
            var updated = 0;
            var failed = 0;
            var details = new List<object>();
            var requestedQualificationId = NormalizeQualificationId(qualificationId.GetValueOrDefault());

            for (var i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Length == 0 || r.All(string.IsNullOrWhiteSpace)) continue;

                var resolvedQualificationId = requestedQualificationId;
                if (resolvedQualificationId <= 0)
                {
                    resolvedQualificationId = NormalizeQualificationId(ParseInt(Cell(r, cQualificationId)) ?? 0);
                }
                var qualificationCode = Cell(r, cQualificationCode).Trim();
                if (resolvedQualificationId <= 0 && !string.IsNullOrWhiteSpace(qualificationCode))
                {
                    var q = _context.Qualifications.FirstOrDefault(x => x.QualificationNumber == qualificationCode);
                    if (q != null) resolvedQualificationId = q.Id;
                }

                if (resolvedQualificationId <= 0)
                {
                    failed++;
                    details.Add(new { row = i, reason = "Qualification not resolved", qualificationCode });
                    continue;
                }

                var learningPhase = Cell(r, cLearningPhases).Trim();
                var phaseCode = Cell(r, cPhasesCode).Trim();
                var phaseDescription = Cell(r, cPhasesDescription).Trim();
                var phaseId = ResolvePhaseId(Cell(r, cCurriculumPhaseId), learningPhase, phaseCode, phaseDescription);

                var subjectCode = Cell(r, cSubjectCode).Trim();
                if (string.IsNullOrWhiteSpace(subjectCode))
                {
                    subjectCode = phaseCode;
                }

                var subjectDescription = Cell(r, cSubjectDescription).Trim();
                var subjectPurpose = Cell(r, cSubjectPurpose).Trim();
                if (string.IsNullOrWhiteSpace(subjectPurpose))
                {
                    subjectPurpose = Cell(r, cPhasesPurpose).Trim();
                }

                if (string.IsNullOrWhiteSpace(subjectCode) || string.IsNullOrWhiteSpace(subjectDescription))
                {
                    failed++;
                    details.Add(new
                    {
                        row = i,
                        reason = "Subject Code/Description missing",
                        subjectCode,
                        subjectDescription
                    });
                    continue;
                }

                var subjectCredits = ParseInt(Cell(r, cSubjectCredits));
                var subjectNqf = ParseInt(Cell(r, cSubjectNqfLevel));
                var subjectPercentage = ParseInt(Cell(r, cSubjectPercentage));

                var entity = _context.Subjects
                    .FirstOrDefault(s => s.QualificationId == resolvedQualificationId && s.SubjectCode == subjectCode);
                var isCreate = entity == null;
                if (entity == null)
                {
                    entity = new Subject
                    {
                        QualificationId = resolvedQualificationId,
                        SubjectCode = subjectCode
                    };
                    _context.Subjects.Add(entity);
                }

                entity.CurriculumPhaseId = phaseId;
                entity.SubjectPurpose = subjectPurpose;
                entity.SubjectDescription = subjectDescription;
                entity.SubjectCredits = subjectCredits;
                entity.SubjectNQFLevel = subjectNqf;
                entity.SubjectPercentage = subjectPercentage;

                _context.SaveChanges();

                if (isCreate)
                {
                    created++;
                }
                else
                {
                    updated++;
                }

                details.Add(new
                {
                    row = i,
                    status = isCreate ? "created" : "updated",
                    subjectId = entity.Id,
                    subjectCode = entity.SubjectCode,
                    curriculumPhaseId = entity.CurriculumPhaseId
                });
            }

            return Ok(new { created, updated, failed, details });
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var items = BuildSubjectQuery().ToList();
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
            var dto = BuildSubjectQuery().FirstOrDefault(s => s.Id == id);
            if (dto == null) return NotFound();
            return Ok(dto);
        }

        [HttpGet("byQualification")]
        public IActionResult GetByQualification([FromQuery] int qualificationId)
        {
            var resolvedQualificationId = NormalizeQualificationId(qualificationId);
            if (resolvedQualificationId <= 0) return Ok(Array.Empty<ETD.Api.DTOs.SubjectDto>());

            var items = BuildSubjectQuery()
                .Where(s => s.QualificationId == resolvedQualificationId)
                .ToList();
            return Ok(items);
        }

        [HttpGet("byPhase")]
        public IActionResult GetByPhase([FromQuery] int qualificationId, [FromQuery] int phaseId)
        {
            var resolvedQualificationId = NormalizeQualificationId(qualificationId);
            if (resolvedQualificationId <= 0) return Ok(Array.Empty<ETD.Api.DTOs.SubjectDto>());

            var items = BuildSubjectQuery()
                .Where(s => s.QualificationId == resolvedQualificationId && s.CurriculumPhaseId == phaseId)
                .ToList();
            return Ok(items);
        }

        [HttpPost]
        public IActionResult Create(ETD.Api.DTOs.CreateSubjectDto model)
        {
            var entity = new Subject
            {
                SubjectPurpose = model.SubjectPurpose,
                SubjectCode = model.PhasesCode,
                SubjectDescription = model.SubjectDescription,
                SubjectCredits = model.SubjectCredits,
                SubjectNQFLevel = model.SubjectNQFLevel,
                SubjectPercentage = model.SubjectPercentage,
                QualificationId = model.QualificationId,
                CurriculumPhaseId = model.CurriculumPhaseId
            };
            _context.Subjects.Add(entity);
            _context.SaveChanges();
            return CreatedAtAction(nameof(Get), new { id = entity.Id }, entity);
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, ETD.Api.DTOs.UpdateSubjectDto updated)
        {
            var item = _context.Subjects.Find(id);
            if (item == null) return NotFound();

            item.SubjectPurpose = updated.SubjectPurpose;
            item.SubjectCode = updated.PhasesCode;
            item.SubjectDescription = updated.SubjectDescription;
            item.SubjectCredits = updated.SubjectCredits;
            item.SubjectNQFLevel = updated.SubjectNQFLevel;
            item.SubjectPercentage = updated.SubjectPercentage;
            item.QualificationId = updated.QualificationId;
            item.CurriculumPhaseId = updated.CurriculumPhaseId;

            _context.SaveChanges();
            return Ok(item);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.Subjects.Find(id);
            if (item == null) return NotFound();

            _context.Subjects.Remove(item);
            _context.SaveChanges();
            return NoContent();
        }

        private IQueryable<ETD.Api.DTOs.SubjectDto> BuildSubjectQuery()
        {
            return _context.Subjects
                .Select(s => new ETD.Api.DTOs.SubjectDto
                {
                    Id = s.Id,
                    SubjectPurpose = s.SubjectPurpose,
                    SubjectCode = s.SubjectCode,
                    PhasesCode = s.SubjectCode,
                    SubjectDescription = s.SubjectDescription,
                    QualificationCode = s.Qualification != null ? s.Qualification.QualificationNumber : string.Empty,
                    LearningPhases = s.CurriculumPhase != null ? s.CurriculumPhase.Name : string.Empty,
                    SubjectCredits = s.SubjectCredits,
                    SubjectNQFLevel = s.SubjectNQFLevel,
                    SubjectPercentage = s.SubjectPercentage,
                    QualificationId = s.QualificationId,
                    CurriculumPhaseId = s.CurriculumPhaseId
                });
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

        private int ResolvePhaseId(string phaseIdRaw, string learningPhase, string phaseCode, string phaseDescription)
        {
            var explicitPhaseId = ParseInt(phaseIdRaw) ?? 0;
            if (explicitPhaseId > 0)
            {
                var explicitPhase = _context.CurriculumPhases.Find(explicitPhaseId);
                if (explicitPhase != null) return explicitPhase.Id;
            }

            CurriculumPhase? phase = null;
            if (!string.IsNullOrWhiteSpace(learningPhase))
            {
                phase = _context.CurriculumPhases.FirstOrDefault(p => p.Name == learningPhase);
            }
            if (phase == null && !string.IsNullOrWhiteSpace(phaseCode))
            {
                phase = _context.CurriculumPhases.FirstOrDefault(p => p.Name == phaseCode);
            }
            if (phase == null && !string.IsNullOrWhiteSpace(phaseDescription))
            {
                phase = _context.CurriculumPhases.FirstOrDefault(p => p.Description == phaseDescription);
            }

            if (phase == null)
            {
                var name = !string.IsNullOrWhiteSpace(learningPhase)
                    ? learningPhase
                    : (!string.IsNullOrWhiteSpace(phaseCode) ? phaseCode : "Default Phase");
                phase = new CurriculumPhase
                {
                    Name = name,
                    Description = phaseDescription,
                    Sequence = _context.CurriculumPhases.Any() ? _context.CurriculumPhases.Max(p => p.Sequence) + 1 : 1
                };
                _context.CurriculumPhases.Add(phase);
                _context.SaveChanges();
            }

            return phase.Id;
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

        private static int FindColumn(string[] header, params string[] names)
        {
            foreach (var name in names)
            {
                var idx = Array.FindIndex(header, h => string.Equals(h?.Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) return idx;
            }

            var normalizedNames = names.Select(NormalizeHeader).Where(n => n.Length > 0).ToHashSet();
            for (var i = 0; i < header.Length; i++)
            {
                if (normalizedNames.Contains(NormalizeHeader(header[i])))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string NormalizeHeader(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var chars = value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }

        private static string Cell(string[] row, int index)
        {
            if (index < 0 || index >= row.Length) return string.Empty;
            return row[index] ?? string.Empty;
        }

        private static int? ParseInt(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim();
            if (int.TryParse(s, out var direct)) return direct;

            var number = ParseFlexibleNumber(s);
            if (!number.HasValue) return null;
            return (int)Math.Round(number.Value, MidpointRounding.AwayFromZero);
        }

        private static double? ParseFlexibleNumber(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var s = raw.Trim().Replace(" ", string.Empty);
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var direct))
            {
                return direct;
            }

            if (s.Contains(',') && !s.Contains('.'))
            {
                s = s.Replace(',', '.');
            }
            else if (s.Contains(',') && s.Contains('.'))
            {
                s = s.Replace(",", string.Empty);
            }

            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var normalized))
            {
                return normalized;
            }

            var match = Regex.Match(raw, @"-?\d+(?:[\.,]\d+)?");
            if (match.Success)
            {
                var token = match.Value.Replace(',', '.');
                if (double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out var tokenValue))
                {
                    return tokenValue;
                }
            }

            return null;
        }
    }
}
