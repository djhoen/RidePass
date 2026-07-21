using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Tenant
{
    public class UpdateCancellationPolicyRequest
    {
        public bool AllowSelfCancel { get; set; }

        // WaitlistEnabled deliberately absent: the waitlist is a super-admin-gated platform feature
        // (Script0180), so only SuperAdminUpdateTenantRequest carries it. It used to live here, which
        // let a tenant admin turn their own waitlist on with a direct PUT even though the Features
        // screen rendered it read-only.
        //
        // The confirm window stays tenant-controlled: it's an operational preference (how long an
        // alternate has to claim a spot) and it's inert while the waitlist is off.
        [Range(5, 240)]
        public int WaitlistConfirmWindowMinutes { get; set; } = 20;
    }
}
