namespace Services.Helpers.Interfaces
{
    public interface IEmailLinkTokens
    {
        /// <summary>
        /// Stateless, tamper-proof token embedded in a List-Unsubscribe / unsubscribe link.
        /// Carries the tenant + recipient so the endpoint can suppress without a DB lookup
        /// of a stored per-send token.
        /// </summary>
        string GenerateUnsubscribe(Guid? tenantId, string email);

        bool TryParseUnsubscribe(string token, out Guid? tenantId, out string email);
    }
}
