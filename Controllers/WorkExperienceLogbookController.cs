using ETD.Api.Data;
using ETD.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkExperienceLogbookController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public WorkExperienceLogbookController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetLatest([FromQuery] int? qualificationId = null, [FromQuery] int? learnerId = null)
        {
            var normalizedQualificationId = NormalizeId(qualificationId);
            var normalizedLearnerId = NormalizeId(learnerId);

            if (normalizedQualificationId == null)
            {
                return BadRequest(new { error = "qualificationId is required." });
            }

            var query = _context.WorkExperienceLogbooks
                .AsNoTracking()
                .Include(x => x.Entries)
                .Where(x => x.QualificationId == normalizedQualificationId);

            if (normalizedLearnerId.HasValue)
            {
                query = query.Where(x => x.LearnerRegistrationId == normalizedLearnerId.Value);
            }
            else
            {
                query = query.Where(x => x.LearnerRegistrationId == null);
            }

            var entity = await query
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync();

            if (entity == null)
            {
                return NotFound(new { error = "No saved work experience logbook was found." });
            }

            return Ok(ToResponse(entity));
        }

        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] SaveWorkExperienceLogbookRequest? request)
        {
            request ??= new SaveWorkExperienceLogbookRequest();

            var normalizedQualificationId = NormalizeId(request.QualificationId);
            if (normalizedQualificationId == null)
            {
                return BadRequest(new { error = "qualificationId is required." });
            }

            var qualification = await _context.Qualifications
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == normalizedQualificationId.Value);
            if (qualification == null)
            {
                return BadRequest(new { error = $"Qualification {normalizedQualificationId.Value} was not found." });
            }

            var normalizedLearnerId = NormalizeId(request.LearnerId);
            if (normalizedLearnerId.HasValue)
            {
                var learnerExists = await _context.LearnerRegistrations
                    .AsNoTracking()
                    .AnyAsync(x => x.Id == normalizedLearnerId.Value && (x.QualificationId == null || x.QualificationId == normalizedQualificationId.Value));
                if (!learnerExists)
                {
                    return BadRequest(new { error = $"Learner {normalizedLearnerId.Value} was not found for qualification {normalizedQualificationId.Value}." });
                }
            }

            var requestId = NormalizeId(request.Id);
            WorkExperienceLogbook? entity = null;
            if (requestId.HasValue)
            {
                entity = await _context.WorkExperienceLogbooks
                    .Include(x => x.Entries)
                    .FirstOrDefaultAsync(x => x.Id == requestId.Value);
            }

            entity ??= await _context.WorkExperienceLogbooks
                .Include(x => x.Entries)
                .FirstOrDefaultAsync(x =>
                    x.QualificationId == normalizedQualificationId.Value &&
                    x.LearnerRegistrationId == normalizedLearnerId);

            var nowUtc = DateTime.UtcNow;
            if (entity == null)
            {
                entity = new WorkExperienceLogbook
                {
                    QualificationId = normalizedQualificationId.Value,
                    LearnerRegistrationId = normalizedLearnerId,
                    CreatedAtUtc = nowUtc
                };
                _context.WorkExperienceLogbooks.Add(entity);
            }

            entity.QualificationId = normalizedQualificationId.Value;
            entity.QualificationNumber = NormalizeText(request.QualificationNumber) != string.Empty
                ? NormalizeText(request.QualificationNumber)
                : NormalizeText(qualification.QualificationNumber);
            entity.LearnerRegistrationId = normalizedLearnerId;
            entity.LearningInstitutionName = NormalizeText(request.LearningInstitutionName);
            entity.LearningInstitutionAddress = NormalizeText(request.LearningInstitutionAddress);
            entity.LearningInstitutionContactPerson = NormalizeText(request.LearningInstitutionContactPerson);
            entity.LearningInstitutionContactPhone = NormalizeText(request.LearningInstitutionContactPhone);
            entity.LearningInstitutionContactEmail = NormalizeText(request.LearningInstitutionContactEmail);
            entity.EmployerName = NormalizeText(request.EmployerName);
            entity.EmployerAddress = NormalizeText(request.EmployerAddress);
            entity.SupervisorName = NormalizeText(request.SupervisorName);
            entity.SupervisorPhone = NormalizeText(request.SupervisorPhone);
            entity.SupervisorEmail = NormalizeText(request.SupervisorEmail);
            entity.UpdatedAtUtc = nowUtc;

            if (entity.Entries.Count > 0)
            {
                _context.WorkExperienceLogbookEntries.RemoveRange(entity.Entries);
                entity.Entries.Clear();
            }

            var normalizedRows = (request.LogRows ?? new List<SaveWorkExperienceLogbookRowRequest>())
                .Select((row, index) => new WorkExperienceLogbookEntry
                {
                    SortOrder = index + 1,
                    SubjectCode = NormalizeText(row.SubjectCode),
                    TopicCode = NormalizeText(row.TopicCode),
                    TopicDescription = NormalizeText(row.TopicDescription),
                    EntryDate = NormalizeText(row.Date),
                    Signature = NormalizeText(row.Signature)
                })
                .Where(row =>
                    row.SubjectCode.Length > 0 ||
                    row.TopicCode.Length > 0 ||
                    row.TopicDescription.Length > 0 ||
                    row.EntryDate.Length > 0 ||
                    row.Signature.Length > 0)
                .ToList();

            foreach (var row in normalizedRows)
            {
                entity.Entries.Add(row);
            }

            await _context.SaveChangesAsync();
            entity.Entries = entity.Entries.OrderBy(x => x.SortOrder).ToList();
            return Ok(ToResponse(entity));
        }

        private static int? NormalizeId(int? value)
        {
            return value.HasValue && value.Value > 0 ? value.Value : null;
        }

        private static string NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static WorkExperienceLogbookResponse ToResponse(WorkExperienceLogbook entity)
        {
            return new WorkExperienceLogbookResponse
            {
                Id = entity.Id,
                QualificationId = entity.QualificationId,
                QualificationNumber = entity.QualificationNumber,
                LearnerId = entity.LearnerRegistrationId,
                LearningInstitutionName = entity.LearningInstitutionName,
                LearningInstitutionAddress = entity.LearningInstitutionAddress,
                LearningInstitutionContactPerson = entity.LearningInstitutionContactPerson,
                LearningInstitutionContactPhone = entity.LearningInstitutionContactPhone,
                LearningInstitutionContactEmail = entity.LearningInstitutionContactEmail,
                EmployerName = entity.EmployerName,
                EmployerAddress = entity.EmployerAddress,
                SupervisorName = entity.SupervisorName,
                SupervisorPhone = entity.SupervisorPhone,
                SupervisorEmail = entity.SupervisorEmail,
                UpdatedAtUtc = entity.UpdatedAtUtc,
                LogRows = (entity.Entries ?? new List<WorkExperienceLogbookEntry>())
                    .OrderBy(x => x.SortOrder)
                    .Select(x => new WorkExperienceLogbookRowResponse
                    {
                        Id = x.Id,
                        SubjectCode = x.SubjectCode,
                        TopicCode = x.TopicCode,
                        TopicDescription = x.TopicDescription,
                        Date = x.EntryDate,
                        Signature = x.Signature
                    })
                    .ToList()
            };
        }

        public sealed class SaveWorkExperienceLogbookRequest
        {
            public int? Id { get; set; }
            public int? QualificationId { get; set; }
            public string? QualificationNumber { get; set; }
            public int? LearnerId { get; set; }
            public string? LearningInstitutionName { get; set; }
            public string? LearningInstitutionAddress { get; set; }
            public string? LearningInstitutionContactPerson { get; set; }
            public string? LearningInstitutionContactPhone { get; set; }
            public string? LearningInstitutionContactEmail { get; set; }
            public string? EmployerName { get; set; }
            public string? EmployerAddress { get; set; }
            public string? SupervisorName { get; set; }
            public string? SupervisorPhone { get; set; }
            public string? SupervisorEmail { get; set; }
            public List<SaveWorkExperienceLogbookRowRequest> LogRows { get; set; } = new();
        }

        public sealed class SaveWorkExperienceLogbookRowRequest
        {
            public string? SubjectCode { get; set; }
            public string? TopicCode { get; set; }
            public string? TopicDescription { get; set; }
            public string? Date { get; set; }
            public string? Signature { get; set; }
        }

        public sealed class WorkExperienceLogbookResponse
        {
            public int Id { get; set; }
            public int? QualificationId { get; set; }
            public string QualificationNumber { get; set; } = string.Empty;
            public int? LearnerId { get; set; }
            public string LearningInstitutionName { get; set; } = string.Empty;
            public string LearningInstitutionAddress { get; set; } = string.Empty;
            public string LearningInstitutionContactPerson { get; set; } = string.Empty;
            public string LearningInstitutionContactPhone { get; set; } = string.Empty;
            public string LearningInstitutionContactEmail { get; set; } = string.Empty;
            public string EmployerName { get; set; } = string.Empty;
            public string EmployerAddress { get; set; } = string.Empty;
            public string SupervisorName { get; set; } = string.Empty;
            public string SupervisorPhone { get; set; } = string.Empty;
            public string SupervisorEmail { get; set; } = string.Empty;
            public DateTime UpdatedAtUtc { get; set; }
            public List<WorkExperienceLogbookRowResponse> LogRows { get; set; } = new();
        }

        public sealed class WorkExperienceLogbookRowResponse
        {
            public int Id { get; set; }
            public string SubjectCode { get; set; } = string.Empty;
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string Date { get; set; } = string.Empty;
            public string Signature { get; set; } = string.Empty;
        }
    }
}
