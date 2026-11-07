namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>What confirming a held kiosk sign-up attached to the account.</summary>
    public class KioskConfirmResponse
    {
        public bool WaiverAttached { get; set; }
        /// <summary>The waiver changed since they signed at the kiosk: ask them to sign the current one.</summary>
        public bool WaiverChanged { get; set; }
        public bool PhotoAttached { get; set; }
        public bool KeptExistingPhoto { get; set; }
    }
}
