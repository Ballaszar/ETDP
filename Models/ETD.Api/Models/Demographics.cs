namespace ETD.Api.Models
{
    public class Demographics
    {
        public int Id { get; set; }

        // FK to Qualification
        public int QualificationId { get; set; }
        public Qualification? Qualification { get; set; }

        public string AgeGroup { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;

        // Demographic counts
        public int Males { get; set; }
        public int Females { get; set; }
        public int African { get; set; }
        public int Whites { get; set; }
        public int Coloureds { get; set; }
        public int Asian { get; set; }
        public int WithDisabilities { get; set; }
        public int Other { get; set; }
        public int Total { get; set; }
        public int TotalNumberOfStudents { get; set; }
    }
}
