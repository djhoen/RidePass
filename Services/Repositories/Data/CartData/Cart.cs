namespace Services.Repositories.Data.CartData
{
    public class Cart
    {
        public List<CartItem> Items { get; set; } = new List<CartItem>();
        public decimal SubTotal { get; set; }
        public decimal CouponDiscount { get; set; }
        public decimal TaxesAndFees { get; set; }
        public decimal Total { get; set; }
        public string? Email { get; set; }
        public int? OrderSourceId { get; set; }
        public string? SelectedCurrency { get; set; }
    }

    public class CartItem
    {
        public int ProductId { get; set; }
        public string? Name { get; set; }
        public decimal Price { get; set; }
        public string? ImageUrl { get; set; }
        public int Qty { get; set; }
        public decimal SubTotal { get; set; }
        public List<SubItem>? Addons { get; set; }
    }

    public class SubItem
    {
        public int ProductId { get; set; }
        public string? Name { get; set; }
        public decimal Price { get; set; }
        public string? ImageUrl { get; set; }
        public bool Selected { get; set; }
        public decimal SubTotal { get; set; }
    }
}
