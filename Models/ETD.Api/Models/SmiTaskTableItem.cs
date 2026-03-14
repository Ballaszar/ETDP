namespace ETD.Api.Models
{
    public class SmiTaskTableItem
    {
        public int Id { get; set; }
        public string QualificationScopeKey { get; set; } = string.Empty;
        public int? QualificationId { get; set; }
        public string QualificationCode { get; set; } = string.Empty;
        public string QualificationDescription { get; set; } = string.Empty;
        public string TaskKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
        public string AssignedAgent { get; set; } = "SMI";
        public string Status { get; set; } = "Pending";
        public int SortOrder { get; set; }
        public string LastConfirmationSource { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAtUtc { get; set; }
    }
}
