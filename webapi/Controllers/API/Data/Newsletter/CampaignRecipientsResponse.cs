namespace webapi.Controllers.API.Data.Newsletter
{
    public class CampaignRecipientsResponse
    {
        public List<CampaignRecipientItem> Items { get; set; } = new();
        /// <summary>Rows matching the filter and search, before paging.</summary>
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
