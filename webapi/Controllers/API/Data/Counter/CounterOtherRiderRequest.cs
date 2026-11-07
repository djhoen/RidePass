namespace webapi.Controllers.API.Data.Counter
{
    /// <summary>Check an "another rider" entry before it goes in the cart (POST Counter/OtherRiders): validated,
    /// existing profiles matched, nothing written.</summary>
    public class CounterOtherRiderRequest : CounterOtherRiderInput
    {
        /// <summary>The paying customer (so the rider can't be them, and so they can be the guardian).</summary>
        public Guid PayerUserId { get; set; }
    }
}
