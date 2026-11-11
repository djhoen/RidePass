using Services.Repositories.Data.SurveyData;
using Services.Repositories.Interfaces;
using Services.Surveys;
using webapi.Controllers.API.Data.LessonAssessment;
using webapi.Controllers.API.Data.Survey;

namespace webapi.Helpers
{
    /// <summary>
    /// Lesson self-assessment plumbing shared by checkout (PurchaseController) and the admin read endpoints
    /// (LessonAssessmentController), Script0318.
    /// </summary>
    public static class LessonAssessments
    {
        /// <summary>The survey the event's type asks, with its questions; null when it asks none (or has no questions).</summary>
        public static async Task<LoadedAssessment?> ForEvent(ILessonAssessmentRepository assessments,
            ISurveyRepository surveys, Guid eventId, Guid tenantId)
        {
            // The survey row is loaded tenant-scoped; its questions and choices are children of it.
            var survey = await assessments.GetSurveyForEvent(eventId, tenantId);
            if (survey is null) return null;
            var questions = await surveys.ListQuestions(survey.Id);
            if (questions.Count == 0) return null;
            var choices = await surveys.ListChoicesForQuestions(questions.Select(q => q.Id));
            return new LoadedAssessment(survey, questions, choices);
        }

        public static List<SurveyAnswerInput> ToInputs(IEnumerable<SubmitSurveyAnswer>? answers) =>
            (answers ?? Enumerable.Empty<SubmitSurveyAnswer>())
                .Select(a => new SurveyAnswerInput(a.QuestionId, a.ChoiceIds, a.FreeText))
                .ToList();

        /// <summary>Flattened answer rows grouped into one readable assessment per ticket, questions in order.</summary>
        public static List<TicketAssessmentDto> Group(IEnumerable<TicketAssessmentAnswer> rows)
        {
            return rows
                .GroupBy(r => r.TicketId)
                .Select(t =>
                {
                    var first = t.First();
                    var answers = t
                        .Where(r => r.QuestionId is not null)
                        .GroupBy(r => r.QuestionId!.Value)
                        .OrderBy(q => q.First().QuestionSortOrder).ThenBy(q => q.Key)
                        .Select(q =>
                        {
                            var picks = q.OrderBy(r => r.ChoiceSortOrder).ToList();
                            var parts = picks.Select(r =>
                                r.ChoiceLabel is null ? r.FreeText ?? string.Empty
                                : string.IsNullOrWhiteSpace(r.FreeText) ? r.ChoiceLabel
                                : $"{r.ChoiceLabel}: {r.FreeText}");
                            return new AssessmentAnswerDto
                            {
                                Prompt = picks[0].Prompt ?? string.Empty,
                                Answer = string.Join("; ", parts.Where(p => p.Length > 0)),
                            };
                        })
                        .ToList();
                    var name = $"{first.RiderFirstName} {first.RiderLastName}".Trim();
                    return new TicketAssessmentDto
                    {
                        TicketId = t.Key,
                        RiderName = name.Length > 0 ? name : null,
                        SurveyTitle = first.SurveyTitle,
                        SubmittedAtUtc = DateTime.SpecifyKind(first.SubmittedAtUtc, DateTimeKind.Utc),
                        Answers = answers,
                    };
                })
                .ToList();
        }
    }
}
