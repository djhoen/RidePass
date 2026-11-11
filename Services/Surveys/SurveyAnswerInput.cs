namespace Services.Surveys
{
    /// <summary>What a respondent submitted for one question: picked choices and/or typed text.</summary>
    public sealed record SurveyAnswerInput(Guid QuestionId, IReadOnlyList<Guid>? ChoiceIds, string? FreeText);
}
