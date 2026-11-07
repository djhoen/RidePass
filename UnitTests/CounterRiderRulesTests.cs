using NUnit.Framework;
using Services.Repositories.Data.RiderData;
using Services.Repositories.Data.UserData;
using Services.Riders;
using webapi.Helpers;

namespace UnitTests
{
    // Gate Sale "another rider" lines (Script0309): what a new profile needs, who signs and where the
    // link goes, and how a line's waiver reads on the counter.
    [TestFixture]
    public class CounterRiderRulesTests
    {
        private static readonly DateOnly Today = new(2026, 11, 7);
        private static readonly DateOnly Adult = new(1990, 5, 1);
        private static readonly DateOnly Minor = new(2014, 3, 9);

        private static OtherRiderInput AdultInput(string? email = "ada@example.test") =>
            new("Ada", "Adult", Adult, email, null);

        private static OtherRiderInput MinorInput() =>
            new("Mia", "Minor", Minor, null, null,
                GuardianFirstName: "Gina", GuardianLastName: "Guard", GuardianEmail: "gina@example.test",
                GuardianPhone: "555 010 0100");

        [Test]
        public void AdultWithEmail_IsValid() =>
            Assert.That(CounterRiderRules.Validate(AdultInput(), Today, false, "payer@example.test"), Is.Null);

        [Test]
        public void NewAdult_NeedsTheirOwnEmail() =>
            Assert.That(CounterRiderRules.Validate(AdultInput(email: null), Today, false, null), Does.Contain("email"));

        [Test]
        public void ExistingAdultProfile_NeedsNoEmail() =>
            Assert.That(CounterRiderRules.Validate(AdultInput(email: null) with { FirstName = null }, Today, true, null), Is.Null);

        [Test]
        public void PayersOwnEmail_IsRefusedForAnotherRider() =>
            Assert.That(CounterRiderRules.Validate(AdultInput("Payer@Example.test "), Today, false, "payer@example.test"),
                Does.Contain("customer's own email"));

        [Test]
        public void MissingName_IsRefused() =>
            Assert.That(CounterRiderRules.Validate(AdultInput() with { LastName = " " }, Today, false, null),
                Does.Contain("first and last name"));

        [TestCase(null)]
        [TestCase("2026-11-08")]   // tomorrow
        [TestCase("1899-12-31")]   // UserController.IsValidBirthdate: 1900 or later
        public void BadBirthdate_IsRefused(string? dob) =>
            Assert.That(CounterRiderRules.Validate(
                AdultInput() with { Birthdate = dob is null ? null : DateOnly.Parse(dob) }, Today, false, null),
                Is.Not.Null);

        [Test]
        public void MinorWithGuardianDetails_IsValid() =>
            Assert.That(CounterRiderRules.Validate(MinorInput(), Today, false, null), Is.Null);

        [Test]
        public void Minor_NeedsAGuardian() =>
            Assert.That(CounterRiderRules.Validate(MinorInput() with { GuardianFirstName = null }, Today, false, null),
                Does.Contain("parent or guardian"));

        [Test]
        public void MinorGuardian_NeedsAValidEmail() =>
            Assert.That(CounterRiderRules.Validate(MinorInput() with { GuardianEmail = "gina-at-example" }, Today, false, null),
                Does.Contain("email"));

        [Test]
        public void MinorWithPayerOrExistingGuardian_NeedsNoGuardianDetails()
        {
            var bare = MinorInput() with { GuardianFirstName = null, GuardianLastName = null, GuardianEmail = null };
            Assert.That(CounterRiderRules.Validate(bare with { GuardianIsPayer = true }, Today, false, null), Is.Null);
            Assert.That(CounterRiderRules.Validate(bare with { HasGuardianAccount = true }, Today, false, null), Is.Null);
        }

        [Test]
        public void GuardianEmailCannotBeTheMinorsOwn() =>
            Assert.That(CounterRiderRules.Validate(MinorInput() with { Email = "GINA@example.test" }, Today, false, null),
                Does.Contain("rider's own email"));

