namespace webapi.Controllers.API.Data.Voucher
{
    /// <summary>Void one unused code of a batch, or (no id) every unused code in it.</summary>
    public class VoidVouchersRequest
    {
        public Guid? CouponId { get; set; }
    }
}
