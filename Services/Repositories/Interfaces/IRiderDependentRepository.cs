using Services.Repositories.Data.RiderData;

namespace Services.Repositories.Interfaces
{
    /// <summary>Minors linked to a guardian's account (Script0309), per tenant.</summary>
    public interface IRiderDependentRepository
    {
        /// <summary>The guardian's row for this rider key, created when missing. An existing row keeps its
        /// name and takes a birthdate / own account only where it had none, so a typo at one sale never
        /// rewrites the child on file.</summary>
        Task<RiderDependent> FindOrCreate(Guid tenantId, Guid guardianUserId, string firstName, string lastName,
            DateTime? birthdate, Guid? riderUserId, Guid? createdByUserId);

        Task<RiderDependent?> GetById(Guid id, Guid tenantId);

        /// <summary>The guardian's dependents at this tenant, by name.</summary>
        Task<List<RiderDependent>> ListForGuardian(Guid tenantId, Guid guardianUserId);
    }
}
