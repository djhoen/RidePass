namespace Services.Repositories.Data.UserData
{
    // A staff member who holds a POS code (users.pos_pin_hash), as the PIN/code verifiers read
    // them: the salted hash can't be queried by value, so the candidates are loaded and each hash
    // is checked in code. The roles ride along so a match can say whether it is a manager (comps
    // and voids) without a second read. Deliberately minimal so the hash isn't pulled into the
    // general User object.
    public class TenantManagerPin
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string PinHash { get; set; } = null!;
        public string Role { get; set; } = "";
        public string[] Roles { get; set; } = System.Array.Empty<string>();
    }
}
