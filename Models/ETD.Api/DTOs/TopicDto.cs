using System.Text.Json.Serialization;

namespace ETD.Api.DTOs
{
    public class TopicDto
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public int? OutcomeId { get; set; }
        public string? OutcomeCode { get; set; }
        public string? OutcomeDescription { get; set; }
        public int QualificationId { get; set; }
        public string SubjectCode { get; set; } = string.Empty;
        public string PhasesCode { get; set; } = string.Empty;
        public string SubjectDescription { get; set; } = string.Empty;
        public double? SubjectCredits { get; set; }
        public double? NotionalHours { get; set; }
        public double? PeriodsPerTopic { get; set; }
        public bool PeriodsPerTopicManualOverride { get; set; }
        public string TopicPurpose { get; set; } = string.Empty;
        public string TopicCode { get; set; } = string.Empty;
        public string? TopicDescription { get; set; }
        public int? TopicCredits { get; set; }
        public int? TopicPercentage { get; set; }
        public int? Order { get; set; }
        public int? AssessmentCriteriaId { get; set; }
        public string? AssessmentCriteriaDescription { get; set; }
    }

    public class CreateTopicDto
    {
        public string TopicPurpose { get; set; } = string.Empty;
        public string TopicCode { get; set; } = string.Empty;
        public string? TopicDescription { get; set; }
        public double? SubjectCredits { get; set; }
        public double? NotionalHours { get; set; }
        [JsonPropertyName("NationalHours")]
        public double? LegacyNationalHours
        {
            set => NotionalHours = value;
        }
        public double? PeriodsPerTopic { get; set; }
        public int? TopicCredits { get; set; }
        public int? TopicPercentage { get; set; }
        public int? Order { get; set; }
        public int SubjectId { get; set; }
        public int? OutcomeId { get; set; }
        public string? AssessmentCriteriaDescription { get; set; }
    }

    public class UpdateTopicDto
    {
        public string TopicPurpose { get; set; } = string.Empty;
        public string TopicCode { get; set; } = string.Empty;
        public string? TopicDescription { get; set; }
        public double? SubjectCredits { get; set; }
        public double? NotionalHours { get; set; }
        [JsonPropertyName("NationalHours")]
        public double? LegacyNationalHours
        {
            set => NotionalHours = value;
        }
        public double? PeriodsPerTopic { get; set; }
        public int? TopicCredits { get; set; }
        public int? TopicPercentage { get; set; }
        public int? Order { get; set; }
        public int SubjectId { get; set; }
        public int? OutcomeId { get; set; }
        public string? AssessmentCriteriaDescription { get; set; }
    }
}
