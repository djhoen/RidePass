using System.Text.RegularExpressions;

namespace Services.Helpers
{
    public class EmailHelper
    {
        // Lightweight format check used by callers that validate addresses before use
        // (e.g. SurveyController). Real delivery goes through SmtpEmailer, not this class.
        public static bool IsValid(string email)
        {
            string regex = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            return Regex.IsMatch(email, regex, RegexOptions.IgnoreCase);
        }
    }
}
