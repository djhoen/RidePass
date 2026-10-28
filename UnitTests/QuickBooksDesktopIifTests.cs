using NUnit.Framework;
using Services.Accounting;
using Services.QuickBooks.Desktop;
using Services.Repositories.Data.QuickBooksData;

namespace UnitTests
{
    // The QuickBooks Desktop export hands a bookkeeper a file that goes straight into their books.
    // Two properties matter more than anything else here:
    //
    //   1. Every journal line names an account EXACTLY as the track's own export spelled it. IIF
    //      import creates any account it doesn't recognise, so a near-miss is not an error, it is a
    //      junk account. The parser must read full names faithfully (column order, quoting, hidden
    //      flags, 1252 bytes) and the writer must emit them byte-for-byte.
    //   2. Each day's rows sum to zero with debits positive and credits negative, which is the only
    //      way IIF expresses sides.
    [TestFixture]
    public class QuickBooksDesktopIifTests
    {
        private const string T = "\t";

        private static string ListFile(params string[] rows) => string.Join("\r\n", rows) + "\r\n";

        // ── Parser ───────────────────────────────────────────────────────────────────────

        [Test]
        public void Parse_reads_accounts_and_classes_through_their_headers()
        {
            var text = ListFile(
                "!HDR" + T + "PROD" + T + "VER",
                "HDR" + T + "QuickBooks Pro Plus" + T + "Version 34.0D",
                "!ACCNT" + T + "NAME" + T + "REFNUM" + T + "ACCNTTYPE" + T + "ACCNUM" + T + "HIDDEN",
                "ACCNT" + T + "Income:Tickets & Fees" + T + "12" + T + "INC" + T + "40510" + T + "N",
                "ACCNT" + T + "Undeposited Funds" + T + "2" + T + "OCASSET" + T + "14999" + T + "N",
                "ACCNT" + T + "Old Income" + T + "3" + T + "INC" + T + "" + T + "Y",
                "!CLASS" + T + "NAME" + T + "REFNUM" + T + "HIDDEN",
                "CLASS" + T + "Corporate:Revenue:Tickets & Fees" + T + "5" + T + "N");

            var lists = IifListParser.Parse(text);

            Assert.That(lists.Product, Is.EqualTo("QuickBooks Pro Plus"));
            Assert.That(lists.Accounts.Select(a => a.FullName),
                Is.EqualTo(new[] { "Income:Tickets & Fees", "Undeposited Funds", "Old Income" }));
            Assert.That(lists.Accounts[0].AccountType, Is.EqualTo("INC"));
            Assert.That(lists.Accounts[0].AccountNumber, Is.EqualTo("40510"));
            Assert.That(lists.Accounts[0].Classification, Is.EqualTo("Revenue"));
            Assert.That(lists.Accounts[1].Classification, Is.EqualTo("Asset"));
            Assert.That(lists.Accounts[2].IsHidden, Is.True);
            Assert.That(lists.Accounts[2].AccountNumber, Is.Null);
            Assert.That(lists.Classes.Single().FullName, Is.EqualTo("Corporate:Revenue:Tickets & Fees"));
        }

        [Test]
        public void Parse_follows_the_header_when_columns_move()
        {
            // A different QuickBooks version ordering the columns differently must not shift
            // the account type into the name.
            var text = ListFile(
                "!ACCNT" + T + "ACCNTTYPE" + T + "HIDDEN" + T + "NAME",
                "ACCNT" + T + "OCLIAB" + T + "N" + T + "Sales Tax Payable");

            var a = IifListParser.Parse(text).Accounts.Single();
            Assert.That(a.FullName, Is.EqualTo("Sales Tax Payable"));
            Assert.That(a.AccountType, Is.EqualTo("OCLIAB"));
            Assert.That(a.Classification, Is.EqualTo("Liability"));
        }

