namespace Services.Repositories.Data.OrderData
{
    public enum OrderStatus
    {
        Error = 0,
        New = 1,
        Confirmation = 2,
        Cancelled = 3,
        Declined = 4,
        Refunded = 5,
        PartialRefund = 6,
        Complete = 7
    }
}
