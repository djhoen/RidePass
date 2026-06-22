namespace webapi.Controllers.API.Data.Event
{
    // Authenticated rider managing their own new-event notification channels from the profile
    // page. Both false = unsubscribe (the public Subscribe path requires at least one channel,
    // so a logged-in user needs this to fully opt out without a tokened email link).
    public class UpdateMyEventSubscriptionRequest
    {
        public bool NotifyEmail { get; set; }
        public bool NotifySms { get; set; }
    }
}
