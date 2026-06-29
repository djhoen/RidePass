namespace Services.Repositories.Data.UserData
{
    // A tenant manager/admin who has set a POS authorization PIN. Used to verify a PIN entered at the
    // F&B POS: the salted hash can't be queried by value, so the candidate managers are loaded and each
    // hash is checked in code. Deliberately minimal so the hash isn't pulled into the general User object.
    public class TenantManagerPin
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string PinHash { get; set; } = null!;
    }
}
