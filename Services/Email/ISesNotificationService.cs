namespace Services.Email
{
    public enum SesHandleResult
    {
        Handled,        // parsed and actioned (or intentionally ignored)
        BadSignature,   // SNS signature failed verification -> reject
        Malformed,      // body wasn't valid SNS JSON -> reject
    }

    public interface ISesNotificationService
    {
        /// <summary>
        /// Processes one raw SNS POST body: verifies the SNS signature, auto-confirms a
        /// subscription handshake, and maps SES bounce/complaint notifications into the
        /// suppression list. Network egress (cert fetch, subscribe confirmation) is the only
        /// part that needs the live SES/SNS topic; parsing + mapping are self-contained.
        /// </summary>
        Task<SesHandleResult> HandleAsync(string rawJson);
    }
}
