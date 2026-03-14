namespace ETD.Api.Models
{
    public class LessonPlan
    {
        public int Id { get; set; }

        // FK to AssessmentCriteria
        public int AssessmentCriteriaId { get; set; }
        public AssessmentCriteria? AssessmentCriteria { get; set; }

        public string Title { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public DateTime? Date { get; set; }
        public int? DurationMinutes { get; set; }
        public string Content { get; set; } = string.Empty;
    }
}
