using NUnit.Framework;
using Services.Cash;

namespace UnitTests
{
    // How the tip jar is divided (CashController.RecordTipJar): evenly in whole cents, the odd
    // cents one each to the first people named. Pure, so every edge is pinned here.
    [TestFixture]
    public class TipJarMathTests
    {
        private static List<Guid> People(int n) => Enumerable.Range(0, n).Select(_ => Guid.NewGuid()).ToList();

        [Test]
        public void EvenSplit_GivesEveryoneTheSame()
        {
            var people = People(2);
            var shares = TipJarMath.Split(12000, people);
            Assert.That(shares.Select(s => s.AmountCents), Is.EqualTo(new[] { 6000, 6000 }));
            Assert.That(shares.Select(s => s.UserId), Is.EqualTo(people));
        }

        [Test]
        public void Remainder_GoesOneCentEachToTheFirstPeople_InOrder()
        {
            var people = People(3);
            var shares = TipJarMath.Split(10001, people);
            Assert.That(shares.Select(s => s.AmountCents), Is.EqualTo(new[] { 3334, 3334, 3333 }));
            Assert.That(shares.Select(s => s.UserId), Is.EqualTo(people));
        }

        [TestCase(1, 3)]      // less than a cent each: the first person gets the only cent
        [TestCase(2, 3)]
        [TestCase(100, 7)]
        [TestCase(99999, 4)]
        public void Shares_SumToTheTotal_AndDifferByAtMostOneCent(int total, int n)
        {
            var shares = TipJarMath.Split(total, People(n));
            Assert.That(shares.Sum(s => s.AmountCents), Is.EqualTo(total));
            Assert.That(shares.Max(s => s.AmountCents) - shares.Min(s => s.AmountCents), Is.LessThanOrEqualTo(1));
            // Larger shares come first, never after a smaller one.
            Assert.That(shares.Select(s => s.AmountCents), Is.Ordered.Descending);
        }

        [Test]
        public void OnePerson_TakesItAll()
        {
            var one = People(1);
            Assert.That(TipJarMath.Split(777, one).Single(), Is.EqualTo((one[0], 777)));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(-12000)]
        public void ZeroOrNegativeTotal_IsRefused(int total) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => TipJarMath.Split(total, People(2)));

        [Test]
        public void NoPeople_IsRefused() =>
            Assert.Throws<ArgumentException>(() => TipJarMath.Split(100, new List<Guid>()));

        [Test]
        public void RepeatedPerson_IsRefused()
        {
            var p = Guid.NewGuid();
            Assert.Throws<ArgumentException>(() => TipJarMath.Split(100, new List<Guid> { p, p }));
        }
    }
}
