using System;

namespace ETD.Api.Models
{
    public class AutomationJob
    {
        public int Id { get; set; }
        public string JobType { get; set; } = "build_qualification";
        public int QualificationId { get; set; }
        public string QualificationNumber { get; set; } = "94020";
        public string Status { get; set; } = "Queued"; // PendingApproval | Queued | Running | Completed | Failed | Cancelled
        public bool RequiresApproval { get; set; }
        public string? RequestedBy { get; set; }
        public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string? ConfigJson { get; set; }
        public string? OutputPath { get; set; }
        public string? Log { get; set; }
        public string? Error { get; set; }
    }
}
