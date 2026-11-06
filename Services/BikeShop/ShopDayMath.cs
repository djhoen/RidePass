using Services.Repositories.Data.BikeShopData;

namespace Services.BikeShop
{
    /// <summary>
    /// The pure half of the bike shop "Today" sheet: which program a rental belongs to, what phase
    /// of its day it is in, and how the day's rows fold into groups, counts and filter lists. No
    /// I/O, so every rule is pinned by ShopDayMathTests instead of by the counter noticing.
    /// </summary>
    public static class ShopDayMath
    {
        public const string PhasePickup = "pickup";
        public const string PhaseOut = "out";
        public const string PhaseReturn = "return";
        public const string PhaseReserved = "reserved";
        public const string PhaseDone = "done";
        public const string PhaseCancelled = "cancelled";
        public const string PhaseUnpaid = "unpaid";

        public const string KindShopRental = "shop_rental";
        public const string KindEventType = "event_type";
        public const string KindPackage = "package";

        public const string ShopRentalKey = "shop_rental";
        public const string ShopRentalLabel = "Rentals";

        public const string ChannelCounter = "counter";
        public const string ChannelOnline = "online";

        /// <summary>
        /// Where a rental sits in the queried day. The day is [dayStartUtc, dayEndUtc), tenant-local
        /// midnight to midnight as UTC instants.
        ///
        /// pending   -> unpaid     (shown with a warning; counted under Unpaid)
        /// cancelled -> cancelled
        /// returned / damaged -> done
        /// out       -> return when it is due back by the end of the day (today or overdue), else out
        /// paid      -> reserved when it starts after the day, else pickup (due today, or overdue)
        ///
        /// failed and abandoned never reach here: the repository excludes them.
        /// </summary>
        public static string PhaseFor(string status, DateTime startsAtUtc, DateTime endsAtUtc,
            DateTime dayStartUtc, DateTime dayEndUtc) => status switch
        {
            "pending" => PhaseUnpaid,
            "cancelled" => PhaseCancelled,
            "returned" or "damaged" => PhaseDone,
            "out" => endsAtUtc < dayEndUtc ? PhaseReturn : PhaseOut,
            "paid" => startsAtUtc >= dayEndUtc ? PhaseReserved : PhasePickup,
            _ => status,
        };

        /// <summary>
        /// Whether a row is behind on the queried day: a return that was due before the day began
        /// (checked out, window over, still not in), or a pickup that should have happened before
        /// the day began (paid, never collected: a no-show still holding its reservation). Other
        /// phases are never overdue.
        /// </summary>
        public static bool IsOverdue(string phase, DateTime startsAtUtc, DateTime endsAtUtc, DateTime dayStartUtc) => phase switch
        {
            PhaseReturn => endsAtUtc < dayStartUtc,
            PhasePickup => startsAtUtc < dayStartUtc,
            _ => false,
        };

        /// <summary>Total minus what was paid: everything while pending, nothing otherwise.</summary>
        public static int BalanceDueFor(string status, int totalCents) =>
            status == "pending" ? Math.Max(0, totalCents) : 0;

        /// <summary>NULL sold_by means nobody at the counter sold it: it came in online.</summary>
        public static string ChannelFor(Guid? soldByUserId) =>
            soldByUserId is null ? ChannelOnline : ChannelCounter;

        /// <summary>
        /// The program a rental was booked through, in priority order: a package purchase that
        /// composed it wins, then the event it hangs off, otherwise it is one of the shop's own
        /// rentals. An event id whose event was not found (deleted, or not this tenant's) falls
        /// through to the shop rather than inventing a group.
        /// </summary>
        public static ShopDayProgramRef ResolveProgram(ShopDayRentalRow rental,
            ShopDayPackageLinkRow? package, ShopDayEventRow? ev)
        {
            if (package is not null)
            {
                return new ShopDayProgramRef(KindPackage, $"{KindPackage}:{package.PackageId}",
                    package.PackageName, null, int.MaxValue);
            }
            if (rental.EventId is not null && ev is not null)
            {
                return new ShopDayProgramRef(KindEventType, $"{KindEventType}:{ev.EventTypeId}",
                    ev.EventTypeName, ev.Color, ev.SortOrder);
            }
            return new ShopDayProgramRef(KindShopRental, ShopRentalKey, ShopRentalLabel, null, int.MinValue);
        }

        public static ShopDayCounts CountOf(IEnumerable<ShopDayRow> rows)
        {
            var counts = new ShopDayCounts();
            foreach (var r in rows)
            {
                switch (r.Phase)
                {
                    case PhasePickup: counts.PickupsDue++; break;
                    case PhaseOut: counts.OutNow++; break;
                    // Due back today is still out right now, so it counts in both.
                    case PhaseReturn: counts.ReturnsDue++; counts.OutNow++; break;
                    case PhaseReserved: counts.Reserved++; break;
                    case PhaseUnpaid: counts.Unpaid++; break;
                    case PhaseDone: counts.Returned++; break;
                }
            }
            return counts;
        }

