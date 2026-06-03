using ETD.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class LlmCompetenceController : ControllerBase
    {
        private readonly LlmCompetenceAssessmentService _service;

        public LlmCompetenceController(LlmCompetenceAssessmentService service)
        {
            _service = service;
        }

        [HttpPost("run")]
        public async Task<IActionResult> Run([FromBody] LlmCompetenceRunRequest request, CancellationToken ct)
        {
            var report = await _service.RunAsync(request, ct);
            return Ok(report);
        }

        [HttpGet("latest")]
        public async Task<IActionResult> Latest(CancellationToken ct)
        {
            var report = await _service.GetLatestAsync(ct);
            return report == null ? NotFound(new { message = "No competence assessment report has been created yet." }) : Ok(report);
        }

        [HttpGet("topics")]
        public async Task<IActionResult> Topics([FromQuery] int? qualificationId = null, [FromQuery] int take = 200, CancellationToken ct = default)
        {
            return Ok(await _service.GetTopicOptionsAsync(qualificationId, take, ct));
        }
    }
}
