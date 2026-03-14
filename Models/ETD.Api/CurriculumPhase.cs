namespace ETD.Api.Models
{
    public class CurriculumPhase
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Sequence { get; set; }

        // Navigation: CurriculumPhase → Subjects (1:N)
        public ICollection<Subject> Subjects { get; set; } = new List<Subject>();
    }
}
