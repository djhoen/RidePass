namespace Services.QuickBooks.Desktop
{
    /// <summary>One account from a Desktop Chart of Accounts export.</summary>
    public sealed record IifAccount(string FullName, string AccountType, string? AccountNumber, bool IsHidden)
    {
        /// <summary>Revenue | Expense | Asset | Liability | Equity | NonPosting, for the mapping dropdowns.</summary>
        public string Classification => IifListParser.ClassificationFor(AccountType);
    }

    /// <summary>One class from a Desktop Class List export.</summary>
    public sealed record IifClass(string FullName, bool IsHidden);

    /// <summary>What an uploaded list file contained. A file may carry either list or both.</summary>
    public sealed record IifLists(string? Product, IReadOnlyList<IifAccount> Accounts, IReadOnlyList<IifClass> Classes)
    {
        public bool HasAccounts => Accounts.Count > 0;
        public bool HasClasses => Classes.Count > 0;
    }

    public sealed class IifParseException : Exception
    {
        public IifParseException(string message) : base(message) { }
    }

    /// <summary>
    /// Reads the Chart of Accounts and Class List out of a QuickBooks Desktop list export
    /// (File > Utilities > Export > Lists to IIF Files). Pure: text in, records out.
    ///
    /// IIF is tab-separated, and every section starts with a header row whose first cell is the
    /// row type prefixed with "!" ("!ACCNT", "!CLASS") and whose remaining cells NAME the columns.
    /// Column order is not fixed across QuickBooks versions, so every data row is read through the
    /// most recent header for its type rather than by position. Sections this feature has no use
    /// for (customers, items, vendors) are skipped whole.
    ///
    /// NAME is the account's FULL name ("Income:Tickets & Fees"), the same string an imported
    /// journal line must quote, which is the entire reason we read it from the track's own file
    /// instead of letting someone type it.
    /// </summary>
    public static class IifListParser
    {
        public static IifLists Parse(string text)
        {
            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var accounts = new List<IifAccount>();
            var classes = new List<IifClass>();
            string? product = null;
            var sawAnyHeader = false;

            var rows = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var raw in rows)
            {
                if (raw.Length == 0) continue;
                var cells = raw.Split('\t').Select(Unquote).ToArray();
                var type = cells[0];
                if (type.Length == 0) continue;

                if (type.StartsWith('!'))
                {
                    sawAnyHeader = true;
                    headers[type[1..]] = cells;
                    continue;
                }

                if (!headers.TryGetValue(type, out var header)) continue;
                string? Get(string column)
                {
                    var i = Array.FindIndex(header, h => string.Equals(h, column, StringComparison.OrdinalIgnoreCase));
                    if (i < 0 || i >= cells.Length) return null;
                    var v = cells[i].Trim();
                    return v.Length == 0 ? null : v;
                }

                switch (type.ToUpperInvariant())
                {
                    case "HDR":
                        product = Get("PROD");
                        break;

                    case "ACCNT":
                        var name = Get("NAME");
                        var accountType = Get("ACCNTTYPE");
                        if (name is null || accountType is null) continue;
                        accounts.Add(new IifAccount(name, accountType.ToUpperInvariant(), Get("ACCNUM"), IsYes(Get("HIDDEN"))));
                        break;

                    case "CLASS":
                        var className = Get("NAME");
                        if (className is null) continue;
                        classes.Add(new IifClass(className, IsYes(Get("HIDDEN"))));
                        break;
                }
            }

            if (!sawAnyHeader)
            {
                throw new IifParseException(
                    "This doesn't look like a QuickBooks Desktop IIF file. In QuickBooks Desktop choose " +
                    "File > Utilities > Export > Lists to IIF Files, tick Chart of Accounts and Class List, " +
                    "and upload the file it saves.");
            }
            if (accounts.Count == 0 && classes.Count == 0)
            {
                throw new IifParseException(
                    "The file has no Chart of Accounts or Class List in it. When exporting from QuickBooks " +
                    "Desktop, tick Chart of Accounts (and Class List if you use classes).");
            }

            // A name exported twice would be ambiguous; keep the first, which is what QuickBooks
            // itself would match on import.
            return new IifLists(
                product,
                accounts.GroupBy(a => a.FullName, StringComparer.Ordinal).Select(g => g.First()).ToList(),
                classes.GroupBy(c => c.FullName, StringComparer.Ordinal).Select(g => g.First()).ToList());
        }

        /// <summary>
        /// Account types a summary journal line may post to. AR and AP are excluded because QuickBooks
        /// requires a customer or vendor name on every line that touches them, and a day's summary has
        /// none; NONPOSTING accounts (estimates, purchase orders) can't carry a journal line at all.
        /// </summary>
        public static bool IsPostable(string accountType) =>
            accountType.ToUpperInvariant() is not ("AR" or "AP" or "NONPOSTING");

        public static string ClassificationFor(string accountType) => accountType.ToUpperInvariant() switch
        {
            "INC" or "EXINC" => "Revenue",
            "COGS" or "EXP" or "EXEXP" => "Expense",
            "BANK" or "AR" or "OCASSET" or "FIXASSET" or "OASSET" => "Asset",
            "AP" or "CCARD" or "OCLIAB" or "LTLIAB" => "Liability",
            "EQUITY" => "Equity",
            _ => "NonPosting",
        };

        /// <summary>Plain-English account type for the screen ("OCLIAB" means nothing to most people).</summary>
        public static string TypeLabel(string accountType) => accountType.ToUpperInvariant() switch
        {
            "BANK" => "Bank",
            "AR" => "Accounts Receivable",
            "OCASSET" => "Other Current Asset",
            "FIXASSET" => "Fixed Asset",
            "OASSET" => "Other Asset",
            "AP" => "Accounts Payable",
            "CCARD" => "Credit Card",
            "OCLIAB" => "Other Current Liability",
            "LTLIAB" => "Long Term Liability",
            "EQUITY" => "Equity",
            "INC" => "Income",
            "COGS" => "Cost of Goods Sold",
            "EXP" => "Expense",
            "EXINC" => "Other Income",
            "EXEXP" => "Other Expense",
            "NONPOSTING" => "Non-Posting",
            _ => accountType,
        };

        private static bool IsYes(string? v) =>
            v is not null && (v.Equals("Y", StringComparison.OrdinalIgnoreCase) || v.Equals("YES", StringComparison.OrdinalIgnoreCase));

        /// <summary>QuickBooks wraps some values in double quotes, doubling any quote inside.</summary>
        private static string Unquote(string cell)
        {
            if (cell.Length >= 2 && cell[0] == '"' && cell[^1] == '"')
            {
                return cell[1..^1].Replace("\"\"", "\"");
            }
            return cell;
        }
    }
}
