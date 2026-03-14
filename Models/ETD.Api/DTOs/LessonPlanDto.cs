namespace ETD.Api.DTOs
{
    public class LessonPlanDto
    {
        public int Id { get; set; }
        public int AssessmentCriteriaId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public DateTime? Date { get; set; }
        public int? DurationMinutes { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class CreateLessonPlanDto
    {
        public int AssessmentCriteriaId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public DateTime? Date { get; set; }
        public int? DurationMinutes { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class UpdateLessonPlanDto
    {
        public string Title { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public DateTime? Date { get; set; }
        public int? DurationMinutes { get; set; }
        public string Content { get; set; } = string.Empty;
    }
}
