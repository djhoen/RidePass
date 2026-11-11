using Dapper;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.SurveyData;
using Services.Repositories.Interfaces;
using Services.Surveys;

namespace Services.Repositories
{
    public class LessonAssessmentRepository : ILessonAssessmentRepository
    {
        private const string SurveyColumns = @"
            s.id, s.tenant_id AS TenantId, s.name, s.title, s.description, s.status,
            s.closes_at_utc AS ClosesAtUtc, s.require_email AS RequireEmail,
            s.public_token AS PublicToken,
            s.created_at AS CreatedAt, s.updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public LessonAssessmentRepository(IDbHelper db) => _db = db;

        public async Task<Survey?> GetSurveyForEvent(Guid eventId, Guid tenantId)
        {
            var sql = $@"
                SELECT {SurveyColumns}
                FROM event e
                JOIN tenant_event_type t ON t.id = e.event_type_id AND t.tenant_id = @tenantId
                JOIN survey s ON s.id = t.assessment_survey_id AND s.tenant_id = @tenantId
                WHERE e.id = @eventId AND e.tenant_id = @tenantId AND s.status <> 'closed'
                LIMIT 1";
            return (await _db.Query<Survey>(sql, new { eventId, tenantId })).FirstOrDefault();
        }

        public async Task<List<Survey>> ListPickableSurveys(Guid tenantId)
        {
            var sql = $@"
                SELECT {SurveyColumns}
                FROM survey s
                WHERE s.tenant_id = @tenantId AND s.status <> 'closed'
                ORDER BY lower(s.name)";
            return (await _db.Query<Survey>(sql, new { tenantId })).ToList();
        }

        public Task<Guid?> SaveForTicket(Guid tenantId, Guid surveyId, Guid ticketId, Guid? userId,
            string? respondentName, string? respondentEmail, string? ipAddress, IReadOnlyList<SurveyAnswerRow> rows)
        {
            return _db.WithTransaction<Guid?>(async (conn, tx) =>
            {
                // Both parents must be this tenant's, or nothing is written.
                var ok = await conn.ExecuteScalarAsync<bool>(@"
                    SELECT EXISTS (SELECT 1 FROM event_ticket_purchase WHERE id = @ticketId AND tenant_id = @tenantId)
                       AND EXISTS (SELECT 1 FROM survey WHERE id = @surveyId AND tenant_id = @tenantId)",
                    new { ticketId, surveyId, tenantId }, tx);
                if (!ok) return null;

                // A re-submitted registration replaces the earlier answers (answers cascade).
                await conn.ExecuteAsync(@"
                    DELETE FROM survey_response r USING survey s
                     WHERE r.event_ticket_purchase_id = @ticketId AND s.id = r.survey_id AND s.tenant_id = @tenantId",
                    new { ticketId, tenantId }, tx);

                var responseId = await conn.ExecuteScalarAsync<Guid>(@"
                    INSERT INTO survey_response (survey_id, user_id, respondent_email, respondent_name, ip_address,
                                                 event_ticket_purchase_id)
                    VALUES (@surveyId, @userId, @respondentEmail, @respondentName, @ipAddress, @ticketId)
                    RETURNING id",
                    new { surveyId, userId, respondentEmail, respondentName, ipAddress, ticketId }, tx);

                foreach (var row in rows)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO survey_answer (response_id, question_id, choice_id, free_text)
                        SELECT @responseId, q.id, @choiceId, @freeText
                        FROM survey_question q
                        WHERE q.id = @questionId AND q.survey_id = @surveyId",
                        new { responseId, questionId = row.QuestionId, choiceId = row.ChoiceId, freeText = row.FreeText, surveyId }, tx);
                }
                return responseId;
            });
        }

        public async Task<List<Guid>> TicketsWithAssessment(Guid tenantId, IReadOnlyCollection<Guid> ticketIds)
        {
            if (ticketIds.Count == 0) return new List<Guid>();
            const string sql = @"
                SELECT DISTINCT r.event_ticket_purchase_id
                FROM survey_response r
                JOIN survey s ON s.id = r.survey_id AND s.tenant_id = @tenantId
                JOIN event_ticket_purchase t ON t.id = r.event_ticket_purchase_id AND t.tenant_id = @tenantId
                WHERE r.event_ticket_purchase_id = ANY(@ids)";
            return (await _db.Query<Guid>(sql, new { tenantId, ids = ticketIds.Distinct().ToArray() })).ToList();
        }

        public async Task<List<TicketAssessmentAnswer>> ListAnswers(Guid tenantId, IReadOnlyCollection<Guid> ticketIds)
        {
            if (ticketIds.Count == 0) return new List<TicketAssessmentAnswer>();
            const string sql = @"
                SELECT t.id AS TicketId, r.id AS ResponseId, s.id AS SurveyId, s.title AS SurveyTitle,
                       r.submitted_at_utc AS SubmittedAtUtc,
                       t.rider_first_name AS RiderFirstName, t.rider_last_name AS RiderLastName,
                       q.id AS QuestionId, q.prompt AS Prompt, q.kind AS Kind,
                       COALESCE(q.sort_order, 0) AS QuestionSortOrder,
                       c.label AS ChoiceLabel, COALESCE(c.sort_order, 0) AS ChoiceSortOrder,
                       a.free_text AS FreeText
                FROM survey_response r
                JOIN survey s ON s.id = r.survey_id AND s.tenant_id = @tenantId
                JOIN event_ticket_purchase t ON t.id = r.event_ticket_purchase_id AND t.tenant_id = @tenantId
                LEFT JOIN survey_answer a ON a.response_id = r.id
                LEFT JOIN survey_question q ON q.id = a.question_id AND q.survey_id = s.id
                LEFT JOIN survey_question_choice c ON c.id = a.choice_id AND c.question_id = q.id
                WHERE r.event_ticket_purchase_id = ANY(@ids)
                ORDER BY t.id, q.sort_order NULLS LAST, q.id, c.sort_order NULLS LAST";
            return (await _db.Query<TicketAssessmentAnswer>(sql, new { tenantId, ids = ticketIds.Distinct().ToArray() })).ToList();
        }
    }
}
