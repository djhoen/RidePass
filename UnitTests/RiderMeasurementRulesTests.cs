using NUnit.Framework;
using Services.Riders;

namespace UnitTests
{
    // Rider height and weight (Script0317): ft/in conversion and the ranges every surface enforces.
    [TestFixture]
    public class RiderMeasurementRulesTests
    {
        [TestCase(5, 7, 67)]
        [TestCase(6, 0, 72)]
        [TestCase(2, 0, 24)]
        [TestCase(8, 0, 96)]
        [TestCase(4, 11, 59)]
        public void Feet_and_inches_become_total_inches(int feet, int inches, int expected) =>
            Assert.That(RiderMeasurementRules.ToInches(feet, inches), Is.EqualTo(expected));

        [Test]
        public void Missing_inches_count_as_zero() =>
            Assert.That(RiderMeasurementRules.ToInches(5, null), Is.EqualTo(60));

        [Test]
        public void Missing_feet_is_no_height() =>
            Assert.That(RiderMeasurementRules.ToInches(null, 7), Is.Null);

        [TestCase(12)]
        [TestCase(-1)]
        public void Inches_outside_0_to_11_are_refused(int inches) =>
            Assert.That(RiderMeasurementRules.ToInches(5, inches), Is.Null);

        [TestCase(67, 5, 7)]
        [TestCase(72, 6, 0)]
        [TestCase(24, 2, 0)]
        public void Total_inches_split_back_into_feet_and_inches(int total, int feet, int inches) =>
            Assert.That(RiderMeasurementRules.FromInches(total), Is.EqualTo((feet, inches)));

        [Test]
        public void Round_trips_every_valid_height()
        {
            for (var h = RiderMeasurementRules.MinHeightIn; h <= RiderMeasurementRules.MaxHeightIn; h++)
            {
                var (ft, inch) = RiderMeasurementRules.FromInches(h);
                Assert.That(RiderMeasurementRules.ToInches(ft, inch), Is.EqualTo(h));
            }
        }

        [Test]
        public void In_range_values_pass() =>
            Assert.That(RiderMeasurementRules.Validate(67, 150, required: true), Is.Null);

        [TestCase(24, 20)]
        [TestCase(96, 450)]
        public void Range_ends_are_inclusive(int h, int w) =>
            Assert.That(RiderMeasurementRules.Validate(h, w, required: true), Is.Null);

        [TestCase(23)]
        [TestCase(97)]
        [TestCase(170)]   // centimetres typed as inches
        public void Height_out_of_range_is_refused(int h) =>
            Assert.That(RiderMeasurementRules.Validate(h, 150, required: true), Does.StartWith("Height must be between"));

        [TestCase(19)]
        [TestCase(451)]
        public void Weight_out_of_range_is_refused(int w) =>
            Assert.That(RiderMeasurementRules.Validate(67, w, required: true), Does.StartWith("Weight must be between"));

        [Test]
        public void Required_and_missing_names_the_rider() =>
            Assert.That(RiderMeasurementRules.Validate(null, null, required: true, who: "the bike rider"),
                Is.EqualTo("Enter the bike rider's height and weight so the shop can size the bike."));

        [Test]
        public void Required_and_missing_without_a_name_says_your() =>
            Assert.That(RiderMeasurementRules.Validate(null, null, required: true),
                Is.EqualTo("Enter your height and weight so the shop can size the bike."));

        [Test]
        public void Optional_and_missing_is_fine() =>
            Assert.That(RiderMeasurementRules.Validate(null, null, required: false), Is.Null);

        [TestCase(67, null)]
        [TestCase(null, 150)]
        public void Half_entered_is_refused_even_when_optional(int? h, int? w) =>
            Assert.That(RiderMeasurementRules.Validate(h, w, required: false, who: "Sam"),
                Is.EqualTo("Enter both Sam's height and weight."));

        [TestCase("off", true)]
        [TestCase("optional", true)]
        [TestCase("required", true)]
        [TestCase("prompt", false)]
        [TestCase(null, false)]
        public void Kiosk_modes(string? mode, bool valid) =>
            Assert.That(RiderMeasurementRules.IsValidMode(mode), Is.EqualTo(valid));

        [Test]
        public void Unknown_mode_normalizes_to_off() =>
            Assert.That(RiderMeasurementRules.NormalizeMode("bogus"), Is.EqualTo(RiderMeasurementRules.ModeOff));

        [Test]
        public void Formats_for_staff() =>
            Assert.That(RiderMeasurementRules.Format(67, 150), Is.EqualTo("5 ft 7 in, 150 lb"));

        [Test]
        public void Formats_nothing_when_unknown() =>
            Assert.That(RiderMeasurementRules.Format(67, null), Is.Null);
    }
}
