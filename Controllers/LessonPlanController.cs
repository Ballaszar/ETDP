using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using ETD.Api.Models;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LessonPlanController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public LessonPlanController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var items = _context.LessonPlans.Select(lp => new ETD.Api.DTOs.LessonPlanDto
                {
                    Id = lp.Id,
                    AssessmentCriteriaId = lp.AssessmentCriteriaId,
                    Title = lp.Title,
                    SortOrder = lp.SortOrder,
                    Date = lp.Date,
                    DurationMinutes = lp.DurationMinutes,
                    Content = lp.Content
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
            var lp = _context.LessonPlans.Find(id);
            if (lp == null) return NotFound();
            var dto = new ETD.Api.DTOs.LessonPlanDto
            {
                Id = lp.Id,
                AssessmentCriteriaId = lp.AssessmentCriteriaId,
                Title = lp.Title,
                SortOrder = lp.SortOrder,
                Date = lp.Date,
                DurationMinutes = lp.DurationMinutes,
                Content = lp.Content
            };
            return Ok(dto);
        }

        // GET: api/LessonPlan/byCriteria?criteriaId=7
        [HttpGet("byCriteria")]
        public IActionResult GetByCriteria([FromQuery] int criteriaId)
        {
            var items = _context.LessonPlans
                .Where(lp => lp.AssessmentCriteriaId == criteriaId)
                .Select(lp => new ETD.Api.DTOs.LessonPlanDto
                {
                    Id = lp.Id,
                    AssessmentCriteriaId = lp.AssessmentCriteriaId,
                    Title = lp.Title,
                    SortOrder = lp.SortOrder,
                    Date = lp.Date,
                    DurationMinutes = lp.DurationMinutes,
                    Content = lp.Content
                })
                .ToList();
            return Ok(items);
        }

        // GET: api/LessonPlan/byQualification?qualificationId=7
        [HttpGet("byQualification")]
        public IActionResult GetByQualification([FromQuery] int qualificationId)
        {
            var items = _context.LessonPlans
                .Where(lp =>
                    lp.AssessmentCriteria != null &&
                    lp.AssessmentCriteria.Topic != null &&
                    lp.AssessmentCriteria.Topic.Subject != null &&
                    lp.AssessmentCriteria.Topic.Subject.QualificationId == qualificationId)
                .Select(lp => new ETD.Api.DTOs.LessonPlanDto
                {
                    Id = lp.Id,
                    AssessmentCriteriaId = lp.AssessmentCriteriaId,
                    Title = lp.Title,
                    SortOrder = lp.SortOrder,
                    Date = lp.Date,
                    DurationMinutes = lp.DurationMinutes,
                    Content = lp.Content
                })
                .ToList();
            return Ok(items);
        }

        [HttpPost]
        public IActionResult Create(ETD.Api.DTOs.CreateLessonPlanDto dto)
        {
            var model = new LessonPlan
            {
                AssessmentCriteriaId = dto.AssessmentCriteriaId,
                Title = dto.Title,
                SortOrder = dto.SortOrder,
                Date = dto.Date,
                DurationMinutes = dto.DurationMinutes,
                Content = dto.Content
            };
            _context.LessonPlans.Add(model);
            _context.SaveChanges();
            return CreatedAtAction(nameof(Get), new { id = model.Id }, model);
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, ETD.Api.DTOs.UpdateLessonPlanDto dto)
        {
            var item = _context.LessonPlans.Find(id);
            if (item == null) return NotFound();
            item.Title = dto.Title;
            item.SortOrder = dto.SortOrder;
            item.Date = dto.Date;
            item.DurationMinutes = dto.DurationMinutes;
            item.Content = dto.Content;
            _context.SaveChanges();
            return Ok(item);
        }

        [HttpPost("reorder")]
        public IActionResult Reorder([FromBody] ReorderLessonPlansRequest req)
        {
            if (req.LessonPlanIds == null || req.LessonPlanIds.Count == 0)
            {
                return BadRequest("No lesson plans provided.");
            }

            var ids = req.LessonPlanIds.Distinct().ToList();
            var plans = _context.LessonPlans.Where(lp => ids.Contains(lp.Id)).ToList();
            if (plans.Count != ids.Count)
            {
                return BadRequest("One or more lesson plans were not found.");
            }

            var order = 1;
            foreach (var id in req.LessonPlanIds)
            {
                var plan = plans.First(p => p.Id == id);
                plan.SortOrder = order++;
            }

            _context.SaveChanges();
            return Ok(new { updated = req.LessonPlanIds.Count });
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.LessonPlans.Find(id);
            if (item == null) return NotFound();

            _context.LessonPlans.Remove(item);
            _context.SaveChanges();
            return NoContent();
        }

        public class ReorderLessonPlansRequest
        {
            public List<int> LessonPlanIds { get; set; } = new();
        }
    }
}
