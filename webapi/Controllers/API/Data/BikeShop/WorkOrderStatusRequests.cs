using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>Create a custom work-order status. Always an extra 'open' working stage; its code is
    /// derived from the name server-side. Behavior-bearing statuses (estimate/ready/done/cancelled)
    /// are the fixed built-ins and can't be created.</summary>
    public class CreateWorkOrderStatusRequest
    {
        [Required, MaxLength(40)] public string Name { get; set; } = null!;
        [MaxLength(40)] public string Color { get; set; } = "grey";
        public bool NotifyCustomer { get; set; }
    }

    /// <summary>Update a status's presentation (built-in or custom). Code and behavior never change.</summary>
    public class UpdateWorkOrderStatusRequest
    {
        [Required, MaxLength(40)] public string Name { get; set; } = null!;
        [MaxLength(40)] public string Color { get; set; } = "grey";
        public bool NotifyCustomer { get; set; }
        [Range(0, 100000)] public int SortOrder { get; set; } = 100;
        public bool IsActive { get; set; } = true;
    }

    /// <summary>Bulk drag-drop reorder of the work-order stages.</summary>
    public class ReorderWorkOrderStatusesRequest
    {
        [Required, MinLength(1)] public List<ReorderWorkOrderStatusItem> Items { get; set; } = new();
    }

    public class ReorderWorkOrderStatusItem
    {
        [Required] public Guid Id { get; set; }
        [Range(0, 100000)] public int SortOrder { get; set; }
    }
}
