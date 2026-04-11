namespace Services.Repositories.Data.OrderData
{
    public class OrderItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public decimal Price { get; set; }
        public int Qty { get; set; }
        public int? ParentOrderItemId { get; set; }
        public int ShipStatusId { get; set; }
        public DateTime? ShipDate { get; set; }
        public string? ProductName { get; set; }
    }
}
