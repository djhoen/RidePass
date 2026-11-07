namespace Services.Repositories.Data.WaiverData
{
    /// <summary>
    /// Who an adult-waiver signing link is for (waiver_sign_request.rider_*, Script0306): the rider's
    /// account and their name and birthdate as the venue holds them. The signature is made AS this
    /// rider, whatever the form says, and attached to their account.
    /// </summary>
    public sealed record SignRequestRider(Guid UserId, string FirstName, string LastName, DateTime Birthdate);
}
