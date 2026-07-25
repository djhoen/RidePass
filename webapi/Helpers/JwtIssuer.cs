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

            // Default lifetime is an IDLE timeout, not a hard cap: SlidingSessionMiddleware
            // re-issues a same-length token on activity, so only inactivity ends the session.
            var life = expiration
                ?? TimeSpan.FromMinutes(int.TryParse(_configuration["Jwt:IdleTimeoutMinutes"], out var m) && m > 0 ? m : 30);

            var claims = new List<Claim>
            {
                // Lets the sliding middleware re-issue with the caller's original duration
                // (30-min session, 21-day remember-me, 1-hour impersonation alike).
                new("session_minutes", ((int)life.TotalMinutes).ToString()),
                new("UserId", user.Id.ToString()),
                new("role", user.Role),   // primary first, so FindFirst("role") is the identity role
                new(ClaimTypes.NameIdentifier, user.Id.ToString())
            };

            // Additional roles a multi-role staffer holds. The permission handlers union over
            // every "role" claim; the primary above stays first for identity checks.
            foreach (var extra in (user.Roles ?? System.Array.Empty<string>()))
            {
                if (extra != user.Role) claims.Add(new Claim("role", extra));
            }

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
                expiration: life,
                additionalClaims: claims.ToArray());

            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }
    }
}
