using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class ImportShopCsvRequest
    {
        // The raw CSV text (client reads the picked file). ~2MB cap keeps a pasted binary or a
        // wrong file from tying the server up; a real 500-SKU catalog is a fraction of this.
        [Required, MaxLength(2_000_000)]
        public string Csv { get; set; } = "";
    }
}
