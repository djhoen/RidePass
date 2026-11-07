namespace webapi.Helpers
{
    /// <summary>
    /// What completing a pushed step came to: the signature / photo it holds (written now, or reused from an
    /// earlier attempt of the same request), or an Error. With an EndReason the request can't be completed as
    /// sent (the waiver changed, the rider's age crossed 18) and is ended so staff send it again; without one
    /// the kiosk may simply try again.
    /// </summary>
    public sealed record KioskPushOutcome(Guid? SignatureId, Guid? PhotoId, string? Error, string? EndReason)
    {
        public static KioskPushOutcome Retry(string error) => new(null, null, error, null);
        public static KioskPushOutcome End(string endReason, string error) => new(null, null, error, endReason);
    }
}
