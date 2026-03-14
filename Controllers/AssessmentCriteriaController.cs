using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using ETD.Api.Models;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AssessmentCriteriaController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AssessmentCriteriaController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var items = _context.AssessmentCriteria.Select(c => new ETD.Api.DTOs.AssessmentCriteriaDto
                {
                    Id = c.Id,
                    Description = c.Description,
                    CriteriaType = c.CriteriaType,
                    Weight = c.Weight,
                    TopicId = c.TopicId
                }).ToList();
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
            var c = _context.AssessmentCriteria.Find(id);
            if (c == null) return NotFound();
            var dto = new ETD.Api.DTOs.AssessmentCriteriaDto
            {
                Id = c.Id,
                Description = c.Description,
                CriteriaType = c.CriteriaType,
                Weight = c.Weight,
                TopicId = c.TopicId
            };
            return Ok(dto);
        }

        // GET: api/AssessmentCriteria/byTopic?topicId=7
        [HttpGet("byTopic")]
        public IActionResult GetByTopic([FromQuery] int topicId)
        {
            var items = _context.AssessmentCriteria
                .Where(c => c.TopicId == topicId)
                .Select(c => new ETD.Api.DTOs.AssessmentCriteriaDto
                {
                    Id = c.Id,
                    Description = c.Description,
                    CriteriaType = c.CriteriaType,
                    Weight = c.Weight,
                    TopicId = c.TopicId
                })
                .ToList();
            return Ok(items);
        }

        // GET: api/AssessmentCriteria/byQualification?qualificationId=7
        [HttpGet("byQualification")]
        public IActionResult GetByQualification([FromQuery] int qualificationId)
        {
            var items = _context.AssessmentCriteria
                .Where(c =>
                    c.Topic != null &&
                    c.Topic.Subject != null &&
                    c.Topic.Subject.QualificationId == qualificationId)
                .Select(c => new ETD.Api.DTOs.AssessmentCriteriaDto
                {
                    Id = c.Id,
                    Description = c.Description,
                    CriteriaType = c.CriteriaType,
                    Weight = c.Weight,
                    TopicId = c.TopicId
                })
                .ToList();
            return Ok(items);
        }

        [HttpPost]
        public IActionResult Create(ETD.Api.DTOs.CreateAssessmentCriteriaDto dto)
        {
            var model = new AssessmentCriteria
            {
                Description = dto.Description,
                CriteriaType = dto.CriteriaType,
                Weight = dto.Weight,
                TopicId = dto.TopicId
            };
            _context.AssessmentCriteria.Add(model);
            _context.SaveChanges();
            return CreatedAtAction(nameof(Get), new { id = model.Id }, model);
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, AssessmentCriteria updated)
        {
            var item = _context.AssessmentCriteria.Find(id);
            if (item == null) return NotFound();

            item.Description = updated.Description;
            item.TopicId = updated.TopicId;

            _context.SaveChanges();
            return Ok(item);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.AssessmentCriteria.Find(id);
            if (item == null) return NotFound();

            _context.AssessmentCriteria.Remove(item);
            _context.SaveChanges();
            return NoContent();
        }
    }
}
