using System.Text.RegularExpressions;

namespace Services.ExtensionMethods
{
    public static class StringExtensions
    {
        public static String ToFriendlyCase(this string PascalString)
        {
            return Regex.Replace(PascalString, "(?!^)([A-Z])", " $1");
        }

        public static string Truncate(this string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
