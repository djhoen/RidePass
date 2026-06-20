# QA Results: Surveys & Feedback

Verified by static trace against current code (no live browser). Citations are file:line.

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| SV1 | PASS | Create saves as `draft` `SurveyController.cs:97`; `UpdateSurvey` updates name/title/description/closes_at/require_email and never touches `public_token` `SurveyRepository.cs:52-61`, so the token is stable across edits. |
| SV2 | PASS | Choices only created for choice kinds: `if ((Kind == single_choice \|\| multiple_choice) && Choices...)` `SurveyController.cs:148-154`; free_form ignores choices. `GetById`/`ListQuestions` order by `sort_order, id` `SurveyRepository.cs:111-114`. |
| SV3 | PASS | `allows_free_text` persisted via `ReplaceChoices` `SurveyRepository.cs:181-187`; surfaced in public + admin + results DTOs `SurveyController.cs:445,654,351`. |
| SV4 | PASS | `ReplaceChoices` deletes then re-inserts `SurveyRepository.cs:178-188`; blank labels dropped (`Where !IsNullOrWhiteSpace`) and sort renumbered `(i+1)*10` `SurveyController.cs:211-213`. |
| SV5 | PASS | `UpdateQuestionSortOrders` is scoped `WHERE q.id = data.id AND q.survey_id = @surveyId` `SurveyRepository.cs:196-201`; a sibling survey's question id can't be moved. Controller verifies the survey belongs to the tenant first `SurveyController.cs:183-184`. |
| SV6 | PASS | Send upserts on `(survey_id, lower(email))` `SurveyRepository.cs:230-235` returning the existing id on conflict (no duplicate token); response reports `Sent` + `Skipped` `SurveyController.cs:296-301`. Actual delivery requires SMTP (when unconfigured, every recipient is skipped but the invite/token still upserts `:283-293`). |
| SV7 | PASS | `survey.Status != "published"` -> "Survey must be published before sending invites." `SurveyController.cs:246-249`. |
| SV8 | PASS | `ResolveAudience` handles custom/event/timeframe/all_customers/subscribers `SurveyController.cs:609-619`; purchaser audiences filter `status IN ('paid','redeemed')` `SurveyRepository.cs:319,336,362,383`; subscribers exclude `unsubscribed_at IS NOT NULL` `:401`; empty audience on Send -> "Selected audience is empty." `SurveyController.cs:258-261`. |
| SV9 | PASS | `InvitePreview` renders subject/body with a throwaway `Guid.NewGuid()` token `SurveyController.cs:578`; `AudiencePreview` only resolves + samples, creates no invites `:595-600`. |
| SV10 | PASS | Per-choice count + percent of total picks `SurveyController.cs:341-350`; `AnsweredCount` = distinct response ids `:325`; Other free-text listed under its choice `:352-355`; free_form list `:361-364`; invite funnel sent/opened/completed `:376-378`. Percent base is total picks (can exceed 100% for multi-choice) - documented. |
| SV11 | PASS | `UpdateStatus` CASE nulls a past `closes_at_utc` on re-publish, preserves future `SurveyRepository.cs:68-77`. |
| SV12 | PASS | `[Authorize(Policy = CampaignsManage)]` on all admin survey endpoints `SurveyController.cs:45,76,86,106,121,...`. |
| SV13 | PASS | `MarkInviteOpened` is idempotent (`WHERE opened_at_utc IS NULL`) `SurveyRepository.cs:256-262`; `InviteEmail` prefilled and `AlreadyCompleted` set from invite `SurveyController.cs:434-436`. |
| SV14 | PASS | Public-token path uses `GetByPublicToken(token, tenantId)` with tenant in the WHERE `SurveyRepository.cs:93-97`. |
| SV15 | PASS | Creates `survey_response` + `survey_answer` rows `SurveyController.cs:503-553`; invite path stamps `completed_at_utc` `:555-558`; returns response id `:560`. |
| SV16 | PASS | Required loop: `if (q.Required && !hasAnswer)` -> "Required question not answered: ..." `SurveyController.cs:487-496`. |
| SV17 | PASS | `survey.RequireEmail && blank email` -> "Email is required for this survey." `SurveyController.cs:476-479`; invite path uses `invite.Email` `:500`. (Note: the concept's "OR no invite path requires email" is NOT separately enforced - only `RequireEmail` is checked - but SV17 only tests the RequireEmail case.) |
| SV18 | PASS | `if (q.Kind == "single_choice" && picks.Count > 1) picks = picks.Take(1)` `SurveyController.cs:534`. |
| SV19 | PASS | For `allows_free_text` picks the answer stores `ChoiceId` + `FreeText`; non-Other picks store only `ChoiceId` `SurveyController.cs:540-550`. |
| SV20 | PASS | Orphan answers ignored (`if (!qById.TryGetValue(...)) continue`) `SurveyController.cs:515`; closed/unpublished rejected "not currently accepting responses" / "Survey is closed" `:469-472`. |
| SV21 | PASS | `userId` parsed from claim and set on the response `SurveyController.cs:499,506`. |
| SV22 | PASS | Create inserts with no status (DB default `'new'` `Script0075:23`), `user_id` null for guest, IP + truncated UA captured `FeedbackController.cs:39-50`, `TrackFeedbackRepository.cs:20-28`. Rating optional 1-5. |
| SV23 | PASS | `userId` from claim linked when signed in `FeedbackController.cs:39,43`. |
| SV24 | PASS | DTO rejects first with 400: `Body` `MinLength(1),MaxLength(4000)` and `Rating` `Range(1,5)` `FeedbackDtos.cs:14-18`; DB CHECKs back it (`length(body) BETWEEN 1 AND 4000`, `rating BETWEEN 1 AND 5`) `Script0075:18-20`. |
| SV25 | PASS | `limit = Math.Clamp(limit,1,200)` `FeedbackController.cs:65`; status filter restricted to new/addressed/dismissed `:67`; `Total` from `CountByTenant(tenantId, statusFilter)` reflects the filtered count `:69`, `TrackFeedbackRepository.cs:51-55`. |
| SV26 | PASS | `UpdateStatus` sets status, admin_notes, `actioned_by_user_id`, `actioned_at_utc = now()` `TrackFeedbackRepository.cs:58-67`; controller passes the actioning user `FeedbackController.cs:88-90`. |
| SV27 | PASS | `[Authorize(Policy = SettingsManage)]` on admin endpoints `FeedbackController.cs:57,77`; public submit only requires resolved tenant `:35`. |
| SV28 | PASS | Invite resolves globally but `GetSurveyByIdAnyTenant` re-fetches via `GetById(surveyId, tenantId)` `SurveyController.cs:690-694`; null on tenant 2 -> 404 returned BEFORE `MarkInviteOpened` `:400-404`. No tracking stamped. |
| SV29 | PASS | `GetByPublicToken` includes `tenant_id` in WHERE `SurveyRepository.cs:93-97` -> 404 cross-tenant. |
| SV30 | PASS | `EnsureQuestionInTenant` loads the question then re-checks the parent survey against the tenant via `GetById(q.SurveyId, tenantId)` `SurveyController.cs:677-683`; guards every question update/delete/reorder/choices endpoint. |
| SV31 | PASS | No per-invite uniqueness: both submits create new `survey_response` rows; `MarkInviteCompleted` stamps only once (`WHERE completed_at_utc IS NULL`) `SurveyRepository.cs:264-269`. `AlreadyCompleted` is advisory only. Matches documented risk (re-submission not blocked). |
| SV32 | PASS | No rate limit / captcha on `POST Feedback`; only the DTO/DB body cap applies `FeedbackController.cs:32-53`. Confirms the documented no-mitigation expectation. |

No FAILs in this plan.
