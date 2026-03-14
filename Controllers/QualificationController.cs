using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using ETD.Api.Models;
using System.Text.RegularExpressions;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QualificationController : ControllerBase
    {
        private const int CesmFieldMaxLength = 50;
        private readonly ApplicationDbContext _context;

        public QualificationController(ApplicationDbContext context)
        {
            _context = context;
        }


        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var items = _context.Qualifications
                    .Select(q => new ETD.Api.DTOs.QualificationDto
                    {
                        Id = q.Id,
                        QualificationNumber = q.QualificationNumber,
                        QualificationDescription = q.QualificationDescription,
                        CesmField = q.CesmField,
                        NqfLevel = q.NqfLevel,
                        Credits = q.Credits,
                        LearningInstitutionName = q.LearningInstitutionName,
                        AccreditationNumber = q.AccreditationNumber,
                        DeanPrincipalCEO = q.DeanPrincipalCEO,
                        SeniorLecturer = q.SeniorLecturer,
                        LogoPath = q.LogoPath,
                        QualificationType = q.QualificationType,
                        Purpose = q.Purpose,
                        LearningDateStart = q.LearningDateStart,
                        LearningDateEnd = q.LearningDateEnd,
                        UsesOutcomes = q.UsesOutcomes
                    })
                    .ToList()
                    .OrderBy(q => SortKey(q.LearningInstitutionName), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(q => SortKey(q.QualificationNumber), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(q => q.Id)
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
            var q = _context.Qualifications.Find(id);
            if (q == null) return NotFound();
            var dto = new ETD.Api.DTOs.QualificationDto
            {
                Id = q.Id,
                QualificationNumber = q.QualificationNumber,
                QualificationDescription = q.QualificationDescription,
                CesmField = q.CesmField,
                NqfLevel = q.NqfLevel,
                Credits = q.Credits,
                LearningInstitutionName = q.LearningInstitutionName,
                AccreditationNumber = q.AccreditationNumber,
                DeanPrincipalCEO = q.DeanPrincipalCEO,
                SeniorLecturer = q.SeniorLecturer,
                LogoPath = q.LogoPath,
                QualificationType = q.QualificationType,
                Purpose = q.Purpose,
                LearningDateStart = q.LearningDateStart,
                LearningDateEnd = q.LearningDateEnd,
                UsesOutcomes = q.UsesOutcomes
            };
            return Ok(dto);
        }

        [HttpGet("search")]
        public IActionResult Search([FromQuery] string text)
        {
            text ??= string.Empty;

            var items = _context.Qualifications
                .Where(q =>
                    q.QualificationNumber.Contains(text) ||
                    q.QualificationDescription.Contains(text) ||
                    q.CesmField.Contains(text) ||
                    q.LearningInstitutionName.Contains(text) ||
                    q.AccreditationNumber.Contains(text) ||
                    q.SeniorLecturer.Contains(text) ||
                    q.QualificationType.Contains(text) ||
                    q.Purpose.Contains(text))
                .Select(q => new ETD.Api.DTOs.QualificationDto
                {
                    Id = q.Id,
                    QualificationNumber = q.QualificationNumber,
                    QualificationDescription = q.QualificationDescription,
                    CesmField = q.CesmField,
                    NqfLevel = q.NqfLevel,
                    Credits = q.Credits,
                    LearningInstitutionName = q.LearningInstitutionName,
                    AccreditationNumber = q.AccreditationNumber,
                    DeanPrincipalCEO = q.DeanPrincipalCEO,
                    SeniorLecturer = q.SeniorLecturer,
                    LogoPath = q.LogoPath,
                    QualificationType = q.QualificationType,
                    Purpose = q.Purpose,
                    LearningDateStart = q.LearningDateStart,
                    LearningDateEnd = q.LearningDateEnd,
                    UsesOutcomes = q.UsesOutcomes
                })
                .ToList()
                .OrderBy(q => SortKey(q.LearningInstitutionName), StringComparer.OrdinalIgnoreCase)
                .ThenBy(q => SortKey(q.QualificationNumber), StringComparer.OrdinalIgnoreCase)
                .ThenBy(q => q.Id)
                .ToList();
            return Ok(items);
        }


        [HttpPost]
        public IActionResult Create(ETD.Api.DTOs.CreateQualificationDto dto)
        {
            if (!TryValidateCesmField(dto.CesmField, out var cesmField, out var validationError))
            {
                return BadRequest(new { error = validationError });
            }

            var model = new Qualification
            {
                QualificationNumber = dto.QualificationNumber,
                QualificationDescription = dto.QualificationDescription,
                CesmField = cesmField,
                NqfLevel = dto.NqfLevel,
                Credits = dto.Credits,
                LearningInstitutionName = dto.LearningInstitutionName,
                AccreditationNumber = dto.AccreditationNumber,
                DeanPrincipalCEO = dto.DeanPrincipalCEO,
                SeniorLecturer = dto.SeniorLecturer,
                LogoPath = dto.LogoPath,
                QualificationType = dto.QualificationType,
                UsesOutcomes = dto.UsesOutcomes,
                Purpose = dto.Purpose,
                LearningDateStart = dto.LearningDateStart,
                LearningDateEnd = dto.LearningDateEnd
            };
            _context.Qualifications.Add(model);
            _context.SaveChanges();
            return CreatedAtAction(nameof(Get), new { id = model.Id }, model);
        }


        [HttpPut("{id}")]
        public IActionResult Update(int id, ETD.Api.DTOs.UpdateQualificationDto dto)
        {
            if (!TryValidateCesmField(dto.CesmField, out var cesmField, out var validationError))
            {
                return BadRequest(new { error = validationError });
            }

            var item = _context.Qualifications.Find(id);
            if (item == null) return NotFound();
            item.QualificationNumber = dto.QualificationNumber;
            item.QualificationDescription = dto.QualificationDescription;
            item.CesmField = cesmField;
            item.NqfLevel = dto.NqfLevel;
            item.Credits = dto.Credits;
            item.LearningInstitutionName = dto.LearningInstitutionName;
            item.AccreditationNumber = dto.AccreditationNumber;
            item.DeanPrincipalCEO = dto.DeanPrincipalCEO;
            item.SeniorLecturer = dto.SeniorLecturer;
            item.LogoPath = dto.LogoPath;
            item.QualificationType = dto.QualificationType;
            item.UsesOutcomes = dto.UsesOutcomes;
            item.Purpose = dto.Purpose;
            item.LearningDateStart = dto.LearningDateStart;
            item.LearningDateEnd = dto.LearningDateEnd;
            _context.SaveChanges();
            return Ok(item);
        }

        [HttpPost("upload-logo")]
        [RequestSizeLimit(20 * 1024 * 1024)]
        public IActionResult UploadLogo([FromForm] IFormFile? file)
        {
            if (file == null || file.Length <= 0)
            {
                return BadRequest(new { error = "Select a logo file first." });
            }

            var ext = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
            };
            if (!allowed.Contains(ext))
            {
                return BadRequest(new { error = "Unsupported logo format. Use PNG, JPG, JPEG, BMP, GIF, or WEBP." });
            }

            var current = Directory.GetCurrentDirectory();
            var logoDir = Path.Combine(current, "Imports", "Logos");
            if (!Directory.Exists(Path.Combine(current, "Imports")) && Directory.Exists(Path.Combine(current, "ETDP")))
            {
                logoDir = Path.Combine(current, "ETDP", "Imports", "Logos");
            }
            Directory.CreateDirectory(logoDir);

            var rawBase = Path.GetFileNameWithoutExtension(file.FileName ?? string.Empty);
            var safeBase = Regex.Replace(rawBase, @"[^A-Za-z0-9._-]+", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(safeBase)) safeBase = "logo";
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{safeBase}_{stamp}{ext}";
            var fullPath = Path.GetFullPath(Path.Combine(logoDir, fileName));

            using (var stream = System.IO.File.Create(fullPath))
            {
                file.CopyTo(stream);
            }

            return Ok(new { path = fullPath, fileName });
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.Qualifications.Find(id);
            if (item == null) return NotFound();

            var subjectIds = _context.Subjects.Where(s => s.QualificationId == id).Select(s => s.Id).ToList();
            var outcomeIds = _context.Outcomes.Where(o => subjectIds.Contains(o.SubjectId)).Select(o => o.Id).ToList();
            var topicIds = _context.Topics.Where(t => subjectIds.Contains(t.SubjectId)).Select(t => t.Id).ToList();
            var subtopicIds = _context.Subtopics.Where(st => topicIds.Contains(st.TopicId)).Select(st => st.Id).ToList();
            var criteriaIds = _context.AssessmentCriteria.Where(c => topicIds.Contains(c.TopicId)).Select(c => c.Id).ToList();

            if (criteriaIds.Count > 0)
            {
                var lessonPlans = _context.LessonPlans.Where(lp => criteriaIds.Contains(lp.AssessmentCriteriaId)).ToList();
                _context.LessonPlans.RemoveRange(lessonPlans);
            }
            if (criteriaIds.Count > 0)
            {
                var criteria = _context.AssessmentCriteria.Where(c => criteriaIds.Contains(c.Id)).ToList();
                _context.AssessmentCriteria.RemoveRange(criteria);
            }
            if (subtopicIds.Count > 0)
            {
                var activities = _context.Activities.Where(a => subtopicIds.Contains(a.SubtopicId)).ToList();
                _context.Activities.RemoveRange(activities);
            }
            if (subtopicIds.Count > 0)
            {
                var subtopics = _context.Subtopics.Where(st => subtopicIds.Contains(st.Id)).ToList();
                _context.Subtopics.RemoveRange(subtopics);
            }
            if (topicIds.Count > 0)
            {
                var topics = _context.Topics.Where(t => topicIds.Contains(t.Id)).ToList();
                _context.Topics.RemoveRange(topics);
            }
            if (outcomeIds.Count > 0)
            {
                var outcomes = _context.Outcomes.Where(o => outcomeIds.Contains(o.Id)).ToList();
                _context.Outcomes.RemoveRange(outcomes);
            }
            if (subjectIds.Count > 0)
            {
                var subjects = _context.Subjects.Where(s => subjectIds.Contains(s.Id)).ToList();
                _context.Subjects.RemoveRange(subjects);
            }

            var demographics = _context.Demographics.Where(d => d.QualificationId == id).ToList();
            _context.Demographics.RemoveRange(demographics);

            var qualificationPhases = _context.QualificationPhases.Where(qp => qp.QualificationId == id).ToList();
            _context.QualificationPhases.RemoveRange(qualificationPhases);

            var toolkitEntries = _context.LecturerToolkitEntries.Where(e => e.QualificationsId == id).ToList();
            _context.LecturerToolkitEntries.RemoveRange(toolkitEntries);

            var jobs = _context.AutomationJobs.Where(j => j.QualificationId == id).ToList();
            _context.AutomationJobs.RemoveRange(jobs);

            var qdesc = item.QualificationDescription ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(qdesc))
            {
                var relatedMaterials = _context.SourceMaterials.Where(s => s.QualificationDescription == qdesc).ToList();
                _context.SourceMaterials.RemoveRange(relatedMaterials);
            }

            _context.Qualifications.Remove(item);
            _context.SaveChanges();
            return NoContent();
        }

        private static string SortKey(string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static bool TryValidateCesmField(string? value, out string normalized, out string? error)
        {
            normalized = (value ?? string.Empty).Trim();
            if (normalized.Length <= CesmFieldMaxLength)
            {
                error = null;
                return true;
            }

            error = $"CESM Field must be {CesmFieldMaxLength} characters or fewer.";
            return false;
        }
    }
}
