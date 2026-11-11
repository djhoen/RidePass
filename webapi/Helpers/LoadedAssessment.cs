using Services.Repositories.Data.SurveyData;
using webapi.Controllers.API.Data.LessonAssessment;
using webapi.Controllers.API.Data.Survey;

namespace webapi.Helpers
{
    /// <summary>An event's self-assessment survey (Script0318) with its questions and choices, loaded once.</summary>
    public sealed record LoadedAssessment(Survey Survey, List<SurveyQuestion> Questions,
        Dictionary<Guid, List<SurveyQuestionChoice>> Choices)
    {
        /// <summary>What checkout renders: the questions, in order, with their choices.</summary>
        public LessonAssessmentSurveyResponse ToResponse() => new()
        {
            SurveyId = Survey.Id,
            Title = Survey.Title,
            Description = Survey.Description,
            Questions = Questions.OrderBy(q => q.SortOrder).ThenBy(q => q.Id).Select(q => new SurveyQuestionDto
            {
                Id = q.Id,
                Kind = q.Kind,
                Prompt = q.Prompt,
                SortOrder = q.SortOrder,
                Required = q.Required,
                Choices = Choices.TryGetValue(q.Id, out var cs)
                    ? cs.OrderBy(c => c.SortOrder).Select(c => new SurveyChoiceDto
                    {
                        Id = c.Id, Label = c.Label, SortOrder = c.SortOrder, AllowsFreeText = c.AllowsFreeText,
                    }).ToList()
                    : new List<SurveyChoiceDto>(),
            }).ToList(),
        };
    }
}