        [Test]
        public void ShortPhone_IsRefused() =>
            Assert.That(CounterRiderRules.Validate(MinorInput() with { GuardianPhone = "12345" }, Today, false, null),
                Does.Contain("too short"));

        [Test]
        public void EighteenthBirthday_IsAnAdult()
        {
            Assert.That(CounterRiderRules.IsMinor(new DateOnly(2008, 11, 7), Today), Is.False);
            Assert.That(CounterRiderRules.IsMinor(new DateOnly(2008, 11, 8), Today), Is.True);
        }

        [Test]
        public void LinkGoesToTheGuardianForAMinor()
        {
            var to = CounterRiderRules.LinkRecipient(true, "kid@example.test", "5550000001", "gina@example.test", "555 010 0100");
            Assert.That(to, Is.Not.Null);
            Assert.That(to!.Guardian, Is.True);
            Assert.That(to.Email, Is.EqualTo("gina@example.test"));
            Assert.That(to.Phone, Is.EqualTo("555 010 0100"));
        }

        [Test]
        public void LinkGoesToTheRiderForAnAdult_AndNeedsAnEmail()
        {
            var to = CounterRiderRules.LinkRecipient(false, "ada@example.test", "12", "gina@example.test", null);
            Assert.That(to!.Email, Is.EqualTo("ada@example.test"));
            Assert.That(to.Phone, Is.Null);   // too short to text
            Assert.That(CounterRiderRules.LinkRecipient(false, null, "5550000001", "gina@example.test", null), Is.Null);
        }

        [TestCase(false, false, null, CounterRiderRules.StatusNotRequired)]
        [TestCase(true, true, null, CounterRiderRules.StatusSigned)]
        [TestCase(true, true, "sent", CounterRiderRules.StatusSigned)]
        [TestCase(true, false, "sent", CounterRiderRules.StatusLinkSent)]
        [TestCase(true, false, "opened", CounterRiderRules.StatusLinkSent)]
        [TestCase(true, false, "pending", CounterRiderRules.StatusLinkSent)]
        [TestCase(true, false, "signed", CounterRiderRules.StatusNeedsSignature)]   // signed but the gate still fails
        [TestCase(true, false, null, CounterRiderRules.StatusNeedsSignature)]
        public void LineStatus(bool required, bool satisfied, string? request, string expected) =>
            Assert.That(CounterRiderRules.LineStatus(required, satisfied, request), Is.EqualTo(expected));

        // The codebase's own rule ([EmailAddress], as CreateCounterRiderRequest validates), not a copy.
        [TestCase("a@b.co", true)]
        [TestCase(" a.b@c.d.example ", true)]
        [TestCase("@b.co", false)]
        [TestCase("a@", false)]
        [TestCase("a@@b.co", false)]
        [TestCase("", false)]
        public void Email(string email, bool ok) => Assert.That(RiderAccounts.IsValidEmail(email), Is.EqualTo(ok));

        [TestCase("Pat Payer", null, "birthdate")]
        [TestCase("Pat Payer", "2010-01-01", "under 18")]
        [TestCase("Pat Payer", "2008-11-07", null)]   // 18 today
        [TestCase("Pat Payer", "1980-02-02", null)]
        public void GuardianMustBeAnAdult(string name, string? dob, string? expected)
        {
            var error = CounterRiderRules.GuardianAgeError(name, dob is null ? null : DateOnly.Parse(dob), Today);
            if (expected is null) Assert.That(error, Is.Null);
            else Assert.That(error, Does.Contain(expected));
        }

        [Test]
        public void EmergencyContact_UsesCreateRidersRule()
        {
            Assert.That(RiderAccounts.EmergencyContactError("Sam", "555 0101"), Is.Null);
            Assert.That(RiderAccounts.EmergencyContactError(" ", "5550101"), Is.Not.Null);
            Assert.That(RiderAccounts.EmergencyContactError("Sam", "12-34"), Is.Not.Null);
        }

