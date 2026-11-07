using System.Text.RegularExpressions;

namespace Services.Storage
{
    /// <summary>
    /// Keys for private tenant images (IImageStorage.SavePrivateAsync): "private/{tenantId}/{kind}-{n}.ext".
    /// Every implementation builds and checks keys here, so a stored key can never point outside the
    /// private area (no "..", no other prefix) and always names its tenant.
    /// </summary>
    public static class PrivateImageKey
    {
        private static readonly Regex Shape = new(
            @"^private/(?<tenant>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/[a-z][a-z0-9-]{0,40}-[0-9a-f]{32}\.(jpg|png)$",
            RegexOptions.CultureInvariant);

        public static string New(Guid tenantId, string kind, string fileExtension)
        {
            var ext = fileExtension.ToLowerInvariant() is ".png" ? ".png" : ".jpg";
            var safeKind = Regex.Replace(kind.ToLowerInvariant(), "[^a-z0-9-]", "");
            if (safeKind.Length == 0 || !char.IsLetter(safeKind[0])) safeKind = "img" + safeKind;
            return $"private/{tenantId:D}/{safeKind}-{Guid.NewGuid():N}{ext}";
        }

        public static bool IsValid(string? key) => key is not null && Shape.IsMatch(key);

        /// <summary>The tenant a key belongs to, or null for a malformed key.</summary>
        public static Guid? TenantOf(string? key)
        {
            if (key is null) return null;
            var m = Shape.Match(key);
            return m.Success ? Guid.Parse(m.Groups["tenant"].Value) : null;
        }

        public static string ContentType(string key) => key.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg";
    }
}
