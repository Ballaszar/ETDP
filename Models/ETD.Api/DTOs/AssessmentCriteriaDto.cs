namespace ETD.Api.DTOs
{
    public class AssessmentCriteriaDto
    {
        public int Id { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? CriteriaType { get; set; }
        public double? Weight { get; set; }
        public int TopicId { get; set; }
    }

    public class CreateAssessmentCriteriaDto
    {
        public string Description { get; set; } = string.Empty;
        public string? CriteriaType { get; set; }
        public double? Weight { get; set; }
        public int TopicId { get; set; }
    }

    public class UpdateAssessmentCriteriaDto
    {
        public string Description { get; set; } = string.Empty;
        public string? CriteriaType { get; set; }
        public double? Weight { get; set; }
    }
}
