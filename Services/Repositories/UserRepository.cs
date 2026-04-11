using Dapper;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly IDbHelper _dbHelper;
        private string _userQuery;
        public UserRepository(IDbHelper doDbHelper)
        {
            _dbHelper = doDbHelper;
            _userQuery = @"SELECT u.*, us.""name"" AS Status
                        FROM ""user"" u
                        LEFT JOIN ""user.status"" us ON us.""id"" = u.""statusId""";
        }

        public async Task CreateUser(User user)
        {
            user.Email = user.Email.ToLower();

            var sql = @"INSERT INTO public.""user"" (""id"", ""displayName"", ""email"", ""phone"", ""firstName"", ""lastName"", ""statusId"", ""stripeId"", ""needsPassSetup"")
                        VALUES (@id, @displayName, @email, @phone, @firstName, @lastName, @statusId, @stripeId, @needsPassSetup)
                        ON CONFLICT (""id"") DO NOTHING";

            await _dbHelper.Execute(sql, user);
        }

        public async Task<int> CreateUserRole(string userId, int roleId)
        {
            var sql = @"INSERT INTO public.""user.userRole"" (""userId"", ""roleId"")
                        VALUES (@userId, @roleId)
                        RETURNING ""id""";

            var result = await _dbHelper.Query<int>(sql, new { userId, roleId });
            return result.FirstOrDefault();
        }

        public async Task<User> GetUser(string id)
        {
            var sql = $@"{_userQuery} WHERE u.""id"" = @id";
            var userResult = await _dbHelper.Query<User>(sql, new { id });

            return userResult.FirstOrDefault();
        }

        public async Task<User> GetUserByEmail(string email)
        {
            var sql = $@"{_userQuery} WHERE LOWER(u.""email"") = LOWER(@email)";
            var result = await _dbHelper.Query<User>(sql, new { email = email.ToLower() });

            return result.FirstOrDefault();
        }

        public async Task<List<Role>> GetAssignedRoles(string userId)
        {
            var sql = @"SELECT r.*
			            FROM ""user.role"" r
			                JOIN ""user.userRole"" ur ON ur.""roleId"" = r.""id""
			            WHERE ur.""userId"" = @userId";
            var result = await _dbHelper.Query<Role>(sql, new { userId });
            return result != null ? result.ToList() : new List<Role>();
        }

        public async Task<List<Role>> GetAvailableRoles(bool activeOnly = true)
        {
            var sql = @"SELECT * FROM ""user.role""";
            if (activeOnly) sql += @" WHERE ""active"" = true";

            var result = await _dbHelper.Query<Role>(sql);

            return result.ToList();
        }

        public async Task<User> Login(string email, string password)
        {
            var sql = @"SELECT * FROM ""user"" WHERE LOWER(""email"") = LOWER(@email) AND ""password"" = @password";
            var result = await _dbHelper.Query<User>(sql, new { email, password });

            return result.FirstOrDefault();
        }

        public async Task SaveUserRoles(string userId, List<int> roleIds)
        {
            // delete all roles and then recreate them
            var sql = @"DELETE FROM ""user.userRole"" WHERE ""userId"" = @userId";
            await _dbHelper.Execute(sql, new { userId });

            // insert all roles
            sql = @"INSERT INTO ""user.userRole"" (""userId"", ""roleId"") VALUES ";

            foreach (int roleId in roleIds)
            {
                sql += $"(@userId, {roleId}), ";
            }

            // remove trailing comma
            sql = sql.Substring(0, sql.Length - 2);

            await _dbHelper.Execute(sql, new { userId });
        }

        public async Task<List<User>> SearchUsers(SearchUsersRequest req)
        {
            var args = new DynamicParameters();
            var joinClause = "";
            var whereClause = "WHERE 1 = 1 ";

            if (!string.IsNullOrEmpty(req.UserId))
            {
                whereClause += @" AND u.""id"" LIKE LOWER(@userId) ";
                args.Add("@userId", req.UserId.ToLower());
            }
            if (!string.IsNullOrEmpty(req.Email))
            {
                whereClause += @" AND u.""email"" LIKE LOWER(@email) ";
                args.Add("@email", req.Email.ToLower());
            }
            if (!string.IsNullOrEmpty(req.FirstName))
            {
                whereClause += @" AND u.""firstName"" LIKE LOWER(@firstName) ";
                args.Add("@firstName", req.FirstName.ToLower());
            }
            if (!string.IsNullOrEmpty(req.LastName))
            {
                whereClause += @" AND u.""lastName"" LIKE LOWER(@lastName) ";
                args.Add("@lastName", req.LastName.ToLower());
            }
            if (!string.IsNullOrEmpty(req.Phone))
            {
                whereClause += @" AND u.""phone"" LIKE @phone ";
                args.Add("@phone", req.Phone);
            }

            if (req.RoleIds != null && req.RoleIds.Count > 0)
            {
                joinClause += @"JOIN ""user.userRole"" ur ON ur.""userId"" = u.""id"" ";
                whereClause += @"AND ur.""roleId"" = ANY (@roleIds) ";
                args.Add("@roleIds", req.RoleIds);
            }

            var sql = $@"SELECT u.* FROM ""user"" u
                        {joinClause}
                        {whereClause}";

            var usersResult = await _dbHelper.Query<User>(sql, args);

            return usersResult.ToList();
        }

        public async Task UpdatePassword(string id, string password)
        {
            var accountInitialized = true;
            string query = $@"UPDATE ""user""
                            SET ""password"" = @password,
                            ""isAccountInitialized"" = @accountInitialized
                            WHERE ""id"" = @id";
            await _dbHelper.Execute(query, new { id, password, accountInitialized });
        }

        public async Task UpdateUser(User user)
        {
            var sql = @"UPDATE ""user""
                        SET ""displayName"" = @displayName,
                            ""aboutMe"" = @aboutMe,
                            ""email"" = @email,
                            ""firstName"" = @firstName,
                            ""lastName"" = @lastName,
                            ""phone"" = @phone,
                            ""statusId"" = @statusId,
                            ""billingAddressId"" = @billingAddressId,
                            ""shippingAddressId"" = @shippingAddressId,
                            ""birthDate"" = @birthDate,
                            ""needsPassSetup"" = @needsPassSetup
                        WHERE ""id"" = @id";
            await _dbHelper.Execute(sql, user);
        }

        public async Task UpdateUserProfileImage(User user)
        {
            var sql = @"UPDATE ""user""
                        SET
                            ""profileImgUrl"" = @profileImgUrl
                        WHERE ""id"" = @id";
            await _dbHelper.Execute(sql, user);
        }
    }
}
