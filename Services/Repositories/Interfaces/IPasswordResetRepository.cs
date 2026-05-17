using Services.Repositories.Data.UserData;

namespace Services.Repositories.Interfaces
{
    public interface IPasswordResetRepository
    {
        Task<Guid> Insert(PasswordResetToken token);
        Task<PasswordResetToken?> GetByTokenHash(string tokenHash);
        Task MarkUsed(Guid id);
    }
}
