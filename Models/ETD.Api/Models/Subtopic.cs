namespace ETD.Api.Models
{
    public class Subtopic
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? Order { get; set; }

        // Foreign key
        public int TopicId { get; set; }

        // Navigation
        public Topic? Topic { get; set; }
        public List<Activity> Activities { get; set; } = new();
    }
}
