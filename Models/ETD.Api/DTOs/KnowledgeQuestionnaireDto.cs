namespace ETD.Api.DTOs
{
    public class KnowledgeQuestionnaireDto
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Questions { get; set; } = string.Empty;
    }

    public class CreateKnowledgeQuestionnaireDto
    {
        public int SubjectId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Questions { get; set; } = string.Empty;
    }

    public class UpdateKnowledgeQuestionnaireDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Questions { get; set; } = string.Empty;
    }
}
