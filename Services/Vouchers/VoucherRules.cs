namespace Services.Vouchers
{
    /// <summary>
    /// Pure rules for vouchers (Script0315), kept apart from the controllers so they are unit tested.
    /// </summary>
    public static class VoucherRules
    {
        public const int MaxCodesPerBatch = 500;
        public const int MaxCodesPerEmployeeGift = 25;

        /// <summary>
        /// How many more codes this person may give this year. Null = unlimited.
        /// Admins (tenant_admin, or a platform super admin acting for the venue) are always
        /// unlimited. Anyone else has what their allowance row says: none without a row, unlimited
        /// with a row whose allowance is null.
        /// </summary>
        public static int? Remaining(bool isAdmin, bool hasAllowanceRow, int? yearlyAllowance, int issuedThisYear)
        {
            if (isAdmin) return null;
            if (!hasAllowanceRow) return 0;
            if (yearlyAllowance is null) return null;
            return Math.Max(0, yearlyAllowance.Value - issuedThisYear);
        }

        /// <summary>
        /// The one ticket a voucher pays for: the most expensive unit for a rider (a race entry or a
        /// rider's gate fee), never a spectator's, with a price above zero. Ties go to the first unit
        /// in the cart. Null when the cart has no such unit.
        /// </summary>
        public static int? PickUnit(IReadOnlyList<(int Index, int PriceCents, bool RiderAudience)> units)
        {
            int? best = null;
            var bestPrice = 0;
            foreach (var u in units)
            {
                if (!u.RiderAudience || u.PriceCents <= 0) continue;
                if (best is null || u.PriceCents > bestPrice)
                {
                    best = u.Index;
                    bestPrice = u.PriceCents;
                }
            }
            return best;
        }

        /// <summary>A ticket tier a voucher can pay for: a race entry, or a gate fee for a rider.</summary>
        public static bool IsRiderAdmission(string tierKind, string? audience) =>
            tierKind == "race_entry" || (tierKind == "gate_fee" && audience == "rider");

        /// <summary>Start of the current calendar year in the venue's time zone, as UTC.</summary>
        public static DateTime YearStartUtc(string? timezoneId, DateTime nowUtc)
        {
            var today = Services.Helpers.TenantClock.LocalDate(timezoneId, nowUtc);
            return Services.Helpers.TenantClock.DayRangeUtc(timezoneId, new DateOnly(today.Year, 1, 1)).StartUtc;
        }
    }
}