        [Test]
        public void Parse_unquotes_values_and_skips_other_lists()
        {
            var text = ListFile(
                "!CUST" + T + "NAME",
                "CUST" + T + "Some Customer",
                "!ACCNT" + T + "NAME" + T + "ACCNTTYPE",
                "ACCNT" + T + "\"Meals, Rooms \"\"Tax\"\"\"" + T + "OCLIAB");

            var lists = IifListParser.Parse(text);
            Assert.That(lists.Accounts.Single().FullName, Is.EqualTo("Meals, Rooms \"Tax\""));
            Assert.That(lists.Classes, Is.Empty);
        }

        [Test]
        public void Parse_rejects_a_file_that_is_not_iif()
        {
            Assert.Throws<IifParseException>(() => IifListParser.Parse("Account,Type\r\nSales,Income\r\n"));
        }

        [Test]
        public void Parse_rejects_an_iif_file_with_no_accounts_or_classes()
        {
            Assert.Throws<IifParseException>(() => IifListParser.Parse(ListFile("!CUST" + T + "NAME", "CUST" + T + "Bob")));
        }

        [Test]
        public void Decode_reads_desktop_ansi_bytes()
        {
            // "Café" as Windows-1252 (0xE9), which is what Desktop writes. Read as UTF-8 it would be
            // invalid; the decoder must fall back to 1252 rather than mangle the name.
            var bytes = new byte[] { (byte)'C', (byte)'a', (byte)'f', 0xE9 };
            Assert.That(IifFormat.Decode(bytes), Is.EqualTo("Café"));
            Assert.That(IifFormat.Encode("Café"), Is.EqualTo(bytes));
        }

        [Test]
        public void Ar_ap_and_non_posting_accounts_are_not_postable()
        {
            Assert.That(IifListParser.IsPostable("AR"), Is.False);
            Assert.That(IifListParser.IsPostable("AP"), Is.False);
            Assert.That(IifListParser.IsPostable("NONPOSTING"), Is.False);
            Assert.That(IifListParser.IsPostable("OCASSET"), Is.True);
            Assert.That(IifListParser.IsPostable("INC"), Is.True);
        }

        // ── Writer ───────────────────────────────────────────────────────────────────────

        private static AccountingEntry CardSale(DateOnly day, int gross, int fee, int cut) => new()
        {
            TenantId = Guid.NewGuid(),
            EntryKind = "sale",
            SourceKind = "event_ticket",
            SourceId = Guid.NewGuid(),
            BusinessDate = day,
            PaymentMethod = "stripe",
            GrossCents = gross,
            StripeFeeCents = fee,
            RidepassCutCents = cut,
            NetToTenantCents = gross - fee - cut,
        };

        private static readonly Dictionary<string, string> Accounts = new()
        {
            [QboAccountKeys.RevenueEventTicket] = "Income:Tickets & Fees",
            [QboAccountKeys.ExpenseStripeFees] = "Expenses:Merchant Fees",
            [QboAccountKeys.ExpenseRidepassFees] = "Expenses:RidePass Fees",
            [QboAccountKeys.AssetRidepassReceivable] = "RidePass Clearing",
        };

        private static List<string[]> DataRows(string iif) =>
            iif.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
               .Where(r => !r.StartsWith('!'))
               .Select(r => r.Split('\t'))
               .ToList();

