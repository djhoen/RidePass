using NUnit.Framework;

namespace UnitTests
{
    [TestFixture]
    public class SampleTests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void SampleTest_ShouldPass()
        {
            Assert.That(true, Is.True);
        }

        // TODO: Add unit tests for your services and helpers
        // Example test categories:
        // - CouponTests: Validate coupon application logic
        // - OrderTests: Validate order creation and status flows
        // - UserTests: Validate user creation and role management
    }
}
