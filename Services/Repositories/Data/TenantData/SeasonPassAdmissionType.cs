namespace Services.Repositories.Data.TenantData
{
    /// <summary>
    /// How a tenant's gate admits a season pass holder. Tracks split into two operating
    /// models: some hold a roster and want the holder to sign up for a specific event
    /// before the pass admits them, others just open the lift and let the pass itself be
    /// the ticket. Drives whether RedeemPassAtGate requires a pre-existing reservation.
    /// </summary>
    public enum SeasonPassAdmissionType
    {
        /// <summary>The holder must reserve a specific event first; a walk-up scan is refused.</summary>
        EventSignUp = 1,

        /// <summary>The pass admits on scan alone, whether or not an event is running that day.
        /// The default, because it is the behavior every tenant already experiences.</summary>
        WalkUp = 2
    }
}
