---
id: FIND-PRIVACY-008
task_id: t_8bba4bf4ad8f
severity: P0 — Critical
lens: privacy
tags: [reverify, privacy, GDPR, ICO-Children, processor, anthropic, safeguarding]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-privacy-008: Tutor sends child free-text to Anthropic with no DPA, no scrub, no safeguarding

## Summary

Tutor sends child free-text to Anthropic with no DPA, no scrub, no safeguarding

## Severity

**P0 — Critical**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

framework: GDPR (Art 28 processor agreements), ICO-Children (Std 9)
severity: P0 (critical)
lens: privacy
related_prior_finding: none

## Goal

Make the Anthropic Claude integration safe for use by minors:
1. PII-scrub student input before sending to api.anthropic.com
2. Add a safeguarding-relevant content classifier on student INPUT
3. Gate the entire tutor feature on consent.third_party_ai = true
4. Disclose Anthropic as a processor in the privacy policy
5. Limit tutor history retention to 90 days

## Background

`src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs:25-75` instantiates
AnthropicClient with apiKey from configuration and calls Messages.Create(...)
with the student's free-text prompt. The request body is sent unmodified to
api.anthropic.com (US-based controller).

`src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs:280-291` stores
the user's free-text input verbatim in TutorMessageDocument with no
redaction, no moderation, no PII scrubbing. The same content is then sent
to Anthropic.

`grep -rn 'PII\|redact\|sanitize\|moderate' src/actors/Cena.Actors/Tutor/`
returns only TutorSafetyGuard, which validates LLM **output**, not student
**input**.

This means a child can type their full name, school, address, phone number,
parent name, or a safeguarding-relevant disclosure (self-harm, abuse,
predatory contact) and that string ends up in:
- The local Marten database (TutorMessageDocument) — forever, because
  retention isn't enforced (FIND-privacy-004)
- Anthropic's logs (US-based third-party processor)

There is no DPA, no SCC, no consent gate, no scrubbing, and no escalation
path for safeguarding signals.

## Files

- `src/actors/Cena.Actors/Tutor/TutorPromptScrubber.cs` (NEW — scrub PII
  from outgoing prompts)
- `src/actors/Cena.Actors/Tutor/SafeguardingClassifier.cs` (NEW — classify
  student input for self-harm / abuse / predatory contact / off-topic
  disclosures BEFORE the LLM call)
- `src/actors/Cena.Actors/Tutor/SafeguardingEscalation.cs` (NEW — when the
  classifier fires, route to a human safeguarding queue, do NOT call
  Anthropic, do NOT echo the content back, surface a "let's talk to a
  trusted adult" UI response)
- `src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs` (use the scrubber)
- `src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs` (require
  `[RequiresConsent(ThirdPartyAi)]` from FIND-privacy-007)
- `src/shared/Cena.Infrastructure/Compliance/DataRetentionPolicy.cs` (add
  TutorMessageRetention = 90 days)
- `docs/legal/privacy-policy.md` (add Anthropic processor disclosure section
  — depends on FIND-privacy-002)
- `docs/legal/processor-agreements/anthropic-dpa.md` (NEW placeholder until
  the real DPA is signed)

## Definition of Done

1. PII scrubber strips phone numbers, addresses, last names, school names,
   parent names from outgoing prompts before sending to Anthropic.
   Replaces with `<redacted:contact>`, `<redacted:address>`, etc.
2. Safeguarding classifier runs BEFORE the Anthropic call. On a positive
   classification:
   - The Anthropic call is skipped
   - The student message is NOT stored in TutorMessageDocument
   - A SafeguardingAlert is created and routed to a moderation queue
   - The student sees a "Let's pause for a second and talk to someone you
     trust" response with a localized child-helpline number (different per
     market: NSPCC for UK, Childhelp for US, Eran for Israel)
3. /api/tutor endpoints decorated with `[RequiresConsent(ThirdPartyAi)]`.
   Without consent, the tutor feature is HIDDEN in the student UI (not
   present-but-broken).
4. TutorMessageRetention is 90 days, enforced by RetentionWorker
   (FIND-privacy-004).
5. Privacy policy section discloses Anthropic as a sub-processor with
   purpose, data categories, country, and DPA reference (FIND-privacy-002).
6. Pact test against a mock Anthropic asserting outbound payload contains
   zero substrings from a known-PII test fixture.
7. Auth test that /api/tutor returns 403 when consent.third_party_ai=false.
8. Safeguarding test: a fixture message containing "I want to hurt myself"
   triggers the classifier, suppresses the Anthropic call, and creates a
   SafeguardingAlert.

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-008-tutor-safeguarding`. Result must
include:

- the redaction patterns used
- the classifier threshold + the test fixtures
- a sample SafeguardingAlert document
- the per-market helpline mapping
- the Pact test result

## Out of scope

- Signing the actual Anthropic DPA (legal task, document the placeholder)
- Other LLM providers (Anthropic is the only one, per FIND-arch-005)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_8bba4bf4ad8f`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
