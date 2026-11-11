using Services.Repositories.Data.RiderData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Rider height and weight (Script0317): on the rider's profile (users for an adult rider account,
    /// rider_dependent for a minor), snapshotted on a lesson bike rental (shop_rental), and held on a kiosk
    /// sign-up until staff confirm it. Values are whole inches / pounds, already range-checked by
    /// Services.Riders.RiderMeasurementRules.
    /// </summary>
    public interface IRiderMeasurementRepository
    {
        /// <summary>The account's numbers. Only an account usable at this tenant (a platform account or this tenant's own).</summary>
        Task<RiderMeasurement?> GetForUser(Guid userId, Guid tenantId);

        Task<RiderMeasurement?> GetForDependent(Guid dependentId, Guid tenantId);

        /// <summary>The guardian's dependents at this tenant, by name, with their numbers.</summary>
        Task<List<DependentMeasurement>> ListDependents(Guid tenantId, Guid guardianUserId);

        /// <summary>Writes the account's numbers (stamps measured_at). False when no such account is usable here.</summary>
        Task<bool> SaveForUser(Guid userId, Guid tenantId, int heightIn, int weightLb);

        /// <summary>Writes a dependent's numbers. When guardianUserId is set, only that guardian's dependent
        /// is written. False when nothing matched.</summary>
        Task<bool> SaveForDependent(Guid dependentId, Guid tenantId, Guid? guardianUserId, int heightIn, int weightLb);

        /// <summary>The numbers the bike was sized for, frozen on the rental.</summary>
        Task SnapshotOnShopRental(Guid rentalId, Guid tenantId, int heightIn, int weightLb);

        /// <summary>The snapshot on a rental, if any (MeasuredAt is when the rental was booked).</summary>
        Task<RiderMeasurement?> GetShopRentalSnapshot(Guid rentalId, Guid tenantId);

        /// <summary>What a held kiosk sign-up typed, kept until staff confirm it.</summary>
        Task HoldOnKioskSignup(Guid signupId, Guid tenantId, int heightIn, int weightLb);

        Task<RiderMeasurement?> GetHeldOnKioskSignup(Guid signupId, Guid tenantId);
    }
}
