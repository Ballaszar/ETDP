using System;

namespace ETD.Api.Models
{
    public class MiraReviewFeedbackEntry
    {
        public int Id { get; set; }
        public string QualificationScopeKey { get; set; } = "qualification:unscoped";
        public int? QualificationId { get; set; }
        public string QualificationCode { get; set; } = string.Empty;
        public string QualificationDescription { get; set; } = string.Empty;
        public string ReportedBy { get; set; } = "Mira";
        public string SourceAgent { get; set; } = "SMI";
        public string ReviewContext { get; set; } = "agent-governance";
        public string ArtifactType { get; set; } = string.Empty;
        public string ArtifactReference { get; set; } = string.Empty;
        public string Severity { get; set; } = "medium";
        public string Status { get; set; } = "new";
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string RecommendedAction { get; set; } = string.Empty;
        public string SourceExcerpt { get; set; } = string.Empty;
        public string OperatorNotes { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ReviewedAtUtc { get; set; }
        public DateTime? ClosedAtUtc { get; set; }
    }
}
