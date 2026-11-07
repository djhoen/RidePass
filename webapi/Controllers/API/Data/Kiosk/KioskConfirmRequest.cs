namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>Staff attach a kiosk sign-up that matched an existing account, after checking the person's ID.</summary>
    public class KioskConfirmRequest
    {
        /// <summary>The cashier checked a photo ID against the account (required).</summary>
        public bool IdChecked { get; set; }
        /// <summary>Replace the photo already on file with the kiosk photo (otherwise the one on file is kept).</summary>
        public bool ReplacePhoto { get; set; }
    }
}
