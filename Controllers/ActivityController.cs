using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using ETD.Api.Models;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ActivityController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ActivityController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var items = _context.Activities.Select(a => new ETD.Api.DTOs.ActivityDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    Description = a.Description,
                    Order = a.Order,
                    SubtopicId = a.SubtopicId
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
            var a = _context.Activities.Find(id);
            if (a == null) return NotFound();
            var dto = new ETD.Api.DTOs.ActivityDto
            {
                Id = a.Id,
                Name = a.Name,
                Description = a.Description,
                Order = a.Order,
                SubtopicId = a.SubtopicId
            };
            return Ok(dto);
        }

        [HttpPost]
        public IActionResult Create(ETD.Api.DTOs.CreateActivityDto dto)
        {
            var model = new Activity
            {
                Name = dto.Name,
                Description = dto.Description,
                Order = dto.Order,
                SubtopicId = dto.SubtopicId
            };
            _context.Activities.Add(model);
            _context.SaveChanges();
            return CreatedAtAction(nameof(Get), new { id = model.Id }, model);
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, ETD.Api.DTOs.UpdateActivityDto dto)
        {
            var item = _context.Activities.Find(id);
            if (item == null) return NotFound();
            item.Name = dto.Name;
            item.Description = dto.Description;
            item.Order = dto.Order;
            item.SubtopicId = dto.SubtopicId;
            _context.SaveChanges();
            return Ok(item);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.Activities.Find(id);
            if (item == null) return NotFound();

            _context.Activities.Remove(item);
            _context.SaveChanges();
            return NoContent();
        }
    }
}
