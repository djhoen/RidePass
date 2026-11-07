namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>A kiosk staff can send a step to: paired, not removed, seen in the last few minutes.</summary>
    public class ActiveKioskItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime? LastSeenAtUtc { get; set; }
        /// <summary>On its start screen now (asking for work).</summary>
        public bool Idle { get; set; }
        /// <summary>Already has a request waiting or on screen.</summary>
        public bool Busy { get; set; }
        public Guid? ActivePushId { get; set; }
    }
}
