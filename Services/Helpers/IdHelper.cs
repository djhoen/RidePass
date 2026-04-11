namespace Services.Helpers
{
    public class IdHelper
    {
        private static Random random = new Random();

        public static string GenerateId(int length)
        {
            const string chars = "ABCDEFGHIJKLMNPQRSTUVWXYZ123456789";
            var stringChars = new char[length];
            var random = new Random();

            for (int i = 0; i < length; i++)
            {
                stringChars[i] = chars[random.Next(chars.Length)];
            }

            return new String(stringChars);
        }
    }
}
