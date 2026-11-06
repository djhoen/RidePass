namespace Services.Helpers
{
    /// <summary>
    /// The one rule for showing a person on a screen: their name, else their email, else
    /// "Unknown". Repositories usually hand back the name already joined (NULLIF(TRIM(first ||
    /// ' ' || last), '')), so most callers use the two-argument form; the three-argument one is for
    /// callers that still hold the parts.
    /// </summary>
    public static class UserDisplayName
    {
        public const string Unknown = "Unknown";

        public static string Of(string? first, string? last, string? email) =>
            Of(string.Join(' ', new[] { first, last }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim())), email);

        public static string Of(string? fullName, string? email) =>
            !string.IsNullOrWhiteSpace(fullName) ? fullName.Trim()
            : !string.IsNullOrWhiteSpace(email) ? email.Trim()
            : Unknown;
    }
}
