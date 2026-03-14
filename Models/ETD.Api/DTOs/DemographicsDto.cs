namespace ETD.Api.DTOs
{
    public class DemographicsDto
    {
        public int Id { get; set; }
        public int QualificationId { get; set; }
        public string AgeGroup { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public int NumberOfMales { get; set; }
        public int NumberOfFemales { get; set; }
        public int NumberAfrican { get; set; }
        public int NumberWhites { get; set; }
        public int NumberColoureds { get; set; }
        public int NumberAsian { get; set; }
        public int NumberWithDisabilities { get; set; }
        public int Other { get; set; }
        public int Total { get; set; }
        public int TotalNumberOfStudents { get; set; }
    }

    public class CreateDemographicsDto
    {
        public int QualificationId { get; set; }
        public string AgeGroup { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public int NumberOfMales { get; set; }
        public int NumberOfFemales { get; set; }
        public int NumberAfrican { get; set; }
        public int NumberWhites { get; set; }
        public int NumberColoureds { get; set; }
        public int NumberAsian { get; set; }
        public int NumberWithDisabilities { get; set; }
        public int Other { get; set; }
        public int Total { get; set; }
        public int TotalNumberOfStudents { get; set; }
    }

    public class UpdateDemographicsDto
    {
        public string AgeGroup { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public int NumberOfMales { get; set; }
        public int NumberOfFemales { get; set; }
        public int NumberAfrican { get; set; }
        public int NumberWhites { get; set; }
        public int NumberColoureds { get; set; }
        public int NumberAsian { get; set; }
        public int NumberWithDisabilities { get; set; }
        public int Other { get; set; }
        public int Total { get; set; }
        public int TotalNumberOfStudents { get; set; }
    }
}
