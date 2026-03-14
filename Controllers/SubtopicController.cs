using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using ETD.Api.Models;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubtopicController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public SubtopicController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var items = _context.Subtopics.Select(st => new ETD.Api.DTOs.SubtopicDto
                {
                    Id = st.Id,
                    Name = st.Name,
                    Description = st.Description,
                    Order = st.Order,
                    TopicId = st.TopicId
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
            var st = _context.Subtopics.Find(id);
            if (st == null) return NotFound();
            var dto = new ETD.Api.DTOs.SubtopicDto
            {
                Id = st.Id,
                Name = st.Name,
                Description = st.Description,
                Order = st.Order,
                TopicId = st.TopicId
            };
            return Ok(dto);
        }

        [HttpPost]
        public IActionResult Create(ETD.Api.DTOs.CreateSubtopicDto dto)
        {
            var model = new Subtopic
            {
                Name = dto.Name,
                Description = dto.Description,
                Order = dto.Order,
                TopicId = dto.TopicId
            };
            _context.Subtopics.Add(model);
            _context.SaveChanges();
            return CreatedAtAction(nameof(Get), new { id = model.Id }, model);
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, ETD.Api.DTOs.UpdateSubtopicDto dto)
        {
            var item = _context.Subtopics.Find(id);
            if (item == null) return NotFound();
            item.Name = dto.Name;
            item.Description = dto.Description;
            item.Order = dto.Order;
            item.TopicId = dto.TopicId;
            _context.SaveChanges();
            return Ok(item);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.Subtopics.Find(id);
            if (item == null) return NotFound();

            _context.Subtopics.Remove(item);
            _context.SaveChanges();
            return NoContent();
        }
    }
}
