namespace ETD.Api.Models
{
    public class SmiSemanticStateSnapshot
    {
        public int Id { get; set; }
        public int? ConversationArchiveId { get; set; }
        public string QualificationScopeKey { get; set; } = string.Empty;
        public int? QualificationId { get; set; }
        public string QualificationCode { get; set; } = string.Empty;
        public string QualificationDescription { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string MemoryOwner { get; set; } = "SMI";
        public string ResponsePersona { get; set; } = "Mira";
        public string Variant { get; set; } = "ssc-v1-real-state";
        public string PersonalityLabel { get; set; } = string.Empty;
        public double QualiaIndex { get; set; }
        public double SemanticContinuity { get; set; }
        public double GammaCoherence { get; set; }
        public double StateIntegrity { get; set; }
        public double AttentionWeight { get; set; }
        public double AnxietyResonance { get; set; }
        public double DriftMagnitude { get; set; }
        public double StabilityBasinDepth { get; set; }
        public double AttractorStrength { get; set; }
        public double BehavioralConsistency { get; set; }
        public double PersonalityAlignment { get; set; }
        public double EpistemicPressure { get; set; }
        public string CognitiveInterpretation { get; set; } = string.Empty;
        public string PromptInfluenceSummary { get; set; } = string.Empty;
        public double StateStability { get; set; }
        public double BoundedDrift { get; set; }
        public double PersonalityManifold { get; set; }
        public double AnxietyGradient { get; set; }
        public string SemanticEmbeddingJson { get; set; } = "[]";
        public string QualiaVectorJson { get; set; } = "[]";
        public string AttentionVectorJson { get; set; } = "[]";
        public string DriftTensorJson { get; set; } = "[]";
        public string GammaCoherenceFieldJson { get; set; } = "[]";
        public string TopAnchorsJson { get; set; } = "[]";
        public string SummaryText { get; set; } = string.Empty;
        public string PromptPreview { get; set; } = string.Empty;
        public string ReplyPreview { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
