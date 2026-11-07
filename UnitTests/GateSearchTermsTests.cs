using NUnit.Framework;
using Services.Helpers;

namespace UnitTests
{
    // The gate's name/email/phone lookup turns typed text into LIKE patterns. A wrong pattern
    // doesn't error, it silently fails to find a paying rider at the gate, so pin the rules down.
    [TestFixture]
    public class GateSearchTermsTests
    {
        [Test]
        public void Words_become_one_lowercased_contains_pattern_each_in_any_order()
        {
            Assert.That(GateSearchTerms.BuildLikePatterns("Reed  JAKE"),
                Is.EqualTo(new[] { "%reed%", "%jake%" }));
        }

        [Test]
        public void Commas_and_tabs_split_words_too()
        {
            Assert.That(GateSearchTerms.BuildLikePatterns("Reed,\tJake"),
                Is.EqualTo(new[] { "%reed%", "%jake%" }));
        }

        [Test]
        public void Email_is_one_word_and_underscore_is_escaped()
        {
            Assert.That(GateSearchTerms.BuildLikePatterns("Jake_Reed@Example.com"),
                Is.EqualTo(new[] { @"%jake\_reed@example.com%" }));
        }

        [Test]
        public void Like_metacharacters_are_escaped()
        {
            Assert.That(GateSearchTerms.BuildLikePatterns(@"100% a\b"),
                Is.EqualTo(new[] { @"%100\%%", @"%a\\b%" }));
        }

        [Test]
        public void At_most_five_words_are_used()
        {
            Assert.That(GateSearchTerms.BuildLikePatterns("a b c d e f g"), Has.Length.EqualTo(5));
        }

        [Test]
        public void Blank_query_yields_no_patterns()
        {
            Assert.That(GateSearchTerms.BuildLikePatterns("   "), Is.Empty);
            Assert.That(GateSearchTerms.BuildLikePatterns(null), Is.Empty);
        }

        [TestCase("(555) 123-4567", "5551234567")]
        [TestCase("555.123.4567", "5551234567")]
        [TestCase("+1 555 123 4567", "5551234567")]
        [TestCase("15551234567", "5551234567")]
        [TestCase("4567", "4567")]
        public void Phone_shaped_query_collapses_to_one_digits_pattern(string query, string digits)
        {
            Assert.That(GateSearchTerms.BuildLikePatterns(query), Is.EqualTo(new[] { $"%{digits}%" }));
            Assert.That(GateSearchTerms.PhoneDigits(query), Is.EqualTo(digits));
        }

        [Test]
        public void Eleven_digits_not_starting_with_1_keep_every_digit()
        {
            Assert.That(GateSearchTerms.PhoneDigits("44 20 7946 0958"), Is.EqualTo("442079460958"));
            Assert.That(GateSearchTerms.PhoneDigits("25551234567"), Is.EqualTo("25551234567"));
        }

        [Test]
        public void Too_few_digits_is_treated_as_words_not_a_phone()
        {
            Assert.That(GateSearchTerms.PhoneDigits("12 3"), Is.Null);
            Assert.That(GateSearchTerms.BuildLikePatterns("12 3"), Is.EqualTo(new[] { "%12%", "%3%" }));
        }

        [Test]
        public void Mixed_query_splits_the_phone_run_from_the_words()
        {
            Assert.That(GateSearchTerms.PhoneDigits("jake 4567"), Is.Null);
            Assert.That(GateSearchTerms.BuildLikePatterns("jake 4567"), Is.EqualTo(new[] { "%jake%", "%4567%" }));
            Assert.That(GateSearchTerms.BuildLikePatterns("Smith (555) 123-4567"),
                Is.EqualTo(new[] { "%smith%", "%5551234567%" }));
            Assert.That(GateSearchTerms.BuildLikePatterns("+1 555-123-4567 reed"),
                Is.EqualTo(new[] { "%5551234567%", "%reed%" }));
        }

        [Test]
        public void Digits_inside_an_email_stay_part_of_the_word()
        {
            Assert.That(GateSearchTerms.BuildLikePatterns("rider2026@x.test"),
                Is.EqualTo(new[] { "%rider2026@x.test%" }));
        }
    }
}
