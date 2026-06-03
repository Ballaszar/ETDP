using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ETD.Api.Controllers
{
    /// <summary>
    /// Resonance Framework API Controller
    /// Provides endpoints for quantum state calculations and semantic memory analysis
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ResonanceController : ControllerBase
    {
        private readonly IResonanceService _resonanceService;

        public ResonanceController(IResonanceService resonanceService)
        {
            _resonanceService = resonanceService;
        }

        /// <summary>
        /// Request model for resonance analysis
        /// </summary>
        public class ResonanceRequest
        {
            /// <summary>
            /// List of responses, each containing coherence, novelty, and constraint values
            /// </summary>
            [JsonProperty("responses")]
            public List<ResponseInput> Responses { get; set; } = new();

            /// <summary>
            /// Optional custom configuration
            /// </summary>
            [JsonProperty("config")]
            public ResonanceConfigInput? Config { get; set; }

            /// <summary>
            /// Whether to include visualization plots as base64-encoded images
            /// </summary>
            [JsonProperty("includeCharts")]
            public bool IncludeCharts { get; set; } = true;

            /// <summary>
            /// Whether to include detailed report
            /// </summary>
            [JsonProperty("includeReport")]
            public bool IncludeReport { get; set; } = true;
        }

        /// <summary>
        /// Single response input
        /// </summary>
        public class ResponseInput
        {
            [JsonProperty("coherence")]
            public double Coherence { get; set; }

            [JsonProperty("novelty")]
            public double Novelty { get; set; }

            [JsonProperty("constraint")]
            public double Constraint { get; set; }
        }

        /// <summary>
        /// Configuration input
        /// </summary>
        public class ResonanceConfigInput
        {
            [JsonProperty("riMax")]
            public double? RIMax { get; set; }

            [JsonProperty("stepsPerResponse")]
            public int? StepsPerResponse { get; set; }

            [JsonProperty("alphaBase")]
            public double? AlphaBase { get; set; }

            [JsonProperty("betaBase")]
            public double? BetaBase { get; set; }

            [JsonProperty("gammaBase")]
            public double? GammaBase { get; set; }
        }

        /// <summary>
        /// Response model for resonance analysis
        /// </summary>
        public class ResonanceResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("message")]
            public string? Message { get; set; }

            [JsonProperty("data")]
            public ResonanceData? Data { get; set; }

            [JsonProperty("error")]
            public string? Error { get; set; }
        }

        /// <summary>
        /// Main data response
        /// </summary>
        public class ResonanceData
        {
            [JsonProperty("results")]
            public List<ResonanceResult> Results { get; set; } = new();

            [JsonProperty("statistics")]
            public ResonanceStatistics Statistics { get; set; } = new();

            [JsonProperty("charts")]
            public ResonanceCharts? Charts { get; set; }

            [JsonProperty("report")]
            public string? Report { get; set; }
        }

        /// <summary>
        /// Single result
        /// </summary>
        public class ResonanceResult
        {
            [JsonProperty("t")]
            public int T { get; set; }

            [JsonProperty("coherence")]
            public double Coherence { get; set; }

            [JsonProperty("novelty")]
            public double Novelty { get; set; }

            [JsonProperty("constraint")]
            public double Constraint { get; set; }

            [JsonProperty("RI")]
            public double ResonanceIndex { get; set; }

            [JsonProperty("resonance_pct")]
            public double ResonancePct { get; set; }

            [JsonProperty("Qz")]
            public double Qz { get; set; }

            [JsonProperty("Az")]
            public double Az { get; set; }

            [JsonProperty("vector_consistency")]
            public double VectorConsistency { get; set; }

            [JsonProperty("SMI")]
            public double SMI { get; set; }

            [JsonProperty("imprint_quadrant")]
            public string ImprintQuadrant { get; set; } = string.Empty;
        }

        /// <summary>
        /// Statistics response
        /// </summary>
        public class ResonanceStatistics
        {
            [JsonProperty("num_responses")]
            public int NumResponses { get; set; }

            [JsonProperty("avg_smi")]
            public double AvgSMI { get; set; }

            [JsonProperty("max_smi")]
            public double MaxSMI { get; set; }

            [JsonProperty("total_smi")]
            public double TotalSMI { get; set; }

            [JsonProperty("avg_resonance_index")]
            public double AvgResonanceIndex { get; set; }

            [JsonProperty("avg_consistency")]
            public double AvgConsistency { get; set; }

            [JsonProperty("qz_mean")]
            public double QzMean { get; set; }

            [JsonProperty("qz_std")]
            public double QzStd { get; set; }

            [JsonProperty("az_mean")]
            public double AzMean { get; set; }

            [JsonProperty("az_std")]
            public double AzStd { get; set; }

            [JsonProperty("quadrant_counts")]
            public Dictionary<string, int> QuadrantCounts { get; set; } = new();
        }

        /// <summary>
        /// Chart visualizations
        /// </summary>
        public class ResonanceCharts
        {
            [JsonProperty("trajectories")]
            public string? Trajectories { get; set; }

            [JsonProperty("imprint_quadrants")]
            public string? ImprintQuadrants { get; set; }

            [JsonProperty("smi_cumulative")]
            public string? SMICumulative { get; set; }

            [JsonProperty("smi_individual")]
            public string? SMIIndividual { get; set; }

            [JsonProperty("phase_space")]
            public string? PhaseSpace { get; set; }

            [JsonProperty("resonance_index")]
            public string? ResonanceIndex { get; set; }
        }

        /// <summary>
        /// Analyze resonance for a sequence of responses
        /// </summary>
        /// <param name="request">Request containing response sequence</param>
        /// <returns>Resonance analysis results with optional charts and report</returns>
        [HttpPost("analyze")]
        [ProducesResponseType(typeof(ResonanceResponse), 200)]
        [ProducesResponseType(typeof(ResonanceResponse), 400)]
        public async Task<IActionResult> Analyze([FromBody] ResonanceRequest request)
        {
            try
            {
                // Validate input
                if (request?.Responses == null || request.Responses.Count == 0)
                {
                    return BadRequest(new ResonanceResponse
                    {
                        Success = false,
                        Message = "Invalid request",
                        Error = "Responses list cannot be empty"
                    });
                }

                // Call service
                var analysisResult = await _resonanceService.AnalyzeResonanceAsync(request);

                if (!analysisResult.Success)
                {
                    return BadRequest(analysisResult);
                }

                return Ok(analysisResult);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new ResonanceResponse
                {
                    Success = false,
                    Message = "Error processing resonance analysis",
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get available configuration options
        /// </summary>
        /// <returns>Configuration defaults and ranges</returns>
        [HttpGet("config")]
        [ProducesResponseType(typeof(object), 200)]
        public IActionResult GetConfiguration()
        {
            return Ok(new
            {
                defaults = new
                {
                    riMax = 2.0,
                    alphaBase = 0.5,
                    betaBase = 0.3,
                    gammaBase = 0.2,
                    stepsPerResponse = 8
                },
                description = "Resonance Framework Configuration",
                riMaxDescription = "Maximum resonance index (calibrate based on dataset)",
                alphaBaseDescription = "Base alpha coefficient for quantum mixing",
                betaBaseDescription = "Base beta coefficient for quantum mixing",
                gammaBaseDescription = "Base gamma coefficient for quantum mixing",
                stepsPerResponseDescription = "Quantum time steps per input response"
            });
        }

        /// <summary>
        /// Health check endpoint
        /// </summary>
        /// <returns>Service status</returns>
        [HttpGet("health")]
        [ProducesResponseType(typeof(object), 200)]
        public IActionResult Health()
        {
            return Ok(new
            {
                status = "ok",
                service = "ResonanceFramework",
                version = "1.0.0"
            });
        }
    }
}
