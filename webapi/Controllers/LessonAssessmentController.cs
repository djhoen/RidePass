using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.LessonAssessment;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Rider self-assessments at lesson checkout (Script0318). Checkout reads which survey an event asks
    /// (public: guests check out too, and the questions are no secret); admins pick the survey per event type
    /// and read the answers per ticket (gated like the Rider Report: ReportsView). Answers are written by the
    /// post-payment registration (PurchaseController.CompleteTicketRegistration).
    /// </summary>
    [ApiController]
    [Route("api/LessonAssessment")]
    public class LessonAssessmentController : ControllerBase
    {
        /// <summary>Bound on one admin lookup (the Rider Report asks for every ticket row it shows).</summary>
        private const int MaxTicketsPerLookup = 10000;
        private const int MaxTicketsPerAnswers = 200;

        private readonly ILessonAssessmentRepository _assessments;
        private readonly ISurveyRepository _surveys;
        private readonly ITenantContext _tenantContext;

        public LessonAssessmentController(ILessonAssessmentRepository assessments, ISurveyRepository surveys,
            ITenantContext tenantContext)
        {
            _assessments = assessments;
            _surveys = surveys;
            _tenantContext = tenantContext;
        }

        /// <summary>The self-assessment the event asks once per rider ticket; data is null when it asks none.</summary>
        [AllowAnonymous]
        [HttpGet("Event/{eventId:guid}")]
        public async Task<IActionResult> ForEvent(Guid eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var loaded = await LessonAssessments.ForEvent(_assessments, _surveys, eventId, _tenantContext.TenantId);
            return new ApiResponses().OkResult(loaded?.ToResponse());
        }

        /// <summary>The surveys the Event Types page can pick from (not closed).</summary>
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Surveys")]
        public async Task<IActionResult> Surveys()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _assessments.ListPickableSurveys(_tenantContext.TenantId);
            return new ApiResponses().OkResult(rows.Select(s => new AssessmentSurveyOption
            {
                Id = s.Id, Name = s.Name, Title = s.Title, Status = s.Status,
            }).ToList());
        }

        /// <summary>Which of these tickets have a self-assessment on file.</summary>
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpPost("Has")]
        public async Task<IActionResult> Has([FromBody] TicketIdsRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ids = (request.TicketIds ?? new List<Guid>()).Distinct().ToList();
            if (ids.Count > MaxTicketsPerLookup)
                return new ApiResponses().BadRequestResult($"Look up at most {MaxTicketsPerLookup} tickets at a time.");
            return new ApiResponses().OkResult(await _assessments.TicketsWithAssessment(_tenantContext.TenantId, ids));
        }

        /// <summary>The answers on these tickets, one assessment per ticket that has one.</summary>
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpPost("Answers")]
        public async Task<IActionResult> Answers([FromBody] TicketIdsRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ids = (request.TicketIds ?? new List<Guid>()).Distinct().ToList();
            if (ids.Count > MaxTicketsPerAnswers)
                return new ApiResponses().BadRequestResult($"Look up at most {MaxTicketsPerAnswers} tickets at a time.");
            var rows = await _assessments.ListAnswers(_tenantContext.TenantId, ids);
            return new ApiResponses().OkResult(LessonAssessments.Group(rows));
        }
    }
}
