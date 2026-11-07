namespace webapi.Helpers
{
    /// <summary>The one check for a drawn signature sent as a PNG data URL (Gate Sale, signing links, kiosk).</summary>
    public static class SignatureImages
    {
        /// <summary>A PNG data URL of a plausible size: over ~800 characters (a non-empty signature) and
        /// under ~1.4 MB (abuse cap).</summary>
        public static bool IsValidPngDataUrl(string? dataUrl) =>
            !string.IsNullOrWhiteSpace(dataUrl)
            && dataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal)
            && dataUrl.Length is > 800 and < 1_400_000;
    }
}
