namespace Services.Scheduling.Handlers
{
    /// <summary>
    /// Shape of the scheduled_task.payload jsonb for kind = 'send_rider_message'.
    /// Both the controller-side enqueue and the handler-side execute use this
    /// type so the contract is in one place.
    /// </summary>
    public class SendRiderMessagePayload
    {
        public Guid EventId { get; set; }
        public List<Guid> PurchaseIds { get; set; } = new();
        public string Channel { get; set; } = "sms";   // 'sms' | 'email'
        public string? Subject { get; set; }            // email only
        public string Body { get; set; } = string.Empty;
    }
}
