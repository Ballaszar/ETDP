using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ETD.Api.Security;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ActivationController : ControllerBase
    {
        private readonly AppAuthorizationOptions _options;
        private readonly ActivationTokenService _tokenService;
        private readonly IHostEnvironment _environment;

        public ActivationController(
            IOptions<AppAuthorizationOptions> options,
            ActivationTokenService tokenService,
            IHostEnvironment environment)
        {
            _options = options.Value;
            _tokenService = tokenService;
            _environment = environment;
        }

        public class ActivateRequest
        {
            public string LecturerEmail { get; set; } = string.Empty;
            public string ActivationKey { get; set; } = string.Empty;
        }

        public class IssueRequest
        {
            public string LecturerEmail { get; set; } = string.Empty;
            public int? LifetimeHours { get; set; }
        }

        [HttpGet("status")]
        public IActionResult Status()
        {
            var bypassed = AppAuthorizationBypass.IsBypassed(_environment, _options);
            if (bypassed)
            {
                return Ok(new
                {
                    bypassed = true,
                    activated = true,
                    apiKeyRequired = false,
                    activationRequired = false,
                    machine = Environment.MachineName
                });
            }



            var apiKeyRequired = _options.RequireApiKey && _options.ApiKeys.Count > 0;
            var activationRequired = _options.RequireActivation && _options.ActivationKeys.Count > 0;
            var token = Request.Headers["X-Activation-Token"].FirstOrDefault();
            var tokenValid = _tokenService.TryValidateInfo(token, out var tokenInfo);
            var activated = !activationRequired || tokenValid;

            return Ok(new
            {
                bypassed = false,
                activated,
                expiresAtUtc = tokenValid ? tokenInfo.ExpiresAtUtc : (DateTimeOffset?)null,
                lecturerEmail = tokenValid ? tokenInfo.LecturerEmail : string.Empty,
                apiKeyRequired,
                activationRequired,
                machine = Environment.MachineName
            });
        }

        [HttpPost("activate")]
        public IActionResult Activate([FromBody] ActivateRequest request)
        {
            var bypassed = AppAuthorizationBypass.IsBypassed(_environment, _options);
            if (bypassed)
            {
                return Ok(new
                {
                    bypassed = true,
                    activated = true,
                    token = string.Empty,
                    expiresAtUtc = (DateTimeOffset?)null,
                    lecturerEmail = string.Empty
                });
            }

            if (!_options.RequireActivation || _options.ActivationKeys.Count == 0)
            {
                var openTokenExpiry = DateTimeOffset.UtcNow.AddHours(_options.TokenLifetimeHours);
                var openToken = _tokenService.CreateToken(openTokenExpiry, request?.LecturerEmail);
                return Ok(new
                {
                    bypassed = false,
                    activated = true,
                    token = openToken,
                    expiresAtUtc = openTokenExpiry,
                    lecturerEmail = request?.LecturerEmail?.Trim() ?? string.Empty
                });
            }

            var lecturerEmail = request?.LecturerEmail?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(lecturerEmail))
            {
                return BadRequest(new { error = "Lecturer email is required." });
            }

            var key = request?.ActivationKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                return BadRequest(new { error = "Activation key is required." });
            }

            var valid = _options.ActivationKeys.Any(x => string.Equals(x, key, StringComparison.Ordinal));
            if (!valid)
            {
                return Unauthorized(new { error = "Activation key is invalid." });
            }

            var expiresAt = DateTimeOffset.UtcNow.AddHours(_options.TokenLifetimeHours);
            var token = _tokenService.CreateToken(expiresAt, lecturerEmail);

            return Ok(new
            {
                bypassed = false,
                activated = true,
                token,
                expiresAtUtc = expiresAt,
                lecturerEmail
            });
        }

        // Admin-only token issuance for offline key distribution.
        // Protect with ETDP_ADMIN_SECRET environment variable and X-Admin-Secret header.
        [HttpPost("issue")]
        public IActionResult Issue([FromBody] IssueRequest request)
        {
            var adminSecret = Environment.GetEnvironmentVariable("ETDP_ADMIN_SECRET") ?? string.Empty;
            var provided = Request.Headers["X-Admin-Secret"].FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(adminSecret) || !string.Equals(adminSecret, provided, StringComparison.Ordinal))
            {
                return Unauthorized(new { error = "Admin secret missing or invalid." });
            }

            var lecturerEmail = request?.LecturerEmail?.Trim() ?? string.Empty;
            var hours = request?.LifetimeHours ?? _options.TokenLifetimeHours;
            var expiresAt = DateTimeOffset.UtcNow.AddHours(hours);
            var token = _tokenService.CreateToken(expiresAt, lecturerEmail);

            return Ok(new
            {
                bypassed = false,
                activated = true,
                token,
                expiresAtUtc = expiresAt,
                lecturerEmail
            });
        }
    }
}
