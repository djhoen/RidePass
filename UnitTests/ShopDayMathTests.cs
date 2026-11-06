using NUnit.Framework;
using Services.BikeShop;
using Services.Repositories.Data.BikeShopData;

namespace UnitTests
{
    // The bike shop "Today" sheet's rules: which phase of the day a rental is in, which program
    // (shop / event type / package) it belongs to, and how the day's rows fold into ordered
    // groups whose counts add up. Pure functions, so every edge is pinned here rather than found
    // on a Saturday morning at the counter.
    [TestFixture]
    public class ShopDayMathTests
    {
        // A Highland business day: 2026-07-11 in America/New_York is 04:00Z to 04:00Z next day.
        private static readonly DateTime DayStart = new(2026, 7, 11, 4, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime DayEnd = new(2026, 7, 12, 4, 0, 0, DateTimeKind.Utc);

        private static DateTime Today(int hour) => DayStart.AddHours(hour);
        private static DateTime Tomorrow(int hour) => DayEnd.AddHours(hour);
        private static DateTime Yesterday(int hour) => DayStart.AddDays(-1).AddHours(hour);

        // ── Phase ─────────────────────────────────────────────────────────────

        [Test]
        public void Paid_StartingToday_IsPickup() =>
            Assert.That(ShopDayMath.PhaseFor("paid", Today(9), Today(17), DayStart, DayEnd), Is.EqualTo("pickup"));

        [Test]
        public void Paid_StartedYesterday_StillNotOut_IsPickup_Overdue() =>
            Assert.That(ShopDayMath.PhaseFor("paid", Yesterday(9), Today(17), DayStart, DayEnd), Is.EqualTo("pickup"));

        [Test]
        public void Paid_StartingAfterToday_IsReserved() =>
            Assert.That(ShopDayMath.PhaseFor("paid", Tomorrow(9), Tomorrow(17), DayStart, DayEnd), Is.EqualTo("reserved"));

        [Test]
        public void Paid_StartingExactlyAtMidnightTomorrow_IsReserved_HalfOpen() =>
            Assert.That(ShopDayMath.PhaseFor("paid", DayEnd, Tomorrow(8), DayStart, DayEnd), Is.EqualTo("reserved"));

        [Test]
        public void Out_EndingToday_IsReturn() =>
            Assert.That(ShopDayMath.PhaseFor("out", Yesterday(9), Today(17), DayStart, DayEnd), Is.EqualTo("return"));

        [Test]
        public void Out_EndedYesterday_IsReturn_Overdue() =>
            Assert.That(ShopDayMath.PhaseFor("out", Yesterday(9), Yesterday(17), DayStart, DayEnd), Is.EqualTo("return"));

        [Test]
        public void Out_EndingAfterToday_IsOut() =>
            Assert.That(ShopDayMath.PhaseFor("out", Today(9), Tomorrow(17), DayStart, DayEnd), Is.EqualTo("out"));

        [Test]
        public void Out_EndingExactlyAtMidnightTomorrow_IsOut_HalfOpen() =>
            Assert.That(ShopDayMath.PhaseFor("out", Today(9), DayEnd, DayStart, DayEnd), Is.EqualTo("out"));

        [TestCase("returned")]
        [TestCase("damaged")]
        public void ReturnedOrDamaged_IsDone(string status) =>
            Assert.That(ShopDayMath.PhaseFor(status, Today(9), Today(17), DayStart, DayEnd), Is.EqualTo("done"));

        [Test]
        public void Pending_IsUnpaid_WhateverTheWindow() =>
            Assert.That(ShopDayMath.PhaseFor("pending", Tomorrow(9), Tomorrow(17), DayStart, DayEnd), Is.EqualTo("unpaid"));

        [Test]
        public void Cancelled_IsCancelled() =>
            Assert.That(ShopDayMath.PhaseFor("cancelled", Today(9), Today(17), DayStart, DayEnd), Is.EqualTo("cancelled"));

        // ── Balance and channel ───────────────────────────────────────────────

        [TestCase("pending", 4500, 4500)]
        [TestCase("paid", 4500, 0)]
        [TestCase("out", 4500, 0)]
        [TestCase("returned", 4500, 0)]
        [TestCase("cancelled", 4500, 0)]
        [TestCase("pending", -5, 0)]
        public void BalanceDue_IsTheTotalWhilePending_NothingOtherwise(string status, int total, int expected) =>
            Assert.That(ShopDayMath.BalanceDueFor(status, total), Is.EqualTo(expected));

        [Test]
        public void Channel_IsOnlineWhenNobodySoldIt()
        {
            Assert.That(ShopDayMath.ChannelFor(null), Is.EqualTo("online"));
            Assert.That(ShopDayMath.ChannelFor(Guid.NewGuid()), Is.EqualTo("counter"));
        }

        // ── Program resolution ────────────────────────────────────────────────

        private static readonly Guid CampTypeId = Guid.NewGuid();
        private static readonly Guid AfterSchoolTypeId = Guid.NewGuid();
        private static readonly Guid PackageId = Guid.NewGuid();

        private static ShopDayEventRow Camp(Guid eventId) => new()
        {
            EventId = eventId, Title = "Kids Day Camp (Sat)", StartsAt = Today(13),
            EventTypeId = CampTypeId, EventTypeName = "Camp", EventTypeCode = "camp", Color = "#F57C00", SortOrder = 50,
        };

        private static ShopDayEventRow AfterSchool(Guid eventId) => new()
        {
            EventId = eventId, Title = "After School (Tue)", StartsAt = Today(19),
            EventTypeId = AfterSchoolTypeId, EventTypeName = "After School", EventTypeCode = "after_school", Color = "#00897B", SortOrder = 55,
        };

        private static ShopDayPackageLinkRow FindYourRide(Guid rentalId) => new()
        {
            RentalId = rentalId, PackageId = PackageId, PackageName = "Find Your Ride",
            SessionStartAt = Today(14), BuyerName = "Package Buyer",
        };

        private static ShopDayRentalRow Rental(string status = "paid", DateTime? starts = null, DateTime? ends = null,
            Guid? eventId = null, string? name = "Indie Granger", Guid? soldBy = null, int total = 6000)
            => new()
            {
                Id = Guid.NewGuid(), Status = status,
                StartsAt = starts ?? Today(9), EndsAt = ends ?? Today(17),
                EventId = eventId, RenterName = name, SoldByUserId = soldBy,
                TotalCents = total, DepositCents = 20000, CreatedAt = Yesterday(10),
            };

        [Test]
        public void Program_PackageWins_EvenWhenTheRentalAlsoHangsOffAnEvent()
        {
            var eventId = Guid.NewGuid();
            var r = Rental(eventId: eventId);
            var program = ShopDayMath.ResolveProgram(r, FindYourRide(r.Id), Camp(eventId));
            Assert.That(program.Kind, Is.EqualTo("package"));
            Assert.That(program.Key, Is.EqualTo($"package:{PackageId}"));
            Assert.That(program.Label, Is.EqualTo("Find Your Ride"));
            Assert.That(program.Color, Is.Null);
        }

        [Test]
        public void Program_EventTypeFromTheEvent_WhenNoPackage()
        {
            var eventId = Guid.NewGuid();
            var program = ShopDayMath.ResolveProgram(Rental(eventId: eventId), null, Camp(eventId));
            Assert.That(program.Kind, Is.EqualTo("event_type"));
            Assert.That(program.Key, Is.EqualTo($"event_type:{CampTypeId}"));
            Assert.That(program.Label, Is.EqualTo("Camp"));
            Assert.That(program.Color, Is.EqualTo("#F57C00"));
        }

        [Test]
        public void Program_EventIdWithNoEventFound_FallsBackToShopRental()
        {
            // The event lookup is tenant-scoped; an id that resolves to nothing must not invent a group.
            var program = ShopDayMath.ResolveProgram(Rental(eventId: Guid.NewGuid()), null, null);
            Assert.That(program.Kind, Is.EqualTo("shop_rental"));
            Assert.That(program.Key, Is.EqualTo("shop_rental"));
            Assert.That(program.Label, Is.EqualTo("Rentals"));
        }

        [Test]
        public void Program_PlainRental_IsShopRental()
        {
            var program = ShopDayMath.ResolveProgram(Rental(), null, null);
            Assert.That(program.Key, Is.EqualTo("shop_rental"));
        }

        // ── Build: the whole sheet ────────────────────────────────────────────

        private static ShopDayRaw SampleDay(out ShopDayRentalRow walkUp, out ShopDayRentalRow campRental,
            out ShopDayRentalRow afterSchoolRental, out ShopDayRentalRow packageRental)
        {
            var campEvent = Guid.NewGuid();
            var afterSchoolEvent = Guid.NewGuid();
            var staff = Guid.NewGuid();

            walkUp = Rental(name: "Zed Walker", soldBy: staff);                       // pickup, counter
            var walkUpLate = Rental(name: "Amy Early", soldBy: staff, starts: Today(9)); // same start, sorts by name
            var outNow = Rental(status: "out", starts: Today(8), ends: Tomorrow(17), name: "Out Overnight", soldBy: staff);
            var dueBack = Rental(status: "out", starts: Yesterday(8), ends: Today(16), name: "Due Back", soldBy: staff);
            var unpaid = Rental(status: "pending", name: "Not Paid", total: 4500);    // online (no sold_by)
            var reserved = Rental(starts: Tomorrow(9), ends: Tomorrow(17), name: "Next Week");
            var done = Rental(status: "returned", name: "All Done", soldBy: staff);
            var cancelled = Rental(status: "cancelled", name: "Gave Up", soldBy: staff);

            campRental = Rental(eventId: campEvent, name: "Camper One", soldBy: staff);
            afterSchoolRental = Rental(eventId: afterSchoolEvent, name: "After Schooler", soldBy: staff);
            packageRental = Rental(name: null, soldBy: null);                           // name from the package buyer

            var bikeType = Guid.NewGuid();
            var gearType = Guid.NewGuid();
            return new ShopDayRaw
            {
                Rentals = new() { reserved, cancelled, campRental, walkUp, outNow, packageRental, done, afterSchoolRental, unpaid, dueBack, walkUpLate },
                Lines = new()
                {
                    new() { RentalId = walkUp.Id, NameSnapshot = "Trek Fuel EX", VariantLabel = "M", Quantity = 1, UnitLabel = "Trek Fuel #3", UnitSerial = "WTU123", ProductTypeId = bikeType, ProductTypeName = "Bikes" },
                    new() { RentalId = walkUp.Id, NameSnapshot = "Full-face helmet", VariantLabel = "L", Quantity = 1, ProductTypeId = gearType, ProductTypeName = "Gear" },
                    new() { RentalId = campRental.Id, NameSnapshot = "Norco Fluid Kids", VariantLabel = "24\"", Quantity = 1, ProductTypeId = bikeType, ProductTypeName = "Bikes" },
                    new() { RentalId = packageRental.Id, NameSnapshot = "Giant Reign", VariantLabel = "L", Quantity = 1, ProductTypeId = bikeType, ProductTypeName = "Bikes" },
                },
                PackageLinks = new() { FindYourRide(packageRental.Id) },
                Events = new() { AfterSchool(afterSchoolEvent), Camp(campEvent) },
            };
        }

        [Test]
        public void Build_GroupsComeOutRentalsThenEventTypesBySortOrderThenPackages()
        {
            var raw = SampleDay(out _, out _, out _, out _);
            var sheet = ShopDayMath.Build(new DateOnly(2026, 7, 11), "America/New_York", DayStart, DayEnd, raw);

            Assert.That(sheet.Date, Is.EqualTo("2026-07-11"));
            Assert.That(sheet.Timezone, Is.EqualTo("America/New_York"));
            Assert.That(sheet.Groups.Select(g => g.Key), Is.EqualTo(new[]
            {
                "shop_rental", $"event_type:{CampTypeId}", $"event_type:{AfterSchoolTypeId}", $"package:{PackageId}",
            }));
            Assert.That(sheet.Groups.Select(g => g.SortOrder), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(sheet.Groups.Select(g => g.Label), Is.EqualTo(new[] { "Rentals", "Camp", "After School", "Find Your Ride" }));
            Assert.That(sheet.Groups[1].Color, Is.EqualTo("#F57C00"));
            Assert.That(sheet.Groups[0].Color, Is.Null);
            // Programs mirror the groups, in the same order.
            Assert.That(sheet.Programs.Select(p => p.Key), Is.EqualTo(sheet.Groups.Select(g => g.Key)));
            Assert.That(sheet.Programs.Select(p => p.Kind), Is.EqualTo(new[] { "shop_rental", "event_type", "event_type", "package" }));
        }

        [Test]
        public void Build_CountsAddUpToRows_PerGroupAndInTotal()
        {
            var raw = SampleDay(out _, out _, out _, out _);
            var sheet = ShopDayMath.Build(new DateOnly(2026, 7, 11), "America/New_York", DayStart, DayEnd, raw);

            var rentals = sheet.Groups[0];
            Assert.That(rentals.Rows.Count, Is.EqualTo(8));
            Assert.That(rentals.Counts.PickupsDue, Is.EqualTo(2));   // Zed Walker, Amy Early
            Assert.That(rentals.Counts.OutNow, Is.EqualTo(2));       // Out Overnight + Due Back
            Assert.That(rentals.Counts.ReturnsDue, Is.EqualTo(1));   // Due Back
            Assert.That(rentals.Counts.Reserved, Is.EqualTo(1));
            Assert.That(rentals.Counts.Unpaid, Is.EqualTo(1));
            Assert.That(rentals.Counts.Returned, Is.EqualTo(1));
            // Every non-cancelled row lands in exactly one of pickup/out/reserved/unpaid/done.
            var cancelledRows = rentals.Rows.Count(r => r.Phase == "cancelled");
            Assert.That(rentals.Counts.PickupsDue + rentals.Counts.OutNow + rentals.Counts.Reserved
                        + rentals.Counts.Unpaid + rentals.Counts.Returned + cancelledRows,
                Is.EqualTo(rentals.Rows.Count));

            Assert.That(sheet.Totals.PickupsDue, Is.EqualTo(sheet.Groups.Sum(g => g.Counts.PickupsDue)));
            Assert.That(sheet.Totals.OutNow, Is.EqualTo(sheet.Groups.Sum(g => g.Counts.OutNow)));
            Assert.That(sheet.Totals.ReturnsDue, Is.EqualTo(sheet.Groups.Sum(g => g.Counts.ReturnsDue)));
            Assert.That(sheet.Totals.Reserved, Is.EqualTo(sheet.Groups.Sum(g => g.Counts.Reserved)));
            Assert.That(sheet.Totals.Unpaid, Is.EqualTo(sheet.Groups.Sum(g => g.Counts.Unpaid)));
            Assert.That(sheet.Totals.Returned, Is.EqualTo(sheet.Groups.Sum(g => g.Counts.Returned)));
            Assert.That(sheet.Totals.PickupsDue, Is.EqualTo(5));     // 2 walk-ups + camp + after school + package
        }

        [Test]
        public void Build_RowsSortByStartThenCustomerName()
        {
            var raw = SampleDay(out _, out _, out _, out _);
            var sheet = ShopDayMath.Build(new DateOnly(2026, 7, 11), "America/New_York", DayStart, DayEnd, raw);
            var names = sheet.Groups[0].Rows.Select(r => r.CustomerName).ToList();
            // Due Back started yesterday, so it leads; then the 08:00 and 09:00 starts (names break the tie).
            Assert.That(names.Take(5), Is.EqualTo(new[] { "Due Back", "Out Overnight", "All Done", "Amy Early", "Gave Up" }));
            Assert.That(names.Last(), Is.EqualTo("Next Week"));
        }

        [Test]
        public void Build_RowCarriesItemsChannelBookingAndBalance()
        {
            var raw = SampleDay(out var walkUp, out var campRental, out _, out var packageRental);
            var sheet = ShopDayMath.Build(new DateOnly(2026, 7, 11), "America/New_York", DayStart, DayEnd, raw);
            var all = sheet.Groups.SelectMany(g => g.Rows).ToDictionary(r => r.RentalId);

            var w = all[walkUp.Id];
            Assert.That(w.Channel, Is.EqualTo("counter"));
            Assert.That(w.Items.Select(i => i.Name), Is.EqualTo(new[] { "Trek Fuel EX", "Full-face helmet" }));
            Assert.That(w.Items[0].UnitLabel, Is.EqualTo("Trek Fuel #3"));
            Assert.That(w.Items[0].UnitSerial, Is.EqualTo("WTU123"));
            Assert.That(w.Items[1].UnitLabel, Is.Null);
            Assert.That(w.EventTitle, Is.Null);
            Assert.That(w.PackageName, Is.Null);
            Assert.That(w.BalanceDueCents, Is.EqualTo(0));
            Assert.That(w.StartsAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));

            var c = all[campRental.Id];
            Assert.That(c.EventTitle, Is.EqualTo("Kids Day Camp (Sat)"));
            Assert.That(c.EventStartsAtUtc, Is.EqualTo(Today(13)));
            Assert.That(c.PackageName, Is.Null);

            var p = all[packageRental.Id];
            Assert.That(p.PackageName, Is.EqualTo("Find Your Ride"));
            Assert.That(p.SessionStartAtUtc, Is.EqualTo(Today(14)));
            Assert.That(p.CustomerName, Is.EqualTo("Package Buyer"));   // rental had no name; buyer fills in
            Assert.That(p.Channel, Is.EqualTo("online"));
            Assert.That(p.EventTitle, Is.Null);

            var unpaid = sheet.Groups[0].Rows.Single(r => r.Phase == "unpaid");
            Assert.That(unpaid.BalanceDueCents, Is.EqualTo(4500));
            Assert.That(unpaid.Channel, Is.EqualTo("online"));
        }

        [Test]
        public void Build_ProductTypesAreTheDistinctCategoriesOnTheDaysLines_SortedByName()
        {
            var raw = SampleDay(out _, out _, out _, out _);
            var sheet = ShopDayMath.Build(new DateOnly(2026, 7, 11), "America/New_York", DayStart, DayEnd, raw);
            Assert.That(sheet.ProductTypes.Select(t => t.Name), Is.EqualTo(new[] { "Bikes", "Gear" }));
            Assert.That(sheet.ProductTypes.Select(t => t.Id).Distinct().Count(), Is.EqualTo(2));
            // And each item says which category it is, so the client can filter rows by type.
            var walkUpItems = sheet.Groups[0].Rows.First(r => r.CustomerName == "Zed Walker").Items;
            Assert.That(walkUpItems.Select(i => i.CategoryId), Is.EquivalentTo(sheet.ProductTypes.Select(t => (Guid?)t.Id)));
            Assert.That(walkUpItems.Select(i => i.CategoryName), Is.EqualTo(new[] { "Bikes", "Gear" }));
        }

        [Test]
        public void Build_EmptyDay_IsEmptyGroupsAndZeroTotals()
        {
            var sheet = ShopDayMath.Build(new DateOnly(2026, 7, 11), "America/New_York", DayStart, DayEnd, new ShopDayRaw());
            Assert.That(sheet.Groups, Is.Empty);
            Assert.That(sheet.Programs, Is.Empty);
            Assert.That(sheet.ProductTypes, Is.Empty);
            Assert.That(sheet.Totals.PickupsDue + sheet.Totals.OutNow + sheet.Totals.ReturnsDue
                        + sheet.Totals.Reserved + sheet.Totals.Unpaid + sheet.Totals.Returned, Is.EqualTo(0));
        }

        // ── Overdue ───────────────────────────────────────────────────────────

        [Test]
        public void Overdue_ReturnDueBeforeTheDayBegan_IsOverdue() =>
            Assert.That(ShopDayMath.IsOverdue("return", Yesterday(9), Yesterday(17), DayStart), Is.True);

        [Test]
        public void Overdue_ReturnDueToday_IsNotOverdue() =>
            Assert.That(ShopDayMath.IsOverdue("return", Yesterday(9), Today(17), DayStart), Is.False);

        [Test]
        public void Overdue_ReturnEndingExactlyAtMidnightToday_IsNotOverdue_HalfOpen() =>
            Assert.That(ShopDayMath.IsOverdue("return", Yesterday(9), DayStart, DayStart), Is.False);

        [Test]
        public void Overdue_PickupThatShouldHaveHappenedBeforeToday_IsOverdue() =>
            Assert.That(ShopDayMath.IsOverdue("pickup", Yesterday(9), Yesterday(17), DayStart), Is.True);

        [Test]
        public void Overdue_PickupStartedYesterdayStillWithinWindow_IsOverdue() =>
            Assert.That(ShopDayMath.IsOverdue("pickup", Yesterday(9), Tomorrow(17), DayStart), Is.True);

        [Test]
        public void Overdue_PickupDueToday_IsNotOverdue() =>
            Assert.That(ShopDayMath.IsOverdue("pickup", Today(9), Today(17), DayStart), Is.False);

        [TestCase("out")]
        [TestCase("reserved")]
        [TestCase("unpaid")]
        [TestCase("done")]
        [TestCase("cancelled")]
        public void Overdue_OtherPhases_NeverOverdue_EvenWithOldWindows(string phase) =>
            Assert.That(ShopDayMath.IsOverdue(phase, Yesterday(9), Yesterday(17), DayStart), Is.False);

        [Test]
        public void Build_FlagsOverdueRows_AndCountsThemUnderReturnsAndPickups()
        {
            var staff = Guid.NewGuid();
            var overdueOut = Rental(status: "out", starts: Yesterday(-16), ends: Yesterday(17), name: "Overdue Out", soldBy: staff);
            var dueBackToday = Rental(status: "out", starts: Yesterday(8), ends: Today(16), name: "Due Back", soldBy: staff);
            var noShow = Rental(status: "paid", starts: Yesterday(9), ends: Yesterday(17), name: "No Show", soldBy: staff);
            var pickupToday = Rental(status: "paid", starts: Today(9), ends: Today(17), name: "On Time", soldBy: staff);
            var sheet = ShopDayMath.Build(new DateOnly(2026, 7, 11), "UTC", DayStart, DayEnd,
                new ShopDayRaw { Rentals = new() { pickupToday, noShow, dueBackToday, overdueOut } });

            var rows = sheet.Groups.Single().Rows.ToDictionary(r => r.CustomerName!);
            Assert.That((rows["Overdue Out"].Phase, rows["Overdue Out"].Overdue), Is.EqualTo(("return", true)));
            Assert.That((rows["Due Back"].Phase, rows["Due Back"].Overdue), Is.EqualTo(("return", false)));
            Assert.That((rows["No Show"].Phase, rows["No Show"].Overdue), Is.EqualTo(("pickup", true)));
            Assert.That((rows["On Time"].Phase, rows["On Time"].Overdue), Is.EqualTo(("pickup", false)));

            var c = sheet.Groups.Single().Counts;
            Assert.That(c.ReturnsDue, Is.EqualTo(2));
            Assert.That(c.OutNow, Is.EqualTo(2));
            Assert.That(c.PickupsDue, Is.EqualTo(2));
            // Oldest start first, so the longest-overdue row heads the sheet.
            Assert.That(sheet.Groups.Single().Rows.Select(r => r.CustomerName), Is.EqualTo(new[] { "Overdue Out", "Due Back", "No Show", "On Time" }));
        }

        [Test]
        public void Build_RentalWithNoLines_StillAppears_WithEmptyItems()
        {
            var r = Rental(name: "No Gear Yet");
            var sheet = ShopDayMath.Build(new DateOnly(2026, 7, 11), "UTC", DayStart, DayEnd,
                new ShopDayRaw { Rentals = new() { r } });
            Assert.That(sheet.Groups.Single().Rows.Single().Items, Is.Empty);
        }

        [Test]
        public void Build_TwoPackagePurchasesOnOneRental_DoNotDuplicateTheRow()
        {
            var r = Rental(name: "Twice Linked");
            var other = new ShopDayPackageLinkRow { RentalId = r.Id, PackageId = Guid.NewGuid(), PackageName = "Other Pack" };
            var sheet = ShopDayMath.Build(new DateOnly(2026, 7, 11), "UTC", DayStart, DayEnd,
                new ShopDayRaw { Rentals = new() { r }, PackageLinks = new() { FindYourRide(r.Id), other } });
            Assert.That(sheet.Groups.Count, Is.EqualTo(1));
            Assert.That(sheet.Groups[0].Label, Is.EqualTo("Find Your Ride"));
            Assert.That(sheet.Groups.SelectMany(g => g.Rows).Count(), Is.EqualTo(1));
        }

        [Test]
        public void Build_PackagesSortByName_WhenSeveral()
        {
            var a = Rental(name: "A");
            var b = Rental(name: "B");
            var zebra = new ShopDayPackageLinkRow { RentalId = a.Id, PackageId = Guid.NewGuid(), PackageName = "Zebra Pack" };
            var alpha = new ShopDayPackageLinkRow { RentalId = b.Id, PackageId = Guid.NewGuid(), PackageName = "alpha pack" };
            var sheet = ShopDayMath.Build(new DateOnly(2026, 7, 11), "UTC", DayStart, DayEnd,
                new ShopDayRaw { Rentals = new() { a, b }, PackageLinks = new() { zebra, alpha } });
            Assert.That(sheet.Groups.Select(g => g.Label), Is.EqualTo(new[] { "alpha pack", "Zebra Pack" }));
        }
    }
}
