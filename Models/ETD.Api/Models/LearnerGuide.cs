namespace ETD.Api.Models
{
    public class LearnerGuide
    {
        public int Id { get; set; }

        // FK to Subject
        public int SubjectId { get; set; }
        public Subject? Subject { get; set; }

        public string Title { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Content { get; set; } = string.Empty;
    }
}
