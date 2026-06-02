using Services.Helpers.Interfaces;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly IDbHelper _db;

        private const string SelectUserColumns = @"
            id, tenant_id AS TenantId, email, password_hash AS PasswordHash,
            first_name AS FirstName, last_name AS LastName, role, status,
            phone,
            birthdate AS Birthdate,
            emergency_contact_name AS EmergencyContactName,
            emergency_contact_phone AS EmergencyContactPhone,
            address_line AS AddressLine,
            address_line2 AS AddressLine2,
            city,
            state,
            postal_code AS PostalCode,
            country,
            bike AS Bike,
            race_number AS RaceNumber,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        public UserRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task<User?> GetByEmail(Guid tenantId, string email)
        {
            var sql = $@"
                SELECT {SelectUserColumns}
                FROM users
                WHERE tenant_id = @tenantId AND LOWER(email) = LOWER(@email)
                LIMIT 1";

            var result = await _db.Query<User>(sql, new { tenantId, email });
            return result.FirstOrDefault();
        }

        public async Task<User?> GetGlobalByEmail(string email)
        {
            // Global accounts are riders and super_admins (tenant_id IS NULL).
            var sql = $@"
                SELECT {SelectUserColumns}
                FROM users
                WHERE tenant_id IS NULL AND LOWER(email) = LOWER(@email)
                LIMIT 1";

            var result = await _db.Query<User>(sql, new { email });
            return result.FirstOrDefault();
        }

        public async Task<User?> GetById(Guid id)
        {
            var sql = $@"
                SELECT {SelectUserColumns}
                FROM users
                WHERE id = @id
                LIMIT 1";

            var result = await _db.Query<User>(sql, new { id });
            return result.FirstOrDefault();
        }

        public async Task<User?> GetByPhoneE164(string phoneE164)
        {
            // fn_phone_e164(phone) hits the expression index from
            // Script0088, so this is an index lookup not a scan. ORDER BY
            // pushes global rider accounts (tenant_id IS NULL) to the front
            // when multiple users own the same number — that matches the
            // Inbox's "find the customer" intent. Final tie-break on
            // created_at DESC takes the most recent signup.
            var sql = $@"
                SELECT {SelectUserColumns}
                FROM users
                WHERE phone IS NOT NULL
                  AND fn_phone_e164(phone) = @phoneE164
                ORDER BY (tenant_id IS NULL) DESC, created_at DESC
                LIMIT 1";

            var result = await _db.Query<User>(sql, new { phoneE164 });
            return result.FirstOrDefault();
        }

        public async Task<Guid> Create(User user)
        {
            const string sql = @"
                INSERT INTO users (tenant_id, email, password_hash, first_name, last_name, role, status, phone, birthdate,
                                   emergency_contact_name, emergency_contact_phone)
                VALUES (@TenantId, @Email, @PasswordHash, @FirstName, @LastName, @Role, @Status, @Phone, @Birthdate,
                        @EmergencyContactName, @EmergencyContactPhone)
                RETURNING id";

            var result = await _db.Query<Guid>(sql, user);
            return result.First();
        }

        public async Task UpdatePhone(Guid userId, string? phone)
        {
            const string sql = "UPDATE users SET phone = @phone, updated_at = now() WHERE id = @userId";
            await _db.Execute(sql, new { userId, phone });
        }

        public async Task UpdateEmergencyContact(Guid userId, string name, string phone)
        {
            const string sql = @"
                UPDATE users
                SET emergency_contact_name = @name, emergency_contact_phone = @phone, updated_at = now()
                WHERE id = @userId";
            await _db.Execute(sql, new { userId, name, phone });
        }

        public async Task UpdateRacerInfo(Guid userId, string? bike, string? raceNumber)
        {
            const string sql = @"
                UPDATE users
                SET bike = @bike, race_number = @raceNumber, updated_at = now()
                WHERE id = @userId";
            await _db.Execute(sql, new { userId, bike, raceNumber });
        }

        public async Task UpdateBirthdate(Guid userId, DateTime birthdate)
        {
            const string sql = "UPDATE users SET birthdate = @birthdate, updated_at = now() WHERE id = @userId";
            await _db.Execute(sql, new { userId, birthdate });
        }

        public async Task UpdateAddress(Guid userId, string? addressLine, string? addressLine2,
            string? city, string? state, string? postalCode, string? country)
        {
            const string sql = @"
                UPDATE users SET
                    address_line  = @addressLine,
                    address_line2 = @addressLine2,
                    city          = @city,
                    state         = @state,
                    postal_code   = @postalCode,
                    country       = @country,
                    updated_at    = now()
                WHERE id = @userId";
            await _db.Execute(sql, new { userId, addressLine, addressLine2, city, state, postalCode, country });
        }

        public async Task<bool> AnySuperAdminExists()
        {
            const string sql = "SELECT COUNT(*) FROM users WHERE role = 'super_admin'";
            var count = await _db.ExecuteScalar(sql);
            return count > 0;
        }

        public async Task<List<User>> SearchAll(string? query, int take = 50)
        {
            var hasQuery = !string.IsNullOrWhiteSpace(query);
            var like = hasQuery ? $"%{query!.Trim()}%" : null;
            var sql = $@"
                SELECT {SelectUserColumns}
                FROM users
                WHERE (@hasQuery = false
                       OR LOWER(email) LIKE LOWER(@like)
                       OR LOWER(first_name) LIKE LOWER(@like)
                       OR LOWER(last_name) LIKE LOWER(@like))
                ORDER BY created_at DESC
                LIMIT @take";
            var result = await _db.Query<User>(sql, new { hasQuery, like, take });
            return result.ToList();
        }

        public async Task<List<User>> ListByTenant(Guid tenantId)
        {
            var sql = $@"
                SELECT {SelectUserColumns}
                FROM users
                WHERE tenant_id = @tenantId
                ORDER BY LOWER(email)";
            var result = await _db.Query<User>(sql, new { tenantId });
            return result.ToList();
        }

        public async Task<List<User>> ListSuperAdmins()
        {
            var sql = $@"
                SELECT {SelectUserColumns}
                FROM users
                WHERE role = 'super_admin' AND status = 'active' AND tenant_id IS NULL
                ORDER BY LOWER(email)";
            return (await _db.Query<User>(sql)).ToList();
        }

        public async Task<List<User>> ListTenantUsersByRole(Guid tenantId, string role)
        {
            var sql = $@"
                SELECT {SelectUserColumns}
                FROM users
                WHERE tenant_id = @tenantId AND role = @role AND status = 'active'
                ORDER BY LOWER(email)";
            return (await _db.Query<User>(sql, new { tenantId, role })).ToList();
        }

        public async Task UpdateRole(Guid id, string role)
        {
            const string sql = "UPDATE users SET role = @role WHERE id = @id";
            await _db.Execute(sql, new { id, role });
        }

        public async Task UpdateStatus(Guid id, string status)
        {
            const string sql = "UPDATE users SET status = @status WHERE id = @id";
            await _db.Execute(sql, new { id, status });
        }

        public async Task UpdatePasswordHash(Guid id, string passwordHash)
        {
            const string sql = "UPDATE users SET password_hash = @passwordHash WHERE id = @id";
            await _db.Execute(sql, new { id, passwordHash });
        }

        public async Task<string?> GetDashboardConfig(Guid userId)
        {
            const string sql = "SELECT dashboard_config::text FROM users WHERE id = @userId";
            var result = await _db.Query<string?>(sql, new { userId });
            return result.FirstOrDefault();
        }

        public async Task SetDashboardConfig(Guid userId, string? jsonOrNull)
        {
            // Cast the text param to jsonb so NULLs and well-formed JSON both work.
            const string sql = "UPDATE users SET dashboard_config = @json::jsonb WHERE id = @userId";
            await _db.Execute(sql, new { userId, json = jsonOrNull });
        }
    }
}