        // ── Who the rider is (TicketRiderKeys, Script0309) ──────────────────────────────────────

        private static User Account(string first, string last) => new() { Id = Guid.NewGuid(), FirstName = first, LastName = last, Email = $"{first}@x.test" };

        [Test]
        public void Dependent_NamedLikeThePayer_IsNeverThePayer()
        {
            var payer = Account("John", "Smith");
            var dep = new RiderDependent { Id = Guid.NewGuid(), GuardianUserId = payer.Id, FirstName = "John", LastName = "Smith" };
            var k = TicketRiderKeys.Resolve(payer, "John", "Smith", null, dep.Id, null, dep, payer);
            Assert.That(k.Kind, Is.EqualTo(RiderKeyingKind.Dependent));
            Assert.That(k.AccountIsRider, Is.False);
            Assert.That(k.Account, Is.SameAs(payer));                          // keyed under the guardian
            Assert.That(k.RiderKey, Is.EqualTo($"dependent:{dep.Id:D}"));       // by id, not by name
            // The same name without the identity is the payer, as before.
            Assert.That(TicketRiderKeys.FromNames(payer, "John", "Smith").AccountIsRider, Is.True);
        }

        [Test]
        public void FriendWithOwnAccount_IsKeyedUnderTheirAccount()
        {
            var payer = Account("Pat", "Payer");
            var friend = Account("Ada", "Adult");
            var k = TicketRiderKeys.Resolve(payer, "Ada", "Adult", friend.Id, null, friend, null, null);
            Assert.That(k.Kind, Is.EqualTo(RiderKeyingKind.RiderAccount));
            Assert.That(k.Account, Is.SameAs(friend));
            Assert.That(k.AccountIsRider, Is.True);
            Assert.That(k.RiderKey, Is.EqualTo("ada adult"));
        }

        [Test]
        public void RiderAccountThatIsThePayer_IsTheNameRule()
        {
            var payer = Account("Pat", "Payer");
            var k = TicketRiderKeys.Resolve(payer, null, null, payer.Id, null, payer, null, null);
            Assert.That(k.Kind, Is.EqualTo(RiderKeyingKind.Names));
            Assert.That(k.AccountIsRider, Is.True);
        }

        [Test]
        public void UnusableFriendAccount_IsNeverThePayer()
        {
            var payer = Account("Ada", "Adult");
            var k = TicketRiderKeys.Resolve(payer, "Ada", "Adult", Guid.NewGuid(), null, null, null, null);
            Assert.That(k.Account, Is.Null);
            Assert.That(k.AccountIsRider, Is.False);
        }

        [Test]
        public void PhotoKey_FollowsTheIdentity()
        {
            var guardian = Account("Gwen", "Guard");
            var depId = Guid.NewGuid();
            var dep = new RiderDependent { Id = depId, GuardianUserId = guardian.Id };
            var k = TicketRiderKeys.Resolve(Account("Pat", "Payer"), "Milo", "Kid", null, depId, null, dep, guardian);
            var key = RiderPhotoRules.KeyFor(k, "payer@x.test", "Milo", "Kid", "ticket", Guid.NewGuid());
            Assert.That(key.OwnerKey, Is.EqualTo($"user:{guardian.Id:D}"));
            Assert.That(key.RiderKey, Is.EqualTo($"dependent:{depId:D}"));
            Assert.That(RiderPhotoRules.AccountPhotoIsRiders(k, "Milo", "Kid"), Is.False);   // the guardian's photo never counts
        }

        [Test]
        public void SmsBody_NamesTheRiderForAGuardian()
        {
            Assert.That(CounterRiderRules.SmsBody("Highland", "Mia", true, "https://x/SignWaiver/t"),
                Does.Contain("for Mia as their parent or guardian").And.EndWith("https://x/SignWaiver/t"));
            Assert.That(CounterRiderRules.SmsBody("Highland", "Ada", false, "L"), Does.Contain("sign your waiver"));
        }
    }
}
