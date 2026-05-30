using ETD.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class FineTuningController : ControllerBase
    {
        private readonly FineTuningService _fineTuningService;

        public FineTuningController(FineTuningService fineTuningService)
        {
            _fineTuningService = fineTuningService;
        }

        [HttpPost("learner-guide-sft/prepare")]
        public async Task<IActionResult> PrepareLearnerGuideSftDataset(
            [FromBody] FineTuningDatasetRequest request,
            CancellationToken ct)
        {
            var result = await _fineTuningService.PrepareLearnerGuideSftDatasetAsync(request, ct);
            return Ok(result);
        }

        [HttpPost("openai/jobs")]
        public async Task<IActionResult> CreateOpenAiFineTuneJob(
            [FromBody] OpenAiFineTuneJobRequest request,
            CancellationToken ct)
        {
            try
            {
                var result = await _fineTuningService.CreateOpenAiFineTuneJobAsync(request, ct);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("openai/jobs/{jobId}")]
        public async Task<IActionResult> GetOpenAiFineTuneJob(string jobId, CancellationToken ct)
        {
            try
            {
                var result = await _fineTuningService.GetOpenAiFineTuneJobAsync(jobId, ct);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
