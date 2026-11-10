using NUnit.Framework;
using Services.Passes;

namespace UnitTests
{
    // Passes aren't transferable (Highland go-live item 12): who counts as the same holder.
    [TestFixture]
    public class PassHolderRulesTests
    {
        private static readonly DateTime Dob = new(1990, 3, 15);

        [Test]
        public void Case_and_spacing_do_not_make_a_new_person() =>
            Assert.That(PassHolderRules.SameName("Alex", "Rivera", "  alex ", "RIVERA"), Is.True);

        [Test]
        public void A_different_name_is_a_different_person() =>
            Assert.That(PassHolderRules.SameName("Alex", "Rivera", "Sam", "Rivera"), Is.False);

        [Test]
        public void A_missing_birthdate_does_not_contradict_one() =>
            Assert.That(PassHolderRules.SamePerson("Alex", "Rivera", Dob, "Alex", "Rivera", null), Is.True);

        [Test]
        public void A_different_birthdate_is_a_different_person() =>
            Assert.That(PassHolderRules.SamePerson("Alex", "Rivera", Dob, "Alex", "Rivera", Dob.AddYears(20)), Is.False);

        [Test]
        public void First_registration_is_never_a_change() =>
            Assert.That(PassHolderRules.IsHolderChange(null, null, null, null, "Alex", "Rivera", Dob, "data:image/png;base64,AA"), Is.False);

        [Test]
        public void Same_holder_and_photo_is_not_a_change() =>
            Assert.That(PassHolderRules.IsHolderChange("Alex", "Rivera", Dob, "p1", "alex", "rivera", Dob, "p1"), Is.False);

        [Test]
        public void A_new_name_is_a_change() =>
            Assert.That(PassHolderRules.IsHolderChange("Alex", "Rivera", Dob, "p1", "Sam", "Rivera", Dob, "p1"), Is.True);

        [Test]
        public void A_new_photo_is_a_change() =>
            Assert.That(PassHolderRules.IsHolderChange("Alex", "Rivera", Dob, "p1", "Alex", "Rivera", Dob, "p2"), Is.True);
    }
}
