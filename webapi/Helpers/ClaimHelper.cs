using System.Security.Claims;

namespace webapi.Helpers
{
    public class ClaimHelper
    {
        public static bool ClaimHasRole(Claim claim, string requiredRoles)
        {
            if (claim == null || claim.Subject == null)
            {
                return false;
            }

            var roles = claim.Subject.Claims.Where(x => x.Type.Contains("role")).ToList();

            foreach (var role in roles)
            {
                if (requiredRoles.Contains(role.Value))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool ClaimMatchesUserId(Claim claim, string userId)
        {
            if (claim == null || claim.Subject == null)
            {
                return false;
            }

            var userIdClaim = claim.Subject.Claims.FirstOrDefault(x => x.Type == "UserId");
            if (userIdClaim == null)
            {
                return false;
            }

            return userIdClaim.Value == userId;
        }

        public static bool ClaimMatchesEmail(Claim claim, string email)
        {
            if (claim == null)
            {
                return false;
            }

            return claim.Value == email;
        }
    }
}
