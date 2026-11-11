namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>What the kiosk screens need: no customer data, only the venue's own settings and waiver text.</summary>
    public class KioskConfigResponse
    {
        public string KioskName { get; set; } = string.Empty;
        public string VenueName { get; set; } = string.Empty;
        public Guid? WaiverId { get; set; }
        public string? WaiverTitle { get; set; }
        public string? WaiverBody { get; set; }
        /// <summary>off | prompt | required (tenant.rider_photo_mode). The photo can be skipped only when off.</summary>
        public string PhotoMode { get; set; } = "off";
        public bool PhotoRequired { get; set; }
        public bool RequireEmergencyContact { get; set; }
        /// <summary>off | optional | required (tenant.kiosk_height_weight_mode): whether sign-up asks height and weight.</summary>
        public string HeightWeightMode { get; set; } = "off";
        /// <summary>The venue's local date (yyyy-MM-dd), so the kiosk judges under-18 on the same day the gate does.</summary>
        public DateOnly Today { get; set; }
    }
}
