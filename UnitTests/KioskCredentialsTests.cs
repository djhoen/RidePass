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
    }
}
