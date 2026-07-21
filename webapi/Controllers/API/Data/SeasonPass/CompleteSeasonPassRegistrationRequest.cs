using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>
    /// Post-payment registration for the season pass checkout: one entry per pass, naming the
    /// holder that pass admits and carrying their gate photo plus a waiver signature when the
    /// product requires one. Mirrors the event-ticket registration step — the difference is
    /// that a pass is always one holder, so there's no rider/ticket fan-out here.
    /// </summary>
    public class CompleteSeasonPassRegistrationRequest
    {
        [Required, MinLength(1)]
        public List<SeasonPassRegistrationItem> Passes { get; set; } = new();
    }
}
