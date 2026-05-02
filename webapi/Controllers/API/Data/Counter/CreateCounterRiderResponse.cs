namespace webapi.Controllers.API.Data.Counter
{
    public class CreateCounterRiderResponse
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
    }
}
