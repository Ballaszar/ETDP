using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using System.Linq;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QualityController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public QualityController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("checks")]
        public IActionResult Checks()
        {
            var list = new System.Collections.Generic.List<object>();

            var qual = _context.Qualifications.FirstOrDefault();
            list.Add(new { key = "qualification_present", pass = qual != null, details = qual?.QualificationNumber });
            list.Add(new { key = "institution_name_present", pass = !string.IsNullOrWhiteSpace(qual?.LearningInstitutionName), details = qual?.LearningInstitutionName });
            list.Add(new { key = "logo_path_present", pass = !string.IsNullOrWhiteSpace(qual?.LogoPath), details = qual?.LogoPath });

            var subjects = qual != null ? _context.Subjects.Where(s => s.QualificationId == qual.Id).ToList() : new System.Collections.Generic.List<ETD.Api.Models.Subject>();
            list.Add(new { key = "subjects_count", pass = subjects.Count > 0, details = subjects.Count });

            var topics = subjects.Count > 0 ? _context.Topics.Where(t => subjects.Select(s => s.Id).Contains(t.SubjectId)).ToList() : new System.Collections.Generic.List<ETD.Api.Models.Topic>();
            list.Add(new { key = "topics_count", pass = topics.Count > 0, details = topics.Count });

            var criteria = topics.Count > 0 ? _context.AssessmentCriteria.Where(c => topics.Select(t => t.Id).Contains(c.TopicId)).ToList() : new System.Collections.Generic.List<ETD.Api.Models.AssessmentCriteria>();
            list.Add(new { key = "assessment_criteria_count", pass = criteria.Count > 0, details = criteria.Count });

            var plans = criteria.Count > 0 ? _context.LessonPlans.Where(lp => criteria.Select(c => c.Id).Contains(lp.AssessmentCriteriaId)).ToList() : new System.Collections.Generic.List<ETD.Api.Models.LessonPlan>();
            list.Add(new { key = "lesson_plans_count", pass = plans.Count > 0, details = plans.Count });

            var missingDurations = plans.Where(p => p.DurationMinutes == null || p.DurationMinutes <= 0).Select(p => p.Title).ToList();
            list.Add(new { key = "lesson_plan_durations_present", pass = missingDurations.Count == 0, details = missingDurations });

            var acWithoutPlan = criteria.Where(c => !plans.Any(p => p.AssessmentCriteriaId == c.Id)).Select(c => c.Description).ToList();
            list.Add(new { key = "criteria_covered_by_lesson_plans", pass = acWithoutPlan.Count == 0, details = acWithoutPlan });

            var chartsDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Exports", "Charts");
            var chartsDirExists = System.IO.Directory.Exists(chartsDir);
            list.Add(new { key = "charts_export_dir_exists", pass = chartsDirExists, details = chartsDir });

            var slidesDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Exports", "Slides");
            list.Add(new { key = "slides_export_dir_exists", pass = System.IO.Directory.Exists(slidesDir), details = slidesDir });

            var keyHealth = false;
            try
            {
                var res = HttpContext.RequestServices.GetService(typeof(ETD.Api.Utils.Secrets));
                keyHealth = _context.SourceMaterials.Any() || res != null;
            }
            catch { }
            list.Add(new { key = "ai_key_or_materials_available", pass = keyHealth, details = "OpenAI key or materials present" });

            return Ok(new { checks = list });
        }
    }
}
