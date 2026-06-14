namespace Services.Helpers.Interfaces
{
    public interface IDbHelper
    {
        Task<int> Execute(string sql, object? param = null, int timeout = 30);
        Task<int> ExecuteScalar(string sql, object? param = null, int timeout = 30);

        /// <summary>
        /// Acquires a session-level Postgres advisory lock on a dedicated connection and
        /// returns a handle that releases it on disposal. Concurrent callers that pass the
        /// same key serialize, which lets a capacity check and the row insert that follows
        /// it behave atomically even though they run on separate pooled connections. The
        /// lock auto-releases if the process dies (the connection drops). Disposal is
        /// idempotent, so callers may release early and still rely on `await using`.
        /// </summary>
        Task<IAsyncDisposable> AcquireAdvisoryLock(string lockKey, int timeout = 30);
        Task<IEnumerable<T>> Query<T>(string sql, object? param = null, int timeout = 30);
        Task<IEnumerable<TR>> Query<T1, T2, TR>(string sql, Func<T1, T2, TR> map, object? param = null, string splitOn = "Id", int timeout = 30);
        IEnumerable<T> QueryNonAsync<T>(string sql, object? param = null, int timeout = 30);
    }
}
