namespace webapi.Controllers.API.Data.User
{
    public class LoampassLinkConfirmRequest
    {
        public string Email { get; set; } = null!;
        public string Code { get; set; } = null!;
    }
}
