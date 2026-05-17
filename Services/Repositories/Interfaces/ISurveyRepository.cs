using Services.Repositories.Data.SurveyData;

namespace Services.Repositories.Interfaces
{
    public interface ISurveyRepository
    {
        // Survey CRUD
        Task<Guid> CreateSurvey(Survey s);
        Task UpdateSurvey(Guid id, Guid tenantId, string name, string title, string? description,
            DateTime? closesAtUtc, bool requireEmail);
        Task UpdateStatus(Guid id, Guid tenantId, string status);
        Task<Survey?> GetById(Guid id, Guid tenantId);
        Task<Survey?> GetByPublicToken(Guid publicToken, Guid tenantId);
        Task<List<Survey>> ListByTenant(Guid tenantId);

        // Questions + choices
        Task<List<SurveyQuestion>> ListQuestions(Guid surveyId);
        Task<Dictionary<Guid, List<SurveyQuestionChoice>>> ListChoicesForQuestions(IEnumerable<Guid> questionIds);
        Task<Guid> CreateQuestion(SurveyQuestion q);
        Task UpdateQuestion(Guid id, string prompt, int sortOrder, bool required);
        Task DeleteQuestion(Guid id);
        Task<SurveyQuestion?> GetQuestion(Guid id);
        Task<Guid> CreateChoice(SurveyQuestionChoice c);
        Task UpdateChoice(Guid id, string label, int sortOrder, bool allowsFreeText);
        Task DeleteChoice(Guid id);
        Task ReplaceChoices(Guid questionId, IEnumerable<(string Label, int SortOrder, bool AllowsFreeText)> choices);

        /// <summary>Atomic bulk update of sort_order for many questions within one survey.</summary>
        Task UpdateQuestionSortOrders(Guid surveyId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);
        /// <summary>Atomic bulk update of sort_order for many choices within one question.</summary>
        Task UpdateChoiceSortOrders(Guid questionId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);

        // Invites
        Task<Guid> CreateInvite(SurveyInvite invite);
        Task<SurveyInvite?> GetInviteById(Guid id);
        Task<SurveyInvite?> GetInviteByToken(Guid token);
        Task MarkInviteSent(Guid id, DateTime sentAtUtc);
        Task MarkInviteOpened(Guid id, DateTime openedAtUtc);
        Task MarkInviteCompleted(Guid id, DateTime completedAtUtc);
        Task<List<SurveyInvite>> ListInvitesForSurvey(Guid surveyId);

        // Responses + answers
        Task<Guid> CreateResponse(SurveyResponse r);
        Task<Guid> CreateAnswer(SurveyAnswer a);
        Task<List<SurveyResponse>> ListResponsesForSurvey(Guid surveyId);
        Task<List<SurveyAnswer>> ListAnswersForSurvey(Guid surveyId);

        // Audience resolution — used by the Send Invites flow. Each returns a
        // distinct lower(email) list. Filtered to successful purchases only
        // (status IN paid/redeemed) so unfinished checkouts don't get blasted.
        Task<List<string>> AudienceEventPurchasers(Guid tenantId, Guid eventId);
        Task<List<string>> AudiencePurchasersInRange(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<List<string>> AudienceAllCustomers(Guid tenantId);
        Task<List<string>> AudienceSubscribers(Guid tenantId);
    }
}
