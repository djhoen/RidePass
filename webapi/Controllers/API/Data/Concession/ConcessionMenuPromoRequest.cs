using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionMenuPromoRequest
    {
        [Required, MaxLength(120)]
        public string Title { get; set; } = null!;
        [MaxLength(240)]
        public string? Subtitle { get; set; }
        public string? ImageUrl { get; set; }
        public Guid? MenuBoardId { get; set; }   // null = show on every menu board
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
