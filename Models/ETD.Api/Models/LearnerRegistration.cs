namespace ETD.Api.Models
{
    public class LearnerRegistration
    {
        public int Id { get; set; }
        public int? QualificationId { get; set; }

        public string? SdpAccreditationNumber { get; set; }
        public string? NationalId { get; set; }
        public string? LearnerAlternateId { get; set; }
        public string? AlternateIdType { get; set; }
        public string? LearnerLastName { get; set; }
        public string? LearnerFirstName { get; set; }
        public string? LearnerMiddleName { get; set; }
        public string? LearnerTitle { get; set; }
        public string? LearnerBirthDate { get; set; }
        public string? EquityCode { get; set; }
        public string? NationalityCode { get; set; }
        public string? HomeLanguageCode { get; set; }
        public string? GenderCode { get; set; }
        public string? CitizenStatusCode { get; set; }
        public string? SocioeconomicCode { get; set; }
        public string? DisabilityCode { get; set; }
        public string? DisabilityRating { get; set; }
        public string? ImmigrantStatus { get; set; }
        public string? HomeAddress1 { get; set; }
        public string? HomeAddress2 { get; set; }
        public string? HomeAddress3 { get; set; }
        public string? PostalAddress1 { get; set; }
        public string? PostalAddress2 { get; set; }
        public string? PostalAddress3 { get; set; }
        public string? LearnerHomeAddressPostalCode { get; set; }
        public string? LearnerHomeAddressPhysicalCode { get; set; }
        public string? LearnerPhoneNumber { get; set; }
        public string? LearnerCellPhoneNumber { get; set; }
        public string? LearnerFaxNumber { get; set; }
        public string? LearnerEmailAddress { get; set; }
        public string? ProvinceCode { get; set; }
        public string? StatssaAreaCode { get; set; }
        public string? PopiActAgree { get; set; }
        public string? PopiActDate { get; set; }
        public string? SkillsProgrammeId { get; set; }
        public string? EmploymentStatus { get; set; }
        public string? LearnerEnrolledDate { get; set; }
        public string? DateOfFisa { get; set; }
        public string? FinalFisaResult { get; set; }
        public string? DateSubmittedToQcto { get; set; }
        public string? LearningInstitutionName { get; set; }
        public string? LearningInstitutionProvince { get; set; }
        public string? LearningInstitutionCityTown { get; set; }
        public string? LearningInstitutionStreetName { get; set; }
        public string? LearningInstitutionStreetNumber { get; set; }
        public string? LearningInstitutionCityTownPhysicalCode { get; set; }
        public string? LearningInstitutionContactPerson { get; set; }
        public string? LearningInstitutionContactPersonPhoneNumber { get; set; }
        public string? LearningInstitutionContactPersonEmailAddress { get; set; }
        public string? WorkExperienceEmployerName { get; set; }
        public string? WorkExperienceEmployerStreetNumber { get; set; }
        public string? WorkExperienceEmployerStreetName { get; set; }
        public string? WorkExperienceEmployerCityTown { get; set; }
        public string? WorkExperienceEmployerProvince { get; set; }
        public string? WorkExperienceEmployerCityTownCode { get; set; }
        public string? WorkExperienceEmployerSupervisorName { get; set; }
        public string? WorkExperienceEmployerSupervisorPhoneNumber { get; set; }
        public string? WorkExperienceEmployerSupervisorEmailAddress { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
