using System.ComponentModel.DataAnnotations;

namespace JobTracker.Web.Autofill;

public sealed class ApplicantProfile
{
    public const string MonthPattern = @"^[0-9]{4}-(0[1-9]|1[0-2])$";

    [StringLength(150)] public string? FullName { get; set; }
    [StringLength(100)] public string? GivenName { get; set; }
    [StringLength(100)] public string? FamilyName { get; set; }
    [StringLength(100)] public string? PreferredName { get; set; }
    [StringLength(254), EmailAddress] public string? Email { get; set; }
    [StringLength(40)] public string? Phone { get; set; }
    [StringLength(150)] public string? AddressLine1 { get; set; }
    [StringLength(150)] public string? AddressLine2 { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? Region { get; set; }
    [StringLength(24)] public string? Postcode { get; set; }
    [StringLength(100)] public string? Country { get; set; }
    [StringLength(500), Url] public string? LinkedIn { get; set; }
    [StringLength(500), Url] public string? GitHub { get; set; }
    [StringLength(500), Url] public string? Portfolio { get; set; }
    [StringLength(160)] public string? NoticePeriod { get; set; }
    [StringLength(160)] public string? Availability { get; set; }
    [MaxLength(20)] public List<ExperienceEntry> Experience { get; set; } = [];
    [MaxLength(20)] public List<EducationEntry> Education { get; set; } = [];
    [MaxLength(20)] public List<CertificationEntry> Certifications { get; set; } = [];
}

public sealed class ExperienceEntry : IValidatableObject
{
    [Required, StringLength(180)] public string? Employer { get; set; }
    [Required, StringLength(180)] public string? Title { get; set; }
    [StringLength(150)] public string? Location { get; set; }
    [RegularExpression(ApplicantProfile.MonthPattern)] public string? StartMonth { get; set; }
    [RegularExpression(ApplicantProfile.MonthPattern)] public string? EndMonth { get; set; }
    public bool Current { get; set; }
    [StringLength(2500)] public string? Responsibilities { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Current && StartMonth is not null && EndMonth is not null
            && string.CompareOrdinal(EndMonth, StartMonth) < 0)
        {
            yield return new ValidationResult("End month must be on or after the start month.", [nameof(EndMonth)]);
        }
    }
}

public sealed class EducationEntry
{
    [Required, StringLength(180)] public string? Institution { get; set; }
    [Required, StringLength(180)] public string? Qualification { get; set; }
    [StringLength(150)] public string? FieldOfStudy { get; set; }
    [RegularExpression(ApplicantProfile.MonthPattern)] public string? StartMonth { get; set; }
    [RegularExpression(ApplicantProfile.MonthPattern)] public string? EndMonth { get; set; }
}

public sealed class CertificationEntry
{
    [Required, StringLength(180)] public string? Name { get; set; }
    [StringLength(180)] public string? Issuer { get; set; }
    [StringLength(140)] public string? CredentialId { get; set; }
    [StringLength(500), Url] public string? CredentialUrl { get; set; }
    [RegularExpression(ApplicantProfile.MonthPattern)] public string? IssuedMonth { get; set; }
    [RegularExpression(ApplicantProfile.MonthPattern)] public string? ExpiryMonth { get; set; }
}
