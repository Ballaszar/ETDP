namespace ETD.Api.Models
{
    public class WorkExperienceLogbook
    {
        public int Id { get; set; }

        public int? QualificationId { get; set; }
        public string QualificationNumber { get; set; } = string.Empty;

        public int? LearnerRegistrationId { get; set; }
        public LearnerRegistration? LearnerRegistration { get; set; }

        public string LearningInstitutionName { get; set; } = string.Empty;
        public string LearningInstitutionAddress { get; set; } = string.Empty;
        public string LearningInstitutionContactPerson { get; set; } = string.Empty;
        public string LearningInstitutionContactPhone { get; set; } = string.Empty;
        public string LearningInstitutionContactEmail { get; set; } = string.Empty;

        public string EmployerName { get; set; } = string.Empty;
        public string EmployerAddress { get; set; } = string.Empty;
        public string SupervisorName { get; set; } = string.Empty;
        public string SupervisorPhone { get; set; } = string.Empty;
        public string SupervisorEmail { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public List<WorkExperienceLogbookEntry> Entries { get; set; } = new();
    }
}
