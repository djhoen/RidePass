using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SmsSettings
{
    /// <summary>
    /// Save-draft body. All fields are optional at the API surface — the
    /// admin can save a partially-filled form and come back later. Validation
    /// of "is this complete enough to submit?" lives on the Submit path so
    /// a draft save can't be blocked by a missing field the admin hasn't
    /// filled in yet.
    /// </summary>
    public class SaveTollfreeVerificationRequest
    {
        [MaxLength(200)] public string? BusinessName { get; set; }
        [MaxLength(500)] public string? BusinessWebsite { get; set; }
        [MaxLength(200)] public string? BusinessStreetAddress { get; set; }
        [MaxLength(100)] public string? BusinessCity { get; set; }
        [MaxLength(100)] public string? BusinessStateProvinceRegion { get; set; }
        [MaxLength(20)]  public string? BusinessPostalCode { get; set; }
        [MaxLength(2)]   public string? BusinessCountry { get; set; }      // ISO-3166 alpha-2

        [MaxLength(100)] public string? BusinessContactFirstName { get; set; }
        [MaxLength(100)] public string? BusinessContactLastName { get; set; }
        [MaxLength(200)] public string? BusinessContactEmail { get; set; }
        [MaxLength(20)]  public string? BusinessContactPhone { get; set; }

        [MaxLength(200)] public string? NotificationEmail { get; set; }

        public string[]? UseCaseCategories { get; set; }
        [MaxLength(2000)] public string? UseCaseSummary { get; set; }
        public string[]? ProductionMessageSamples { get; set; }

        [MaxLength(50)]  public string? OptInType { get; set; }
        public string[]? OptInImageUrls { get; set; }

        [MaxLength(20)]  public string? MessageVolume { get; set; }
        [MaxLength(2000)] public string? AdditionalInformation { get; set; }
    }
}
