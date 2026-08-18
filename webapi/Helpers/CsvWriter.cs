using System.Text;

namespace webapi.Helpers
{
    /// <summary>
    /// Minimal RFC 4180 CSV assembly for report exports. Extracted so a new export does not have
    /// to re-derive the escaping rules (PayoutCsvBuilder and ReportsController.ExportTrackside each
    /// grew their own copy, and both got the formula-injection guard right only because it was
    /// copied by hand).
    ///
    /// A section-oriented writer rather than a single header + rows: a close-of-day export is
    /// several small tables stacked in one file, which is what an accountant expects to open.
    /// </summary>
    public class CsvWriter
    {
        private readonly StringBuilder _sb = new();

        /// <summary>Blank separator line, so stacked sections read as separate tables in a spreadsheet.</summary>
        public CsvWriter Blank()
        {
            _sb.AppendLine();
            return this;
        }

        /// <summary>A single unquoted-if-possible cell on its own line, used for section headings.</summary>
        public CsvWriter Title(string text)
        {
            _sb.AppendLine(Escape(text));
            return this;
        }

        public CsvWriter Row(params object?[] cells)
        {
            _sb.AppendLine(string.Join(',', cells.Select(Format)));
            return this;
        }

        public CsvWriter Rows(IEnumerable<object?[]> rows)
        {
            foreach (var r in rows) Row(r);
            return this;
        }

        /// <summary>Cents rendered as plain decimal dollars, no currency symbol or thousands separator.</summary>
        public static string Money(long cents) => (cents / 100m).ToString("0.00");

        public override string ToString() => _sb.ToString();

        public byte[] ToBytes() => Encoding.UTF8.GetBytes(_sb.ToString());

        private static string Format(object? cell) => cell switch
        {
            null => "",
            string s => Escape(s),
            // Numbers are written raw: invariant culture so a decimal point never becomes a comma,
            // and no injection guard, because a negative number legitimately starts with '-' and
            // quoting it would land "'-12.34" in the cell.
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => Escape(cell.ToString() ?? ""),
        };

        /// <summary>
        /// Quote when the value contains a quote, comma or line break; double internal quotes.
        /// A leading = + - @ (or tab/CR) is prefixed with an apostrophe first: Excel and Sheets
        /// evaluate such a cell as a FORMULA, and staff names, memos and event titles all flow
        /// through here, so an exported file is a real injection vector without it. A value that
        /// is simply a number is exempt, so a refund total is not disfigured to defuse a threat
        /// it does not carry.
        /// </summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if ("=+-@\t\r".IndexOf(value[0]) >= 0 && !IsPlainNumber(value)) value = "'" + value;
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static bool IsPlainNumber(string value) =>
            decimal.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out _);
    }
}
