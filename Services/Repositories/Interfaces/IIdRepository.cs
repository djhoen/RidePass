namespace Services.Repositories.Interfaces
{
    public interface IIdRepository
    {
        Task<bool> IdAvailable(string id, string tableName);
        Task<string> GetUniqueId(string table, int idLength);
    }
}
