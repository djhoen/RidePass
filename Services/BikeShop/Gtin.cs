namespace Services.BikeShop
{
    /// <summary>
    /// Normalises the many spellings of a product barcode to ONE comparable key.
    ///
    /// The same physical part carries the same GS1 identifier, but it reaches us in different
    /// widths depending on what printed it and what scanned it: a US retail pack is UPC-A (12
    /// digits), the European pack of the identical part is EAN-13, a small item may be EAN-8, and
    /// a case label is GTIN-14. Plenty of scanners also emit a UPC-A as 13 digits with a leading
    /// zero. Compared as strings those are four different products.
    ///
    /// GS1's own guidance is to store every identifier right-justified and zero-filled to 14
    /// digits, and that is what this does. "0759677001028", "759677001028" and "00759677001028"
    /// all become the same key.
    ///
    /// Deliberately NOT a general "clean up the string" helper. A code that isn't a valid GTIN
    /// (a shop's own SKU like "BIKE-250F", or a mistyped number) comes back null so the caller can
    /// fall through to a SKU match rather than silently matching the wrong part. Getting a barcode
    /// wrong at a register sells the customer something they didn't pick up.
    /// </summary>
    public static class Gtin
    {
        /// <summary>Every GS1 identifier width we accept before padding to 14.</summary>
        private static readonly int[] ValidLengths = { 8, 12, 13, 14 };

        /// <summary>
        /// The GTIN-14 form of a scanned code, or null when it isn't a valid GTIN.
        /// Surrounding whitespace and embedded separators (spaces, hyphens) are ignored, because
        /// that is how a human retypes a barcode off a label.
        /// </summary>
        public static string? Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Strip anything that isn't a digit: a retyped code often carries the hyphens printed
            // under the bars. A code containing letters is a SKU, not a GTIN, and is rejected
            // below by the length check rather than being silently stripped down to its digits.
            Span<char> digits = stackalloc char[raw.Length];
            var n = 0;
            foreach (var c in raw)
            {
                if (char.IsDigit(c)) digits[n++] = c;
                else if (c is not (' ' or '-' or '\t' or '_')) return null;   // letters etc. => not a GTIN
            }
            if (n == 0) return null;

            var value = new string(digits[..n]);
            if (Array.IndexOf(ValidLengths, value.Length) < 0) return null;
            if (!HasValidCheckDigit(value)) return null;

            return value.PadLeft(14, '0');
        }

        /// <summary>True when the code is a structurally valid GTIN of any accepted width.</summary>
        public static bool IsValid(string? raw) => Normalize(raw) is not null;

        /// <summary>
        /// The standard GS1 mod-10 check: from the rightmost digit before the check digit, weight
        /// alternating 3 and 1, sum, and the check digit is what rounds that sum up to a multiple
        /// of ten. Works for all four widths precisely because the weighting is anchored on the
        /// right, which is also why padding to 14 afterwards never invalidates it.
        /// </summary>
        private static bool HasValidCheckDigit(string digitsOnly)
        {
            var sum = 0;
            var weight = 3;
            for (var i = digitsOnly.Length - 2; i >= 0; i--)
            {
                sum += (digitsOnly[i] - '0') * weight;
                weight = weight == 3 ? 1 : 3;
            }
            var expected = (10 - (sum % 10)) % 10;
            return expected == digitsOnly[^1] - '0';
        }
    }
}
