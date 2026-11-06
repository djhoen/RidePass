namespace webapi.Controllers.API.Data.Cash
{
    // A staff member who may take a share of the tip jar.
    public class TipJarStaffDto
    {
        public Guid UserId { get; set; }
        public string Name { get; set; } = null!;
    }
}
