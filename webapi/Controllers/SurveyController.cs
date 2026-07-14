using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.SurveyData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Survey;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Tenant-built surveys: admin CRUD + email distribution + public fill page.
    /// Three question kinds — single_choice (poll), multiple_choice, free_form.
    /// Anonymous-friendly (user_id + email both nullable). Per-recipient invite
    /// tokens enable open/complete tracking; the survey-level public token is
    /// for broad sharing with no per-person tracking.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SurveyController : ControllerBase
    {
        private readonly ISurveyRepository _surveys;
        private readonly ITenantRepository _tenants;
        private readonly ITenantContext _tenantContext;
        private readonly ISmtpEmailer _emailer;
        private readonly IConfiguration _config;

        public SurveyController(
            ISurveyRepository surveys,
            ITenantRepository tenants,
            ITenantContext tenantContext,
            ISmtpEmailer emailer,
            IConfiguration config)
        {
            _surveys = surveys;
            _tenants = tenants;
            _tenantContext = tenantContext;
            _emailer = emailer;
            _config = config;
        }

        // ── Admin: list / get / create / update / status ────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpGet("Admin")]
        public async Task<IActionResult> ListAdmin()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _surveys.ListByTenant(_tenantContext.TenantId);

            // Cheap counts: questions per survey + responses per survey. For a
            // typical tenant with a handful of surveys this is fine; if a tenant
            // ever ends up with hundreds, pre-aggregate in SQL.
            var items = new List<SurveyListItem>(rows.Count);
            foreach (var s in rows)
            {
                var qs = await _surveys.ListQuestions(s.Id);
                var responses = await _surveys.ListResponsesForSurvey(s.Id);
                items.Add(new SurveyListItem
                {
                    Id = s.Id,
                    Name = s.Name,
                    Title = s.Title,
                    Status = s.Status,
                    ClosesAtUtc = ToUtc(s.ClosesAtUtc),
                    PublicToken = s.PublicToken,
                    QuestionCount = qs.Count,
                    ResponseCount = responses.Count,
                    CreatedAtUtc = DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc),
                });
            }
            return new ApiResponses().OkResult(items);
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpGet("Admin/{id:guid}")]
        public async Task<IActionResult> GetAdmin(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var s = await _surveys.GetById(id, _tenantContext.TenantId);
            if (s is null) return new ApiResponses().NotFoundResult("Survey not found.");
            return new ApiResponses().OkResult(await BuildAdminResponse(s));
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpPost("Admin")]
        public async Task<IActionResult> CreateAdmin([FromBody] CreateSurveyRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var entity = new Survey
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                Title = req.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                Status = "draft",
                ClosesAtUtc = req.ClosesAtUtc,
                RequireEmail = req.RequireEmail,
            };
            var id = await _surveys.CreateSurvey(entity);
            var saved = await _surveys.GetById(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult(await BuildAdminResponse(saved!));
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpPut("Admin/{id:guid}")]
        public async Task<IActionResult> UpdateAdmin(Guid id, [FromBody] UpdateSurveyRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var existing = await _surveys.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Survey not found.");
            await _surveys.UpdateSurvey(id, _tenantContext.TenantId,
                req.Name.Trim(), req.Title.Trim(),
                string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                req.ClosesAtUtc, req.RequireEmail);
            var refreshed = await _surveys.GetById(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult(await BuildAdminResponse(refreshed!));
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpPut("Admin/{id:guid}/Status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateSurveyStatusRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var existing = await _surveys.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Survey not found.");
            await _surveys.UpdateStatus(id, _tenantContext.TenantId, req.Status);
            return new ApiResponses().OkResult(new { id, status = req.Status });
        }

        // ── Admin: questions + choices ──────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpPost("Admin/{surveyId:guid}/Questions")]
        public async Task<IActionResult> CreateQuestion(Guid surveyId, [FromBody] CreateQuestionRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var survey = await _surveys.GetById(surveyId, _tenantContext.TenantId);
            if (survey is null) return new ApiResponses().NotFoundResult("Survey not found.");
            var qId = await _surveys.CreateQuestion(new SurveyQuestion
            {
                SurveyId = surveyId,
                Kind = req.Kind,
                Prompt = req.Prompt.Trim(),
                SortOrder = req.SortOrder,
                Required = req.Required,
            });
            if ((req.Kind == "single_choice" || req.Kind == "multiple_choice") && req.Choices is { Count: > 0 })
            {
                var inputs = req.Choices
                    .Where(c => !string.IsNullOrWhiteSpace(c.Label))
                    .Select((c, i) => (Label: c.Label.Trim(), SortOrder: (i + 1) * 10, AllowsFreeText: c.AllowsFreeText));
                await _surveys.ReplaceChoices(qId, inputs);
            }
            return new ApiResponses().OkResult(await BuildQuestionDto(qId));
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpPut("Admin/Questions/{questionId:guid}")]
        public async Task<IActionResult> UpdateQuestion(Guid questionId, [FromBody] UpdateQuestionRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await EnsureQuestionInTenant(questionId)) return new ApiResponses().NotFoundResult("Question not found.");
            await _surveys.UpdateQuestion(questionId, req.Prompt.Trim(), req.SortOrder, req.Required);
            return new ApiResponses().OkResult(await BuildQuestionDto(questionId));
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpDelete("Admin/Questions/{questionId:guid}")]
        public async Task<IActionResult> DeleteQuestion(Guid questionId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await EnsureQuestionInTenant(questionId)) return new ApiResponses().NotFoundResult("Question not found.");
            await _surveys.DeleteQuestion(questionId);
            return new ApiResponses().OkResult(new { id = questionId });
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpPost("Admin/{surveyId:guid}/Questions/Reorder")]
        public async Task<IActionResult> ReorderQuestions(Guid surveyId, [FromBody] ReorderQuestionsRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var survey = await _surveys.GetById(surveyId, _tenantContext.TenantId);
            if (survey is null) return new ApiResponses().NotFoundResult("Survey not found.");
            if (req.Items.Count == 0) return new ApiResponses().OkResult();
            var ids = req.Items.Select(i => i.Id).ToList();
            var orders = req.Items.Select(i => i.SortOrder).ToList();
            await _surveys.UpdateQuestionSortOrders(surveyId, ids, orders);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpPost("Admin/Questions/{questionId:guid}/Choices/Reorder")]
        public async Task<IActionResult> ReorderChoices(Guid questionId, [FromBody] ReorderChoicesRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await EnsureQuestionInTenant(questionId)) return new ApiResponses().NotFoundResult("Question not found.");
            if (req.Items.Count == 0) return new ApiResponses().OkResult();
            var ids = req.Items.Select(i => i.Id).ToList();
            var orders = req.Items.Select(i => i.SortOrder).ToList();
            await _surveys.UpdateChoiceSortOrders(questionId, ids, orders);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpPut("Admin/Questions/{questionId:guid}/Choices")]
        public async Task<IActionResult> ReplaceChoices(Guid questionId, [FromBody] ReplaceChoicesRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await EnsureQuestionInTenant(questionId)) return new ApiResponses().NotFoundResult("Question not found.");
            var inputs = req.Choices
                .Where(c => !string.IsNullOrWhiteSpace(c.Label))
                .Select((c, i) => (Label: c.Label.Trim(), SortOrder: (i + 1) * 10, AllowsFreeText: c.AllowsFreeText));
            await _surveys.ReplaceChoices(questionId, inputs);
            return new ApiResponses().OkResult(await BuildQuestionDto(questionId));
        }

        // ── Admin: invites + send ───────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpGet("Admin/{id:guid}/Invites")]
        public async Task<IActionResult> ListInvites(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var s = await _surveys.GetById(id, _tenantContext.TenantId);
            if (s is null) return new ApiResponses().NotFoundResult("Survey not found.");
            var invites = await _surveys.ListInvitesForSurvey(id);
            var dtos = invites.Select(i => new SurveyInviteDto
            {
                Id = i.Id,
                Email = i.Email,
                SentAtUtc = ToUtc(i.SentAtUtc),
                OpenedAtUtc = ToUtc(i.OpenedAtUtc),
                CompletedAtUtc = ToUtc(i.CompletedAtUtc),
                CreatedAtUtc = DateTime.SpecifyKind(i.CreatedAt, DateTimeKind.Utc),
            }).ToList();
            return new ApiResponses().OkResult(dtos);
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpPost("Admin/{id:guid}/Send")]
        public async Task<IActionResult> SendInvites(Guid id, [FromBody] SendSurveyInvitesRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var survey = await _surveys.GetById(id, _tenantContext.TenantId);
            if (survey is null) return new ApiResponses().NotFoundResult("Survey not found.");
            if (survey.Status != "published")
            {
                return new ApiResponses().BadRequestResult("Survey must be published before sending invites.");
            }

            var tenant = await _tenants.GetById(_tenantContext.TenantId);
            if (tenant is null) return new ApiResponses().BadRequestResult("Tenant not found.");

            var apex = _config["App:RootDomain"] ?? "ridepass.io";
            var baseUrl = $"https://{tenant.Subdomain}.{apex}";

            var resolved = await ResolveAudience(req.Audience, _tenantContext.TenantId);
            if (resolved.Count == 0)
            {
                return new ApiResponses().BadRequestResult("Selected audience is empty.");
            }

            var sent = 0;
            var skipped = new List<string>();

            foreach (var email in resolved)
            {

                // Upsert (lower(email) unique per survey). On conflict, returns
                // the existing invite id so we don't generate duplicate tokens.
                var inviteId = await _surveys.CreateInvite(new SurveyInvite
                {
                    SurveyId = id,
                    Email = email,
                });
                var invite = await _surveys.GetInviteById(inviteId);
                if (invite is null) { skipped.Add(email); continue; }

                var link = $"{baseUrl}/Survey/{invite.Token}";
                var subject = BuildInviteSubject(survey, tenant.DisplayName);
                var body = BuildInviteHtml(survey, tenant.DisplayName, link);

                if (_emailer.IsConfigured)
                {
                    var ok = await _emailer.Send(email, subject, body, null, Services.Email.TenantEmailIdentity.For(tenant));
                    if (ok)
                    {
                        await _surveys.MarkInviteSent(inviteId, DateTime.UtcNow);
                        sent++;
                        continue;
                    }
                }
                skipped.Add(email);
            }

            return new ApiResponses().OkResult(new SendSurveyInvitesResponse
            {
                Sent = sent,
                Skipped = skipped.Count,
                SkippedEmails = skipped,
            });
        }

        // ── Admin: results aggregation ──────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpGet("Admin/{id:guid}/Results")]
        public async Task<IActionResult> Results(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var survey = await _surveys.GetById(id, _tenantContext.TenantId);
            if (survey is null) return new ApiResponses().NotFoundResult("Survey not found.");

            var questions = await _surveys.ListQuestions(id);
            var qIds = questions.Select(q => q.Id).ToList();
            var choicesByQ = await _surveys.ListChoicesForQuestions(qIds);
            var responses = await _surveys.ListResponsesForSurvey(id);
            var answers = await _surveys.ListAnswersForSurvey(id);
            var invites = await _surveys.ListInvitesForSurvey(id);

            var answersByQ = answers.GroupBy(a => a.QuestionId).ToDictionary(g => g.Key, g => g.ToList());

            var results = questions.Select(q =>
            {
                var qa = answersByQ.TryGetValue(q.Id, out var list) ? list : new List<SurveyAnswer>();
                var answeredCount = qa.Select(a => a.ResponseId).Distinct().Count();

                var qResult = new SurveyQuestionResult
                {
                    QuestionId = q.Id,
                    Kind = q.Kind,
                    Prompt = q.Prompt,
                    AnsweredCount = answeredCount,
                };

                if (q.Kind is "single_choice" or "multiple_choice")
                {
                    var choices = choicesByQ.TryGetValue(q.Id, out var cs) ? cs : new List<SurveyQuestionChoice>();
                    var byChoice = qa.Where(a => a.ChoiceId.HasValue)
                        .GroupBy(a => a.ChoiceId!.Value)
                        .ToDictionary(g => g.Key, g => g.ToList());
                    var totalPicks = byChoice.Values.Sum(v => v.Count);
                    qResult.ChoiceResults = choices.Select(c =>
                    {
                        var rows = byChoice.TryGetValue(c.Id, out var rs) ? rs : new();
                        return new SurveyChoiceResult
                        {
                            ChoiceId = c.Id,
                            Label = c.Label,
                            Count = rows.Count,
                            Percent = totalPicks == 0 ? 0 : Math.Round(100.0 * rows.Count / totalPicks, 1),
                            AllowsFreeText = c.AllowsFreeText,
                            FreeTextAnswers = c.AllowsFreeText
                                ? rows.Where(r => !string.IsNullOrWhiteSpace(r.FreeText))
                                      .Select(r => r.FreeText!).ToList()
                                : new List<string>(),
                        };
                    }).ToList();
                }
                else
                {
                    qResult.FreeFormAnswers = qa
                        .Where(a => !string.IsNullOrWhiteSpace(a.FreeText) && !a.ChoiceId.HasValue)
                        .Select(a => a.FreeText!)
                        .ToList();
                }

                return qResult;
            }).ToList();

            return new ApiResponses().OkResult(new SurveyResultsResponse
            {
                Id = survey.Id,
                Title = survey.Title,
                Status = survey.Status,
                ResponseCount = responses.Count,
                InviteSent = invites.Count(i => i.SentAtUtc.HasValue),
                InviteOpened = invites.Count(i => i.OpenedAtUtc.HasValue),
                InviteCompleted = invites.Count(i => i.CompletedAtUtc.HasValue),
                Questions = results,
            });
        }

        // ── Public: fetch + submit ──────────────────────────────────────────
        // Token can be either a survey public_token (broad share) or an invite
        // token (per-recipient tracking). We try invite first since we know
        // more about the respondent in that case.
        [HttpGet("Public/{token:guid}")]
        public async Task<IActionResult> GetPublic(Guid token)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var invite = await _surveys.GetInviteByToken(token);
            Survey? survey;
            if (invite is not null)
            {
                // Invite token resolved — load the survey scoped to the
                // currently-resolved tenant. If the invite belongs to a
                // different tenant, this returns null and we 404 below before
                // touching any tracking state.
                survey = await GetSurveyByIdAnyTenant(invite.SurveyId);
                if (survey is null) return new ApiResponses().NotFoundResult("Survey not found.");
                // Stamp open-tracking on first view. Idempotent — only stamps
                // if currently null.
                await _surveys.MarkInviteOpened(invite.Id, DateTime.UtcNow);
            }
            else
            {
                // Public-token path: scope by tenant at the data layer so a
                // token leaked out-of-band can't be replayed on another tenant.
                survey = await _surveys.GetByPublicToken(token, _tenantContext.TenantId);
                if (survey is null) return new ApiResponses().NotFoundResult("Survey not found.");
            }

            if (survey.Status != "published")
            {
                return new ApiResponses().BadRequestResult("Survey is not currently accepting responses.");
            }
            if (survey.ClosesAtUtc.HasValue && survey.ClosesAtUtc.Value < DateTime.UtcNow)
            {
                return new ApiResponses().BadRequestResult("Survey is closed.");
            }

            var questions = await _surveys.ListQuestions(survey.Id);
            var choicesByQ = await _surveys.ListChoicesForQuestions(questions.Select(q => q.Id));

            return new ApiResponses().OkResult(new PublicSurveyResponse
            {
                Id = survey.Id,
                Title = survey.Title,
                Description = survey.Description,
                Status = survey.Status,
                RequireEmail = survey.RequireEmail,
                ClosesAtUtc = ToUtc(survey.ClosesAtUtc),
                InviteToken = invite?.Token,
                InviteEmail = invite?.Email,
                AlreadyCompleted = invite?.CompletedAtUtc.HasValue,
                Questions = questions.Select(q => new SurveyQuestionDto
                {
                    Id = q.Id,
                    Kind = q.Kind,
                    Prompt = q.Prompt,
                    SortOrder = q.SortOrder,
                    Required = q.Required,
                    Choices = choicesByQ.TryGetValue(q.Id, out var cs)
                        ? cs.Select(c => new SurveyChoiceDto { Id = c.Id, Label = c.Label, SortOrder = c.SortOrder, AllowsFreeText = c.AllowsFreeText }).ToList()
                        : new List<SurveyChoiceDto>(),
                }).ToList(),
            });
        }

        [HttpPost("Public/{token:guid}/Submit")]
        public async Task<IActionResult> SubmitPublic(Guid token, [FromBody] SubmitSurveyRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var invite = await _surveys.GetInviteByToken(token);
            Survey? survey;
            if (invite is not null)
            {
                // Invite path — survey id scoped to the resolved tenant.
                survey = await GetSurveyByIdAnyTenant(invite.SurveyId);
            }
            else
            {
                // Public-token path — tenant scope enforced in the SQL.
                survey = await _surveys.GetByPublicToken(token, _tenantContext.TenantId);
            }
            if (survey is null) return new ApiResponses().NotFoundResult("Survey not found.");
            if (survey.Status != "published")
                return new ApiResponses().BadRequestResult("Survey is not currently accepting responses.");
            if (survey.ClosesAtUtc.HasValue && survey.ClosesAtUtc.Value < DateTime.UtcNow)
                return new ApiResponses().BadRequestResult("Survey is closed.");

            // Email is required when the survey says so OR there's no invite to
            // identify the respondent.
            if (survey.RequireEmail && string.IsNullOrWhiteSpace(req.RespondentEmail))
            {
                return new ApiResponses().BadRequestResult("Email is required for this survey.");
            }

            var questions = await _surveys.ListQuestions(survey.Id);
            var choicesByQ = await _surveys.ListChoicesForQuestions(questions.Select(q => q.Id));
            var qById = questions.ToDictionary(q => q.Id);

            // Validate required questions are answered and answer shapes match
            // question kind. Cheap server-side check; UI also enforces.
            foreach (var q in questions)
            {
                var supplied = req.Answers.FirstOrDefault(a => a.QuestionId == q.Id);
                var hasAnswer =
                    (q.Kind == "free_form" && !string.IsNullOrWhiteSpace(supplied?.FreeText))
                    || (q.Kind != "free_form" && supplied?.ChoiceIds is { Count: > 0 });
                if (q.Required && !hasAnswer)
                {
                    return new ApiResponses().BadRequestResult($"Required question not answered: {q.Prompt}");
                }
            }

            Guid? userId = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : (Guid?)null;
            var respondentEmail = invite?.Email ?? req.RespondentEmail?.Trim();
            var respondentName = string.IsNullOrWhiteSpace(req.RespondentName) ? null : req.RespondentName.Trim();

            var responseId = await _surveys.CreateResponse(new SurveyResponse
            {
                SurveyId = survey.Id,
                UserId = userId,
                InviteId = invite?.Id,
                RespondentEmail = respondentEmail,
                RespondentName = respondentName,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            });

            foreach (var ans in req.Answers)
            {
                if (!qById.TryGetValue(ans.QuestionId, out var q)) continue; // ignore orphan answers

                if (q.Kind == "free_form")
                {
                    if (!string.IsNullOrWhiteSpace(ans.FreeText))
                    {
                        await _surveys.CreateAnswer(new SurveyAnswer
                        {
                            ResponseId = responseId,
                            QuestionId = q.Id,
                            FreeText = ans.FreeText.Trim(),
                        });
                    }
                }
                else
                {
                    var allChoices = choicesByQ.TryGetValue(q.Id, out var cs) ? cs : new();
                    var choiceById = allChoices.ToDictionary(c => c.Id);
                    var picks = (ans.ChoiceIds ?? new()).Where(choiceById.ContainsKey).Distinct().ToList();
                    if (q.Kind == "single_choice" && picks.Count > 1) picks = picks.Take(1).ToList();

                    // "Other — please explain" choices store both choice_id and
                    // free_text on the same answer row. The submit DTO carries
                    // a single FreeText per question; we attach it only to picks
                    // whose choice is flagged allows_free_text.
                    var otherText = string.IsNullOrWhiteSpace(ans.FreeText) ? null : ans.FreeText.Trim();
                    foreach (var choiceId in picks)
                    {
                        var choice = choiceById[choiceId];
                        await _surveys.CreateAnswer(new SurveyAnswer
                        {
                            ResponseId = responseId,
                            QuestionId = q.Id,
                            ChoiceId = choiceId,
                            FreeText = choice.AllowsFreeText ? otherText : null,
                        });
                    }
                }
            }

            if (invite is not null)
            {
                await _surveys.MarkInviteCompleted(invite.Id, DateTime.UtcNow);
            }

            return new ApiResponses().OkResult(new { id = responseId });
        }

        // ── Email preview ───────────────────────────────────────────────────
        // Renders the exact subject + HTML body that recipients will receive.
        // The link points to a placeholder token so admins can sanity-check
        // formatting without burning a real invite token.
        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpGet("Admin/{id:guid}/InvitePreview")]
        public async Task<IActionResult> InvitePreview(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var survey = await _surveys.GetById(id, _tenantContext.TenantId);
            if (survey is null) return new ApiResponses().NotFoundResult("Survey not found.");
            var tenant = await _tenants.GetById(_tenantContext.TenantId);
            if (tenant is null) return new ApiResponses().BadRequestResult("Tenant not found.");

            var apex = _config["App:RootDomain"] ?? "ridepass.io";
            var sampleLink = $"https://{tenant.Subdomain}.{apex}/Survey/{Guid.NewGuid()}";
            return new ApiResponses().OkResult(new InvitePreviewResponse
            {
                Subject = BuildInviteSubject(survey, tenant.DisplayName),
                BodyHtml = BuildInviteHtml(survey, tenant.DisplayName, sampleLink),
            });
        }

        // ── Audience preview ────────────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpPost("Admin/{id:guid}/Audience/Preview")]
        public async Task<IActionResult> AudiencePreview(Guid id, [FromBody] AudienceCriteria criteria)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var survey = await _surveys.GetById(id, _tenantContext.TenantId);
            if (survey is null) return new ApiResponses().NotFoundResult("Survey not found.");

            var resolved = await ResolveAudience(criteria, _tenantContext.TenantId);
            return new ApiResponses().OkResult(new AudiencePreviewResponse
            {
                Count = resolved.Count,
                Sample = resolved.Take(10).ToList(),
            });
        }

        // ── Helpers ─────────────────────────────────────────────────────────
        // Resolves an AudienceCriteria into a deduped, valid email list. Returns
        // the lowercased canonical form so the per-survey unique invite index
        // (lower(email)) doesn't trip on case differences.
        private async Task<List<string>> ResolveAudience(AudienceCriteria criteria, Guid tenantId)
        {
            IEnumerable<string> raw = criteria.Type switch
            {
                "custom" => criteria.Emails ?? Enumerable.Empty<string>(),
                "event" when criteria.EventId.HasValue
                    => await _surveys.AudienceEventPurchasers(tenantId, criteria.EventId.Value),
                "timeframe" when criteria.FromUtc.HasValue && criteria.ToUtc.HasValue
                    => await _surveys.AudiencePurchasersInRange(tenantId, criteria.FromUtc.Value, criteria.ToUtc.Value),
                "all_customers" => await _surveys.AudienceAllCustomers(tenantId),
                "subscribers" => await _surveys.AudienceSubscribers(tenantId),
                _ => Enumerable.Empty<string>(),
            };

            return raw
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Where(EmailHelper.IsValid)
                .Select(e => e.ToLowerInvariant())
                .Distinct()
                .ToList();
        }

        private async Task<SurveyAdminResponse> BuildAdminResponse(Survey s)
        {
            var qs = await _surveys.ListQuestions(s.Id);
            var choicesByQ = await _surveys.ListChoicesForQuestions(qs.Select(q => q.Id));
            return new SurveyAdminResponse
            {
                Id = s.Id,
                Name = s.Name,
                Title = s.Title,
                Description = s.Description,
                Status = s.Status,
                ClosesAtUtc = ToUtc(s.ClosesAtUtc),
                RequireEmail = s.RequireEmail,
                PublicToken = s.PublicToken,
                CreatedAtUtc = DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc),
                UpdatedAtUtc = DateTime.SpecifyKind(s.UpdatedAt, DateTimeKind.Utc),
                Questions = qs.Select(q => new SurveyQuestionDto
                {
                    Id = q.Id,
                    Kind = q.Kind,
                    Prompt = q.Prompt,
                    SortOrder = q.SortOrder,
                    Required = q.Required,
                    Choices = choicesByQ.TryGetValue(q.Id, out var cs)
                        ? cs.Select(c => new SurveyChoiceDto { Id = c.Id, Label = c.Label, SortOrder = c.SortOrder, AllowsFreeText = c.AllowsFreeText }).ToList()
                        : new List<SurveyChoiceDto>(),
                }).ToList(),
            };
        }

        private async Task<SurveyQuestionDto> BuildQuestionDto(Guid questionId)
        {
            var q = (await _surveys.GetQuestion(questionId))!;
            var choicesByQ = await _surveys.ListChoicesForQuestions(new[] { questionId });
            return new SurveyQuestionDto
            {
                Id = q.Id,
                Kind = q.Kind,
                Prompt = q.Prompt,
                SortOrder = q.SortOrder,
                Required = q.Required,
                Choices = choicesByQ.TryGetValue(q.Id, out var cs)
                    ? cs.Select(c => new SurveyChoiceDto { Id = c.Id, Label = c.Label, SortOrder = c.SortOrder, AllowsFreeText = c.AllowsFreeText }).ToList()
                    : new List<SurveyChoiceDto>(),
            };
        }

        private async Task<bool> EnsureQuestionInTenant(Guid questionId)
        {
            var q = await _surveys.GetQuestion(questionId);
            if (q is null) return false;
            var s = await _surveys.GetById(q.SurveyId, _tenantContext.TenantId);
            return s is not null;
        }

        // For public endpoints we hit by token (no tenant in URL). The caller
        // already arrived on a tenant subdomain so the resolved tenant context
        // is meaningful, but the token lookup itself is global. We re-fetch
        // through the tenant-scoped GetById to make sure the survey actually
        // belongs to the resolved tenant.
        private async Task<Survey?> GetSurveyByIdAnyTenant(Guid surveyId)
        {
            if (!_tenantContext.IsResolved) return null;
            return await _surveys.GetById(surveyId, _tenantContext.TenantId);
        }

        private static DateTime? ToUtc(DateTime? dt) =>
            dt.HasValue ? DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc) : (DateTime?)null;

        private static string BuildInviteSubject(Survey survey, string tenantName) =>
            $"{tenantName} — {survey.Title}";

        private static string BuildInviteHtml(Survey survey, string tenantName, string link)
        {
            var safeTenant = System.Net.WebUtility.HtmlEncode(tenantName);
            var safeTitle = System.Net.WebUtility.HtmlEncode(survey.Title);
            var safeDesc = string.IsNullOrWhiteSpace(survey.Description)
                ? ""
                : $"<p style=\"color:#444\">{System.Net.WebUtility.HtmlEncode(survey.Description)}</p>";
            return $@"
<div style=""font-family:Arial,sans-serif;max-width:560px;margin:auto;padding:24px"">
    <h2 style=""margin:0 0 12px"">{safeTenant} — {safeTitle}</h2>
    {safeDesc}
    <p>We'd love your input. It takes just a minute.</p>
    <p>
        <a href=""{link}"" style=""display:inline-block;padding:12px 20px;background:#0d6efd;color:#fff;text-decoration:none;border-radius:6px"">
            Take the survey
        </a>
    </p>
    <p style=""color:#888;font-size:12px"">If the button doesn't work, copy this link: <br>{link}</p>
</div>";
        }
    }
}
