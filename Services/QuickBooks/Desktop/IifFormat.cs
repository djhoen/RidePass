using System.Text;

namespace Services.QuickBooks.Desktop
{
    /// <summary>
    /// Byte-level conventions shared by the IIF list parser and the journal writer.
    ///
    /// QuickBooks Desktop reads and writes IIF in the Windows ANSI code page (1252 on a US install),
    /// not UTF-8. That matters for more than display: an IIF import resolves accounts and classes BY
    /// NAME, so "Café Sales" encoded as UTF-8 does not match the "Café Sales" in the company file and
    /// the import quietly creates a second account. Names are therefore decoded from the track's own
    /// export and re-encoded the same way on the way back out.
    /// </summary>
    public static class IifFormat
    {
        private static readonly Lazy<Encoding> Ansi = new(() =>
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        });

        /// <summary>Windows-1252, what Desktop writes and expects.</summary>
        public static Encoding DesktopEncoding => Ansi.Value;

        /// <summary>
        /// Decode an uploaded file. A UTF-8 file (BOM, or valid UTF-8 with non-ASCII bytes, e.g. one
        /// re-saved by a text editor) is honored; anything else is read as Desktop's own 1252. Plain
        /// ASCII decodes identically either way.
        /// </summary>
        public static string Decode(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            try
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return DesktopEncoding.GetString(bytes);
            }
        }

        public static byte[] Encode(string content) => DesktopEncoding.GetBytes(content);

        /// <summary>
        /// Characters an IIF field cannot carry: tabs and line breaks end the field or the row, and a
        /// double quote opens a quoted field. A name containing one cannot be referenced safely.
        /// </summary>
        public static bool IsSafeField(string value) =>
            value.IndexOfAny(new[] { '\t', '\r', '\n', '"' }) < 0;
    }
}
