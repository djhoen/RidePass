using NUnit.Framework;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.RiderData;
using Services.Repositories.Data.UserData;
using webapi.Helpers;

namespace UnitTests
{
    // A waiver pushed to a kiosk: who signs for a kiosk sign-up's profile (the signing helpers' own rule),
    // whether the waiver can still be signed, and which tickets one request may carry.
    [TestFixture]
    public class KioskPushRulesTests
    {
        private static readonly DateTime Now = new(2026, 6, 15, 18, 0, 0, DateTimeKind.Utc);
        private const string Tz = "America/Denver";

        private static DateTime YearsAgo(int years, int days = 0) => Now.Date.AddYears(-years).AddDays(days);

        [Test]
        public void Account_SignsAsGuardianWhileMinor_AsAdultFrom18()
        {
            Assert.That(AccountWaiverSigning.SignModeFor(new User { Birthdate = YearsAgo(17) }, null, Tz, Now), Is.EqualTo("guardian"));
            Assert.That(AccountWaiverSigning.SignModeFor(new User { Birthdate = YearsAgo(18) }, null, Tz, Now), Is.EqualTo("adult"));
        }

        [Test]
        public void Account_TheIdVerifiedBirthdateWins_AsInSignForAccount()
        {
            var user = new User { Birthdate = YearsAgo(30), IdVerifiedDob = YearsAgo(16) };
            Assert.That(AccountWaiverSigning.SignModeFor(user, null, Tz, Now), Is.EqualTo("guardian"));
            Assert.That(AccountWaiverStatus.IsMinorToday(user, Tz, Now), Is.True);
        }

        [Test]
        public void Account_NoBirthdate_IsNotAMinor_AsInSignForAccount()
        {
            Assert.That(AccountWaiverSigning.SignModeFor(new User(), null, Tz, Now), Is.EqualTo("adult"));
        }

        [Test]
        public void Dependent_AnAdultDependentSignsAsThemselves()
        {
            Assert.That(AccountWaiverSigning.SignModeFor(null, new RiderDependent { Birthdate = YearsAgo(10) }, Tz, Now), Is.EqualTo("guardian"));
            Assert.That(AccountWaiverSigning.SignModeFor(null, new RiderDependent { Birthdate = YearsAgo(19) }, Tz, Now), Is.EqualTo("adult"));
        }

        [Test]
        public void Dependent_UnknownBirthdate_CantTell()
        {
            Assert.That(AccountWaiverSigning.SignModeFor(null, new RiderDependent(), Tz, Now), Is.Null);
            Assert.That(AccountWaiverSigning.SignModeFor(null, null, Tz, Now), Is.Null);
        }

        [Test]
        public void EighteenthBirthday_CountsOnTheTenantsDate()
        {
            // 18 tomorrow by UTC's date, but at 18:00 UTC it is still the 15th in Denver: a minor there.
            var late = new DateTime(2026, 6, 16, 3, 0, 0, DateTimeKind.Utc);
            var dep = new RiderDependent { Birthdate = new DateTime(2008, 6, 16) };
            Assert.That(AccountWaiverSigning.SignModeFor(null, dep, Tz, late), Is.EqualTo("guardian"));
            Assert.That(AccountWaiverSigning.SignModeFor(null, dep, "UTC", late), Is.EqualTo("adult"));
        }

        [Test]
        public void WaiverUsable_MissingOrExpiredIsNot()
        {
            Assert.That(KioskPushService.WaiverUsable(null, Now), Is.False);
            Assert.That(KioskPushService.WaiverUsable(new TenantWaiver { ExpiresAt = Now.AddMinutes(-1) }, Now), Is.False);
            Assert.That(KioskPushService.WaiverUsable(new TenantWaiver { ExpiresAt = Now.AddDays(1) }, Now), Is.True);
            // Inactive but not expired: an event may still pin it.
            Assert.That(KioskPushService.WaiverUsable(new TenantWaiver { IsActive = false }, Now), Is.True);
        }

        [Test]
        public void SameOrderRider_SameEventAndBuyerOnly()
        {
            var ev = Guid.NewGuid();
            var tenant = Guid.NewGuid();
            var a = new EventTicketPurchase { TenantId = tenant, PurchaserEmail = "lou@example.test" };
            Assert.That(KioskPushService.SameOrderRider(a, ev, new EventTicketPurchase { TenantId = tenant, PurchaserEmail = " LOU@example.test" }, ev), Is.True);
            Assert.That(KioskPushService.SameOrderRider(a, ev, new EventTicketPurchase { TenantId = tenant, PurchaserEmail = "pip@example.test" }, ev), Is.False);
            Assert.That(KioskPushService.SameOrderRider(a, ev, new EventTicketPurchase { TenantId = tenant, PurchaserEmail = "lou@example.test" }, Guid.NewGuid()), Is.False);
            var buyer = Guid.NewGuid();
            var owned = new EventTicketPurchase { TenantId = tenant, PurchaserUserId = buyer, PurchaserEmail = "x@example.test" };
            Assert.That(KioskPushService.SameOrderRider(owned, ev, new EventTicketPurchase { TenantId = tenant, PurchaserUserId = buyer, PurchaserEmail = "y@example.test" }, ev), Is.True);
            Assert.That(KioskPushService.SameOrderRider(owned, ev, new EventTicketPurchase { TenantId = tenant, PurchaserEmail = "x@example.test" }, ev), Is.False);
        }
    }
}
