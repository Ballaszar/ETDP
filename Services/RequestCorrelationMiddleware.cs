namespace ETD.Api.Services
{
    public class RequestCorrelationMiddleware
    {
        private readonly RequestDelegate _next;

        public RequestCorrelationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var incoming = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
            var correlationId = string.IsNullOrWhiteSpace(incoming) ? Guid.NewGuid().ToString("N") : incoming.Trim();
            context.Items["CorrelationId"] = correlationId;
            context.Response.Headers["X-Correlation-Id"] = correlationId;
            await _next(context);
        }
    }
}
