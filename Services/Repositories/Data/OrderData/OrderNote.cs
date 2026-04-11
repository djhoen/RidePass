namespace Services.Repositories.Data.OrderData
{
    public class OrderNote
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string Note { get; set; }
        public string? CreatedByUserId { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
