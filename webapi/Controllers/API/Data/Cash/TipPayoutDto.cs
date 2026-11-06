namespace webapi.Controllers.API.Data.Cash
{
    // One logged payout of tips to a staff member. Source: drawer (the worker's own close) |
    // cash | payroll (a manager settling house-owes). PaidByName reads "(removed user)" once the
    // payer's account is gone; the payout itself is kept (Script0296).
    public class TipPayoutDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Name { get; set; } = null!;
        public int AmountCents { get; set; }
        public string Source { get; set; } = null!;
        public string PaidByName { get; set; } = null!;
        public string? Note { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
