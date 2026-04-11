using Dapper;
using Npgsql;
using Services.Helpers.Interfaces;

namespace Services.Helpers
{
    public class DbHelper : IDbHelper
    {
        public const int DEFAULT_TIMEOUT = 30;

        // TODO: Move connection string to appsettings.json or environment variable
        public const string CONNECTION_STRING = "Server=YOUR_SERVER;Port=5432;Database=YOUR_DB;User Id=YOUR_USER;Password=YOUR_PASSWORD;";

        public string ConnectionString
        {
            get
            {
                return CONNECTION_STRING;
            }
        }

        public async Task<int> Execute(string sql, object? param = null, int timeout = DEFAULT_TIMEOUT)
        {
            using (var dbConnection = new NpgsqlConnection(CONNECTION_STRING))
            {
                try
                {
                    return await dbConnection.ExecuteAsync(sql, param, commandTimeout: timeout);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        public async Task<int> ExecuteScalar(string sql, object? param = null, int timeout = DEFAULT_TIMEOUT)
        {
            using (var dbConnection = new NpgsqlConnection(CONNECTION_STRING))
            {
                try
                {
                    return await dbConnection.ExecuteScalarAsync<int>(sql, param, commandTimeout: timeout);
                }
                catch (Exception ex)
                {
                    return -1;
                }
            }
        }

        public async Task<IEnumerable<T>> Query<T>(string sql, object? param = null, int timeout = DEFAULT_TIMEOUT)
        {
            using (var dbConnection = new NpgsqlConnection(CONNECTION_STRING))
            {
                try
                {
                    return await dbConnection.QueryAsync<T>(sql, param, commandTimeout: timeout);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        public async Task<IEnumerable<TR>> Query<T1, T2, TR>(string sql, Func<T1, T2, TR> map, object? param = null, string splitOn = "Id", int timeout = DEFAULT_TIMEOUT)
        {
            using (var dbConnection = new NpgsqlConnection(CONNECTION_STRING))
            {
                try
                {
                    return await dbConnection.QueryAsync<T1, T2, TR>(sql, map, param, splitOn: splitOn, commandTimeout: timeout);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        public IEnumerable<T> QueryNonAsync<T>(string sql, object? param = null, int timeout = DEFAULT_TIMEOUT)
        {
            using (var dbConnection = new NpgsqlConnection(CONNECTION_STRING))
            {
                try
                {
                    return dbConnection.Query<T>(sql, param, commandTimeout: timeout);
                }
                catch (Exception ex)
                {
                    return default;
                }
            }
        }
    }
}
