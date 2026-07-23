namespace webapi.Controllers.API.Data.Waiver
{
    public class CreateWaiverSignRequestRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? Name { get; set; }
        /// <summary>Optional specific waiver document; null = the tenant's active default.</summary>
        public Guid? WaiverId { get; set; }
    }
}
