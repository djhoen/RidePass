# QA Test Plan: Surveys & Feedback

> Scope: tenant-built surveys (questions / choices / "Other" free-text, invites + send, public fill, results aggregation) and ad-hoc track feedback (public submit + admin moderation). Last updated: 2026-06-20.

## Surface map
- **Survey admin (`CampaignsManage`):** `SurveyController`: `GET/POST/PUT Admin`, `PUT Admin/{id}/Status`, question CRUD (`POST Admin/{surveyId}/Questions`, `PUT/DELETE Admin/Questions/{id}`, `POST .../Questions/Reorder`), choices (`PUT Admin/Questions/{id}/Choices`, `POST .../Choices/Reorder`), invites (`GET Admin/{id}/Invites`, `POST Admin/{id}/Send`), `GET Admin/{id}/Results`, `GET Admin/{id}/InvitePreview`, `POST Admin/{id}/Audience/Preview`.
- **Survey public (no auth, tenant subdomain):** `GET Public/{token}`, `POST Public/{token}/Submit`. Token = survey `public_token` (broad share) OR a per-recipient `survey_invite.token`.
- **Feedback:** `FeedbackController`: `POST Feedback` (public, no auth), `GET Feedback/Admin` + `PUT Feedback/Admin/{id}/Status` (both `SettingsManage`).
- **Repositories:** `Services/Repositories/SurveyRepository.cs`, `TrackFeedbackRepository.cs`.
- **Schema:** `Script0075_TrackFeedback.sql`; `Script0076_Surveys.sql` (survey / question / choice / invite / response / answer); `Script0077_SurveyOtherChoice.sql` (`allows_free_text`, relaxed answer CHECK).

## Concepts under test
- A **survey** has `status` (`draft`/`published`/`closed`), optional `closes_at_utc`, `require_email`, a unique `public_token`, and ordered `survey_question` rows. Question `kind` is `single_choice`, `multiple_choice`, or `free_form`.
- A **choice** with `allows_free_text=true` ("Other, please explain") stores BOTH `choice_id` and `free_text` on one answer row. `Script0077` relaxed the answer CHECK from "exactly one of (choice_id, free_text)" to "at least one is non-null".
- **Invites** are per-recipient (`survey_invite`, unique `(survey_id, lower(email))`), each with its own `token` and `sent`/`opened`/`completed` timestamps. Sending requires the survey be `published`. The survey-level `public_token` shares one link with no per-person tracking.
- **Submitting** validates required questions server-side and the answer shape per kind; `single_choice` is capped to one pick; orphan answers (unknown question id) are ignored. Email is required when `require_email` is set OR (for non-invite paths) there is no invite to identify the respondent.
- **Results** aggregate per question: choice counts + percent of total picks, free-text lists, and "Other" free-text under each flagged choice; plus invite funnel counts (sent / opened / completed).
- **Track feedback** is unsolicited: guest-friendly (`user_id` nullable), captures name + email on the row, optional 1-5 rating, body 1-4000 chars, status `new`/`addressed`/`dismissed`.

## Preconditions / test data
- A tenant with SMTP configured (so `POST Send` actually sends; otherwise every recipient is "skipped"). A second tenant for isolation.
- A draft survey with one of each question kind: a `single_choice` poll (3 choices, one flagged `allows_free_text`), a `multiple_choice` (4 choices), and a required `free_form`.
- An audience source for each `ResolveAudience` type: a custom email list, an event with paid purchasers, a date range, all-customers, and newsletter subscribers.
- A rider account (authenticated submit) plus a guest browser session.

---

