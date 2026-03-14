using System;

namespace ETD.Api.Models
{
    public class SystemErrorLog
    {
        public int Id { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = "server"; // server | client
        public string Severity { get; set; } = "error";
        public string Message { get; set; } = string.Empty;
        public string? StackTrace { get; set; }
        public string? Path { get; set; }
        public string? Method { get; set; }
        public int? StatusCode { get; set; }
        public string? CorrelationId { get; set; }
        public string? ClientCorrelationId { get; set; }
        public string? UserAgent { get; set; }
        public string? MachineName { get; set; }
        public string? ExtraJson { get; set; }
    }
}
