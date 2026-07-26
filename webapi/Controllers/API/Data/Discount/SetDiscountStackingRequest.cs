namespace webapi.Controllers.API.Data.Discount
{
    /// <summary>Whether several discounts may combine on one sale (Script0254).</summary>
    public class SetDiscountStackingRequest
    {
        public bool Allow { get; set; }
    }
}