        /// <summary>
        /// Folds the day's raw rows into the sheet. Groups come out Rentals first, then event types
        /// by their sort order (name as the tiebreak), then packages by name; rows inside a group
        /// by start time then customer name.
        /// </summary>
        public static ShopDaySheet Build(DateOnly date, string timezone, DateTime dayStartUtc,
            DateTime dayEndUtc, ShopDayRaw raw)
        {
            var linesByRental = raw.Lines
                .GroupBy(l => l.RentalId)
                .ToDictionary(g => g.Key, g => g.ToList());
            // A rental is composed by at most one package purchase; take the first if data ever
            // disagrees rather than duplicating the row across two package groups.
            var packageByRental = new Dictionary<Guid, ShopDayPackageLinkRow>();
            foreach (var p in raw.PackageLinks) packageByRental.TryAdd(p.RentalId, p);
            var eventById = new Dictionary<Guid, ShopDayEventRow>();
            foreach (var e in raw.Events) eventById.TryAdd(e.EventId, e);

            var groups = new Dictionary<string, (ShopDayProgramRef Program, List<ShopDayRow> Rows)>();
            foreach (var r in raw.Rentals)
            {
                packageByRental.TryGetValue(r.Id, out var package);
                ShopDayEventRow? ev = null;
                if (r.EventId is Guid eventId) eventById.TryGetValue(eventId, out ev);
                var program = ResolveProgram(r, package, ev);

                var row = new ShopDayRow
                {
                    RentalId = r.Id,
                    OrderNumber = r.OrderNumber,
                    Status = r.Status,
                    StartsAtUtc = AsUtc(r.StartsAt),
                    EndsAtUtc = AsUtc(r.EndsAt),
                    CheckedOutAtUtc = AsUtc(r.CheckedOutAt),
                    ReturnedAtUtc = AsUtc(r.ReturnedAt),
                    // A package books the rental under the buyer; fall back to that name when the
                    // rental itself carries none, so the sheet never shows a nameless package row.
                    CustomerName = FirstNonBlank(r.RenterName, package?.BuyerName),
                    CustomerPhone = FirstNonBlank(r.RenterPhone),
                    CustomerEmail = FirstNonBlank(r.RenterEmail),
                    CustomerUserId = r.RenterUserId,
                    Items = (linesByRental.GetValueOrDefault(r.Id) ?? new())
                        .Select(l => new ShopDayItem
                        {
                            Name = l.NameSnapshot,
                            VariantLabel = l.VariantLabel,
                            Quantity = l.Quantity,
                            UnitLabel = l.UnitLabel,
                            UnitSerial = l.UnitSerial,
                            CategoryId = l.ProductTypeId,
                            CategoryName = l.ProductTypeName,
                        }).ToList(),
                    Channel = ChannelFor(r.SoldByUserId),
                    EventTitle = program.Kind == KindEventType ? ev?.Title : null,
                    EventStartsAtUtc = program.Kind == KindEventType ? AsUtc(ev?.StartsAt) : null,
                    PackageName = program.Kind == KindPackage ? package?.PackageName : null,
                    SessionStartAtUtc = program.Kind == KindPackage ? AsUtc(package?.SessionStartAt) : null,
                    TotalCents = r.TotalCents,
                    DepositCents = r.DepositCents,
                    BalanceDueCents = BalanceDueFor(r.Status, r.TotalCents),
                    Phase = PhaseFor(r.Status, r.StartsAt, r.EndsAt, dayStartUtc, dayEndUtc),
                };
                row.Overdue = IsOverdue(row.Phase, r.StartsAt, r.EndsAt, dayStartUtc);

                if (!groups.TryGetValue(program.Key, out var bucket))
                {
                    bucket = (program, new List<ShopDayRow>());
                    groups[program.Key] = bucket;
                }
                bucket.Rows.Add(row);
            }

            var ordered = groups.Values
                .OrderBy(g => KindRank(g.Program.Kind))
                .ThenBy(g => g.Program.Kind == KindEventType ? g.Program.SortOrder : 0)
                .ThenBy(g => g.Program.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Program.Key, StringComparer.Ordinal)
                .ToList();

            var sheet = new ShopDaySheet
            {
                Date = date.ToString("yyyy-MM-dd"),
                Timezone = timezone,
            };
            var position = 0;
            foreach (var (program, rows) in ordered)
            {
                var sortedRows = rows
                    .OrderBy(x => x.StartsAtUtc)
                    .ThenBy(x => x.CustomerName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.RentalId)
                    .ToList();
                sheet.Groups.Add(new ShopDayGroup
                {
                    Key = program.Key,
                    Kind = program.Kind,
                    Label = program.Label,
                    Color = program.Color,
                    SortOrder = position++,
                    Counts = CountOf(sortedRows),
                    Rows = sortedRows,
                });
                sheet.Programs.Add(new ShopDayProgram { Key = program.Key, Label = program.Label, Kind = program.Kind });
            }
            sheet.Totals = CountOf(sheet.Groups.SelectMany(g => g.Rows));

            sheet.ProductTypes = raw.Lines
                .Where(l => l.ProductTypeId is not null)
                .GroupBy(l => l.ProductTypeId!.Value)
                .Select(g => new ShopDayProductType
                {
                    Id = g.Key,
                    Name = g.Select(l => l.ProductTypeName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? string.Empty,
                })
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Id)
                .ToList();

            return sheet;
        }

        private static int KindRank(string kind) => kind switch
        {
            KindShopRental => 0,
            KindEventType => 1,
            _ => 2,
        };

        private static string? FirstNonBlank(params string?[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            return null;
        }

        private static DateTime AsUtc(DateTime d) => Services.Helpers.TenantClock.AsUtc(d);
        private static DateTime? AsUtc(DateTime? d) => d.HasValue ? AsUtc(d.Value) : null;
    }

    /// <summary>One resolved program: the group a rental lands in.</summary>
    public sealed record ShopDayProgramRef(string Kind, string Key, string Label, string? Color, int SortOrder);
}
