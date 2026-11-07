using System.Data;
using Dapper;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.RiderData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class RiderDependentRepository : IRiderDependentRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, guardian_user_id AS GuardianUserId, rider_user_id AS RiderUserId,
            first_name AS FirstName, last_name AS LastName, birthdate AS Birthdate, rider_key AS RiderKey,
            emergency_contact_name AS EmergencyContactName, emergency_contact_phone AS EmergencyContactPhone,
            created_by_user_id AS CreatedByUserId, created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public RiderDependentRepository(IDbHelper db) => _db = db;

        public async Task<RiderDependent?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM rider_dependent WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<RiderDependent>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<RiderDependent?> FindByKey(Guid tenantId, Guid guardianUserId, string riderKey)
        {
            var sql = $@"SELECT {Columns} FROM rider_dependent
                         WHERE tenant_id = @tenantId AND guardian_user_id = @guardianUserId AND rider_key = @riderKey LIMIT 1";
            return (await _db.Query<RiderDependent>(sql, new { tenantId, guardianUserId, riderKey })).FirstOrDefault();
        }

        public async Task<List<RiderDependent>> ListForGuardian(Guid tenantId, Guid guardianUserId)
        {
            var sql = $@"SELECT {Columns} FROM rider_dependent
                         WHERE tenant_id = @tenantId AND guardian_user_id = @guardianUserId
                         ORDER BY lower(first_name), lower(last_name)";
            return (await _db.Query<RiderDependent>(sql, new { tenantId, guardianUserId })).ToList();
        }

        public Task<List<OtherRiderProfileIds>> SaveOtherRiderProfiles(Guid tenantId, IReadOnlyList<OtherRiderProfileWrite> writes)
        {
            // One transaction for every line of the sale, so a failure leaves no half-made profile.
            return _db.WithTransaction(async (conn, tx) =>
            {
                var result = new List<OtherRiderProfileIds>(writes.Count);
                foreach (var w in writes)
                {
                    Guid? riderId = w.Rider is null ? null : await SaveAccount(conn, tx, tenantId, w.Rider);
                    Guid? guardianId = null, dependentId = null;
                    if (w.Guardian is not null)
                    {
                        guardianId = await SaveAccount(conn, tx, tenantId, w.Guardian);
                        dependentId = await SaveDependent(conn, tx, tenantId, guardianId.Value, riderId, w);
                        // The dependent keeps the account it already had (only an empty one is filled).
                        riderId = await conn.ExecuteScalarAsync<Guid?>(
                            "SELECT rider_user_id FROM rider_dependent WHERE id = @dependentId AND tenant_id = @tenantId",
                            new { dependentId, tenantId }, tx);
                    }
                    result.Add(new OtherRiderProfileIds(riderId, dependentId, guardianId));
                }
                return result;
            });
        }

        /// <summary>An existing rider account (fill-empty only, and only a rider account usable at this tenant)
        /// or a new platform rider account; a concurrent creation of the same email resolves to that account.</summary>
        private static async Task<Guid> SaveAccount(IDbConnection conn, IDbTransaction tx, Guid tenantId, RiderAccountWrite a)
        {
            Guid id;
            if (a.ExistingId is Guid existing)
            {
                id = existing;
            }
            else
            {
                var u = a.NewAccount ?? throw new InvalidOperationException("No account to write.");
                var created = await conn.QueryFirstOrDefaultAsync<Guid?>(@"
                    INSERT INTO users (tenant_id, email, password_hash, first_name, last_name, role, roles, status,
                                       phone, birthdate, emergency_contact_name, emergency_contact_phone)
                    VALUES (NULL, @Email, @PasswordHash, @FirstName, @LastName, 'rider', ARRAY['rider']::text[], 'active',
                            @Phone, @Birthdate::date, @EmergencyContactName, @EmergencyContactPhone)
                    ON CONFLICT (LOWER(email)) WHERE tenant_id IS NULL DO NOTHING
                    RETURNING id", u, tx);
                if (created is Guid c) return c;
                // Created a moment ago by someone else: only a rider account may be used.
                var raced = (await conn.QueryAsync<(Guid Id, string Role)>(
                    "SELECT id, role FROM users WHERE tenant_id IS NULL AND LOWER(email) = LOWER(@Email)",
                    new { u.Email }, tx)).ToList();
                if (raced.Count == 0 || raced[0].Role != "rider")
                    throw new InvalidOperationException("That email belongs to a staff account. Use a different email.");
                id = raced[0].Id;
            }
            if (a.FillPhone is not null || a.FillBirthdate is not null || a.FillEmergencyContactPhone is not null)
            {
                await conn.ExecuteAsync(@"
                    UPDATE users
                       SET phone = CASE WHEN COALESCE(phone, '') = '' AND @phone::text IS NOT NULL THEN @phone ELSE phone END,
                           birthdate = COALESCE(birthdate, @birthdate::date),
                           emergency_contact_name = CASE WHEN COALESCE(emergency_contact_phone, '') = '' AND @ecPhone::text IS NOT NULL
                                                         THEN @ecName ELSE emergency_contact_name END,
                           emergency_contact_phone = CASE WHEN COALESCE(emergency_contact_phone, '') = '' AND @ecPhone::text IS NOT NULL
                                                          THEN @ecPhone ELSE emergency_contact_phone END,
                           updated_at = now()
                     WHERE id = @id AND role = 'rider' AND (tenant_id IS NULL OR tenant_id = @tenantId)",
                    new
                    {
                        id, tenantId, phone = a.FillPhone, birthdate = a.FillBirthdate,
                        ecName = a.FillEmergencyContactName, ecPhone = a.FillEmergencyContactPhone,
                    }, tx);
            }
            return id;
        }

        private static async Task<Guid> SaveDependent(IDbConnection conn, IDbTransaction tx, Guid tenantId,
            Guid guardianUserId, Guid? riderUserId, OtherRiderProfileWrite w)
        {
            var riderKey = Services.Riders.IdCheckRules.Normalize($"{w.FirstName} {w.LastName}");
            // One statement, so two cashiers adding the same child at once land on one row. The DO UPDATE
            // only fills gaps (and is what makes RETURNING hand back the existing row).
            return await conn.ExecuteScalarAsync<Guid>(@"
                INSERT INTO rider_dependent
                    (tenant_id, guardian_user_id, rider_user_id, first_name, last_name, birthdate, rider_key,
                     emergency_contact_name, emergency_contact_phone, created_by_user_id)
                VALUES (@tenantId, @guardianUserId, @riderUserId, @firstName, @lastName, @birthdate::date, @riderKey,
                        @ecName, @ecPhone, @createdBy)
                ON CONFLICT (tenant_id, guardian_user_id, rider_key) DO UPDATE
                    SET birthdate = COALESCE(rider_dependent.birthdate, EXCLUDED.birthdate),
                        rider_user_id = COALESCE(rider_dependent.rider_user_id, EXCLUDED.rider_user_id),
                        emergency_contact_name = CASE WHEN COALESCE(rider_dependent.emergency_contact_phone, '') = ''
                                                      THEN EXCLUDED.emergency_contact_name ELSE rider_dependent.emergency_contact_name END,
                        emergency_contact_phone = COALESCE(NULLIF(rider_dependent.emergency_contact_phone, ''), EXCLUDED.emergency_contact_phone)
                RETURNING id",
                new
                {
                    tenantId, guardianUserId, riderUserId,
                    firstName = w.FirstName.Trim(), lastName = w.LastName.Trim(),
                    birthdate = w.Birthdate?.Date, riderKey,
                    ecName = w.EmergencyContactName, ecPhone = w.EmergencyContactPhone, createdBy = w.CreatedByUserId,
                }, tx);
        }
    }
}
