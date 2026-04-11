using System.Security.Cryptography;
using System.Text;

namespace Services.Helpers
{
    public class EncryptionHelper
    {
        // TODO: Move keys to secure storage (e.g., Azure Key Vault, environment variables)
        private static readonly byte[] _decryptKey = { 181, 2, 145, 66, 182, 233, 11, 209, 92, 0, 191, 195, 89, 207, 156, 126, 180, 155, 68, 123, 131, 134, 22, 175, 167, 156, 197, 149, 60, 110, 218, 69 };
        private static readonly byte[] _decryptIv = { 100, 103, 121, 116, 53, 109, 107, 105, 111, 112, 103, 116, 114, 102, 54, 55 };
        private const string DELIMITER = ")(*&^%$#@!";

        public static string Decrypt(string token)
        {
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
            try
            {
                using (var aesManaged = new AesManaged())
                {
                    var decryptor = aesManaged.CreateDecryptor(_decryptKey, _decryptIv);
                    using (var cryptoStream = new CryptoStream(new MemoryStream(token), decryptor, CryptoStreamMode.Read))
                    {
                        using (var outputStream = new MemoryStream())
                        {
                            var buffer = new byte[500];
                            int bytesRead = cryptoStream.Read(buffer, 0, 500);

                            while (bytesRead != 0)
                            {
                                outputStream.Write(buffer, 0, bytesRead);
                                bytesRead = cryptoStream.Read(buffer, 0, 500);
                            }

                            return outputStream.ToArray();
                        }
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static byte[] Encrypt(byte[] value)
        {
            using (var aesManaged = new AesManaged())
            {
                ICryptoTransform encryptor = aesManaged.CreateEncryptor(_decryptKey, _decryptIv);

                using (MemoryStream memoryStream = new MemoryStream())
                {
                    using (CryptoStream cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                    {
                        cryptoStream.Write(value, 0, value.Length);
                    }

                    return memoryStream.ToArray();
                }
            }
        }

        public static string Encrypt(string value, TimeSpan? expiration)
        {
            var token = string.Join(
                DELIMITER,
                value,
                expiration == null ? string.Empty : DateTime.UtcNow.Add(expiration.Value).Ticks.ToString());

            return Convert.ToBase64String(Encrypt(Encoding.UTF8.GetBytes(token)));
        }
    }
}
