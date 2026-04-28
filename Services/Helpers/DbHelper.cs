using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Services.Helpers.Interfaces;

namespace Services.Helpers
{
    public class DbHelper : IDbHelper
    {
        public const int DEFAULT_TIMEOUT = 30;

        public string ConnectionString { get; }

        public DbHelper(IConfiguration configuration)
        {
            ConnectionString = configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Default is not configured. Set it via appsettings, user-secrets, or environment variables.");
        }

        public async Task<int> Execute(string sql, object? param = null, int timeout = DEFAULT_TIMEOUT)
        {
            using var dbConnection = new NpgsqlConnection(ConnectionString);
            return await dbConnection.ExecuteAsync(sql, param, commandTimeout: timeout);
        }

        public async Task<int> ExecuteScalar(string sql, object? param = null, int timeout = DEFAULT_TIMEOUT)
        {
            using var dbConnection = new NpgsqlConnection(ConnectionString);
            try
            {
                return await dbConnection.ExecuteScalarAsync<int>(sql, param, commandTimeout: timeout);
            }
            catch
            {
                return -1;
            }
        }

        public async Task<IEnumerable<T>> Query<T>(string sql, object? param = null, int timeout = DEFAULT_TIMEOUT)
        {
            using var dbConnection = new NpgsqlConnection(ConnectionString);
            return await dbConnection.QueryAsync<T>(sql, param, commandTimeout: timeout);
        }

        public async Task<IEnumerable<TR>> Query<T1, T2, TR>(string sql, Func<T1, T2, TR> map, object? param = null, string splitOn = "Id", int timeout = DEFAULT_TIMEOUT)
        {
            using var dbConnection = new NpgsqlConnection(ConnectionString);
            return await dbConnection.QueryAsync<T1, T2, TR>(sql, map, param, splitOn: splitOn, commandTimeout: timeout);
        }

        public IEnumerable<T> QueryNonAsync<T>(string sql, object? param = null, int timeout = DEFAULT_TIMEOUT)
        {
            using var dbConnection = new NpgsqlConnection(ConnectionString);
            try
            {
                return dbConnection.Query<T>(sql, param, commandTimeout: timeout);
            }
            catch
            {
                return Enumerable.Empty<T>();
            }
        }
    }
}
