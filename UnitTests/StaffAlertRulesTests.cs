using NUnit.Framework;
using Services.Alerts;

namespace UnitTests
{
    /// <summary>
    /// The tripwires decide what reaches a track owner's inbox. Two failure modes matter: missing
    /// the thing that mattered, and firing on a normal Saturday until the owner filters the alerts
    /// into a folder they never open. The tests below pin both edges.
    /// </summary>
    [TestFixture]
    public class StaffAlertRulesTests
    {
        private static readonly Guid Cashier = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly ISet<(Guid, string)> NoHistory = new HashSet<(Guid, string)>();

        private static StaffActionInput Action(string action, string metadata, string email = "cashier@track.test")
            => new()
            {
                ActorUserId = Cashier,
                ActorEmail = email,
                Action = action,
                Summary = "",
                IpAddress = "203.0.113.45",
                CreatedAtUtc = new DateTime(2026, 7, 25, 18, 0, 0, DateTimeKind.Utc),
                Metadata = metadata,
            };

        private static bool Has(List<StaffAlertFlag> flags, string rule) => flags.Any(f => f.Rule == rule);

        [Test]
        public void An_ordinary_card_refund_trips_nothing()
        {
            var flags = StaffAlertRules.Evaluate(new[]
            {
                Action("purchase.refund",
                    """{"refundCents":2500,"paymentMethod":"stripe","stripeRefundId":"re_123","wasCheckedIn":false}"""),
            }, refundThresholdCents: 50000, NoHistory);

            Assert.That(flags, Is.Empty,
                "A normal refund on a card, below threshold, must never generate an alert.");
        }

        [Test]
        public void A_cash_refund_is_flagged_because_no_processor_can_corroborate_it()
        {
            var flags = StaffAlertRules.Evaluate(new[]
            {
                Action("purchase.refund",
                    """{"refundCents":4000,"paymentMethod":"cash","stripeRefundId":null,"wasCheckedIn":false}"""),
            }, 50000, NoHistory);

            Assert.That(Has(flags, StaffAlertRules.RuleCashRefund), Is.True);
        }

        [Test]
        public void A_card_refund_with_no_processor_id_is_still_flagged()
        {
            // Payment method says card but nothing was actually reversed at Stripe: either a bug
            // or a refund that only happened in our books.
            var flags = StaffAlertRules.Evaluate(new[]
            {
                Action("purchase.refund",
                    """{"refundCents":4000,"paymentMethod":"stripe","wasCheckedIn":false}"""),
            }, 50000, NoHistory);

            Assert.That(Has(flags, StaffAlertRules.RuleCashRefund), Is.True);
        }

        [Test]
        public void Refunding_a_purchase_that_already_rode_is_flagged()
        {
            var flags = StaffAlertRules.Evaluate(new[]
            {
                Action("purchase.refund",
                    """{"refundCents":3000,"paymentMethod":"stripe","stripeRefundId":"re_1","wasCheckedIn":true}"""),
            }, 50000, NoHistory);

            Assert.That(Has(flags, StaffAlertRules.RuleRefundAfterCheckIn), Is.True);
        }

        [Test]
        public void A_shop_refund_redirected_to_store_credit_is_flagged()
        {
            var flags = StaffAlertRules.Evaluate(new[]
            {
                Action("shop.refund",
                    """{"totalCents":18000,"paymentMethod":"cash","destination":"credit","creditedCents":18000}"""),
            }, 50000, NoHistory);

            Assert.That(Has(flags, StaffAlertRules.RuleRefundToCredit), Is.True,
                "Cash in, value out to an account someone can spend, is the sharpest pattern here.");
        }

        [Test]
        public void A_shop_refund_back_to_the_original_card_is_not_flagged_as_redirected()
        {
            var flags = StaffAlertRules.Evaluate(new[]
            {
                Action("shop.refund",
                    """{"totalCents":18000,"paymentMethod":"stripe","destination":"original"}"""),
            }, 50000, NoHistory);

            Assert.That(Has(flags, StaffAlertRules.RuleRefundToCredit), Is.False);
        }

        [Test]
        public void Granting_store_credit_is_flagged_but_deducting_it_is_not()
        {
            var granted = StaffAlertRules.Evaluate(new[]
            {
                Action("credit.manual_adjust", """{"deltaCents":25000,"accountEmail":"friend@example.com"}"""),
            }, 50000, NoHistory);
            var deducted = StaffAlertRules.Evaluate(new[]
            {
                Action("credit.manual_adjust", """{"deltaCents":-25000,"accountEmail":"friend@example.com"}"""),
            }, 50000, NoHistory);

            Assert.Multiple(() =>
            {
                Assert.That(Has(granted, StaffAlertRules.RuleCreditGrant), Is.True);
                Assert.That(Has(deducted, StaffAlertRules.RuleCreditGrant), Is.False,
                    "Taking credit away costs the tenant nothing and must not create noise.");
            });
        }

