using System.Text.Json;
using ETD.Api.Data;
using ETD.Api.Models;

namespace ETD.Api.Services
{
    public class ExceptionLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionLoggingMiddleware> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public ExceptionLoggingMiddleware(
            RequestDelegate next,
            ILogger<ExceptionLoggingMiddleware> logger,
            IServiceScopeFactory scopeFactory)
        {
            _next = next;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                var correlationId = context.Items.TryGetValue("CorrelationId", out var cidObj)
                    ? cidObj?.ToString()
                    : Guid.NewGuid().ToString("N");

                _logger.LogError(ex, "Unhandled exception. CorrelationId={CorrelationId}", correlationId);

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    db.SystemErrorLogs.Add(new SystemErrorLog
                    {
                        Source = "server",
                        Severity = "error",
                        Message = ex.Message,
                        StackTrace = ex.ToString(),
                        Path = context.Request.Path,
                        Method = context.Request.Method,
                        StatusCode = 500,
                        CorrelationId = correlationId,
                        ClientCorrelationId = context.Request.Headers["X-Client-Correlation-Id"].FirstOrDefault(),
                        UserAgent = context.Request.Headers.UserAgent.ToString(),
                        MachineName = Environment.MachineName
                    });
                    await db.SaveChangesAsync();
                }
                catch (Exception logEx)
                {
                    _logger.LogError(logEx, "Failed to persist exception log.");
                }

                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                context.Response.Headers["X-Correlation-Id"] = correlationId ?? string.Empty;

                var payload = JsonSerializer.Serialize(new
                {
                    error = "An unexpected error occurred.",
                    correlationId
                });
                await context.Response.WriteAsync(payload);
            }
        }
    }
}
