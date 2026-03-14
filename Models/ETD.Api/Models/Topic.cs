namespace ETD.Api.Models
{
    public class Topic
    {
        public int Id { get; set; }
        public string TopicPurpose { get; set; } = string.Empty;
        public string TopicCode { get; set; } = string.Empty;
        public string? TopicDescription { get; set; }
        public double? SubjectCredits { get; set; }
        public double? NotionalHours { get; set; }
        public double? PeriodsPerTopic { get; set; }
        public bool PeriodsPerTopicManualOverride { get; set; } = false;
        public int? TopicCredits { get; set; }
        public int? TopicPercentage { get; set; }
        public int? Order { get; set; }

        // Foreign key
        public int SubjectId { get; set; }
        public int? OutcomeId { get; set; }

        // Navigation
        public Subject? Subject { get; set; }
        public Outcome? Outcome { get; set; }
        public List<Subtopic> Subtopics { get; set; } = new();
    }
}
