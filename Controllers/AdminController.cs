using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using ETD.Api.Models;
using System.Text;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        public class SeedSkeletonRequest
        {
            public int? QualificationId { get; set; }
            public string? QualificationNumber { get; set; }
            public bool OnlyMissing { get; set; } = true;
            public int? MaxNew { get; set; }
            public bool DryRun { get; set; } = false;
        }

        [HttpPost("seed-skeleton")]
        public IActionResult SeedSkeleton([FromBody] SeedSkeletonRequest? req)
        {
            req ??= new SeedSkeletonRequest();

            Qualification? qualification = null;
            if (req.QualificationId.HasValue && req.QualificationId.Value > 0)
            {
                qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId.Value);
            }
            if (qualification == null && !string.IsNullOrWhiteSpace(req.QualificationNumber))
            {
                var qn = req.QualificationNumber.Trim();
                qualification = _context.Qualifications.FirstOrDefault(q => q.QualificationNumber == qn);
            }
            qualification ??= _context.Qualifications.FirstOrDefault();
            if (qualification == null)
            {
                return BadRequest("No qualification found.");
            }

            var criteria = (from c in _context.AssessmentCriteria
                            join t in _context.Topics on c.TopicId equals t.Id
                            join s in _context.Subjects on t.SubjectId equals s.Id
                            where s.QualificationId == qualification.Id
                            select new
                            {
                                c.Id,
                                c.Description,
                                TopicDescription = t.TopicDescription
                            }).ToList();

            if (criteria.Count == 0)
            {
                return Ok(new
                {
                    qualificationId = qualification.Id,
                    qualificationNumber = qualification.QualificationNumber,
                    created = 0,
                    skippedExisting = 0,
                    considered = 0,
                    dryRun = req.DryRun,
                    message = "No assessment criteria found for qualification."
                });
            }

            var criteriaIds = criteria.Select(c => c.Id).ToList();
            var existingCriteria = _context.LessonPlans
                .Where(lp => criteriaIds.Contains(lp.AssessmentCriteriaId))
                .Select(lp => lp.AssessmentCriteriaId)
                .Distinct()
                .ToHashSet();

            var toolkitByCriteria = _context.LecturerToolkitEntries
                .Where(e => e.QualificationsId == qualification.Id && e.AssessmentCriteriaId.HasValue)
                .GroupBy(e => e.AssessmentCriteriaId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var candidates = criteria
                .OrderBy(c => c.Id)
                .Where(c => !req.OnlyMissing || !existingCriteria.Contains(c.Id))
                .ToList();

            if (req.MaxNew.HasValue && req.MaxNew.Value > 0)
            {
                candidates = candidates.Take(req.MaxNew.Value).ToList();
            }

            var toCreate = new List<LessonPlan>();
            var skippedExisting = 0;
            foreach (var c in criteria.OrderBy(x => x.Id))
            {
                if (req.OnlyMissing && existingCriteria.Contains(c.Id))
                {
                    skippedExisting++;
                }
            }

            foreach (var c in candidates)
            {
                var content = BuildSkeletonContent(
                    c.Description ?? "Assessment Criteria",
                    c.TopicDescription ?? "",
                    toolkitByCriteria.TryGetValue(c.Id, out var entries) ? entries : null);

                toCreate.Add(new LessonPlan
                {
                    AssessmentCriteriaId = c.Id,
                    Title = $"Skeleton Example - AC {c.Id}",
                    Date = DateTime.UtcNow.Date,
                    DurationMinutes = 45,
                    Content = content
                });
            }

            if (!req.DryRun && toCreate.Count > 0)
            {
                _context.LessonPlans.AddRange(toCreate);
                _context.SaveChanges();
            }

            return Ok(new
            {
                qualificationId = qualification.Id,
                qualificationNumber = qualification.QualificationNumber,
                created = toCreate.Count,
                skippedExisting,
                considered = candidates.Count,
                dryRun = req.DryRun
            });
        }

        private static string BuildSkeletonContent(string criteriaDescription, string topicDescription, List<LecturerToolkitEntry>? entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SKELETON EXAMPLE]");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(topicDescription))
            {
                sb.AppendLine($"Topic focus: {topicDescription}");
                sb.AppendLine();
            }
            sb.AppendLine($"Assessment alignment: {criteriaDescription}");
            sb.AppendLine();
            sb.AppendLine("Learning objectives:");
            sb.AppendLine("- Understand the key concept.");
            sb.AppendLine("- Apply the concept to a practical scenario.");
            sb.AppendLine("- Reflect on outcomes.");
            sb.AppendLine();

            if (entries != null && entries.Count > 0)
            {
                var fromToolkit = entries
                    .Select(e => e.LessonPlanContent)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Take(2)
                    .ToList();
                if (fromToolkit.Count > 0)
                {
                    sb.AppendLine("Starter content from toolkit:");
                    foreach (var text in fromToolkit)
                    {
                        sb.AppendLine("- " + text.Trim());
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("Activities:");
            sb.AppendLine("- Mini lecture");
            sb.AppendLine("- Demonstration");
            sb.AppendLine("- Guided practice");
            sb.AppendLine("- Formative check");
            return sb.ToString().Trim();
        }
    }
}
