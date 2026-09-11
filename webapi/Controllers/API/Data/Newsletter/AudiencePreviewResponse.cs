namespace webapi.Controllers.API.Data.Newsletter
{
    public class AudiencePreviewResponse
    {
        public int Count { get; set; }
        /// <summary>How many of them the suppression list will skip on a send.</summary>
        public int Suppressed { get; set; }
        /// <summary>How many have a phone on their account (the reach of a text).</summary>
        public int WithPhone { get; set; }
        public string Summary { get; set; } = string.Empty;
        /// <summary>A few of the people, so the builder shows who it is picking.</summary>
        public List<AudiencePreviewPerson> Sample { get; set; } = new();
    }

    public class AudiencePreviewPerson
    {
        public string Email { get; set; } = string.Empty;
        public string? Name { get; set; }
    }
}
