namespace ETD.Api.DTOs
{
    public class LearnerGuideDto
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class CreateLearnerGuideDto
    {
        public int SubjectId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class UpdateLearnerGuideDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Content { get; set; } = string.Empty;
    }
}
