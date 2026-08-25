using Services.Repositories.Data.AccountingData;

namespace Services.Accounting
{
    /// <summary>One resolved bucket a revenue line reports under.</summary>
    public sealed record ProfitCenterBucket(string Key, string Label, int SortOrder, string Color);

    /// <summary>
    /// Resolves a QuickBooks revenue slot to the bucket it reports under: the tenant's own profit
    /// centers when they have configured any, the static <see cref="QboDepartments"/> grouping
    /// otherwise. This is the ONE place reports go through, so the Revenue by Department report,
    /// the End of Day report and the QuickBooks mapping screen can never disagree about which
    /// bucket a dollar belongs to.
    ///
    /// A revenue key the tenant has not assigned anywhere falls back to its BUILT-IN department
    /// rather than to a catch-all, for the same reason QboDepartments falls unknown keys into
    /// Other instead of dropping them: a revenue line must always have a home, and the built-in
    /// home is a better guess than an undifferentiated bucket. Custom-bucket keys are prefixed
    /// ("pc:&lt;id&gt;") so they can never collide with the built-in department keys.
    /// </summary>
    public sealed class ProfitCenterMap
    {
        private readonly Dictionary<string, ProfitCenterBucket> _byRevenueKey;
        private readonly Dictionary<string, ProfitCenterBucket> _fallbackByDepartment;

        /// <summary>True when the tenant has configured their own centers.</summary>
        public bool IsCustom { get; }

        private ProfitCenterMap(bool isCustom,
            Dictionary<string, ProfitCenterBucket> byRevenueKey,
            Dictionary<string, ProfitCenterBucket> fallbackByDepartment)
        {
            IsCustom = isCustom;
            _byRevenueKey = byRevenueKey;
            _fallbackByDepartment = fallbackByDepartment;
        }

        /// <summary>The built-in QboDepartments grouping, shared by every unconfigured tenant.</summary>
        public static ProfitCenterMap BuiltIn()
        {
            return new ProfitCenterMap(false,
                new Dictionary<string, ProfitCenterBucket>(StringComparer.Ordinal),
                BuiltInBuckets());
        }

        public static ProfitCenterMap FromConfig(
            IReadOnlyList<ProfitCenter> centers,
            IReadOnlyList<ProfitCenterAssignment> assignments)
        {
            if (centers.Count == 0) return BuiltIn();

            var buckets = centers.ToDictionary(
                c => c.Id,
                c => new ProfitCenterBucket($"pc:{c.Id}", c.Name, c.SortOrder,
                    ProfitCenterPalette.IsValid(c.Color) ? c.Color!.Trim() : ProfitCenterPalette.Unassigned));

            var byKey = new Dictionary<string, ProfitCenterBucket>(StringComparer.Ordinal);
            foreach (var a in assignments)
            {
                // An assignment pointing at a center that no longer exists (can't happen with the
                // FK cascade, but this code should not trust that forever) simply falls back.
                if (buckets.TryGetValue(a.ProfitCenterId, out var bucket))
                {
                    byKey[a.RevenueKey] = bucket;
                }
            }
            return new ProfitCenterMap(true, byKey, BuiltInBuckets());
        }

        public ProfitCenterBucket ForRevenueKey(string? revenueKey)
        {
            if (revenueKey is not null && _byRevenueKey.TryGetValue(revenueKey, out var bucket))
            {
                return bucket;
            }
            var dept = QboDepartments.ForRevenueKey(revenueKey);
            return _fallbackByDepartment[dept];
        }

        private static Dictionary<string, ProfitCenterBucket> BuiltInBuckets()
        {
            var result = new Dictionary<string, ProfitCenterBucket>(StringComparer.Ordinal);
            for (var i = 0; i < QboDepartments.All.Length; i++)
            {
                var key = QboDepartments.All[i];
                // Built-in departments sort AFTER any custom centers, so a tenant's own buckets
                // lead the report and the fallbacks trail it. Custom sort_order is small ints.
                //
                // They get palette colors too, so a tenant who has never opened the Profit Centers
                // page still gets consistently-colored reports. "Other" is the catch-all, so it
                // takes the neutral gray rather than a hue that would claim to be a real unit.
                var color = key == QboDepartments.Other
                    ? ProfitCenterPalette.Unassigned
                    : ProfitCenterPalette.DefaultForIndex(i);
                result[key] = new ProfitCenterBucket(key, QboDepartments.Label(key), 10_000 + i, color);
            }
            return result;
        }
    }
}
