namespace webapi.Controllers.API.Data.SmsSettings
{
    public class ProvisionSmsRequest
    {
        // E.164 number selected from a prior /Search result. Server validates
        // it against Twilio when it tries to buy — we don't re-search to
        // confirm because numbers can disappear from inventory between the
        // search and the purchase, and Twilio's purchase API is the only
        // authoritative source.
        public string PhoneNumber { get; set; } = null!;
    }
}
