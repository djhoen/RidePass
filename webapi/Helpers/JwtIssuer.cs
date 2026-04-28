using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Services.Repositories.Data.UserData;

namespace webapi.Helpers
{
    public interface IJwtIssuer
    {
        string IssueForUser(User user, TimeSpan? expiration = null, Guid? impersonatedBy = null);
    }

    public class JwtIssuer : IJwtIssuer
    {
        private readonly IConfiguration _configuration;

        public JwtIssuer(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string IssueForUser(User user, TimeSpan? expiration = null, Guid? impersonatedBy = null)
        {
            var issuer = _configuration["Jwt:Issuer"]!;
            var signingKey = _configuration["Jwt:SigningKey"]!;

            var claims = new List<Claim>
            {
                new("UserId", user.Id.ToString()),
                new("role", user.Role),
                new(ClaimTypes.NameIdentifier, user.Id.ToString())
            };

            if (user.TenantId.HasValue)
            {
                claims.Add(new Claim("tenant_id", user.TenantId.Value.ToString()));
            }

            if (impersonatedBy.HasValue)
            {
                claims.Add(new Claim("impersonated_by", impersonatedBy.Value.ToString()));
            }

            var jwt = JwtHelper.GetJwtToken(
                email: user.Email,
                signingKey: signingKey,
                issuer: issuer,
                audience: issuer,
                expiration: expiration ?? TimeSpan.FromHours(24),
                additionalClaims: claims.ToArray());

            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }
    }
}
