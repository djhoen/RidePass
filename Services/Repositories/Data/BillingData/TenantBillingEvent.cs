namespace Services.Repositories.Data.BillingData
{
    public class TenantBillingEvent
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        // 'sms' today; future: 'mms', 'voice_minute', etc.
        public string Kind { get; set; } = null!;
        // 'sms_send' for outbound SMS, future 'tenant_message' once the
        // inbound conversation feature lands.
        public string SourceTable { get; set; } = null!;
        // Twilio Message SID for sms / mms.
        public string SourceId { get; set; } = null!;
        // What Twilio charged us, in millionths of one dollar (so $0.00750 = 7500).
        public long TwilioCostMicros { get; set; }
        // What we charge the tenant in whole cents (computed once at insert time).
        public int BilledCents { get; set; }
        // The tenant_ledger_entry.id created when this event was attached to
        // the tenant's ledger as a negative SMS-charge adjustment. Null until
        // attached by SmsBillingPayoutAttacher.
        public Guid? PayoutEntryId { get; set; }
        public DateTime? PushedToPayoutAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
