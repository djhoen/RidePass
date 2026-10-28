using System.Globalization;
using System.Text;
using Services.Accounting;

namespace Services.QuickBooks.Desktop
{
    public sealed class IifWriteException : Exception
    {
        public IifWriteException(string message) : base(message) { }
    }

    /// <summary>
    /// Writes balanced <see cref="JournalDraft"/>s as one IIF file of GENERAL JOURNAL transactions
    /// that QuickBooks Desktop imports with File > Utilities > Import > IIF Files. Pure.
    ///
    /// Each day becomes one transaction: a TRNS row for its first line, an SPL row for each of the
    /// rest, and an ENDTRNS terminator. IIF has no debit/credit column; the sign carries the side
    /// (positive debit, negative credit), and QuickBooks rejects a transaction whose rows do not sum
    /// to zero, so a draft the builder has already proven balanced imports as-is.
    ///
    /// The file deliberately carries no !ACCNT or !CLASS sections. Those would let the import create
    /// accounts, which is exactly what must not happen: every line names an account the track
    /// already has, checked against its own uploaded Chart of Accounts before this is ever called.
    /// </summary>
    public static class IifJournalWriter
    {
        public const string Memo = "RidePass daily sales summary";

        public static string DocNumberFor(DateOnly businessDate) => $"RP-{businessDate:yyyyMMdd}";

        /// <param name="accountNameByKey">Desktop full account name per QboAccountKeys slot.</param>
        /// <remarks>
        /// A line's ClassId is already the Desktop class FULL name here (the Desktop export resolves
        /// classes by name), so it is written straight into the CLASS column.
        /// </remarks>
        public static string Write(IEnumerable<JournalDraft> drafts, IReadOnlyDictionary<string, string> accountNameByKey)
        {
            var sb = new StringBuilder();
            void Row(params string[] cells) => sb.Append(string.Join('\t', cells)).Append("\r\n");

            Row("!TRNS", "TRNSID", "TRNSTYPE", "DATE", "ACCNT", "CLASS", "AMOUNT", "DOCNUM", "MEMO");
            Row("!SPL", "SPLID", "TRNSTYPE", "DATE", "ACCNT", "CLASS", "AMOUNT", "DOCNUM", "MEMO");
            Row("!ENDTRNS");

            foreach (var draft in drafts)
            {
                if (draft.IsEmpty) continue;
                if (draft.TotalDebitCents != draft.TotalCreditCents)
                {
                    throw new IifWriteException($"{draft.BusinessDate:yyyy-MM-dd} is not balanced and was not written.");
                }

                // US month/day/year, which is how Desktop parses IIF dates.
                var date = draft.BusinessDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
                var doc = DocNumberFor(draft.BusinessDate);

                for (var i = 0; i < draft.Lines.Count; i++)
                {
                    var line = draft.Lines[i];
                    if (!accountNameByKey.TryGetValue(line.AccountKey, out var account) || string.IsNullOrWhiteSpace(account))
                    {
                        throw new IifWriteException(
                            $"No QuickBooks Desktop account is mapped for \"{QboAccountKeys.Label(line.AccountKey)}\".");
                    }
                    var className = line.ClassId ?? string.Empty;
                    if (!IifFormat.IsSafeField(account) || !IifFormat.IsSafeField(className))
                    {
                        throw new IifWriteException(
                            $"The account or class mapped for \"{QboAccountKeys.Label(line.AccountKey)}\" contains a tab, " +
                            "line break or double quote, which an IIF file can't carry. Rename it in QuickBooks Desktop " +
                            "and upload your lists again.");
                    }

                    var signedCents = line.IsDebit ? line.AmountCents : -line.AmountCents;
                    var amount = (signedCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
                    var memo = $"{Memo}: {QboAccountKeys.Label(line.AccountKey)}";

                    Row(i == 0 ? "TRNS" : "SPL", "", "GENERAL JOURNAL", date, account, className, amount, doc, memo);
                }
                Row("ENDTRNS");
            }

            return sb.ToString();
        }
    }
}
