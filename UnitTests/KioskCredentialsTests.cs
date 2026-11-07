using NUnit.Framework;
using webapi.Security;

namespace UnitTests
{
    // Front-desk kiosk credentials (Script0310): one-time pairing codes and the device secret.
    [TestFixture]
    public class KioskCredentialsTests
    {
        [Test]
        public void PairingCode_IsEightUnambiguousCharacters()
        {
            for (var i = 0; i < 200; i++)
            {
                var code = KioskCredentials.NewPairingCode();
                Assert.That(code, Has.Length.EqualTo(KioskCredentials.CodeLength));
                Assert.That(code, Does.Not.Contain("0").And.Not.Contain("O").And.Not.Contain("1").And.Not.Contain("I").And.Not.Contain("L"));
            }
        }

        [Test]
        public void TypedCode_IsNormalized()
        {
            Assert.That(KioskCredentials.NormalizeCode(" abcd-23 45 "), Is.EqualTo("ABCD2345"));
            Assert.That(KioskCredentials.Display("ABCD2345"), Is.EqualTo("ABCD-2345"));
        }

        [Test]
        public void PairingCodeHash_IsTenantBound_AndIgnoresFormatting()
        {
            var t1 = Guid.NewGuid();
            var t2 = Guid.NewGuid();
            Assert.That(KioskCredentials.HashPairingCode(t1, "abcd-2345"), Is.EqualTo(KioskCredentials.HashPairingCode(t1, "ABCD2345")));
            Assert.That(KioskCredentials.HashPairingCode(t1, "ABCD2345"), Is.Not.EqualTo(KioskCredentials.HashPairingCode(t2, "ABCD2345")));
        }

        [Test]
        public void Secret_IsLongRandomAndNeverStoredAsIs()
        {
            var a = KioskCredentials.NewSecret();
            var b = KioskCredentials.NewSecret();
            Assert.That(a, Does.StartWith("kio_"));
            Assert.That(a.Length, Is.GreaterThan(40));
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(KioskCredentials.Hash(a), Has.Length.EqualTo(64).And.Not.Contain(a));
        }

        private static Microsoft.AspNetCore.Http.DefaultHttpContext Request(string? token, string ip)
        {
            var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
            if (token is not null) ctx.Request.Headers[KioskCredentials.HeaderName] = token;
            return ctx;
        }

        [Test]
        public void RateLimit_UnknownTokensShareTheAddress_ValidOnesGetTheirOwn()
        {
            var cache = new KioskCredentialCache();
            var real = KioskCredentials.NewSecret();
            // Made-up tokens can't buy a fresh allowance each: they all land on the address.
            Assert.That(cache.PartitionFor(Request("kio_made-up-1", "10.0.0.5")), Is.EqualTo("kiosk-ip:10.0.0.5"));
            Assert.That(cache.PartitionFor(Request("kio_made-up-2", "10.0.0.5")), Is.EqualTo("kiosk-ip:10.0.0.5"));
            Assert.That(cache.PartitionFor(Request(real, "10.0.0.5")), Is.EqualTo("kiosk-ip:10.0.0.5"));
            // Once proven valid, the device has its own.
            cache.MarkValid(KioskCredentials.Hash(real));
            Assert.That(cache.PartitionFor(Request(real, "10.0.0.5")), Is.EqualTo("kiosk:" + KioskCredentials.Hash(real)));
            // Revoked: back to the address.
            cache.Forget(KioskCredentials.Hash(real));
            Assert.That(cache.PartitionFor(Request(real, "10.0.0.5")), Is.EqualTo("kiosk-ip:10.0.0.5"));
        }
    }
}
