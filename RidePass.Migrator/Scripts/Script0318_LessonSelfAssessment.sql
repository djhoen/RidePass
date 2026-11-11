-- Rider self-assessment ("rider history") at lesson checkout.
--
-- Highland wants every lesson rider to answer a short questionnaire (how long have you ridden, which
-- trails, any injuries) so the coach can place them in the right group. The questionnaire is an ordinary
-- tenant survey (Script0076): admins already know how to build one, and the same question kinds render.
-- An admin picks the survey per event type (tenant_event_type.assessment_survey_id); every event of that
-- type then asks it once per rider ticket at online checkout. ON DELETE SET NULL: deleting the survey just
-- stops asking, it never blocks the delete or breaks the event type.
--
-- Answers reuse survey_response / survey_answer, so they also show in the survey's own results. A
-- response made at checkout names the ticket it belongs to (survey_response.event_ticket_purchase_id):
-- one response per ticket (a re-submitted registration replaces it; the unique index is the backstop),
-- and it goes when the ticket row goes. Tenant scope comes through survey.tenant_id and
-- event_ticket_purchase.tenant_id, both already per tenant; every read joins them.
--
-- Additive and nullable: existing rows keep NULL and behave exactly as before.
-- Rerunnable: ADD COLUMN / CREATE INDEX IF NOT EXISTS.

ALTER TABLE tenant_event_type
    ADD COLUMN IF NOT EXISTS assessment_survey_id uuid NULL REFERENCES survey(id) ON DELETE SET NULL;

ALTER TABLE survey_response
    ADD COLUMN IF NOT EXISTS event_ticket_purchase_id uuid NULL REFERENCES event_ticket_purchase(id) ON DELETE CASCADE;

CREATE UNIQUE INDEX IF NOT EXISTS uq_survey_response_ticket
    ON survey_response (event_ticket_purchase_id)
    WHERE event_ticket_purchase_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_tenant_event_type_assessment_survey
    ON tenant_event_type (tenant_id, assessment_survey_id)
    WHERE assessment_survey_id IS NOT NULL;
