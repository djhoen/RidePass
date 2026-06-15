namespace webapi.Controllers.API.Data.Suppression
{
    public class AddSuppressionRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? Note { get; set; }
    }
}
