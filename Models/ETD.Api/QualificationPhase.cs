namespace ETD.Api.Models
{
    public class QualificationPhase
    {
        public int Id { get; set; }

        public int QualificationId { get; set; }
        public int CurriculumPhaseId { get; set; }

        public CurriculumPhase? CurriculumPhase { get; set; }
        // Optionally: public Qualification Qualification { get; set; }
    }
}
