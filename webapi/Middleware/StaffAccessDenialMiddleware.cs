using System.Text.Json;
using Services.Access;

namespace webapi.Middleware
{
    /// <summary>
    /// Turns the bare 403 produced by a staff access policy denial into a response that says what
    /// happened. An IAuthorizationHandler can only succeed or stay silent, so the reason would
    /// otherwise be lost between the handler and the browser, and a cashier blocked mid-shift
    /// would see nothing but "Forbidden".
    ///
    /// Only rewrites a 403 that the policy actually caused (the handler leaves a marker in
    /// HttpContext.Items). Every other 403, an ordinary missing permission, is left exactly as it
    /// was so this cannot start explaining away real authorization failures.
    /// </summary>
    public class StaffAccessDenialMiddleware
    {
        public const string DenialItemKey = "StaffAccessDenial";

        private readonly RequestDelegate _next;

        public StaffAccessDenialMiddleware(RequestDelegate next) => _next = next;

        public async Task Invoke(HttpContext context)
        {
            await _next(context);

            if (context.Response.StatusCode != StatusCodes.Status403Forbidden) return;
            if (context.Response.HasStarted) return;
            if (context.Items[DenialItemKey] is not StaffAccessPolicy.Denial denial) return;
            if (denial == StaffAccessPolicy.Denial.None) return;

            var error = denial switch
            {
                StaffAccessPolicy.Denial.OffSite =>
                    "This action can only be done on the track's own network. You're signed in "
                    + "from somewhere else, so sales, refunds and check-ins are blocked. Ask an "
                    + "admin to add this location under Settings if it should be allowed.",
                StaffAccessPolicy.Denial.OffHours =>
                    "This action is only allowed during your track's operating hours. Sales, "
                    + "refunds and check-ins are blocked outside them. An admin can change the "
                    + "hours under Settings.",
                _ => "This action isn't allowed from here right now.",
            };

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                status = "403",
                message = "Fail",
                error,
            }));
        }
    }
}
