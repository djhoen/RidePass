using System.Text.Json;

namespace Services.Alerts
{
    /// <summary>One audit entry, reduced to what the rules actually read. Keeping this separate
    /// from AuditLogEntry means the rules never touch jsonb parsing or the database.</summary>
    public class StaffActionInput
    {
        public Guid? ActorUserId { get; set; }
        public string? ActorEmail { get; set; }
        public string Action { get; set; } = null!;
        public string Summary { get; set; } = null!;
        public string? IpAddress { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        /// <summary>Raw metadata JSON as written by the action, or null.</summary>
        public string? Metadata { get; set; }
    }

    public class StaffAlertFlag
    {
        public string Rule { get; set; } = null!;
        /// <summary>Who did it, for grouping in the email.</summary>
        public string Who { get; set; } = null!;
        /// <summary>One line an owner can act on without opening anything.</summary>
        public string Detail { get; set; } = null!;
    }

    /// <summary>
    /// The tripwires. Pure logic over one tenant's activity for one local day, so every rule is
    /// reachable from a test with a list of plain objects.
    ///
    /// These are deliberately shaped to fire on things that are unusual rather than things that
    /// are merely large. A busy Saturday should never generate an alert; a cash refund at 1am on
    /// a purchase that already rode should generate one every time. An alert system that cries
    /// wolf gets filtered to a folder nobody reads, which is worse than having none.
    /// </summary>
    public static class StaffAlertRules
    {
        public const string RuleRefundTotal = "refund_total_over_threshold";
        public const string RuleCashRefund = "cash_refund_no_processor_trace";
        public const string RuleRefundAfterCheckIn = "refund_after_check_in";
        public const string RuleRefundToCredit = "refund_redirected_to_store_credit";
        public const string RuleCreditGrant = "manual_store_credit_grant";
        public const string RulePinFailures = "repeated_manager_pin_failures";
        public const string RuleNewAddress = "new_address_for_staffer";

        private const int PinFailureThreshold = 3;