## Admin (build + distribute + read results)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| SV1 [NN] | Create + edit a survey | POST a draft, PUT name/title/description/`closes_at_utc`/`require_email` | Saves as `draft`; reopen confirms; `public_token` returned and stable across edits. |
| SV2 [NN] | Add questions of each kind | POST `single_choice` (with choices), `multiple_choice`, `free_form` (`required=true`) | Choices created only for choice kinds; free_form ignores any choices. `GET Admin/{id}` returns questions ordered by `sort_order, id`. |
| SV3 [NN] | "Other" choice | Add a choice with `allows_free_text=true` | Persists the flag; surfaced in `GET Public` and `Results` choice DTOs. |
| SV4 [NN] | Replace choices | `PUT Admin/Questions/{id}/Choices` with a new set | Old choices deleted and replaced; blank-label choices dropped; sort orders renumbered (10, 20, ...). |
| SV5 [NN] | Reorder questions / choices | Reorder endpoints with new sort orders | Persisted; `UpdateQuestionSortOrders` is scoped by `survey_id` (a sibling survey's question id cannot be moved). |
| SV6 [NN] | Publish + send invites | Set status `published`; `POST Admin/{id}/Send` with a custom audience | Each valid, lowercased, deduped email gets one invite (upsert on `(survey_id, lower(email))`); response reports `Sent` + `Skipped`. Re-sending to the same email reuses the existing token (no duplicate invite). |
| SV7 [NN] | Cannot send while draft | `POST Send` on a draft/closed survey | Rejected ("Survey must be published before sending invites."). |
| SV8 [NN] | Audience resolution | `Audience/Preview` for each type (custom / event / timeframe / all_customers / subscribers) | Returns deduped count + sample; purchaser audiences include only `paid`/`redeemed` rows; subscribers excludes `unsubscribed_at` rows; empty audience on Send is rejected. |
| SV9 [NN] | Invite + audience preview | `GET InvitePreview`, `POST Audience/Preview` | Preview renders the exact subject/body with a throwaway token; audience preview never creates invites. |
| SV10 [NN] | Results aggregation | After several responses, `GET Admin/{id}/Results` | Per-choice counts + percent (of total picks) correct; `AnsweredCount` = distinct responses touching the question; "Other" free-text listed under its choice; free_form answers listed; invite funnel (sent/opened/completed) correct. |
| SV11 [NN] | Reopen clears stale close date | Set a past `closes_at_utc`, status `closed`, then re-publish | `UpdateStatus` nulls a past `closes_at_utc` on re-publish so the public gate stops rejecting; a future close date is preserved. |
| SV12 [R] | Permission gate | Survey admin endpoints without `CampaignsManage` | 403. |

---

## User (public fill)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| SV13 [NN] | Fill via invite token | Open `GET Public/{inviteToken}` | Survey loads; `opened_at_utc` stamped once (idempotent); `InviteEmail` pre-filled; `AlreadyCompleted` reflects prior completion. |
| SV14 [NN] | Fill via public token | Open `GET Public/{publicToken}` | Survey loads with no per-recipient tracking; tenant scope enforced in SQL (`GetByPublicToken`). |
| SV15 [NN] | Submit valid response | Answer all required questions and submit | `survey_response` + `survey_answer` rows created; invite path stamps `completed_at_utc`; returns response id. |
| SV16 [NN] | Required-question enforcement | Submit leaving a `required` question blank | Rejected ("Required question not answered: ..."). |
| SV17 [NN] | Email-required gate | On a `require_email` survey, submit via public token with no email | Rejected ("Email is required for this survey."). Invite path uses the invite email automatically. |
| SV18 [NN] | single_choice cap | Submit a single_choice question with 2+ choice ids | Server keeps only the first pick. |
| SV19 [NN] | "Other" free-text capture | Pick an `allows_free_text` choice and supply text | Answer row stores BOTH `choice_id` and `free_text`; non-"Other" picks store only `choice_id`; the free-text shows under that choice in Results. |
| SV20 [NN] | Orphan / closed / unpublished | Submit an answer with an unknown question id; submit to a `closed` or past-`closes_at` survey | Orphan answers silently ignored; closed/unpublished submit rejected ("not currently accepting responses" / "Survey is closed"). |
| SV21 [R] | Authenticated submit links user | Submit while logged in | `survey_response.user_id` set to the caller; still anonymous to other respondents. |

---

## Feedback

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| SV22 [NN] | Guest feedback submit | `POST Feedback` as a guest with name/email/rating/body | Row created with `status='new'`, `user_id` null, IP + truncated UA captured; rating optional (1-5). |
| SV23 [NN] | Signed-in feedback | Submit while logged in | `user_id` linked to the account. |
| SV24 [NN] | Body / rating bounds | Submit empty body; body > 4000 chars; rating 0 or 6 | Rejected by the DB CHECKs (body length 1-4000, rating 1-5). |
| SV25 [NN] | Admin list + filter | `GET Feedback/Admin` with `status=new`/`addressed`/`dismissed`, paging | Filtered + paged (limit clamped 1-200); `Total` reflects the filtered count. |
| SV26 [NN] | Moderate feedback | `PUT Feedback/Admin/{id}/Status` to `addressed` with notes | Status + `admin_notes` saved; `actioned_by_user_id` + `actioned_at_utc` stamped. |
| SV27 [R] | Permission gate | Feedback admin endpoints without `SettingsManage` | 403. Public submit needs only a resolved tenant. |

---

## Edge / cross-tenant

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| SV28 [NN] | Cross-tenant invite replay | On tenant 2's subdomain, open `GET Public/{token}` for a tenant-1 invite | 404 before any tracking is stamped: the invite resolves globally but the survey re-fetch (`GetById(surveyId, tenantId)`) is tenant-scoped and returns null. |
| SV29 [NN] | Cross-tenant public token | Use a tenant-1 `public_token` on tenant 2 | 404; `GetByPublicToken` includes `tenant_id` in the WHERE. |
| SV30 [NN] | Cross-tenant question edit | `PUT/DELETE Admin/Questions/{id}` for a question whose survey belongs to another tenant | 404; `EnsureQuestionInTenant` re-checks the parent survey against the resolved tenant before any update/delete. |
| SV31 [NN] | Duplicate / repeat responses | Submit twice with the same invite token | Both create new `survey_response` rows (no per-invite uniqueness); `completed_at_utc` is stamped only once. `AlreadyCompleted` is advisory, not enforced; flag whether re-submission should be blocked. |
| SV32 [NN] | Feedback spam | Hammer `POST Feedback` repeatedly as a guest | No rate-limit / captcha; only body-length cap. Confirm abuse mitigation expectations. |

---

## Known risks / watch-items
- **Repeat submissions not deduped** (SV31): a public/invite link can be submitted any number of times; results counts inflate. `AlreadyCompleted` only advises the UI.
- **Cross-tenant isolation depends on the survey re-fetch** (SV28/SV30): invite and question lookups are global by token/id; isolation is enforced only by the subsequent tenant-scoped `GetById`/`EnsureQuestionInTenant`. Any new code path that trusts the token directly would leak.
- **Send is all-or-nothing per email and depends on SMTP** (SV6): if the emailer is unconfigured, every recipient is "skipped" yet invites are still upserted (tokens created, never sent). Confirm the admin sees the skip count clearly.
- **Results percent base** (SV10): percent is over total *picks*, not total responses; for `multiple_choice` the percentages can exceed 100% across choices. Verify the UI labels this correctly.
- **No public-facing rate limiting** on feedback or anonymous survey submit (SV32): both accept unauthenticated writes scoped only to the resolved tenant.
- **No survey hard-delete endpoint**: surveys can be closed but not deleted via the API; confirm whether cleanup is expected.
