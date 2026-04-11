namespace Services.Repositories.Data.AddressData
{
    public class Address
    {
        public int Id { get; set; }
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? City { get; set; }
        public string? StateCode { get; set; }
        public string? Zip { get; set; }
        public string? CountryCode { get; set; }
        public string? Name { get; set; }
    }
}