        /// <summary>
        /// <paramref name="knownAddresses"/> is the set of (actor, ip) pairs already seen for this
        /// tenant BEFORE the scanned day. Anything outside it is a first sighting. Pass an empty
        /// set to disable that rule, which is what a tenant with no history should do rather than
        /// have every staffer flagged on day one.
        /// </summary>
        public static List<StaffAlertFlag> Evaluate(
            IEnumerable<StaffActionInput> dayActions,
            int refundThresholdCents,
            ISet<(Guid ActorUserId, string Ip)> knownAddresses)
        {
            var flags = new List<StaffAlertFlag>();
            var actions = dayActions.ToList();

            var refundTotals = new Dictionary<string, int>();
            var pinFailures = new Dictionary<string, int>();

            foreach (var a in actions)
            {
                var who = a.ActorEmail ?? a.ActorUserId?.ToString() ?? "an unidentified account";
                var meta = Parse(a.Metadata);

                switch (a.Action)
                {
                    case "purchase.refund":
                    {
                        var cents = Int(meta, "refundCents");
                        Add(refundTotals, who, cents);

                        // Cash never touched the processor, so there is no independent record that
                        // the money went back to a customer rather than into a pocket.
                        if (String(meta, "paymentMethod") == "cash" || String(meta, "stripeRefundId") is null)
                        {
                            flags.Add(new StaffAlertFlag
                            {
                                Rule = RuleCashRefund,
                                Who = who,
                                Detail = $"Refunded {Money(cents)} with no payment-processor record ({String(meta, "paymentMethod") ?? "unknown tender"}).",
                            });
                        }

                        // The customer already rode. Legitimate sometimes, worth a look always.
                        if (Bool(meta, "wasCheckedIn"))
                        {
                            flags.Add(new StaffAlertFlag
                            {
                                Rule = RuleRefundAfterCheckIn,
                                Who = who,
                                Detail = $"Refunded {Money(cents)} on a purchase that had already been checked in.",
                            });
                        }
                        break;
                    }

                    case "concession.refund":
                    {
                        var cents = Int(meta, "totalCents");
                        Add(refundTotals, who, cents);
                        if (String(meta, "paymentMethod") == "cash")
                        {
                            flags.Add(new StaffAlertFlag
                            {
                                Rule = RuleCashRefund,
                                Who = who,
                                Detail = $"Refunded {Money(cents)} of food and drink in cash, authorised by {String(meta, "authorizedBy") ?? "an unnamed manager"}.",
                            });
                        }
                        break;
                    }

                    case "shop.refund":
                    {
                        var cents = Int(meta, "totalCents");
                        Add(refundTotals, who, cents);

                        // The money did not go back the way it came. It went somewhere spendable.
                        if (String(meta, "destination") == "credit")
                        {
                            flags.Add(new StaffAlertFlag
                            {
                                Rule = RuleRefundToCredit,
                                Who = who,
                                Detail = $"Refunded {Money(cents)} of a shop sale to store credit instead of the original payment.",
                            });
                        }
                        else if (String(meta, "paymentMethod") == "cash")
                        {
                            flags.Add(new StaffAlertFlag
                            {
                                Rule = RuleCashRefund,
                                Who = who,
                                Detail = $"Refunded {Money(cents)} of a shop sale in cash, with no payment-processor record.",
                            });
                        }
                        break;
                    }

                    case "credit.manual_adjust":
                    {
                        // Value created with no sale behind it. Only grants matter; deducting
                        // credit costs the tenant nothing.
                        var delta = Int(meta, "deltaCents");
                        if (delta > 0)
                        {
                            flags.Add(new StaffAlertFlag
                            {
                                Rule = RuleCreditGrant,
                                Who = who,
                                Detail = $"Granted {Money(delta)} of store credit to {String(meta, "accountEmail") ?? "an account"} with no sale behind it.",
                            });
                        }
                        break;
                    }

                    case "concession.manager_pin_failed":
                        Add(pinFailures, who, 1);
                        break;
                }

                // A staffer appearing from an address the tenant has never seen. On its own this
                // is often just a new phone on mobile data; alongside anything else in this list
                // it is the difference between careless and deliberate.
                if (a.ActorUserId is Guid actorId
                    && !string.IsNullOrWhiteSpace(a.IpAddress)
                    && knownAddresses.Count > 0
                    && !knownAddresses.Contains((actorId, a.IpAddress!)))
                {
                    if (!flags.Any(f => f.Rule == RuleNewAddress && f.Who == who))
                    {
                        flags.Add(new StaffAlertFlag
                        {
                            Rule = RuleNewAddress,
                            Who = who,
                            Detail = $"Acted from {a.IpAddress}, an address this track hasn't seen them use before.",
                        });
                    }
                }
            }

            foreach (var (who, total) in refundTotals)
            {
                if (total >= refundThresholdCents)
                {
                    flags.Add(new StaffAlertFlag
                    {
                        Rule = RuleRefundTotal,
                        Who = who,
                        Detail = $"Refunded {Money(total)} in total today, over the {Money(refundThresholdCents)} alert threshold.",
                    });
                }
            }

            foreach (var (who, count) in pinFailures)
            {
                if (count >= PinFailureThreshold)
                {
                    flags.Add(new StaffAlertFlag
                    {
                        Rule = RulePinFailures,
                        Who = who,
                        Detail = $"Failed the manager PIN {count} times, which is what guessing looks like.",
                    });
                }
            }

            return flags;
        }

        private static void Add(Dictionary<string, int> map, string key, int amount) =>
            map[key] = map.TryGetValue(key, out var cur) ? cur + amount : amount;

        private static string Money(int cents) => $"${cents / 100m:0.00}";

        /// <summary>Metadata is written by our own code, but a malformed or absent blob must never
        /// take the whole nightly sweep down for a tenant.</summary>
        private static JsonElement? Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonDocument.Parse(json).RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? String(JsonElement? meta, string name) =>
            meta is { } m && m.ValueKind == JsonValueKind.Object
                && m.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;

        private static int Int(JsonElement? meta, string name) =>
            meta is { } m && m.ValueKind == JsonValueKind.Object
                && m.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number
                && p.TryGetInt32(out var v)
                ? v
                : 0;

        private static bool Bool(JsonElement? meta, string name) =>
            meta is { } m && m.ValueKind == JsonValueKind.Object
                && m.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
    }
}
