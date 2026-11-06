using Microsoft.AspNetCore.DataProtection;
using NUnit.Framework;
using webapi.Security;

namespace UnitTests
{
    // The cashier token a shared register holds after a staff code is verified: it must name the
    // person back, only at the tenant it was minted for, only until it expires, and never after
    // being tampered with. EphemeralDataProtectionProvider keeps the key ring in memory.
    [TestFixture]
    public class CashierTokenServiceTests
    {
        private static CashierTokenService NewService() => new(new EphemeralDataProtectionProvider());

        [Test]
        public void RoundTrip_NamesTheSameUser_AndExpiryIsFourteenHoursOut()
        {
            var svc = NewService();
            var tenant = Guid.NewGuid();
            var user = Guid.NewGuid();
            var before = DateTime.UtcNow;

            var (token, expiresAt) = svc.Issue(tenant, user);
            var result = svc.Validate(token, tenant);

            Assert.That(result.IsValid, Is.True, result.Error);
            Assert.That(result.UserId, Is.EqualTo(user));
            Assert.That(result.ExpiresAtUtc, Is.EqualTo(expiresAt));
            Assert.That(expiresAt - before, Is.InRange(CashierTokenService.Lifetime - TimeSpan.FromMinutes(1), CashierTokenService.Lifetime + TimeSpan.FromMinutes(1)));
            Assert.That(token, Does.Not.Contain(user.ToString()), "the payload must be opaque");
        }

        [Test]
        public void TenantMismatch_IsRejected()
        {
            var svc = NewService();
            var (token, _) = svc.Issue(Guid.NewGuid(), Guid.NewGuid());
            var result = svc.Validate(token, Guid.NewGuid());
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.UserId, Is.Null);
            Assert.That(result.Error, Is.Not.Empty);
        }

        [Test]
        public void ExpiredToken_IsRejected()
        {
            var svc = NewService();
            var tenant = Guid.NewGuid();
            var (token, _) = svc.Issue(tenant, Guid.NewGuid(), TimeSpan.FromSeconds(-5));
            var result = svc.Validate(token, tenant);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Error, Does.Contain("expired"));
        }

        [Test]
        public void TamperedOrForeignToken_IsRejected()
        {
            var svc = NewService();
            var tenant = Guid.NewGuid();
            var (token, _) = svc.Issue(tenant, Guid.NewGuid());
            var tampered = token[..^4] + (token.EndsWith("AAAA") ? "BBBB" : "AAAA");
            Assert.That(svc.Validate(tampered, tenant).IsValid, Is.False);
            Assert.That(svc.Validate("not-a-token", tenant).IsValid, Is.False);
            // Minted under another key ring (another process with no shared keys).
            var (foreign, _) = NewService().Issue(tenant, Guid.NewGuid());
            Assert.That(svc.Validate(foreign, tenant).IsValid, Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void BlankToken_IsRejected(string? token)
        {
            var result = NewService().Validate(token, Guid.NewGuid());
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Error, Is.Not.Empty);
        }
    }
}
