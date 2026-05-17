-- Surveys: support an "Other — please explain" choice that captures free-form
-- text alongside the picked choice.
--
-- Modelling: choices can opt into accepting free text via allows_free_text.
-- When a respondent picks such a choice, the answer row stores BOTH
-- choice_id (which Other-style choice they picked) AND free_text (their
-- explanation). This requires relaxing the original "exactly one of
-- (choice_id, free_text) is non-null" CHECK to "at least one is non-null".

ALTER TABLE survey_question_choice
    ADD COLUMN allows_free_text boolean NOT NULL DEFAULT false;

ALTER TABLE survey_answer DROP CONSTRAINT chk_answer_kind;
ALTER TABLE survey_answer
    ADD CONSTRAINT chk_answer_kind CHECK (
        choice_id IS NOT NULL OR free_text IS NOT NULL
    );
