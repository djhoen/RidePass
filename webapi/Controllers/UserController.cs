using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using webapi.Helpers;
using webapi.Models;
using Services.Helpers;
using Services.Repositories.Interfaces;
using Services.Repositories.Data.UserData;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IIdRepository _idRepository;

        public UserController(IUserRepository userRepository, IIdRepository idRepository)
        {
            _userRepository = userRepository;
            _idRepository = idRepository;
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var user = await _userRepository.GetUserByEmail(request.Email);

                if (user == null)
                    return new ApiResponses().BadRequestResult("Invalid email or password.");

                var token = JwtHelper.GetJwtToken(
                    user.Email,
                    "your-signing-key",
                    "your-issuer",
                    "your-audience",
                    TimeSpan.FromHours(24),
                    new[]
                    {
                        new Claim("UserId", user.Id),
                        new Claim(ClaimTypes.NameIdentifier, user.Id)
                    }
                );

                var tokenString = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);

                return new ApiResponses().OkResult(new { Token = tokenString, User = user });
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [HttpPost("CreateAccount")]
        public async Task<IActionResult> CreateAccount([FromBody] CreateAccountRequest request)
        {
            try
            {
                var existingUser = await _userRepository.GetUserByEmail(request.Email);
                if (existingUser != null)
                    return new ApiResponses().BadRequestResult("An account with this email already exists.");

                var id = await _idRepository.GetUniqueId("User", 10);

                var user = new User
                {
                    Id = id,
                    Email = request.Email,
                    Password = request.Password,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Phone = request.Phone
                };

                await _userRepository.CreateUser(user);

                return new ApiResponses().OkResult(user);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize]
        [HttpGet("GetProfile")]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var user = await _userRepository.GetUser(userId);

                return new ApiResponses().OkResult(user);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize]
        [HttpPost("UpdateProfile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var user = await _userRepository.GetUser(userId);

                user.FirstName = request.FirstName ?? user.FirstName;
                user.LastName = request.LastName ?? user.LastName;
                user.Email = request.Email ?? user.Email;
                user.Phone = request.Phone ?? user.Phone;
                user.AboutMe = request.AboutMe ?? user.AboutMe;
                user.DisplayName = request.DisplayName ?? user.DisplayName;

                await _userRepository.UpdateUser(user);

                return new ApiResponses().OkResult(user);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize]
        [HttpPost("UpdatePassword")]
        public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var user = await _userRepository.GetUser(userId);

                await _userRepository.UpdatePassword(userId, request.NewPassword);

                return new ApiResponses().OkResult(null);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet("GetUsers")]
        public async Task<IActionResult> GetUsers()
        {
            try
            {
                var users = await _userRepository.SearchUsers(new SearchUsersRequest());

                return new ApiResponses().OkResult(users);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("SearchUsers")]
        public async Task<IActionResult> SearchUsers([FromBody] SearchRequest request)
        {
            try
            {
                var searchUsersRequest = new SearchUsersRequest
                {
                    RoleIds = request.RoleIds,
                    UserId = request.UserId,
                    Email = request.Email,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Phone = request.Phone
                };

                var users = await _userRepository.SearchUsers(searchUsersRequest);

                return new ApiResponses().OkResult(users);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("SaveUserRoles")]
        public async Task<IActionResult> SaveUserRoles([FromBody] SaveUserRolesRequest request)
        {
            try
            {
                await _userRepository.SaveUserRoles(request.UserId, request.RoleIds);

                return new ApiResponses().OkResult(null);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }
    }
}
