using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>Admin support override of a credits pass's remaining ride count (goodwill grant,
    /// mis-scan correction). The reason is required because every adjustment is audit-logged.</summary>
    public class AdjustSeasonPassCreditsRequest
    {
        [Range(0, 1000)] public int CreditsRemaining { get; set; }
        [Required] public string Reason { get; set; } = null!;
    }
}
