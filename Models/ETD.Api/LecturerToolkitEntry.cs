namespace ETD.Api.Models
{
    public class LecturerToolkitEntry
    {
        public int Id { get; set; }
        public int QualificationsId { get; set; }
        public string LearningInstitutionName { get; set; } = string.Empty;
        public string LecturerName { get; set; } = string.Empty;
        public string SubjectCode { get; set; } = string.Empty;
        public string SubjectDescription { get; set; } = string.Empty;
        public int? AssessmentCriteriaId { get; set; }
        public string? AssessmentCriteriaDescription { get; set; }
        public string Lpn { get; set; } = string.Empty;
        public string LessonPlanDescription { get; set; } = string.Empty;
        public string LessonPlanContent { get; set; } = string.Empty;
        public string TimeStart { get; set; } = string.Empty;
        public string TimeEnd { get; set; } = string.Empty;
        public string LecturerActions { get; set; } = string.Empty;
        public string LearnerActions { get; set; } = string.Empty;
        public string LearningAids { get; set; } = string.Empty;
    }
}
