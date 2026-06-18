using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.UserData;

namespace Services.Repositories.Data.CustomerData
{
    // Result of CustomerRepository.ListForTenant — one row per distinct user with any
    // activity (purchase or waiver signature) at this tenant.
    public class CustomerSummary
    {
        public Guid UserId { get; set; }
        public string Email { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public DateTime? Birthdate { get; set; }
        public DateTime? LastActivityAt { get; set; }
        public int TotalPurchases { get; set; }
        public int TotalSpentCents { get; set; }
        public bool HasWaiverSigned { get; set; }
    }

    // Result of CustomerRepository.GetDetail — single user + their full activity at
    // this tenant. The frontend renders tabs/sections from this.
    public class CustomerDetail
    {
        public User User { get; set; } = null!;
        public List<EventTicketPurchase> EventTickets { get; set; } = new();
        public List<SeasonPassPurchase> SeasonPasses { get; set; } = new();
        public List<RiderWaiverSignatureWithWaiver> WaiverSignatures { get; set; } = new();
    }

    // RiderWaiverSignature joined with the waiver template's title + version so the
    // detail page can show "Waiver: <title> v<version>" without a second fetch.
    public class RiderWaiverSignatureWithWaiver : RiderWaiverSignature
    {
        public string WaiverTitle { get; set; } = null!;
        public int WaiverVersion { get; set; }
    }

    // One row per top rider. The repository returns both metrics on every entry so
    // the UI can flip tabs (Days Ridden / Total Spent) without a second fetch.
    public class TopRiderEntry
    {
        public Guid UserId { get; set; }
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public int Days { get; set; }         // count of paid passes/tickets in the period
        public int SpentCents { get; set; }   // total paid amount in the period
    }
}
