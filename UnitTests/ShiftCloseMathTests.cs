using NUnit.Framework;
using Services.Cash;

namespace UnitTests
{
    // The shift-close arithmetic CashController runs when a worker closes out a drawer with tips
    // in it: how much of their tips the drawer pays, what the house still owes, and what a manager
    // may settle later. Pure functions, so every edge is pinned here rather than found in a till.
    [TestFixture]
    public class ShiftCloseMathTests
    {
        [TestCase(500, 500, 500)]   // takes exactly what was earned
        [TestCase(300, 500, 300)]   // takes less; the rest becomes house-owes
        [TestCase(800, 500, 500)]   // asks for more than earned: capped at earned
        [TestCase(-50, 500, 0)]     // negative request: floored at zero
        [TestCase(0, 0, 0)]
        [TestCase(100, -20, 0)]     // nonsense negative earnings never produce a payout
        public void ClampTipsFromDrawer_StaysWithinZeroAndEarned(int requested, int earned, int expected) =>
            Assert.That(ShiftCloseMath.ClampTipsFromDrawer(requested, earned), Is.EqualTo(expected));

        [TestCase(0, 500, true)]
        [TestCase(500, 500, true)]
        [TestCase(501, 500, false)]
        [TestCase(-1, 500, false)]
        [TestCase(1, 0, false)]
        [TestCase(0, 0, true)]
        public void IsValidTipsFromDrawer_IsExactlyNoClampNeeded(int requested, int earned, bool valid) =>
            Assert.That(ShiftCloseMath.IsValidTipsFromDrawer(requested, earned), Is.EqualTo(valid));

        [TestCase(500, 500, 0)]
        [TestCase(500, 300, 200)]
        [TestCase(500, 0, 500)]
        [TestCase(300, 500, 0)]     // an over-take can't make the house owe a negative amount
        [TestCase(0, 0, 0)]
        public void HouseOwes_IsEarnedMinusTaken_NeverNegative(int earned, int taken, int expected) =>
            Assert.That(ShiftCloseMath.HouseOwes(earned, taken), Is.EqualTo(expected));

        [Test]
        public void HouseOwes_PlusTaken_AlwaysEqualsEarned_ForEveryValidTake()
        {
            // The invariant the turn-in snapshot relies on: tips_from_drawer + house_owes = tips_earned.
            const int earned = 1234;
            for (var taken = 0; taken <= earned; taken++)
            {
                Assert.That(ShiftCloseMath.IsValidTipsFromDrawer(taken, earned), Is.True);
                Assert.That(ShiftCloseMath.HouseOwes(earned, taken) + taken, Is.EqualTo(earned), $"taken={taken}");
            }
        }

        [TestCase(2000, 0, 2000)]
        [TestCase(2000, 1500, 500)]
        [TestCase(2000, 2000, 0)]
        [TestCase(2000, 2500, 0)]   // overpaid by a manual fix reads as nothing owed, not a debt owed back
        [TestCase(0, 0, 0)]
        public void OutstandingOwed_IsHouseOwesMinusManagerPayouts_NeverNegative(long houseOwes, long paid, long expected) =>
            Assert.That(ShiftCloseMath.OutstandingOwed(houseOwes, paid), Is.EqualTo(expected));

        [TestCase(1, 500, true)]
        [TestCase(500, 500, true)]
        [TestCase(501, 500, false)]
        [TestCase(0, 500, false)]
        [TestCase(-5, 500, false)]
        [TestCase(1, 0, false)]
        public void IsValidPayout_PositiveAndWithinOutstanding(int amount, long outstanding, bool valid) =>
            Assert.That(ShiftCloseMath.IsValidPayout(amount, outstanding), Is.EqualTo(valid));
    }
}
