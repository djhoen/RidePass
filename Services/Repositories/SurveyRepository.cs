using Services.Helpers.Interfaces;
using Services.Repositories.Data.SurveyData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class SurveyRepository : ISurveyRepository
    {
        private const string SurveyColumns = @"
            id, tenant_id AS TenantId, name, title, description, status,
            closes_at_utc AS ClosesAtUtc, require_email AS RequireEmail,
            public_token AS PublicToken,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string QuestionColumns = @"
            id, survey_id AS SurveyId, kind, prompt, sort_order AS SortOrder,
            required, created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string ChoiceColumns = @"
            id, question_id AS QuestionId, label, sort_order AS SortOrder,
            allows_free_text AS AllowsFreeText, created_at AS CreatedAt";

        private const string InviteColumns = @"
            id, survey_id AS SurveyId, email, token,
            sent_at_utc AS SentAtUtc, opened_at_utc AS OpenedAtUtc,
            completed_at_utc AS CompletedAtUtc, created_at AS CreatedAt";

        private const string ResponseColumns = @"
            id, survey_id AS SurveyId, user_id AS UserId, invite_id AS InviteId,
            respondent_email AS RespondentEmail, respondent_name AS RespondentName,
            submitted_at_utc AS SubmittedAtUtc, ip_address AS IpAddress";

        private const string AnswerColumns = @"
            id, response_id AS ResponseId, question_id AS QuestionId,
            choice_id AS ChoiceId, free_text AS FreeText, created_at AS CreatedAt";

        private readonly IDbHelper _db;
        public SurveyRepository(IDbHelper db) => _db = db;

        // ── Survey CRUD ──────────────────────────────────────────────────────
        public async Task<Guid> CreateSurvey(Survey s)
        {
            const string sql = @"
                INSERT INTO survey (tenant_id, name, title, description, status,
                                    closes_at_utc, require_email)
                VALUES (@TenantId, @Name, @Title, @Description, @Status,
                        @ClosesAtUtc, @RequireEmail)
                RETURNING id";
            return (await _db.Query<Guid>(sql, s)).First();
        }

        public async Task UpdateSurvey(Guid id, Guid tenantId, string name, string title, string? description,
            DateTime? closesAtUtc, bool requireEmail)
        {
            const string sql = @"
                UPDATE survey
                SET name = @name, title = @title, description = @description,
                    closes_at_utc = @closesAtUtc, require_email = @requireEmail
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, name, title, description, closesAtUtc, requireEmail });
        }

        public async Task UpdateStatus(Guid id, Guid tenantId, string status)
        {
            // Reopening (any → published) clears a stale close date so the
            // public submit gate doesn't immediately reject responses. A future
            // close date is preserved.
            const string sql = @"
                UPDATE survey
                SET status = @status,
                    closes_at_utc = CASE
                        WHEN @status = 'published' AND closes_at_utc IS NOT NULL AND closes_at_utc < now()
                            THEN NULL
                        ELSE closes_at_utc
                    END
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, status });
        }

        public async Task<Survey?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {SurveyColumns} FROM survey WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<Survey>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Survey?> GetByPublicToken(Guid publicToken, Guid tenantId)
        {
            // Tenant scope is baked into the WHERE clause so a public_token
            // leaked out-of-band can't be used to view (or submit to) the
            // survey via a different tenant's subdomain. Defense in depth —
            // public_tokens are random GUIDs and not guessable, but isolation
            // by tenant is non-negotiable.
            var sql = $@"
                SELECT {SurveyColumns} FROM survey
                WHERE public_token = @publicToken AND tenant_id = @tenantId
                LIMIT 1";
            return (await _db.Query<Survey>(sql, new { publicToken, tenantId })).FirstOrDefault();
        }

        public async Task<List<Survey>> ListByTenant(Guid tenantId)
        {
            var sql = $@"SELECT {SurveyColumns} FROM survey
                         WHERE tenant_id = @tenantId
                         ORDER BY created_at DESC";
            return (await _db.Query<Survey>(sql, new { tenantId })).ToList();
        }

        // ── Questions + Choices ──────────────────────────────────────────────
        public async Task<List<SurveyQuestion>> ListQuestions(Guid surveyId)
        {
            var sql = $@"SELECT {QuestionColumns} FROM survey_question
                         WHERE survey_id = @surveyId
                         ORDER BY sort_order, id";
            return (await _db.Query<SurveyQuestion>(sql, new { surveyId })).ToList();
        }

        public async Task<Dictionary<Guid, List<SurveyQuestionChoice>>> ListChoicesForQuestions(IEnumerable<Guid> questionIds)
        {
            var ids = questionIds.ToArray();
            if (ids.Length == 0) return new();
            var sql = $@"SELECT {ChoiceColumns} FROM survey_question_choice
                         WHERE question_id = ANY(@ids)
                         ORDER BY question_id, sort_order, id";
            var rows = await _db.Query<SurveyQuestionChoice>(sql, new { ids });
            return rows.GroupBy(c => c.QuestionId).ToDictionary(g => g.Key, g => g.ToList());
        }

        public async Task<Guid> CreateQuestion(SurveyQuestion q)
        {
            const string sql = @"
                INSERT INTO survey_question (survey_id, kind, prompt, sort_order, required)
                VALUES (@SurveyId, @Kind, @Prompt, @SortOrder, @Required)
                RETURNING id";
            return (await _db.Query<Guid>(sql, q)).First();
        }

        public async Task UpdateQuestion(Guid id, string prompt, int sortOrder, bool required)
        {
            const string sql = @"
                UPDATE survey_question SET prompt = @prompt, sort_order = @sortOrder, required = @required
                WHERE id = @id";
            await _db.Execute(sql, new { id, prompt, sortOrder, required });
        }

        public async Task DeleteQuestion(Guid id)
        {
            await _db.Execute("DELETE FROM survey_question WHERE id = @id", new { id });
        }

        public async Task<SurveyQuestion?> GetQuestion(Guid id)
        {
            var sql = $"SELECT {QuestionColumns} FROM survey_question WHERE id = @id LIMIT 1";
            return (await _db.Query<SurveyQuestion>(sql, new { id })).FirstOrDefault();
        }

        public async Task<Guid> CreateChoice(SurveyQuestionChoice c)
        {
            const string sql = @"
                INSERT INTO survey_question_choice (question_id, label, sort_order, allows_free_text)
                VALUES (@QuestionId, @Label, @SortOrder, @AllowsFreeText)
                RETURNING id";
            return (await _db.Query<Guid>(sql, c)).First();
        }

        public async Task UpdateChoice(Guid id, string label, int sortOrder, bool allowsFreeText)
        {
            const string sql = @"UPDATE survey_question_choice
                                 SET label = @label, sort_order = @sortOrder, allows_free_text = @allowsFreeText
                                 WHERE id = @id";
            await _db.Execute(sql, new { id, label, sortOrder, allowsFreeText });
        }

        public async Task DeleteChoice(Guid id)
        {
            await _db.Execute("DELETE FROM survey_question_choice WHERE id = @id", new { id });
        }

        public async Task ReplaceChoices(Guid questionId, IEnumerable<(string Label, int SortOrder, bool AllowsFreeText)> choices)
        {
            await _db.Execute("DELETE FROM survey_question_choice WHERE question_id = @questionId", new { questionId });
            const string insert = @"
                INSERT INTO survey_question_choice (question_id, label, sort_order, allows_free_text)
                VALUES (@questionId, @label, @sortOrder, @allowsFreeText)";
            foreach (var c in choices)
            {
                await _db.Execute(insert, new { questionId, label = c.Label, sortOrder = c.SortOrder, allowsFreeText = c.AllowsFreeText });
            }
        }

        public async Task UpdateQuestionSortOrders(Guid surveyId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders)
        {
            if (ids.Count == 0) return;
            // Scope by survey_id — caller has already verified the survey belongs to
            // the tenant. The predicate prevents a stray question id from a sibling
            // survey from being moved.
            const string sql = @"
                UPDATE survey_question AS q
                SET sort_order = data.sort_order
                FROM (SELECT unnest(@ids::uuid[]) AS id,
                             unnest(@orders::int[]) AS sort_order) AS data
                WHERE q.id = data.id AND q.survey_id = @surveyId";
            await _db.Execute(sql, new
            {
                surveyId,
                ids = ids.ToArray(),
                orders = sortOrders.ToArray(),
            });
        }

        public async Task UpdateChoiceSortOrders(Guid questionId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders)
        {
            if (ids.Count == 0) return;
            const string sql = @"
                UPDATE survey_question_choice AS c
                SET sort_order = data.sort_order
                FROM (SELECT unnest(@ids::uuid[]) AS id,
                             unnest(@orders::int[]) AS sort_order) AS data
                WHERE c.id = data.id AND c.question_id = @questionId";
            await _db.Execute(sql, new
            {
                questionId,
                ids = ids.ToArray(),
                orders = sortOrders.ToArray(),
            });
        }

        // ── Invites ──────────────────────────────────────────────────────────
        public async Task<Guid> CreateInvite(SurveyInvite invite)
        {
            const string sql = @"
                INSERT INTO survey_invite (survey_id, email)
                VALUES (@SurveyId, @Email)
                ON CONFLICT (survey_id, lower(email)) DO UPDATE SET email = EXCLUDED.email
                RETURNING id";
            return (await _db.Query<Guid>(sql, invite)).First();
        }

        public async Task<SurveyInvite?> GetInviteById(Guid id)
        {
            var sql = $"SELECT {InviteColumns} FROM survey_invite WHERE id = @id LIMIT 1";
            return (await _db.Query<SurveyInvite>(sql, new { id })).FirstOrDefault();
        }

        public async Task<SurveyInvite?> GetInviteByToken(Guid token)
        {
            var sql = $"SELECT {InviteColumns} FROM survey_invite WHERE token = @token LIMIT 1";
            return (await _db.Query<SurveyInvite>(sql, new { token })).FirstOrDefault();
        }

        public async Task MarkInviteSent(Guid id, DateTime sentAtUtc)
        {
            await _db.Execute("UPDATE survey_invite SET sent_at_utc = @sentAtUtc WHERE id = @id",
                new { id, sentAtUtc });
        }

        public async Task MarkInviteOpened(Guid id, DateTime openedAtUtc)
        {
            // Only stamp the first time it's opened.
            await _db.Execute(
                "UPDATE survey_invite SET opened_at_utc = @openedAtUtc WHERE id = @id AND opened_at_utc IS NULL",
                new { id, openedAtUtc });
        }

        public async Task MarkInviteCompleted(Guid id, DateTime completedAtUtc)
        {
            await _db.Execute(
                "UPDATE survey_invite SET completed_at_utc = @completedAtUtc WHERE id = @id AND completed_at_utc IS NULL",
                new { id, completedAtUtc });
        }

        public async Task<List<SurveyInvite>> ListInvitesForSurvey(Guid surveyId)
        {
            var sql = $"SELECT {InviteColumns} FROM survey_invite WHERE survey_id = @surveyId ORDER BY created_at DESC";
            return (await _db.Query<SurveyInvite>(sql, new { surveyId })).ToList();
        }

        // ── Responses + Answers ──────────────────────────────────────────────
        public async Task<Guid> CreateResponse(SurveyResponse r)
        {
            const string sql = @"
                INSERT INTO survey_response (survey_id, user_id, invite_id,
                                             respondent_email, respondent_name, ip_address)
                VALUES (@SurveyId, @UserId, @InviteId, @RespondentEmail, @RespondentName, @IpAddress)
                RETURNING id";
            return (await _db.Query<Guid>(sql, r)).First();
        }

        public async Task<Guid> CreateAnswer(SurveyAnswer a)
        {
            const string sql = @"
                INSERT INTO survey_answer (response_id, question_id, choice_id, free_text)
                VALUES (@ResponseId, @QuestionId, @ChoiceId, @FreeText)
                RETURNING id";
            return (await _db.Query<Guid>(sql, a)).First();
        }

        public async Task<List<SurveyResponse>> ListResponsesForSurvey(Guid surveyId)
        {
            var sql = $@"SELECT {ResponseColumns} FROM survey_response
                         WHERE survey_id = @surveyId
                         ORDER BY submitted_at_utc DESC";
            return (await _db.Query<SurveyResponse>(sql, new { surveyId })).ToList();
        }

        public async Task<List<SurveyAnswer>> ListAnswersForSurvey(Guid surveyId)
        {
            var sql = $@"
                SELECT {AnswerColumns}
                FROM survey_answer a
                JOIN survey_response r ON r.id = a.response_id
                WHERE r.survey_id = @surveyId";
            return (await _db.Query<SurveyAnswer>(sql, new { surveyId })).ToList();
        }

        // ── Audience resolution ──────────────────────────────────────────────
        // 'paid'/'redeemed' = the customer actually completed the transaction.
        // Pending/cancelled/refunded are filtered out — survey blasting an
        // abandoned cart is bad form.
        private const string PaidStatuses = "('paid','redeemed')";

        public async Task<List<string>> AudienceEventPurchasers(Guid tenantId, Guid eventId)
        {
            // Anyone who's likely to attend this event:
            //  - bought a ticket for it
            //  - bought an extra (parking, t-shirt, etc) for it
            //  - bought a day pass valid on a date the event spans
            //  - holds a season pass whose validity range overlaps the event
            var sql = $@"
                SELECT DISTINCT lower(email) AS email
                FROM (
                    SELECT etp.purchaser_email AS email
                    FROM event_ticket_purchase etp
                    JOIN event_ticket_tier tier ON tier.id = etp.tier_id
                    WHERE etp.tenant_id = @tenantId
                      AND tier.event_id = @eventId
                      AND etp.status IN {PaidStatuses}
                    UNION
                    SELECT eep.purchaser_email AS email
                    FROM event_extra_purchase eep
                    WHERE eep.tenant_id = @tenantId
                      AND eep.event_id = @eventId
                      AND eep.status IN {PaidStatuses}
                    UNION
                    SELECT spp.purchaser_email AS email
                    FROM season_pass_purchase spp
                    JOIN event ev ON ev.id = @eventId
                    WHERE spp.tenant_id = @tenantId
                      AND spp.status IN {PaidStatuses}
                      AND spp.valid_from_date <= ev.ends_at::date
                      AND spp.valid_to_date >= ev.starts_at::date
                ) sub
                WHERE email IS NOT NULL AND email <> ''";
            return (await _db.Query<string>(sql, new { tenantId, eventId })).ToList();
        }

        public async Task<List<string>> AudiencePurchasersInRange(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            var sql = $@"
                SELECT DISTINCT lower(email) AS email
                FROM (
                    SELECT etp.purchaser_email AS email FROM event_ticket_purchase etp
                    WHERE etp.tenant_id = @tenantId AND etp.status IN {PaidStatuses}
                      AND etp.created_at >= @fromUtc AND etp.created_at < @toUtc
                    UNION
                    SELECT eep.purchaser_email AS email FROM event_extra_purchase eep
                    WHERE eep.tenant_id = @tenantId AND eep.status IN {PaidStatuses}
                      AND eep.created_at >= @fromUtc AND eep.created_at < @toUtc
                    UNION
                    SELECT spp.purchaser_email AS email FROM season_pass_purchase spp
                    WHERE spp.tenant_id = @tenantId AND spp.status IN {PaidStatuses}
                      AND spp.created_at >= @fromUtc AND spp.created_at < @toUtc
                ) sub
                WHERE email IS NOT NULL AND email <> ''";
            return (await _db.Query<string>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }

        public async Task<List<string>> AudienceAllCustomers(Guid tenantId)
        {
            var sql = $@"
                SELECT DISTINCT lower(email) AS email
                FROM (
                    SELECT etp.purchaser_email AS email FROM event_ticket_purchase etp
                    WHERE etp.tenant_id = @tenantId AND etp.status IN {PaidStatuses}
                    UNION
                    SELECT eep.purchaser_email AS email FROM event_extra_purchase eep
                    WHERE eep.tenant_id = @tenantId AND eep.status IN {PaidStatuses}
                    UNION
                    SELECT spp.purchaser_email AS email FROM season_pass_purchase spp
                    WHERE spp.tenant_id = @tenantId AND spp.status IN {PaidStatuses}
                ) sub
                WHERE email IS NOT NULL AND email <> ''";
            return (await _db.Query<string>(sql, new { tenantId })).ToList();
        }

        public async Task<List<string>> AudienceSubscribers(Guid tenantId)
        {
            var sql = @"
                SELECT DISTINCT lower(email) AS email
                FROM newsletter_subscriber
                WHERE tenant_id = @tenantId
                  AND unsubscribed_at IS NULL
                  AND email IS NOT NULL AND email <> ''";
            return (await _db.Query<string>(sql, new { tenantId })).ToList();
        }
    }
}
