using Services.Repositories.Data.SurveyData;

namespace Services.Surveys
{
    /// <summary>
    /// Pure rules for a set of answers to a survey: every required question answered, and the answers
    /// turned into the survey_answer rows to store (unknown questions and choices dropped, one pick for a
    /// single-choice question, "Other" text kept only on a choice that allows it). The same shapes the
    /// public survey page stores, so a lesson self-assessment (Script0318) reads like any other response.
    /// </summary>
    public static class SurveyAnswerRules
    {
        public const int MaxFreeTextLength = 4000;

        /// <summary>The first required question with no answer, or null when all are answered.</summary>
        public static SurveyQuestion? FirstUnanswered(IReadOnlyList<SurveyQuestion> questions,
            IReadOnlyDictionary<Guid, List<SurveyQuestionChoice>> choicesByQuestion,
            IReadOnlyList<SurveyAnswerInput> answers)
        {
            var rows = ToRows(questions, choicesByQuestion, answers);
            return questions
                .OrderBy(q => q.SortOrder).ThenBy(q => q.Id)
                .FirstOrDefault(q => q.Required && rows.All(r => r.QuestionId != q.Id));
        }

        /// <summary>The rows to store, in question order.</summary>
        public static List<SurveyAnswerRow> ToRows(IReadOnlyList<SurveyQuestion> questions,
            IReadOnlyDictionary<Guid, List<SurveyQuestionChoice>> choicesByQuestion,
            IReadOnlyList<SurveyAnswerInput> answers)
        {
            var rows = new List<SurveyAnswerRow>();
            foreach (var q in questions.OrderBy(q => q.SortOrder).ThenBy(q => q.Id))
            {
                var supplied = answers.FirstOrDefault(a => a.QuestionId == q.Id);
                if (supplied is null) continue;
                var text = Clean(supplied.FreeText);
                if (q.Kind == "free_form")
                {
                    if (text is not null) rows.Add(new SurveyAnswerRow(q.Id, null, text));
                    continue;
                }
                var choices = choicesByQuestion.TryGetValue(q.Id, out var cs) ? cs : new List<SurveyQuestionChoice>();
                var byId = choices.ToDictionary(c => c.Id);
                var picks = (supplied.ChoiceIds ?? Array.Empty<Guid>()).Where(byId.ContainsKey).Distinct().ToList();
                if (q.Kind == "single_choice" && picks.Count > 1) picks = picks.Take(1).ToList();
                foreach (var id in picks)
                    rows.Add(new SurveyAnswerRow(q.Id, id, byId[id].AllowsFreeText ? text : null));
            }
            return rows;
        }

        private static string? Clean(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var t = s.Trim();
            return t.Length > MaxFreeTextLength ? t[..MaxFreeTextLength] : t;
        }
    }
}
