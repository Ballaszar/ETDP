namespace ETD.Api.Models
{
    public class WorkExperienceLogbookEntry
    {
        public int Id { get; set; }

        public int WorkExperienceLogbookId { get; set; }
        public WorkExperienceLogbook? WorkExperienceLogbook { get; set; }

        public int SortOrder { get; set; }
        public string SubjectCode { get; set; } = string.Empty;
        public string TopicCode { get; set; } = string.Empty;
        public string TopicDescription { get; set; } = string.Empty;
        public string EntryDate { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }
}
