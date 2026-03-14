namespace ETD.Api.DTOs
{
    public class ActivityDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? Order { get; set; }
        public int SubtopicId { get; set; }
    }

    public class CreateActivityDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? Order { get; set; }
        public int SubtopicId { get; set; }
    }

    public class UpdateActivityDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? Order { get; set; }
        public int SubtopicId { get; set; }
    }
}
