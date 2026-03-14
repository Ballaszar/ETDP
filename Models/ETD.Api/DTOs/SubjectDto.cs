namespace ETD.Api.DTOs
{
    public class SubjectDto
    {
        public int Id { get; set; }
        public string SubjectPurpose { get; set; } = string.Empty;
        public string SubjectCode { get; set; } = string.Empty;
        public string PhasesCode { get; set; } = string.Empty;
        public string SubjectDescription { get; set; } = string.Empty;
        public string QualificationCode { get; set; } = string.Empty;
        public string LearningPhases { get; set; } = string.Empty;
        public int? SubjectCredits { get; set; }
        public int? SubjectNQFLevel { get; set; }
        public int? SubjectPercentage { get; set; }
        public int QualificationId { get; set; }
        public int CurriculumPhaseId { get; set; }
    }

    public class CreateSubjectDto
    {
        public string SubjectPurpose { get; set; } = string.Empty;
        public string PhasesCode { get; set; } = string.Empty;
        public string SubjectDescription { get; set; } = string.Empty;
        public int? SubjectCredits { get; set; }
        public int? SubjectNQFLevel { get; set; }
        public int? SubjectPercentage { get; set; }
        public int QualificationId { get; set; }
        public int CurriculumPhaseId { get; set; }
    }

    public class UpdateSubjectDto
    {
        public string SubjectPurpose { get; set; } = string.Empty;
        public string PhasesCode { get; set; } = string.Empty;
        public string SubjectDescription { get; set; } = string.Empty;
        public int? SubjectCredits { get; set; }
        public int? SubjectNQFLevel { get; set; }
        public int? SubjectPercentage { get; set; }
        public int QualificationId { get; set; }
        public int CurriculumPhaseId { get; set; }
    }
}
