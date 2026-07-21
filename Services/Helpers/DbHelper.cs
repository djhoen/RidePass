using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Services.Helpers.Interfaces;

namespace Services.Helpers
{
    /// <summary>
    /// Teaches Dapper how to bind DateOnly. The pinned Dapper (2.0.123) predates native support,
    /// so ANY DateOnly parameter throws "cannot be used as a parameter value" at runtime. That is
    /// invisible to the compiler, so it only shows up when the screen is opened. Registered in
    /// DbHelper's static constructor rather than Program.cs so every consumer gets it: the API,
    /// the background workers, and anything else that touches the database.
    /// </summary>
    internal sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(System.Data.IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = System.Data.DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }

        public override DateOnly Parse(object value) => value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            string s => DateOnly.Parse(s),
            _ => throw new InvalidCastException($"Cannot convert {value?.GetType().Name ?? "null"} to DateOnly."),
        };
    }

    public class DbHelper : IDbHelper
    {
        public const int DEFAULT_TIMEOUT = 30;

        static DbHelper()
        {
            // Both the value and nullable forms: a `DateOnly? onDate = null` parameter still
            // reaches Dapper as a member it has to know how to bind.
            SqlMapper.AddTypeHandler(typeof(DateOnly), new DateOnlyTypeHandler());
            SqlMapper.AddTypeHandler(typeof(DateOnly?), new DateOnlyTypeHandler());
        }

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

        public async Task ExecuteBatch(IReadOnlyList<(string Sql, object? Param)> statements, int timeout = DEFAULT_TIMEOUT)
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                foreach (var (sql, param) in statements)
                {
                    await conn.ExecuteAsync(sql, param, transaction: tx, commandTimeout: timeout);
                }
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
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

        public async Task<IAsyncDisposable> AcquireAdvisoryLock(string lockKey, int timeout = DEFAULT_TIMEOUT)
        {
            var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            try
            {
                // hashtext(text) -> int4, implicitly widened to the bigint pg_advisory_lock overload.
                await connection.ExecuteAsync(
                    "SELECT pg_advisory_lock(hashtext(@lockKey))",
                    new { lockKey }, commandTimeout: timeout);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
            return new AdvisoryLockHandle(connection, lockKey);
        }

        private sealed class AdvisoryLockHandle : IAsyncDisposable
        {
            private readonly NpgsqlConnection _connection;
            private readonly string _lockKey;
            private bool _released;

            public AdvisoryLockHandle(NpgsqlConnection connection, string lockKey)
            {
                _connection = connection;
                _lockKey = lockKey;
            }

            public async ValueTask DisposeAsync()
            {
                if (_released) return;
                _released = true;
                try
                {
                    await _connection.ExecuteAsync(
                        "SELECT pg_advisory_unlock(hashtext(@lockKey))",
                        new { lockKey = _lockKey });
                }
                catch
                {
                    // Closing the connection releases the session lock regardless.
                }
                finally
                {
                    await _connection.DisposeAsync();
                }
            }
        }
    }
}
