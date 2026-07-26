using NUnit.Framework;
using Services.Email;
using Services.Helpers;
using Services.Repositories.Data.NewsletterData;

namespace UnitTests
{
    // The two pure pieces of the drip-campaign sweep. Both decide something a tenant cannot see
    // until it has already gone wrong in a real rider's inbox, which is why they are unit-tested
    // rather than eyeballed.
    [TestFixture]
    public class SendWindowTests
    {
        // 2026-07-25 is a Saturday; the date is irrelevant, only the time of day matters.
        private static DateTime Utc(int hour, int minute = 0) => new(2026, 7, 25, hour, minute, 0, DateTimeKind.Utc);

        [Test]
        public void NoWindow_IsAlwaysOpen()
        {
            Assert.That(SendWindow.IsOpen(null, null, "UTC", Utc(3)), Is.True);
        }

        [Test]
        public void DaytimeWindow_OpenInside_ClosedOutside()
        {
            var s = TimeSpan.FromHours(9);
            var e = TimeSpan.FromHours(18);
            Assert.Multiple(() =>
            {
                Assert.That(SendWindow.IsOpen(s, e, "UTC", Utc(9)), Is.True, "start is inclusive");
                Assert.That(SendWindow.IsOpen(s, e, "UTC", Utc(13)), Is.True);
                Assert.That(SendWindow.IsOpen(s, e, "UTC", Utc(17, 59)), Is.True);
                Assert.That(SendWindow.IsOpen(s, e, "UTC", Utc(18)), Is.False, "end is exclusive");
                Assert.That(SendWindow.IsOpen(s, e, "UTC", Utc(3)), Is.False);
                Assert.That(SendWindow.IsOpen(s, e, "UTC", Utc(23)), Is.False);
            });
        }

        // The case a single range gets wrong: 22:00 to 06:00 is two ranges. Treated as one,
        // start <= now < end is never true and the automation silently never sends.
        [Test]
        public void OvernightWindow_WrapsMidnight()
        {
            var s = TimeSpan.FromHours(22);
            var e = TimeSpan.FromHours(6);
            Assert.Multiple(() =>
            {
                Assert.That(SendWindow.IsOpen(s, e, "UTC", Utc(23)), Is.True, "before midnight");
                Assert.That(SendWindow.IsOpen(s, e, "UTC", Utc(2)), Is.True, "after midnight");
                Assert.That(SendWindow.IsOpen(s, e, "UTC", Utc(5, 59)), Is.True);
                Assert.That(SendWindow.IsOpen(s, e, "UTC", Utc(6)), Is.False);
                Assert.That(SendWindow.IsOpen(s, e, "UTC", Utc(12)), Is.False);
                Assert.That(SendWindow.IsOpen(s, e, "UTC", Utc(21, 59)), Is.False);
            });
        }

        [Test]
        public void EqualBounds_AreTreatedAsNoWindow_NotAPermanentBlock()
        {
            var t = TimeSpan.FromHours(9);
            Assert.That(SendWindow.IsOpen(t, t, "UTC", Utc(3)), Is.True);
        }

        // The window is the TENANT'S local hours, not the server's. 16:00 UTC is 10:00 in Denver,
        // which is inside a 9-to-6 window; evaluated in UTC it is also inside, so the test uses
        // an hour where the two answers differ.
        [Test]
        public void WindowIsEvaluatedInTenantLocalTime()
        {
            var s = TimeSpan.FromHours(9);
            var e = TimeSpan.FromHours(18);
            // 02:00 UTC on the 26th is 20:00 on the 25th in Denver: outside a 9-to-6 window both
            // ways. 14:00 UTC is 08:00 Denver: inside in UTC, OUTSIDE locally.
            var utc = new DateTime(2026, 7, 25, 14, 0, 0, DateTimeKind.Utc);
            Assert.Multiple(() =>
            {
                Assert.That(SendWindow.IsOpen(s, e, "UTC", utc), Is.True);
                Assert.That(SendWindow.IsOpen(s, e, "America/Denver", utc), Is.False, "08:00 local is too early");
            });
        }

        [Test]
        public void UnknownTimezone_FallsBackToUtc_RatherThanThrowing()
        {
            var s = TimeSpan.FromHours(9);
            var e = TimeSpan.FromHours(18);
            Assert.That(SendWindow.IsOpen(s, e, "Mars/Olympus_Mons", Utc(13)), Is.True);
        }
    }

    [TestFixture]
    public class AutomationMergeFieldTests
    {
        private static AutomationPassSubject Subject() => new()
        {
            PurchaseId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            TenantId = Guid.NewGuid(),
            Email = "rider@example.com",
            HolderName = "Alex Rivera",
            ProductName = "Season Pass",
            PurchasedAtUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidToDate = new DateTime(2026, 12, 31),
            CreditsRemaining = null,
            UpgradePriceCents = 12500,
            UpgradeProductName = "Season Pass Plus",
        };

