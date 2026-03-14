namespace ETD.Api.DTOs
{
    public class SubtopicDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? Order { get; set; }
        public int TopicId { get; set; }
    }

    public class CreateSubtopicDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? Order { get; set; }
        public int TopicId { get; set; }
    }

    public class UpdateSubtopicDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? Order { get; set; }
        public int TopicId { get; set; }
    }
}
