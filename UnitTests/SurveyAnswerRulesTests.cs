using NUnit.Framework;
using Services.Repositories.Data.SurveyData;
using Services.Surveys;
using webapi.Helpers;

namespace UnitTests
{
    // Lesson self-assessment (Script0318): required questions, the stored answer rows, and the admin read-back.
    [TestFixture]
    public class SurveyAnswerRulesTests
    {
        private static readonly Guid QYears = Guid.NewGuid();     // single choice, required
        private static readonly Guid QTrails = Guid.NewGuid();    // multiple choice, optional, has "Other"
        private static readonly Guid QInjuries = Guid.NewGuid();  // free form, required
        private static readonly Guid CNew = Guid.NewGuid();
        private static readonly Guid CFew = Guid.NewGuid();
        private static readonly Guid CGreen = Guid.NewGuid();
        private static readonly Guid COther = Guid.NewGuid();

        private static List<SurveyQuestion> Questions() => new()
        {
            new SurveyQuestion { Id = QYears, Kind = "single_choice", Prompt = "How long have you ridden?", SortOrder = 1, Required = true },
            new SurveyQuestion { Id = QTrails, Kind = "multiple_choice", Prompt = "Which trails?", SortOrder = 2, Required = false },
            new SurveyQuestion { Id = QInjuries, Kind = "free_form", Prompt = "Any injuries?", SortOrder = 3, Required = true },
        };

        private static Dictionary<Guid, List<SurveyQuestionChoice>> Choices() => new()
        {
            [QYears] = new() { new() { Id = CNew, QuestionId = QYears, Label = "New" }, new() { Id = CFew, QuestionId = QYears, Label = "A few years" } },
            [QTrails] = new() { new() { Id = CGreen, QuestionId = QTrails, Label = "Green" },
                                new() { Id = COther, QuestionId = QTrails, Label = "Other", AllowsFreeText = true } },
        };

        [Test]
        public void All_required_answered_passes()
        {
            var answers = new List<SurveyAnswerInput>
            {
                new(QYears, new[] { CNew }, null),
                new(QInjuries, null, "None"),
            };
            Assert.That(SurveyAnswerRules.FirstUnanswered(Questions(), Choices(), answers), Is.Null);
        }

        [Test]
        public void Missing_required_choice_is_reported_first()
        {
            var answers = new List<SurveyAnswerInput> { new(QInjuries, null, "None") };
            Assert.That(SurveyAnswerRules.FirstUnanswered(Questions(), Choices(), answers)?.Id, Is.EqualTo(QYears));
        }

        [Test]
        public void Blank_free_text_does_not_answer_a_required_question()
        {
            var answers = new List<SurveyAnswerInput> { new(QYears, new[] { CNew }, null), new(QInjuries, null, "   ") };
            Assert.That(SurveyAnswerRules.FirstUnanswered(Questions(), Choices(), answers)?.Id, Is.EqualTo(QInjuries));
        }

        [Test]
        public void A_choice_from_another_question_does_not_count()
        {
            var answers = new List<SurveyAnswerInput> { new(QYears, new[] { CGreen }, null), new(QInjuries, null, "None") };
            Assert.That(SurveyAnswerRules.FirstUnanswered(Questions(), Choices(), answers)?.Id, Is.EqualTo(QYears));
        }

        [Test]
        public void Single_choice_keeps_one_pick()
        {
            var rows = SurveyAnswerRules.ToRows(Questions(), Choices(),
                new List<SurveyAnswerInput> { new(QYears, new[] { CNew, CFew }, null) });
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].ChoiceId, Is.EqualTo(CNew));
        }

        [Test]
        public void Other_text_is_kept_only_on_a_choice_that_allows_it()
        {
            var rows = SurveyAnswerRules.ToRows(Questions(), Choices(),
                new List<SurveyAnswerInput> { new(QTrails, new[] { CGreen, COther }, "Blue jumps") });
            Assert.That(rows.Single(r => r.ChoiceId == CGreen).FreeText, Is.Null);
            Assert.That(rows.Single(r => r.ChoiceId == COther).FreeText, Is.EqualTo("Blue jumps"));
        }

        [Test]
        public void Unknown_questions_are_dropped()
        {
            var rows = SurveyAnswerRules.ToRows(Questions(), Choices(),
                new List<SurveyAnswerInput> { new(Guid.NewGuid(), null, "stray") });
            Assert.That(rows, Is.Empty);
        }

        [Test]
        public void Free_text_is_trimmed_and_capped()
        {
            var rows = SurveyAnswerRules.ToRows(Questions(), Choices(),
                new List<SurveyAnswerInput> { new(QInjuries, null, "  " + new string('x', 5000) + "  ") });
            Assert.That(rows[0].FreeText!.Length, Is.EqualTo(SurveyAnswerRules.MaxFreeTextLength));
        }

        [Test]
        public void Admin_read_back_groups_per_ticket_in_question_order()
        {
            var ticket = Guid.NewGuid();
            var response = Guid.NewGuid();
            TicketAssessmentAnswer Row(Guid q, string prompt, int sort, string? label, int cSort, string? text) => new()
            {
                TicketId = ticket, ResponseId = response, SurveyTitle = "Rider history", SubmittedAtUtc = DateTime.UtcNow,
                RiderFirstName = "Sam", RiderLastName = "Boyd", QuestionId = q, Prompt = prompt, QuestionSortOrder = sort,
                ChoiceLabel = label, ChoiceSortOrder = cSort, FreeText = text,
            };
            var grouped = LessonAssessments.Group(new[]
            {
                Row(QInjuries, "Any injuries?", 3, null, 0, "Wrist"),
                Row(QTrails, "Which trails?", 2, "Other", 2, "Blue jumps"),
                Row(QTrails, "Which trails?", 2, "Green", 1, null),
                Row(QYears, "How long have you ridden?", 1, "New", 1, null),
            });
            Assert.That(grouped, Has.Count.EqualTo(1));
            var a = grouped[0];
            Assert.That(a.RiderName, Is.EqualTo("Sam Boyd"));
            Assert.That(a.Answers.Select(x => x.Prompt),
                Is.EqualTo(new[] { "How long have you ridden?", "Which trails?", "Any injuries?" }));
            Assert.That(a.Answers[1].Answer, Is.EqualTo("Green; Other: Blue jumps"));
            Assert.That(a.Answers[2].Answer, Is.EqualTo("Wrist"));
        }

        [Test]
        public void A_response_with_no_answers_still_reads_back()
        {
            var ticket = Guid.NewGuid();
            var grouped = LessonAssessments.Group(new[]
            {
                new TicketAssessmentAnswer { TicketId = ticket, SurveyTitle = "Rider history", SubmittedAtUtc = DateTime.UtcNow },
            });
            Assert.That(grouped.Single().Answers, Is.Empty);
        }
    }
}
