using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using ETD.Api.Models;
using System.Linq;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QualificationPhaseController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public QualificationPhaseController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("{qualificationId}")]
        public IActionResult GetByQualification(int qualificationId)
        {
            var resolvedQualificationId = NormalizeQualificationId(qualificationId);
            if (resolvedQualificationId <= 0)
            {
                return Ok(Array.Empty<object>());
            }

            var items = _context.QualificationPhases
                .Where(qp => qp.QualificationId == resolvedQualificationId)
                .Select(qp => new
                {
                    id = qp.Id,
                    qualificationId = qp.QualificationId,
                    curriculumPhaseId = qp.CurriculumPhaseId
                })
                .ToList();
            return Ok(items);
        }

        [HttpPost]
        public IActionResult Create([FromBody] QualificationPhase model)
        {
            var normalizedQualificationId = NormalizeQualificationId(model.QualificationId);
            if (normalizedQualificationId <= 0)
            {
                return BadRequest("Qualification not resolved.");
            }

            model.QualificationId = normalizedQualificationId;
            var exists = _context.QualificationPhases
                .Any(qp => qp.QualificationId == model.QualificationId && qp.CurriculumPhaseId == model.CurriculumPhaseId);
            if (exists)
            {
                var existing = _context.QualificationPhases
                    .First(qp => qp.QualificationId == model.QualificationId && qp.CurriculumPhaseId == model.CurriculumPhaseId);
                return Ok(new
                {
                    id = existing.Id,
                    qualificationId = existing.QualificationId,
                    curriculumPhaseId = existing.CurriculumPhaseId
                });
            }

            _context.QualificationPhases.Add(model);
            _context.SaveChanges();
            return CreatedAtAction(nameof(GetByQualification), new { qualificationId = model.QualificationId }, new
            {
                id = model.Id,
                qualificationId = model.QualificationId,
                curriculumPhaseId = model.CurriculumPhaseId
            });
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.QualificationPhases.Find(id);
            if (item == null) return NotFound();
            _context.QualificationPhases.Remove(item);
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
    }
}
