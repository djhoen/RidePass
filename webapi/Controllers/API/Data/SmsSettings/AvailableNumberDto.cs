namespace webapi.Controllers.API.Data.SmsSettings
{
    public class AvailableNumberDto
    {
        public string PhoneNumber { get; set; } = null!;   // E.164, the value to send back to Provision
        public string FriendlyName { get; set; } = null!;  // pretty-formatted for the UI list
        public string Region { get; set; } = "";
        public string IsoCountry { get; set; } = "";
    }
}
