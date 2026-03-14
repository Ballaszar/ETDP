using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using ETD.Api.Models;
using System.Linq;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DemographicsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public DemographicsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var items = _context.Demographics
                    .Select(d => new ETD.Api.DTOs.DemographicsDto
                    {
                        Id = d.Id,
                        QualificationId = d.QualificationId,
                        AgeGroup = d.AgeGroup,
                        Region = d.Region,
                        NumberOfMales = d.Males,
                        NumberOfFemales = d.Females,
                        NumberAfrican = d.African,
                        NumberWhites = d.Whites,
                        NumberColoureds = d.Coloureds,
                        NumberAsian = d.Asian,
                        NumberWithDisabilities = d.WithDisabilities,
                        Other = d.Other,
                        Total = d.Total,
                        TotalNumberOfStudents = d.TotalNumberOfStudents
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
            var item = _context.Demographics.Find(id);
            if (item == null) return NotFound();
            var dto = new ETD.Api.DTOs.DemographicsDto
            {
                Id = item.Id,
                QualificationId = item.QualificationId,
                AgeGroup = item.AgeGroup,
                Region = item.Region,
                NumberOfMales = item.Males,
                NumberOfFemales = item.Females,
                NumberAfrican = item.African,
                NumberWhites = item.Whites,
                NumberColoureds = item.Coloureds,
                NumberAsian = item.Asian,
                NumberWithDisabilities = item.WithDisabilities,
                Other = item.Other,
                Total = item.Total,
                TotalNumberOfStudents = item.TotalNumberOfStudents
            };
            return Ok(dto);
        }

        // ⭐ NEW ENDPOINT — REQUIRED BY FRONTEND
        [HttpGet("byQualification")]
        public IActionResult GetByQualification([FromQuery] int qualificationId)
        {
            var items = _context.Demographics
                .Where(d => d.QualificationId == qualificationId)
                .Select(d => new ETD.Api.DTOs.DemographicsDto
                {
                    Id = d.Id,
                    QualificationId = d.QualificationId,
                    AgeGroup = d.AgeGroup,
                    Region = d.Region,
                    NumberOfMales = d.Males,
                    NumberOfFemales = d.Females,
                    NumberAfrican = d.African,
                    NumberWhites = d.Whites,
                    NumberColoureds = d.Coloureds,
                    NumberAsian = d.Asian,
                    NumberWithDisabilities = d.WithDisabilities,
                    Other = d.Other,
                    Total = d.Total,
                    TotalNumberOfStudents = d.TotalNumberOfStudents
                })
                .ToList();
            return Ok(items);
        }


        [HttpPost]
        public IActionResult Create(ETD.Api.DTOs.CreateDemographicsDto dto)
        {
            if (dto.QualificationId <= 0 || !_context.Qualifications.Any(q => q.Id == dto.QualificationId))
            {
                return BadRequest("A valid QualificationId is required.");
            }
            var model = new Demographics
            {
                QualificationId = dto.QualificationId,
                AgeGroup = dto.AgeGroup,
                Region = dto.Region,
                Males = dto.NumberOfMales,
                Females = dto.NumberOfFemales,
                African = dto.NumberAfrican,
                Whites = dto.NumberWhites,
                Coloureds = dto.NumberColoureds,
                Asian = dto.NumberAsian,
                WithDisabilities = dto.NumberWithDisabilities,
                Other = dto.Other,
                Total = dto.Total,
                TotalNumberOfStudents = dto.TotalNumberOfStudents
            };
            _context.Demographics.Add(model);
            _context.SaveChanges();
            return CreatedAtAction(nameof(Get), new { id = model.Id }, model);
        }


        [HttpPut("{id}")]
        public IActionResult Update(int id, ETD.Api.DTOs.UpdateDemographicsDto dto)
        {
            var item = _context.Demographics.Find(id);
            if (item == null) return NotFound();
            item.AgeGroup = dto.AgeGroup;
            item.Region = dto.Region;
            item.Males = dto.NumberOfMales;
            item.Females = dto.NumberOfFemales;
            item.African = dto.NumberAfrican;
            item.Whites = dto.NumberWhites;
            item.Coloureds = dto.NumberColoureds;
            item.Asian = dto.NumberAsian;
            item.WithDisabilities = dto.NumberWithDisabilities;
            item.Other = dto.Other;
            item.Total = dto.Total;
            item.TotalNumberOfStudents = dto.TotalNumberOfStudents;
            _context.SaveChanges();
            return Ok(item);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.Demographics.Find(id);
            if (item == null) return NotFound();

            _context.Demographics.Remove(item);
            _context.SaveChanges();
            return NoContent();
        }
    }
}
