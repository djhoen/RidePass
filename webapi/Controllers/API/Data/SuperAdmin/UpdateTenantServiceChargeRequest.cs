using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class UpdateTenantServiceChargeRequest
    {
        [Range(0, 10000)]
        public int ServiceChargeBps { get; set; }

        [Range(0, int.MaxValue)]
        public int? MonthlyServiceChargeCapCents { get; set; }
    }
}
