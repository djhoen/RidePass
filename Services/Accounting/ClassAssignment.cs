namespace Services.Accounting
{
    /// <summary>
    /// Turns a tenant's bucket-to-class mapping into the per-revenue-slot map
    /// <see cref="JournalEntryBuilder.Build"/> stamps onto revenue lines. Shared by the QuickBooks
    /// Online sync (class = QBO Class.Id) and the Desktop export (class = the Desktop full name), so
    /// both editions tag a day's revenue with exactly the same grouping the Revenue by Department
    /// report shows.
    ///
    /// Two hops: a revenue slot resolves to a reporting bucket via <see cref="ProfitCenterMap"/>, and
    /// a bucket to a class via the mapping. A bucket left unmapped contributes nothing, so its slots
    /// post unclassed rather than blocking the day: classes are presentation inside QuickBooks, never
    /// a correctness gate on the money.
    /// </summary>
    public static class ClassAssignment
    {
        /// <summary>
        /// Null when nothing resolves, which keeps an unconfigured tenant's draft byte-identical to
        /// the one built before classes existed.
        /// </summary>
        public static IReadOnlyDictionary<string, string>? ForRevenueSlots(
            ProfitCenterMap map,
            IReadOnlyDictionary<string, string> classByBucket)
        {
            if (classByBucket.Count == 0) return null;

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var revenueKey in QboAccountKeys.All.Where(k => k.StartsWith("revenue_", StringComparison.Ordinal)))
            {
                if (classByBucket.TryGetValue(map.ForRevenueKey(revenueKey).Key, out var classRef))
                {
                    result[revenueKey] = classRef;
                }
            }
            return result.Count > 0 ? result : null;
        }
    }
}
