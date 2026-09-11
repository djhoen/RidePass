using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Repositories.Data.NewsletterData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Newsletter;
using Services.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Saved email bodies a track starts campaigns and automation emails from. Same permission
    /// as campaigns: whoever writes the emails keeps the templates.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
    public class EmailTemplateController : ControllerBase
    {
        private readonly IEmailTemplateRepository _templates;
        private readonly ITenantContext _tenantContext;

        public EmailTemplateController(IEmailTemplateRepository templates, ITenantContext tenantContext)
        {
            _templates = templates;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _templates.ListForTenant(_tenantContext.TenantId);
            return new ApiResponses().OkResult(rows.Select(ToItem).ToList());
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var t = await _templates.GetById(id, _tenantContext.TenantId);
            if (t is null) return new ApiResponses().NotFoundResult("Template not found.");
            return new ApiResponses().OkResult(ToItem(t));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertEmailTemplateRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var error = Validate(request);
            if (error is not null) return new ApiResponses().BadRequestResult(error);
            var t = new EmailTemplate
            {
                TenantId = _tenantContext.TenantId,
                Name = request.Name.Trim(),
                Subject = Trim(request.Subject),
                PreviewText = Trim(request.PreviewText),
                BodyHtml = request.BodyHtml,
                CreatedByUserId = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : null,
            };
            t.Id = await _templates.Create(t);
            return new ApiResponses().OkResult(ToItem(t));
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertEmailTemplateRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var existing = await _templates.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Template not found.");
            var error = Validate(request);
            if (error is not null) return new ApiResponses().BadRequestResult(error);
            existing.Name = request.Name.Trim();
            existing.Subject = Trim(request.Subject);
            existing.PreviewText = Trim(request.PreviewText);
            existing.BodyHtml = request.BodyHtml;
            await _templates.Update(existing);
            return new ApiResponses().OkResult(ToItem(existing));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _templates.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult(new { deleted = true });
        }

        private static string? Validate(UpsertEmailTemplateRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.Name)) return "Give the template a name.";
            if (r.Name.Trim().Length > 120) return "Keep the template name under 120 characters.";
            if (string.IsNullOrWhiteSpace(r.BodyHtml) || r.BodyHtml.Trim() == "<p></p>") return "A template needs a message body.";
            return null;
        }

        private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static EmailTemplateItem ToItem(EmailTemplate t) => new()
        {
            Id = t.Id,
            Name = t.Name,
            Subject = t.Subject,
            PreviewText = t.PreviewText,
            BodyHtml = t.BodyHtml,
            UpdatedAtUtc = DateTime.SpecifyKind(t.UpdatedAt, DateTimeKind.Utc),
        };
    }
}
