namespace ETD.Api.DTOs
{
    public class OutcomeDto
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public int QualificationId { get; set; }
        public string SubjectCode { get; set; } = string.Empty;
        public string OutcomeCode { get; set; } = string.Empty;
        public string OutcomeDescription { get; set; } = string.Empty;
        public int? Order { get; set; }
    }

    public class CreateOutcomeDto
    {
        public int SubjectId { get; set; }
        public string OutcomeCode { get; set; } = string.Empty;
        public string OutcomeDescription { get; set; } = string.Empty;
        public int? Order { get; set; }
    }

    public class UpdateOutcomeDto
    {
        public int SubjectId { get; set; }
        public string OutcomeCode { get; set; } = string.Empty;
        public string OutcomeDescription { get; set; } = string.Empty;
        public int? Order { get; set; }
    }
}

