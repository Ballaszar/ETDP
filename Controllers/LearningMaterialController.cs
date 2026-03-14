using ETD.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class LearningMaterialController : ControllerBase
    {
        private static readonly string[] RequiredSubDirectories =
        {
            "Project Roll Out Plan",
            "Learning Schedule",
            "Learner Guide",
            "Summative Assessment",
            "Summative Memoranda",
            "Workbooks",
            "Workbook Memoranda",
            "SlideShows",
            "LearnerRegistration",
            "Logbook",
            "Progress Report",
            "TemplateUploads"
        };

        private readonly ApplicationDbContext _context;

        public LearningMaterialController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("ensure-workspace")]
        public IActionResult EnsureWorkspace([FromBody] EnsureWorkspaceRequest? request)
        {
            var qualificationId = request?.QualificationId ?? 0;
            if (qualificationId <= 0)
            {
                return BadRequest("qualificationId is required.");
            }

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId);
            if (qualification == null)
            {
                return NotFound("Qualification not found.");
            }

            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documentsPath))
            {
                return StatusCode(500, "Unable to resolve My Documents path.");
            }

            var rootDirectoryName = BuildRootDirectoryName(
                qualification.QualificationNumber,
                qualification.QualificationDescription,
                qualificationId);

            var rootPath = Path.Combine(documentsPath, rootDirectoryName);
            Directory.CreateDirectory(rootPath);

            var createdDirectories = new List<WorkspaceDirectoryResult>();
            foreach (var directoryName in RequiredSubDirectories)
            {
                var fullPath = Path.Combine(rootPath, directoryName);
                Directory.CreateDirectory(fullPath);
                createdDirectories.Add(new WorkspaceDirectoryResult
                {
                    Name = directoryName,
                    FullPath = fullPath
                });
            }

            return Ok(new
            {
                qualificationId,
                qualificationNumber = qualification.QualificationNumber,
                qualificationDescription = qualification.QualificationDescription,
                rootPath,
                directories = createdDirectories
            });
        }

        private static string BuildRootDirectoryName(string? qualificationNumber, string? qualificationDescription, int qualificationId)
        {
            var safeNumber = MakeSafePathPart(qualificationNumber);
            var safeDescription = MakeSafePathPart(qualificationDescription);

            if (safeNumber.Length == 0 && safeDescription.Length == 0)
            {
                return $"Qualification_{qualificationId}";
            }

            if (safeDescription.Length == 0)
            {
                return safeNumber;
            }

            if (safeNumber.Length == 0)
            {
                return safeDescription;
            }

            return $"{safeNumber} - {safeDescription}";
        }

        private static string MakeSafePathPart(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            var cleaned = new string(chars);
            while (cleaned.Contains("  ", StringComparison.Ordinal))
            {
                cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
            }

            return cleaned.Trim();
        }

        public sealed class EnsureWorkspaceRequest
        {
            public int QualificationId { get; set; }
        }

        public sealed class WorkspaceDirectoryResult
        {
            public string Name { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;
        }
    }
}
