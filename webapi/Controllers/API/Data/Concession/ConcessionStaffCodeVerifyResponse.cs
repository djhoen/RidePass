namespace webapi.Controllers.API.Data.Concession
{
    // Who the staff code belongs to, plus the opaque cashier token the register sends with every
    // sale / tab action until it expires. Returned only on a match; a bad code is a 400 with a
    // generic message so the digits never reveal whose code they were close to.
    public class ConcessionStaffCodeVerifyResponse
    {
        public Guid UserId { get; set; }
        public string Name { get; set; } = null!;
        // Whether this person may also approve comps and voids with the same code.
        public bool IsManager { get; set; }
        public string CashierToken { get; set; } = null!;
        public DateTime ExpiresAtUtc { get; set; }
    }
}
