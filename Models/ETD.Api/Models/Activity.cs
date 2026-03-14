namespace ETD.Api.Models
{
    public class Activity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? Order { get; set; }

        // Foreign key
        public int SubtopicId { get; set; }

        // Navigation
        public Subtopic? Subtopic { get; set; }
    }
}
