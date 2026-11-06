using NUnit.Framework;
using Services.Helpers;

namespace UnitTests
{
    // The one display-name rule every screen shares: name, else email, else "Unknown".
    [TestFixture]
    public class UserDisplayNameTests
    {
        [TestCase("Dev Cashier", "dev@x.test", "Dev Cashier")]
        [TestCase("  Dev Cashier  ", "dev@x.test", "Dev Cashier")]
        [TestCase("", "dev@x.test", "dev@x.test")]
        [TestCase("   ", "dev@x.test", "dev@x.test")]
        [TestCase(null, "dev@x.test", "dev@x.test")]
        [TestCase(null, null, "Unknown")]
        [TestCase("", "  ", "Unknown")]
        public void Of_NameThenEmailThenUnknown(string? name, string? email, string expected) =>
            Assert.That(UserDisplayName.Of(name, email), Is.EqualTo(expected));

        [TestCase("Dev", "Cashier", null, "Dev Cashier")]
        [TestCase("Dev", null, null, "Dev")]
        [TestCase(null, "Cashier", null, "Cashier")]
        [TestCase(" ", "", "dev@x.test", "dev@x.test")]
        [TestCase(null, null, null, "Unknown")]
        public void Of_JoinsThePartsBeforeFallingBack(string? first, string? last, string? email, string expected) =>
            Assert.That(UserDisplayName.Of(first, last, email), Is.EqualTo(expected));
    }
}
