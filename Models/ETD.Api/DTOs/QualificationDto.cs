namespace ETD.Api.DTOs
{

    public class QualificationDto
    {
        public int Id { get; set; }
        public string QualificationNumber { get; set; } = string.Empty;
        public string QualificationDescription { get; set; } = string.Empty;
        public string CesmField { get; set; } = string.Empty;
        public string NqfLevel { get; set; } = string.Empty;
        public string Credits { get; set; } = string.Empty;
        public string LearningInstitutionName { get; set; } = string.Empty;
        public string AccreditationNumber { get; set; } = string.Empty;
        public string? DeanPrincipalCEO { get; set; }
        public string SeniorLecturer { get; set; } = string.Empty;
        public string? LogoPath { get; set; }
        public string QualificationType { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public DateTime? LearningDateStart { get; set; }
        public DateTime? LearningDateEnd { get; set; }
        public bool UsesOutcomes { get; set; }
    }


    public class CreateQualificationDto
    {
        public string QualificationNumber { get; set; } = string.Empty;
        public string QualificationDescription { get; set; } = string.Empty;
        public string CesmField { get; set; } = string.Empty;
        public string NqfLevel { get; set; } = string.Empty;
        public string Credits { get; set; } = string.Empty;
        public string LearningInstitutionName { get; set; } = string.Empty;
        public string AccreditationNumber { get; set; } = string.Empty;
        public string? DeanPrincipalCEO { get; set; }
        public string SeniorLecturer { get; set; } = string.Empty;
        public string? LogoPath { get; set; }
        public string QualificationType { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public DateTime? LearningDateStart { get; set; }
        public DateTime? LearningDateEnd { get; set; }
        public bool UsesOutcomes { get; set; }
    }

    public class UpdateQualificationDto
    {
        public string QualificationNumber { get; set; } = string.Empty;
        public string QualificationDescription { get; set; } = string.Empty;
        public string CesmField { get; set; } = string.Empty;
        public string NqfLevel { get; set; } = string.Empty;
        public string Credits { get; set; } = string.Empty;
        public string LearningInstitutionName { get; set; } = string.Empty;
        public string AccreditationNumber { get; set; } = string.Empty;
        public string? DeanPrincipalCEO { get; set; }
        public string SeniorLecturer { get; set; } = string.Empty;
        public string? LogoPath { get; set; }
        public string QualificationType { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public DateTime? LearningDateStart { get; set; }
        public DateTime? LearningDateEnd { get; set; }
        public bool UsesOutcomes { get; set; }
    }
}
