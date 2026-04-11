namespace Services.Helpers.Interfaces
{
    public interface IDbHelper
    {
        Task<int> Execute(string sql, object? param = null, int timeout = 30);
        Task<int> ExecuteScalar(string sql, object? param = null, int timeout = 30);
        Task<IEnumerable<T>> Query<T>(string sql, object? param = null, int timeout = 30);
        Task<IEnumerable<TR>> Query<T1, T2, TR>(string sql, Func<T1, T2, TR> map, object? param = null, string splitOn = "Id", int timeout = 30);
        IEnumerable<T> QueryNonAsync<T>(string sql, object? param = null, int timeout = 30);
    }
}
