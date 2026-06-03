namespace ETD.Api.Models
{
    public class SmiContentQuestionnaire
    {
        public int Id { get; set; }
        public int QualificationId { get; set; }
        public Qualification? Qualification { get; set; }
        public int SubjectId { get; set; }
        public Subject? Subject { get; set; }
        public int? PhaseId { get; set; }
        public string MainCategoryCode { get; set; } = string.Empty;
        public string MainCategoryLabel { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Questions { get; set; } = string.Empty;
    }
}