        [Test]
        public void SubstitutesKnownTokens()
        {
            var v = AutomationMergeFields.For(Subject(), "Moto Land", "https://motoland.ridepass.io");
            var html = AutomationMergeFields.Render(
                "<p>Hi {{first_name}}, your {{pass_name}} can move up to {{upgrade_name}} for {{upgrade_price}}.</p>",
                v, htmlEncode: true);
            Assert.That(html, Is.EqualTo(
                "<p>Hi Alex, your Season Pass can move up to Season Pass Plus for $125.00.</p>"));
        }

        [Test]
        public void UpgradeLinkPointsAtThisRidersPass()
        {
            var v = AutomationMergeFields.For(Subject(), "Moto Land", "https://motoland.ridepass.io/");
            Assert.That(AutomationMergeFields.Render("{{upgrade_link}}", v, htmlEncode: true),
                Is.EqualTo("https://motoland.ridepass.io/User/PassUpgrade/11111111-2222-3333-4444-555555555555"),
                "a trailing slash on the base URL must not double up");
        }

        // A typo must produce an awkward sentence, never ship "{{frist_name}}" to a paying customer.
        [Test]
        public void UnknownTokenRendersEmpty_NotLiteral()
        {
            var v = AutomationMergeFields.For(Subject(), "Moto Land", "https://x.test");
            Assert.That(AutomationMergeFields.Render("Hi {{frist_name}}!", v, htmlEncode: true), Is.EqualTo("Hi !"));
        }

        [Test]
        public void NoUpgradeConfigured_RendersEmpty_NotFree()
        {
            var s = Subject();
            s.UpgradePriceCents = null;
            s.UpgradeProductName = null;
            var v = AutomationMergeFields.For(s, "Moto Land", "https://x.test");
            Assert.Multiple(() =>
            {
                Assert.That(AutomationMergeFields.Render("{{upgrade_price}}", v, htmlEncode: true), Is.Empty,
                    "\"$0.00\" would read as a free upgrade");
                Assert.That(AutomationMergeFields.Render("{{upgrade_name}}", v, htmlEncode: true), Is.Empty);
            });
        }

        [Test]
        public void UnlimitedPass_HasNoCreditCount()
        {
            var v = AutomationMergeFields.For(Subject(), "Moto Land", "https://x.test");
            Assert.That(AutomationMergeFields.Render("{{credits_remaining}}", v, htmlEncode: true), Is.Empty);
        }

        [Test]
        public void CreditPack_ReportsRidesLeft()
        {
            var s = Subject();
            s.CreditsRemaining = 3;
            var v = AutomationMergeFields.For(s, "Moto Land", "https://x.test");
            Assert.That(AutomationMergeFields.Render("{{credits_remaining}} rides left", v, htmlEncode: true),
                Is.EqualTo("3 rides left"));
        }

        // A rider called "Bob & Sue" must not break the markup around them.
        [Test]
        public void HtmlBodiesEncodeValues_PlainTextDoesNot()
        {
            var s = Subject();
            s.HolderName = "Bob & Sue <VIP>";
            var v = AutomationMergeFields.For(s, "Moto Land", "https://x.test");
            Assert.Multiple(() =>
            {
                Assert.That(AutomationMergeFields.Render("{{holder_name}}", v, htmlEncode: true),
                    Is.EqualTo("Bob &amp; Sue &lt;VIP&gt;"));
                Assert.That(AutomationMergeFields.Render("{{holder_name}}", v, htmlEncode: false),
                    Is.EqualTo("Bob & Sue <VIP>"));
            });
        }

        [Test]
        public void MissingName_FallsBackToThere()
        {
            var s = Subject();
            s.HolderName = null;
            var v = AutomationMergeFields.For(s, "Moto Land", "https://x.test");
            Assert.That(AutomationMergeFields.Render("Hi {{first_name}},", v, htmlEncode: true), Is.EqualTo("Hi there,"));
        }

        [Test]
        public void UnclosedTokenIsLeftAlone_RatherThanEatingTheRestOfTheEmail()
        {
            var v = AutomationMergeFields.For(Subject(), "Moto Land", "https://x.test");
            Assert.That(AutomationMergeFields.Render("Hi {{first_name, see you soon", v, htmlEncode: true),
                Is.EqualTo("Hi {{first_name, see you soon"));
        }

        [Test]
        public void WhitespaceInsideBracesIsTolerated()
        {
            var v = AutomationMergeFields.For(Subject(), "Moto Land", "https://x.test");
            Assert.That(AutomationMergeFields.Render("{{ first_name }}", v, htmlEncode: true), Is.EqualTo("Alex"));
        }

        [Test]
        public void TemplateWithNoTokensIsUnchanged()
        {
            var v = AutomationMergeFields.For(Subject(), "Moto Land", "https://x.test");
            Assert.That(AutomationMergeFields.Render("Come ride with us!", v, htmlEncode: true),
                Is.EqualTo("Come ride with us!"));
        }
    }
}
