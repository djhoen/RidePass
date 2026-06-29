using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    // The POS sends a manager PIN to confirm it authorizes a gated action and to show the manager's name
    // ("Approved by Jane") before the sale is rung. The sale request re-sends the PIN so the server
    // re-verifies it authoritatively at checkout.
    public class ConcessionVerifyManagerPinRequest
    {
        [Required, MaxLength(8)] public string Pin { get; set; } = null!;
    }
}
