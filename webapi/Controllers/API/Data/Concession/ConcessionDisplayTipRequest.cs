using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionDisplayTipRequest
    {
        [Range(0, 100000)]   // $0 .. $1,000 sanity cap; the POS applies it and the sale re-validates
        public int TipCents { get; set; }
    }
}
