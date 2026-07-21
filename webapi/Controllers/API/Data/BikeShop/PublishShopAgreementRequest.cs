using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>New terms for a shop agreement. Always published as a new version: existing
    /// signatures must keep meaning what they meant when they were given.</summary>
    public class PublishShopAgreementRequest
    {
        [Required, MaxLength(160)] public string Title { get; set; } = null!;
        [Required, MaxLength(20000)] public string Body { get; set; } = null!;
    }
}
