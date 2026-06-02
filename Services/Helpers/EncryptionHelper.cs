using System.Security.Cryptography;
using System.Text;

namespace Services.Helpers
{
    /// <summary>
    /// AES-256-CBC string encryption used for sensitive blobs stored at rest
    /// (e.g. per-tenant Twilio subaccount auth tokens). The key + IV are NOT
    /// in source — <see cref="Configure"/> must be called once at process
    /// startup with values pulled from config (Encryption:KeyBase64 /
    /// Encryption:IvBase64), populated via dotnet user-secrets in dev and
    /// environment variables in production. Any Encrypt/Decrypt call before
    /// Configure throws — fail fast rather than silently mis-encrypting.
    /// </summary>
    public static class EncryptionHelper
    {
        private static byte[]? _key;
        private static byte[]? _iv;
        private const string DELIMITER = ")(*&^%$#@!";

        /// <summary>
        /// One-shot startup configuration. <paramref name="key"/> must be 32 bytes
        /// (AES-256), <paramref name="iv"/> must be 16 bytes (AES block size).
        /// </summary>
        public static void Configure(byte[] key, byte[] iv)
        {
            if (key is null || key.Length != 32)
                throw new ArgumentException("AES-256 key must be exactly 32 bytes.", nameof(key));
            if (iv is null || iv.Length != 16)
                throw new ArgumentException("AES IV must be exactly 16 bytes.", nameof(iv));
            _key = key;
            _iv = iv;
        }

        public static string Decrypt(string token)
        {
            EnsureConfigured();
            token = token.Replace(' ', '+');
            byte[] decrypted = null;
            try
            {
                decrypted = Decrypt(Convert.FromBase64String(token));
            }
            catch
            {
            }

            if (decrypted == null)
            {
                return null;
            }

            var parts = Encoding.UTF8.GetString(decrypted).Split(new[] { DELIMITER }, StringSplitOptions.None);
            if (parts[1] == string.Empty)
            {
                parts[1] = null;
            }

            if (!string.IsNullOrEmpty(parts[1]) && long.Parse(parts[1]) < DateTime.UtcNow.Ticks)
            {
                return null;
            }

            return parts[0];
        }

        public static byte[] Decrypt(byte[] token)
        {
            EnsureConfigured();
            try
            {
                using var aes = Aes.Create();
                var decryptor = aes.CreateDecryptor(_key!, _iv!);
                using var cryptoStream = new CryptoStream(new MemoryStream(token), decryptor, CryptoStreamMode.Read);
                using var outputStream = new MemoryStream();
                var buffer = new byte[500];
                int bytesRead = cryptoStream.Read(buffer, 0, 500);

                while (bytesRead != 0)
                {
                    outputStream.Write(buffer, 0, bytesRead);
                    bytesRead = cryptoStream.Read(buffer, 0, 500);
                }

                return outputStream.ToArray();
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static byte[] Encrypt(byte[] value)
        {
            EnsureConfigured();
            using var aes = Aes.Create();
            ICryptoTransform encryptor = aes.CreateEncryptor(_key!, _iv!);

            using MemoryStream memoryStream = new MemoryStream();
            using (CryptoStream cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
            {
                cryptoStream.Write(value, 0, value.Length);
            }

            return memoryStream.ToArray();
        }

        public static string Encrypt(string value, TimeSpan? expiration)
        {
            var token = string.Join(
                DELIMITER,
                value,
                expiration == null ? string.Empty : DateTime.UtcNow.Add(expiration.Value).Ticks.ToString());

            return Convert.ToBase64String(Encrypt(Encoding.UTF8.GetBytes(token)));
        }

        private static void EnsureConfigured()
        {
            if (_key is null || _iv is null)
            {
                throw new InvalidOperationException(
                    "EncryptionHelper not configured. Call EncryptionHelper.Configure(key, iv) at startup using values from Encryption:KeyBase64 / Encryption:IvBase64.");
            }
        }
    }
}
