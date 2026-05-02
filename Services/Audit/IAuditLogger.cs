namespace Services.Audit
{
    /// <summary>
    /// Convenience service for writing audit_log entries from request handlers. Pulls actor + ip
    /// from the ambient HttpContext (registered Scoped) so callers only supply action + summary +
    /// optional metadata.
    /// </summary>
    public interface IAuditLogger
    {
        Task Log(
            string action,
            string summary,
            string? targetKind = null,
            Guid? targetId = null,
            Guid? tenantId = null,
            object? metadata = null);
    }
}
