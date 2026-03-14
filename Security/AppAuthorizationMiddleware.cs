using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ETD.Api.Security
{
    public class AppAuthorizationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly AppAuthorizationOptions _options;
        private readonly ActivationTokenService _tokenService;
        private readonly IHostEnvironment _environment;

        public AppAuthorizationMiddleware(
            RequestDelegate next,
            IOptions<AppAuthorizationOptions> options,
            ActivationTokenService tokenService,
            IHostEnvironment environment)
        {
            _next = next;
            _options = options.Value;
            _tokenService = tokenService;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!IsProtectedApiPath(context.Request.Path))
            {
                await _next(context);
                return;
            }

            if (AppAuthorizationBypass.IsBypassed(_environment, _options))
            {
                await _next(context);
                return;
            }

            if (_options.RequireApiKey && _options.ApiKeys.Count > 0)
            {
                var apiKey = context.Request.Headers["X-App-Api-Key"].FirstOrDefault();
                var keyValid = _options.ApiKeys.Any(x => string.Equals(x, apiKey, StringComparison.Ordinal));
                if (!keyValid)
                {
                    await WriteUnauthorized(context, "API key is missing or invalid.");
                    return;
                }
            }

            if (_options.RequireActivation && _options.ActivationKeys.Count > 0)
            {
                var token = context.Request.Headers["X-Activation-Token"].FirstOrDefault();
                if (!_tokenService.TryValidate(token, out _))
                {
                    await WriteUnauthorized(context, "Activation token is missing, invalid, or expired.");
                    return;
                }
            }

            await _next(context);
        }

        private static bool IsProtectedApiPath(PathString path)
        {
            if (!path.StartsWithSegments("/api")) return false;
            if (path.StartsWithSegments("/api/Activation")) return false;
            return true;
        }

        private static async Task WriteUnauthorized(HttpContext context, string message)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            var payload = JsonSerializer.Serialize(new { error = message });
            await context.Response.WriteAsync(payload);
        }
    }
}
