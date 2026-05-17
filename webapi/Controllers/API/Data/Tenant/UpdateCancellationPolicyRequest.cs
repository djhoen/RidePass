using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Tenant
{
    public class UpdateCancellationPolicyRequest
    {
        public bool AllowSelfCancel { get; set; }
        public bool WaitlistEnabled { get; set; } = true;

        [Range(5, 240)]
        public int WaitlistConfirmWindowMinutes { get; set; } = 20;
    }
}
