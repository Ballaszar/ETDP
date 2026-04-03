using ETD.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CurriculumPipelineController : ControllerBase
    {
        private readonly CurriculumPipelineService _curriculumPipelineService;
        private readonly CurriculumDeliveryPilotService _curriculumDeliveryPilotService;

        public CurriculumPipelineController(
            CurriculumPipelineService curriculumPipelineService,
            CurriculumDeliveryPilotService curriculumDeliveryPilotService)
        {
            _curriculumPipelineService = curriculumPipelineService;
            _curriculumDeliveryPilotService = curriculumDeliveryPilotService;
        }

        [HttpPost("jobs")]
        public async Task<IActionResult> Start([FromBody] StartCurriculumPipelineRequest request, CancellationToken cancellationToken)
        {
            if (request == null || request.QualificationId <= 0)
            {
                return BadRequest("QualificationId is required.");
            }

            try
            {
                var job = await _curriculumPipelineService.QueueQualificationAsync(
                    request.QualificationId,
                    request.StartPage,
                    request.ForceRestart,
                    cancellationToken);
                return Ok(job);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("jobs/{jobId}")]
        public async Task<IActionResult> Get(string jobId, CancellationToken cancellationToken)
        {
            var job = await _curriculumPipelineService.GetJobAsync(jobId, cancellationToken);
            if (job == null) return NotFound("Pipeline job not found.");
            return Ok(job);
        }

        [HttpGet("jobs/latest")]
        public async Task<IActionResult> GetLatest([FromQuery] int qualificationId, CancellationToken cancellationToken)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required.");

            var job = await _curriculumPipelineService.GetLatestJobAsync(qualificationId, cancellationToken);
            if (job == null) return NotFound("No curriculum pipeline job found.");
            return Ok(job);
        }

        [HttpGet("topic-evidence")]
        public async Task<IActionResult> GetTopicEvidence([FromQuery] int qualificationId, CancellationToken cancellationToken)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required.");

            try
            {
                var summary = await _curriculumDeliveryPilotService.BuildTopicEvidenceSummaryAsync(qualificationId, cancellationToken);
                return Ok(summary);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        public sealed class StartCurriculumPipelineRequest
        {
            public int QualificationId { get; set; }
            public int? StartPage { get; set; }
            public bool ForceRestart { get; set; }
        }
    }
}
