namespace ETD.Api.Models
{
    public class Subject
    {
        public int Id { get; set; }
        public string SubjectPurpose { get; set; } = string.Empty;
        public string SubjectCode { get; set; } = string.Empty;
        public string SubjectDescription { get; set; } = string.Empty;
        public int? SubjectCredits { get; set; }
        public int? SubjectNQFLevel { get; set; }
        public int? SubjectPercentage { get; set; }

        // FK to CurriculumPhase
        public int CurriculumPhaseId { get; set; }
        public CurriculumPhase? CurriculumPhase { get; set; }

        // FK to Qualification (required by controller)
        public int QualificationId { get; set; }
        public Qualification? Qualification { get; set; }

        // Navigation: Subject → Topics (1:N)
        public ICollection<Topic> Topics { get; set; } = new List<Topic>();
        public ICollection<Outcome> Outcomes { get; set; } = new List<Outcome>();
    }
}
