namespace Services.Repositories.Data.PaymentData
{
    /// <summary>The rider a ticket is for, named when it is sold (a Gate Sale "another rider" line,
    /// Script0309): written in the ticket's INSERT. The purchaser stays the payer.</summary>
    public sealed record TicketRiderAtSale(string FirstName, string LastName, DateOnly? Birthdate,
        Guid? RiderUserId, Guid? RiderDependentId);
}
