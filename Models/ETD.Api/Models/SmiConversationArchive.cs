namespace ETD.Api.Models
{
    public class SmiConversationArchive
    {
        public int Id { get; set; }
        public string QualificationScopeKey { get; set; } = string.Empty;
        public int? QualificationId { get; set; }
        public string QualificationCode { get; set; } = string.Empty;
        public string QualificationDescription { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string MemoryOwner { get; set; } = "SMI";
        public string ResponsePersona { get; set; } = "Mira";
        public string UserPrompt { get; set; } = string.Empty;
        public string AssistantReply { get; set; } = string.Empty;
        public string PromptPreview { get; set; } = string.Empty;
        public string ReplyPreview { get; set; } = string.Empty;
        public string MemoryKeywords { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
