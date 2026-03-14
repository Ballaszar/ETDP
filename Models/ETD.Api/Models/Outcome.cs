namespace ETD.Api.Models
{
    public class Outcome
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public Subject? Subject { get; set; }
        public string OutcomeCode { get; set; } = string.Empty;
        public string OutcomeDescription { get; set; } = string.Empty;
        public int? Order { get; set; }
        public ICollection<Topic> Topics { get; set; } = new List<Topic>();
    }
}

