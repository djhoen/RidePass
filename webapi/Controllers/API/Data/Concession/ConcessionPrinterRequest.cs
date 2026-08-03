using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    // Create/update a kitchen ticket printer.
    public class ConcessionPrinterRequest
    {
        [Required, MaxLength(80)]
        public string Name { get; set; } = null!;

        // The printer's ePOS-Print endpoint, e.g. https://192.168.1.50. Must be https: the POS page
        // is served over https and browsers block mixed content, so an http address never prints.
        [Required, MaxLength(400)]
        public string Url { get; set; } = null!;

        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;

        // Stations this printer handles. Leave EMPTY to print the whole order, which is the setup
        // for a single printer at the pass while the cook screens stay split by station.
        public List<Guid> StationIds { get; set; } = new();
    }
}
