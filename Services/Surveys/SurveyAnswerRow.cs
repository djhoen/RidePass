namespace Services.Surveys
{
    /// <summary>One survey_answer row to write: a picked choice (with "Other" text when the choice allows it) or free text.</summary>
    public sealed record SurveyAnswerRow(Guid QuestionId, Guid? ChoiceId, string? FreeText);
}
