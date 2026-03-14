using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Utils;
using System.IO;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OutcomeController : ControllerBase
    {
        private static readonly string[] TemplateRoots =
        {
            @"C:\ETDP\ETDP\Imports\ExcelCSVTemplates"
        };

        private readonly ApplicationDbContext _context;

        public OutcomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("import-csv")]
        public IActionResult ImportCsv([FromQuery] int? qualificationId)
        {
            var path = ResolveTemplate("Outcomes.csv", "OutcomesV2.csv");
            if (path == null) return NotFound("Template not found: Outcomes.csv or OutcomesV2.csv");

            var rows = Csv.ReadSemicolonCsv(path);
            if (rows.Count == 0) return BadRequest("CSV is empty");

            var header = rows[0];
            int Col(string name) => Array.FindIndex(header, h => string.Equals(h?.Trim(), name, StringComparison.OrdinalIgnoreCase));

            var cQualificationId = Col("QualificationId");
            var cQualificationCode = Col("Qualification Code");
            var cPhasesCode = Col("PhasesCode");
            var cSubjectCode = Col("Subject Code");
            var cSubjectDescription = Col("Subject Description");
            var cOutcomeCode = Col("Outcome Code");
            var cOutcomeDescription = Col("Outcome Description");
            var cOutcomeOrder = Col("Outcome Order");

            int created = 0, failed = 0;
            var details = new List<object>();
            var seenKeys = new HashSet<string>(
                _context.Outcomes
                    .Select(x => $"{x.SubjectId}|{x.OutcomeCode.ToLower()}")
                    .ToList());
            var requestedQualificationId = NormalizeQualificationId(qualificationId.GetValueOrDefault());

            for (int i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Length == 0) continue;

                int resolvedQualificationId = requestedQualificationId;
                if (resolvedQualificationId <= 0 && cQualificationId >= 0 && cQualificationId < r.Length)
                {
                    var parsed = 0;
                    int.TryParse(r[cQualificationId], out parsed);
                    resolvedQualificationId = NormalizeQualificationId(parsed);
                }
                if (resolvedQualificationId <= 0 && cQualificationCode >= 0 && cQualificationCode < r.Length)
                {
                    var qualificationCode = (r[cQualificationCode] ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(qualificationCode))
                    {
                        var q = _context.Qualifications.FirstOrDefault(x => x.QualificationNumber == qualificationCode);
                        if (q != null) resolvedQualificationId = q.Id;
                    }
                }

                var phasesCode = cPhasesCode >= 0 && cPhasesCode < r.Length ? (r[cPhasesCode] ?? "").Trim() : "";
                var subjectCode = cSubjectCode >= 0 && cSubjectCode < r.Length ? (r[cSubjectCode] ?? "").Trim() : "";
                var subjectDescription = cSubjectDescription >= 0 && cSubjectDescription < r.Length ? (r[cSubjectDescription] ?? "").Trim() : "";
                var lookupCode = !string.IsNullOrWhiteSpace(phasesCode) ? phasesCode : subjectCode;

                var outcomeCode = cOutcomeCode >= 0 && cOutcomeCode < r.Length ? (r[cOutcomeCode] ?? "").Trim() : "";
                var outcomeDescription = cOutcomeDescription >= 0 && cOutcomeDescription < r.Length ? (r[cOutcomeDescription] ?? "").Trim() : "";
                var order = cOutcomeOrder >= 0 && cOutcomeOrder < r.Length && int.TryParse(r[cOutcomeOrder], out var o) ? o : (int?)null;

                if (string.IsNullOrWhiteSpace(outcomeCode) || string.IsNullOrWhiteSpace(outcomeDescription))
                {
                    failed++;
                    details.Add(new { row = i, reason = "Outcome Code/Description missing" });
                    continue;
                }

                Subject? subject = null;
                if (resolvedQualificationId > 0 && !string.IsNullOrWhiteSpace(lookupCode))
                {
                    subject = _context.Subjects.FirstOrDefault(s => s.QualificationId == resolvedQualificationId && s.SubjectCode == lookupCode);
                }
                if (subject == null && !string.IsNullOrWhiteSpace(lookupCode))
                {
                    subject = _context.Subjects
                        .Where(s => s.SubjectCode == lookupCode)
                        .OrderByDescending(s => s.Id)
                        .FirstOrDefault();
                }
                if (subject == null && resolvedQualificationId > 0 && !string.IsNullOrWhiteSpace(subjectDescription))
                {
                    subject = _context.Subjects.FirstOrDefault(s => s.QualificationId == resolvedQualificationId && s.SubjectDescription == subjectDescription);
                }

                if (subject == null)
                {
                    failed++;
                    details.Add(new { row = i, reason = "Subject not resolved", qualificationId = resolvedQualificationId, phasesCode, subjectCode, subjectDescription });
                    continue;
                }

                var dedupeKey = $"{subject.Id}|{outcomeCode.ToLower()}";
                if (seenKeys.Contains(dedupeKey))
                {
                    failed++;
                    details.Add(new { row = i, reason = "Duplicate outcome code for subject", subjectId = subject.Id, outcomeCode });
                    continue;
                }

                var model = new Outcome
                {
                    SubjectId = subject.Id,
                    OutcomeCode = outcomeCode,
                    OutcomeDescription = outcomeDescription,
                    Order = order
                };
                _context.Outcomes.Add(model);
                seenKeys.Add(dedupeKey);
                created++;
                details.Add(new { row = i, status = "created", subjectId = subject.Id, outcomeCode });
            }

            _context.SaveChanges();
            return Ok(new { created, failed, details });
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

        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var items = _context.Outcomes
                    .Select(o => new ETD.Api.DTOs.OutcomeDto
                    {
                        Id = o.Id,
                        SubjectId = o.SubjectId,
                        QualificationId = o.Subject != null ? o.Subject.QualificationId : 0,
                        SubjectCode = o.Subject != null ? o.Subject.SubjectCode : string.Empty,
                        OutcomeCode = o.OutcomeCode,
                        OutcomeDescription = o.OutcomeDescription,
                        Order = o.Order
                    })
                    .ToList();
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
            var o = _context.Outcomes.Find(id);
            if (o == null) return NotFound();
            var dto = new ETD.Api.DTOs.OutcomeDto
            {
                Id = o.Id,
                SubjectId = o.SubjectId,
                QualificationId = o.Subject != null ? o.Subject.QualificationId : 0,
                SubjectCode = o.Subject != null ? o.Subject.SubjectCode : string.Empty,
                OutcomeCode = o.OutcomeCode,
                OutcomeDescription = o.OutcomeDescription,
                Order = o.Order
            };
            return Ok(dto);
        }

        // GET: api/Outcome/bySubject?subjectId=5
        [HttpGet("bySubject")]
        public IActionResult GetBySubject([FromQuery] int subjectId)
        {
            var items = _context.Outcomes
                .Where(o => o.SubjectId == subjectId)
                .Select(o => new ETD.Api.DTOs.OutcomeDto
                {
                    Id = o.Id,
                    SubjectId = o.SubjectId,
                    QualificationId = o.Subject != null ? o.Subject.QualificationId : 0,
                    SubjectCode = o.Subject != null ? o.Subject.SubjectCode : string.Empty,
                    OutcomeCode = o.OutcomeCode,
                    OutcomeDescription = o.OutcomeDescription,
                    Order = o.Order
                })
                .ToList();
            return Ok(items);
        }

        // GET: api/Outcome/byQualification?qualificationId=7
        [HttpGet("byQualification")]
        public IActionResult GetByQualification([FromQuery] int qualificationId)
        {
            var resolvedQualificationId = NormalizeQualificationId(qualificationId);
            if (resolvedQualificationId <= 0) return Ok(Array.Empty<ETD.Api.DTOs.OutcomeDto>());

            var items = _context.Outcomes
                .Where(o => o.Subject != null && o.Subject.QualificationId == resolvedQualificationId)
                .Select(o => new ETD.Api.DTOs.OutcomeDto
                {
                    Id = o.Id,
                    SubjectId = o.SubjectId,
                    QualificationId = o.Subject != null ? o.Subject.QualificationId : 0,
                    SubjectCode = o.Subject != null ? o.Subject.SubjectCode : string.Empty,
                    OutcomeCode = o.OutcomeCode,
                    OutcomeDescription = o.OutcomeDescription,
                    Order = o.Order
                })
                .ToList();
            return Ok(items);
        }

        [HttpPost]
        public IActionResult Create(ETD.Api.DTOs.CreateOutcomeDto dto)
        {
            var subject = _context.Subjects.Find(dto.SubjectId);
            if (subject == null) return BadRequest("Subject not found");

            var model = new Outcome
            {
                SubjectId = dto.SubjectId,
                OutcomeCode = dto.OutcomeCode,
                OutcomeDescription = dto.OutcomeDescription,
                Order = dto.Order
            };
            _context.Outcomes.Add(model);
            _context.SaveChanges();
            var result = new ETD.Api.DTOs.OutcomeDto
            {
                Id = model.Id,
                SubjectId = model.SubjectId,
                QualificationId = subject.QualificationId,
                SubjectCode = subject.SubjectCode,
                OutcomeCode = model.OutcomeCode,
                OutcomeDescription = model.OutcomeDescription,
                Order = model.Order
            };
            return CreatedAtAction(nameof(Get), new { id = model.Id }, result);
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, ETD.Api.DTOs.UpdateOutcomeDto dto)
        {
            var item = _context.Outcomes.Find(id);
            if (item == null) return NotFound();

            var subject = _context.Subjects.Find(dto.SubjectId);
            if (subject == null) return BadRequest("Subject not found");

            item.SubjectId = dto.SubjectId;
            item.OutcomeCode = dto.OutcomeCode;
            item.OutcomeDescription = dto.OutcomeDescription;
            item.Order = dto.Order;
            _context.SaveChanges();
            var result = new ETD.Api.DTOs.OutcomeDto
            {
                Id = item.Id,
                SubjectId = item.SubjectId,
                QualificationId = subject.QualificationId,
                SubjectCode = subject.SubjectCode,
                OutcomeCode = item.OutcomeCode,
                OutcomeDescription = item.OutcomeDescription,
                Order = item.Order
            };
            return Ok(result);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.Outcomes.Find(id);
            if (item == null) return NotFound();

            _context.Outcomes.Remove(item);
            _context.SaveChanges();
            return NoContent();
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
    }
}
