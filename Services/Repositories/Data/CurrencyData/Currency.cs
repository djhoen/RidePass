namespace Services.Repositories.Data.CurrencyData
{
    public class Currency
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string? Symbol { get; set; }
        public decimal ExchangeRate { get; set; }
        public DateTime? LastUpdated { get; set; }
    }
}
