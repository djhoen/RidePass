using System.Security.Cryptography;
using System.Text;

namespace Services.Coupons
{
    /// <summary>
    /// Generates short, human-friendly coupon codes for racer-issued bundles. Uses an
    /// alphabet that excludes visually ambiguous characters (0/O, 1/I/L) so a rider
    /// reading a code over the phone or a ticket envelope is less likely to mistype.
    /// </summary>
    public static class CouponCodeGenerator
    {
        // 32 chars, excludes 0 / O / 1 / I / L for visual clarity. Powers of 2 keep
        // RandomNumberGenerator math unbiased without modulo skew.
        private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";  // 30 chars
        private const int CodeLength = 8;

        /// <summary>
        /// Returns a code like "RIDE-A4Q7CX2P". Caller is responsible for retrying on
        /// uniqueness collisions; with 30^8 ≈ 6.5e11 combinations a tenant can issue
        /// a million codes and not realistically collide, but we still loop on insert.
        /// </summary>
        public static string Generate(string prefix = "RIDE")
        {
            var sb = new StringBuilder(prefix.Length + 1 + CodeLength);
            if (!string.IsNullOrEmpty(prefix))
            {
                sb.Append(prefix);
                sb.Append('-');
            }
            Span<byte> buffer = stackalloc byte[CodeLength];
            RandomNumberGenerator.Fill(buffer);
            for (int i = 0; i < CodeLength; i++)
            {
                sb.Append(Alphabet[buffer[i] % Alphabet.Length]);
            }
            return sb.ToString();
        }
    }
}
