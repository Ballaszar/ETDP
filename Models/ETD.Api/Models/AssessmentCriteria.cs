namespace ETD.Api.Models
{
    public class AssessmentCriteria
    {
        public int Id { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? CriteriaType { get; set; }
        public double? Weight { get; set; }

        // FK to Topic
        public int TopicId { get; set; }
        public Topic? Topic { get; set; }

        // Lesson plans linked to this criteria
        public ICollection<LessonPlan> LessonPlans { get; set; } = new List<LessonPlan>();
    }
}
