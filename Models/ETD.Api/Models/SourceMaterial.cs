namespace ETD.Api.Models
{
    public class SourceMaterial
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty; // pdf | docx | txt
        public string Url { get; set; } = string.Empty;
        public string? QualificationCode { get; set; }
        public string? QualificationDescription { get; set; }
        public string? SubjectDescription { get; set; }
        public string? TopicDescription { get; set; }
        public string? AssessmentCriteriaDescription { get; set; }
        public string? KnowledgeSourceType { get; set; }
        public int? KnowledgeNumber { get; set; }
        public string? KnowledgeLabel { get; set; }
        public string? KnowledgeRootPath { get; set; }
        public System.DateTime? KnowledgeUploadedAtUtc { get; set; }
        public string ExtractedText { get; set; } = string.Empty;
        public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;
    }
}