        [Test]
        public void Refund_totals_accumulate_across_surfaces_before_the_threshold_applies()
        {
            // Splitting a day's refunds across the gate, the food window and the shop must not
            // duck under a per-action threshold.
            var flags = StaffAlertRules.Evaluate(new[]
            {
                Action("purchase.refund", """{"refundCents":20000,"paymentMethod":"stripe","stripeRefundId":"re_1"}"""),
                Action("concession.refund", """{"totalCents":15000,"paymentMethod":"stripe"}"""),
                Action("shop.refund", """{"totalCents":16000,"paymentMethod":"stripe","destination":"original"}"""),
            }, refundThresholdCents: 50000, NoHistory);

            Assert.That(Has(flags, StaffAlertRules.RuleRefundTotal), Is.True, "51000 total is over the 50000 threshold");
        }

        [Test]
        public void Refund_totals_are_tracked_per_person_not_per_track()
        {
            // Two cashiers each under the threshold must not add up into a false alarm.
            var flags = StaffAlertRules.Evaluate(new[]
            {
                Action("purchase.refund", """{"refundCents":30000,"paymentMethod":"stripe","stripeRefundId":"re_1"}""", "a@track.test"),
                Action("purchase.refund", """{"refundCents":30000,"paymentMethod":"stripe","stripeRefundId":"re_2"}""", "b@track.test"),
            }, refundThresholdCents: 50000, NoHistory);

            Assert.That(Has(flags, StaffAlertRules.RuleRefundTotal), Is.False);
        }

        [Test]
        public void Manager_pin_failures_only_trip_once_they_look_like_guessing()
        {
            var twice = StaffAlertRules.Evaluate(new[]
            {
                Action("concession.manager_pin_failed", "{}"),
                Action("concession.manager_pin_failed", "{}"),
            }, 50000, NoHistory);
            var thrice = StaffAlertRules.Evaluate(new[]
            {
                Action("concession.manager_pin_failed", "{}"),
                Action("concession.manager_pin_failed", "{}"),
                Action("concession.manager_pin_failed", "{}"),
            }, 50000, NoHistory);

            Assert.Multiple(() =>
            {
                Assert.That(Has(twice, StaffAlertRules.RulePinFailures), Is.False, "Two fat-fingers is a Tuesday.");
                Assert.That(Has(thrice, StaffAlertRules.RulePinFailures), Is.True);
            });
        }

        [Test]
        public void A_new_address_is_flagged_once_per_person_not_once_per_action()
        {
            var known = new HashSet<(Guid, string)> { (Cashier, "198.51.100.1") };
            var flags = StaffAlertRules.Evaluate(new[]
            {
                Action("purchase.refund", """{"refundCents":100,"paymentMethod":"stripe","stripeRefundId":"re_1"}"""),
                Action("purchase.refund", """{"refundCents":100,"paymentMethod":"stripe","stripeRefundId":"re_2"}"""),
                Action("purchase.refund", """{"refundCents":100,"paymentMethod":"stripe","stripeRefundId":"re_3"}"""),
            }, 50000, known);

            Assert.That(flags.Count(f => f.Rule == StaffAlertRules.RuleNewAddress), Is.EqualTo(1),
                "A busy shift from one new address is one fact, not thirty.");
        }

        [Test]
        public void A_tenant_with_no_address_history_does_not_flag_everyone_on_day_one()
        {
            var flags = StaffAlertRules.Evaluate(new[]
            {
                Action("purchase.refund", """{"refundCents":100,"paymentMethod":"stripe","stripeRefundId":"re_1"}"""),
            }, 50000, NoHistory);

            Assert.That(Has(flags, StaffAlertRules.RuleNewAddress), Is.False,
                "An empty history means we know nothing yet, not that everything is suspicious.");
        }

        [Test]
        public void A_known_address_is_not_flagged()
        {
            var known = new HashSet<(Guid, string)> { (Cashier, "203.0.113.45") };
            var flags = StaffAlertRules.Evaluate(new[]
            {
                Action("purchase.refund", """{"refundCents":100,"paymentMethod":"stripe","stripeRefundId":"re_1"}"""),
            }, 50000, known);

            Assert.That(Has(flags, StaffAlertRules.RuleNewAddress), Is.False);
        }

        [Test]
        public void Malformed_or_missing_metadata_does_not_take_the_sweep_down()
        {
            Assert.DoesNotThrow(() =>
            {
                var flags = StaffAlertRules.Evaluate(new[]
                {
                    Action("purchase.refund", "not json at all"),
                    Action("purchase.refund", null!),
                    Action("shop.refund", "[]"),
                }, 50000, NoHistory);
                Assert.That(flags, Is.Not.Null);
            });
        }

        [Test]
        public void An_unrecognised_action_is_ignored_rather_than_guessed_at()
        {
            var flags = StaffAlertRules.Evaluate(new[]
            {
                Action("tenant.update", """{"whatever":1}"""),
            }, 50000, NoHistory);

            Assert.That(flags, Is.Empty);
        }
    }
}
