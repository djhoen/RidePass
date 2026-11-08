using Services.Repositories.Data.RiderData;

namespace Services.Repositories.Interfaces
{
    /// <summary>Minors linked to a guardian's account (Script0309), per tenant.</summary>
    public interface IRiderDependentRepository
    {
        Task<RiderDependent?> GetById(Guid id, Guid tenantId);

        /// <summary>The guardian's row for this rider key (IdCheckRules.Normalize of the name), if any.</summary>
        Task<RiderDependent?> FindByKey(Guid tenantId, Guid guardianUserId, string riderKey);

        /// <summary>The guardian's dependents at this tenant, by name.</summary>
        Task<List<RiderDependent>> ListForGuardian(Guid tenantId, Guid guardianUserId);

        /// <summary>
        /// Writes a sale's "another rider" profiles in ONE transaction: new rider accounts, empty fields of
        /// existing rider accounts (never overwritten, never on a non-rider account), and each minor's
        /// dependent row (an existing row keeps its name, birthdate, account and emergency contact; only
        /// gaps are filled). Returns the ids per write, in order; the rider account id is the dependent's
        /// stored one.
        /// </summary>
        Task<List<OtherRiderProfileIds>> SaveOtherRiderProfiles(Guid tenantId, IReadOnlyList<OtherRiderProfileWrite> writes);

        /// <summary>Undoes a profile save that could not be completed (a kiosk sign-up whose attach failed): removes the
        /// accounts and dependent that save just created, with anything already filed under them, so no orphan is left.
        /// Only ever called with ids created by that save.</summary>
        Task DiscardNewProfile(Guid tenantId, IReadOnlyCollection<Guid> newUserIds, Guid? newDependentId);
    }
}
