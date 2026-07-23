namespace webapi.Controllers.API.Data.Waiver
{
    /// <summary>Submission from the public /SignWaiver/{token} page.</summary>
    public class SignByTokenRequest
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime? Birthdate { get; set; }
        public string SignatureDataUrl { get; set; } = string.Empty;
        public string? ParentName { get; set; }
        public string? ParentPhone { get; set; }
    }
}
