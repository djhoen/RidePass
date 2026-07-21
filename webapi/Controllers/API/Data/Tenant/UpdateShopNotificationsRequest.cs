namespace webapi.Controllers.API.Data.Tenant
{
    /// <summary>The bike shop's customer-notification policy. Text is billed per message, so a
    /// tenant opts into it explicitly rather than it following whether Twilio happens to be set up.</summary>
    public class UpdateShopNotificationsRequest
    {
        public bool ReadyNotifyEmail { get; set; } = true;
        public bool ReadyNotifySms { get; set; }
        /// <summary>Days after pickup to email a service reminder; 0 turns it off.</summary>
        public int ServiceReminderDays { get; set; }
    }
}
