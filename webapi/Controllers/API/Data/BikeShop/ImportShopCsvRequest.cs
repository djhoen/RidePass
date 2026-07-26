using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class ImportShopCsvRequest
    {
        // The raw CSV text (client reads the picked file). ~2MB cap keeps a pasted binary or a
        // wrong file from tying the server up; a real 500-SKU catalog is a fraction of this.
        [Required, MaxLength(2_000_000)]
        public string Csv { get; set; } = "";

        /// <summary>
        /// Refresh rows that already exist instead of rejecting the file. Off by default: a first
        /// load into a live catalog should still refuse rather than quietly rewrite it. On, each
        /// row is matched to an existing variant by barcode, then MPN, then SKU, and ONLY the
        /// columns the file carried are written, so a cost-only export never blanks retail prices.
        /// Stock is never touched either way; it moves through the movement ledger.
        /// </summary>
        public bool UpdateExisting { get; set; }
    }
}
