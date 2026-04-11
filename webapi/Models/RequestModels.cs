namespace webapi.Models
{
    // User
    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class CreateAccountRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string? Phone { get; set; }
    }

    public class UpdateProfileRequest
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? AboutMe { get; set; }
        public string? DisplayName { get; set; }
    }

    public class UpdatePasswordRequest
    {
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
    }

    public class SearchRequest
    {
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? UserId { get; set; }
        public string? Phone { get; set; }
        public List<int>? RoleIds { get; set; }
        // Order search fields
        public int? OrderId { get; set; }
        public string? CouponCode { get; set; }
        public List<int>? StatusIds { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? PaymentStatus { get; set; }
        public string? StripePaymentId { get; set; }
        public List<int>? OrderSourceIds { get; set; }
    }

    public class SaveUserRolesRequest
    {
        public string UserId { get; set; }
        public List<int> RoleIds { get; set; }
    }

    // Order
    public class CreateOrderNoteRequest
    {
        public int OrderId { get; set; }
        public string Note { get; set; }
    }

    public class UpdateOrderRequest
    {
        public int OrderId { get; set; }
        public int? OrderStatusId { get; set; }
        public string? PaymentStatus { get; set; }
    }

    // Blog
    public class BlogFeedRequest
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public string? Url { get; set; }
        public string? Description { get; set; }
        public string? CoverImageUrl { get; set; }
    }

    public class BlogPostRequest
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public string? Url { get; set; }
        public string? Summary { get; set; }
        public string? SummaryImgUrl { get; set; }
        public bool Published { get; set; }
        public bool ShowAuthorInfo { get; set; }
    }

    public class BlogPostSectionRequest
    {
        public int? Id { get; set; }
        public int BlogPostId { get; set; }
        public string? SectionTitle { get; set; }
        public string? SectionText { get; set; }
        public string? SectionMediaUrl { get; set; }
        public int? SectionMediaTypeId { get; set; }
        public string? SectionMediaPosition { get; set; }
        public string? SectionMediaText { get; set; }
        public string? SectionMediaWidth { get; set; }
        public int SortOrder { get; set; }
    }

    public class BlogFeedItemRequest
    {
        public int BlogFeedId { get; set; }
        public int PostId { get; set; }
    }

    public class DeleteRequest
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
    }

    // FAQ
    public class FaqRequest
    {
        public int? Id { get; set; }
        public int FaqTypeId { get; set; }
        public string Question { get; set; }
        public string Answer { get; set; }
    }

    // Coupon
    public class CouponRequest
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public decimal Amount { get; set; }
        public int CouponTypeId { get; set; }
        public int? ProductId { get; set; }
        public string? Description { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? ExpireDate { get; set; }
        public int? UserUsageLimit { get; set; }
        public int? TotalUsageLimit { get; set; }
        public bool ApplyToMultipleOrderItems { get; set; }
    }

    // Site
    public class BannerRequest
    {
        public int? Id { get; set; }
        public string? Name { get; set; }
        public string? Text { get; set; }
        public string? ActionUrl { get; set; }
        public bool IsActive { get; set; }
        public string? Class { get; set; }
    }

    public class SettingRequest
    {
        public int? Id { get; set; }
        public string Name { get; set; }
        public string? Value { get; set; }
        public string? Category { get; set; }
    }

    // Address
    public class AddressRequest
    {
        public int? Id { get; set; }
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? City { get; set; }
        public string? StateCode { get; set; }
        public string? Zip { get; set; }
        public string? CountryCode { get; set; }
        public string? Name { get; set; }
    }

    // Notification
    public class MarkAsReadRequest
    {
        public string? UserId { get; set; }
    }

    // Product
    public class ProductOfferRequest
    {
        public int? Id { get; set; }
        public string? ShortDescription { get; set; }
        public string? LongDescription { get; set; }
        public int ProductId { get; set; }
        public int OfferProductId { get; set; }
        public bool IsActive { get; set; }
    }

    // Payment
    public class CheckoutSessionRequest
    {
        public string? SuccessUrl { get; set; }
        public string? CancelUrl { get; set; }
        // TODO: Add cart/line item details
    }
}
