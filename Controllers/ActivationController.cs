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
            public string ActivationKey { get; set; } = string.Empty;
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
            var tokenValid = _tokenService.TryValidate(token, out var expiresAtUtc);
            var activated = !activationRequired || tokenValid;

            return Ok(new
            {
                bypassed = false,
                activated,
                expiresAtUtc = tokenValid ? expiresAtUtc : (DateTimeOffset?)null,
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
                    expiresAtUtc = (DateTimeOffset?)null
                });
            }

            if (!_options.RequireActivation || _options.ActivationKeys.Count == 0)
            {
                var openTokenExpiry = DateTimeOffset.UtcNow.AddHours(_options.TokenLifetimeHours);
                var openToken = _tokenService.CreateToken(openTokenExpiry);
                return Ok(new
                {
                    bypassed = false,
                    activated = true,
                    token = openToken,
                    expiresAtUtc = openTokenExpiry
                });
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
            var token = _tokenService.CreateToken(expiresAt);

            return Ok(new
            {
                bypassed = false,
                activated = true,
                token,
                expiresAtUtc = expiresAt
            });
        }
    }
}
