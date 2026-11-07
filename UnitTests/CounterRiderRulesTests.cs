using NUnit.Framework;
using Services.Riders;

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
        [TestCase("1900-01-01")]   // a typo for 2000
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

        [TestCase("a@b.co", true)]
        [TestCase("a.b@c.d.example", true)]
        [TestCase("a@b", false)]
        [TestCase("@b.co", false)]
        [TestCase("a@@b.co", false)]
        [TestCase("a b@c.co", false)]
        [TestCase("a@b.co.", false)]
        public void Email(string email, bool ok) => Assert.That(CounterRiderRules.IsValidEmail(email), Is.EqualTo(ok));

        [Test]
        public void SmsBody_NamesTheRiderForAGuardian()
        {
            Assert.That(CounterRiderRules.SmsBody("Highland", "Mia", true, "https://x/SignWaiver/t"),
                Does.Contain("for Mia as their parent or guardian").And.EndWith("https://x/SignWaiver/t"));
            Assert.That(CounterRiderRules.SmsBody("Highland", "Ada", false, "L"), Does.Contain("sign your waiver"));
        }
    }
}
