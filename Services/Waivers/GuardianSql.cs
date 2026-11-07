namespace Services.Waivers
{
    /// <summary>
    /// The SQL twin of GuardianWaiverRule.IsGuardianSigned for a rider_waiver_signature row: flagged as
    /// signed by a parent AND carrying the guardian's name. One fragment, used by every query that asks
    /// "is this a valid guardian signature?" (linking guards, Signed Waivers), so none can drift.
    /// </summary>
    public static class GuardianSql
    {
        /// <param name="alias">The rider_waiver_signature alias in the query (e.g. "s", "cur").</param>
        public static string IsGuardianSigned(string alias) =>
            $"({alias}.signed_by_parent AND COALESCE(trim({alias}.parent_name), '') <> '')";
    }
}