        [Test]
        public void Write_emits_one_balanced_general_journal_per_day()
        {
            var d1 = new DateOnly(2026, 10, 24);
            var d2 = new DateOnly(2026, 10, 25);
            var drafts = new[]
            {
                JournalEntryBuilder.Build(new[] { CardSale(d1, 10_000, 320, 400) }, d1),
                JournalEntryBuilder.Build(new[] { CardSale(d2, 5_000, 175, 200) }, d2),
            };

            var iif = IifJournalWriter.Write(drafts, Accounts);
            var rows = DataRows(iif);

            Assert.That(iif, Does.StartWith("!TRNS\tTRNSID\tTRNSTYPE\tDATE\tACCNT\tCLASS\tAMOUNT\tDOCNUM\tMEMO\r\n"));
            Assert.That(rows.Count(r => r[0] == "TRNS"), Is.EqualTo(2));
            Assert.That(rows.Count(r => r[0] == "ENDTRNS"), Is.EqualTo(2));

            // Each transaction: rows between TRNS and ENDTRNS sum to zero, carry one date and doc number.
            var current = new List<string[]>();
            foreach (var r in rows)
            {
                if (r[0] == "ENDTRNS")
                {
                    var sum = current.Sum(c => decimal.Parse(c[6], System.Globalization.CultureInfo.InvariantCulture));
                    Assert.That(sum, Is.EqualTo(0m));
                    Assert.That(current.Select(c => c[3]).Distinct().Count(), Is.EqualTo(1));
                    Assert.That(current.All(c => c[2] == "GENERAL JOURNAL"));
                    current.Clear();
                    continue;
                }
                current.Add(r);
            }

            var first = rows.Where(r => r[0] is "TRNS" or "SPL").Take(4).ToList();
            Assert.That(first.All(r => r[3] == "10/24/2026"));
            Assert.That(first.All(r => r[7] == "RP-20261024"));

            // Revenue is a CREDIT, so negative; the full $100 lands on the mapped income account.
            var lines = rows.Where(r => r[0] is "TRNS" or "SPL").ToList();
            var revenue = lines.Single(r => r[4] == "Income:Tickets & Fees" && r[3] == "10/24/2026");
            Assert.That(revenue[6], Is.EqualTo("-100.00"));
            var receivable = lines.Single(r => r[4] == "RidePass Clearing" && r[3] == "10/24/2026");
            Assert.That(receivable[6], Is.EqualTo("92.80"));
        }

        [Test]
        public void Write_puts_the_mapped_class_on_revenue_lines_only()
        {
            var day = new DateOnly(2026, 10, 24);
            var classes = new Dictionary<string, string> { [QboAccountKeys.RevenueEventTicket] = "Corporate:Revenue:Tickets & Fees" };
            var draft = JournalEntryBuilder.Build(new[] { CardSale(day, 10_000, 320, 400) }, day, classes);

            var rows = DataRows(IifJournalWriter.Write(new[] { draft }, Accounts)).Where(r => r[0] != "ENDTRNS").ToList();

            Assert.That(rows.Single(r => r[4] == "Income:Tickets & Fees")[5], Is.EqualTo("Corporate:Revenue:Tickets & Fees"));
            Assert.That(rows.Where(r => r[4] != "Income:Tickets & Fees").All(r => r[5] == ""));
        }

        [Test]
        public void Write_refuses_an_unmapped_account_rather_than_guess()
        {
            var day = new DateOnly(2026, 10, 24);
            var draft = JournalEntryBuilder.Build(new[] { CardSale(day, 10_000, 320, 400) }, day);
            var missingFees = new Dictionary<string, string>(Accounts);
            missingFees.Remove(QboAccountKeys.ExpenseStripeFees);

            Assert.Throws<IifWriteException>(() => IifJournalWriter.Write(new[] { draft }, missingFees));
        }

        [Test]
        public void Write_refuses_a_name_an_iif_field_cannot_carry()
        {
            var day = new DateOnly(2026, 10, 24);
            var draft = JournalEntryBuilder.Build(new[] { CardSale(day, 10_000, 320, 400) }, day);
            var tabbed = new Dictionary<string, string>(Accounts) { [QboAccountKeys.RevenueEventTicket] = "Income\tTickets" };

            Assert.Throws<IifWriteException>(() => IifJournalWriter.Write(new[] { draft }, tabbed));
        }

        [Test]
        public void Write_skips_days_that_net_to_nothing()
        {
            var day = new DateOnly(2026, 10, 24);
            var empty = new JournalDraft(day, Array.Empty<JournalLine>(), 2);
            var iif = IifJournalWriter.Write(new[] { empty }, Accounts);
            Assert.That(DataRows(iif), Is.Empty);
        }
    }
}
