namespace ETD.Api.DTOs
{
    public class WorkbookDto
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class CreateWorkbookDto
    {
        public int SubjectId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class UpdateWorkbookDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Content { get; set; } = string.Empty;
    }
}
