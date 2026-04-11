using Services.Repositories.Data.UserData;

namespace Services.Repositories.Interfaces
{
    public interface IUserRepository
    {
        Task CreateUser(User user);
        Task<int> CreateUserRole(string userId, int roleId);
        Task<User> GetUser(string id);
        Task<User> GetUserByEmail(string email);
        Task<List<Role>> GetAssignedRoles(string userId);
        Task<List<Role>> GetAvailableRoles(bool activeOnly = true);
        Task<User> Login(string email, string password);
        Task SaveUserRoles(string userId, List<int> roleIds);
        Task<List<User>> SearchUsers(SearchUsersRequest req);
        Task UpdatePassword(string id, string password);
        Task UpdateUser(User user);
        Task UpdateUserProfileImage(User user);
    }
}
