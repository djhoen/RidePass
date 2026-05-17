namespace webapi.Controllers.API.Data.Tenant
{
    public class UpdateGiftCardSettingsRequest
    {
        public bool Enabled { get; set; } = true;
        public int MinCents { get; set; } = 1000;
        public int MaxCents { get; set; } = 50000;
    }
}
