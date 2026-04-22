---
persona: redteam
subject: STUDENT-INPUT-MODALITIES-001
date: 2026-04-21
verdict: red (broad framing) / yellow (narrow framing)
---

## Summary

Q1 photo upload multiplies every unsafe-content and prompt-injection vector Cena owns. Q3 freeform text is a second injection mouth that did not exist before. Q2 hide-then-reveal is trivially bypassable if the API ever serves the full question+answer JSON regardless of UI state. Autoresearch iteration-02 proposed a 6-layer defense (ExifStripper → hardened prompt → JSON schema → LaTeX allowlist → dual-LLM → CAS). That architecture is **researched but not productized** (brief §2 table). Until every one of those layers is real code covered by property tests, the broad framing of Q1 is a ship-blocker. Narrow framing (diagnose-wrong-answer only, session-scoped, per-student hard rate cap) is yellow and shippable *if* the items below are all green.

## Section 7.8 answers

**Q1 OCR prompt-injection ("ignore previous instructions" handwritten on paper).** Current productized mitigations are thin: `PhotoUploadEndpoints.cs` has EXIF strip (PRR-001 — kills Category 2.2.1 metadata injection only), CSAM + AI-safety moderation (RDY-001), OCR Layers 0–5 (ADR-0033). What's **missing and load-bearing**: (a) Layer 1 image re-encode (JPEG quality 85) that destroys LSB + DCT stego — autoresearch recommends it, `ExifStripper` doesn't do it; verify. (b) Layer 2 hardened system prompt with per-session canary — not in the codebase as of 2026-04-21 (grep `canary_token` → research doc only). (c) Layer 3 Pydantic JSON-schema output enforcement for the vision model — not in LLM ACL today; Gemini Flash is called with free-text output. (d) Layer 4 LaTeX allowlist + injection-pattern regex (the `SUSPICIOUS_PATTERNS` list including Hebrew `התעלם` / Arabic `تجاهل`) — **does not exist in LLM-003** (verify: `src/llm-acl/src/cena_llm/sanitizer/injection_detector.py` has Unicode bidi but not the multilingual ignore-list). (e) CAS backstop only protects the math-rendering path — a freeform "show me your work" upload that extracts `surrounding_text` skips CAS entirely. **Residual risk is 3.5/6 layers short of the designed architecture.** Narrow framing makes this tolerable; broad framing makes it a data-exfil vector.

**Q1 CSAM-via-paper (drawn content).** Moderation model (RDY-001) is trained on photo-realistic CSAM. A pencil sketch of abusive content bypasses CLIP-style image classifiers at substantially higher rates (Thorn / IWF 2024 guidance: hand-drawn depictions drop classifier recall by 30–60 points depending on style). Cena has no text-captioning pre-moderation step that would describe the drawing in words for a second classifier. **Required mitigation**: dual-moderation — run the image through (1) the existing visual CSAM classifier AND (2) a vision-captioning model whose caption is then text-moderated. Anything that captions as depicting a minor in a sexual context gets hard-blocked + reported per Israeli 1998 Protection of Minors Law. Without the caption-then-text-moderate step, we will eventually ship a photo that our pipeline accepted because it was "a drawing."

**Q3 freeform text prompt-injection — rubric-grader isolation per PRR-022.** PRR-022 is `ban-PII-in-LLM-prompts` — that's a **PII scrubber, not an injection scrubber**. Those are different problems. Rubric grading for a Literature answer MUST follow the dual-LLM pattern from autoresearch iter-02 §4.6: student text goes to a quarantined grader that outputs only `{score: 0-5, rubric_hits: [ids]}`; the student NEVER sees grader free-text. Any path that lets the rubric grader emit explanatory prose back to the student re-opens PII-leak, misconception-leak, and injection-feedback vulnerabilities. **Current state**: rubric DSL exists (PRR-033), grader isolation does not. Ship-blocker for Q3 free-form humanities.

**Q2 hide-then-reveal bypass (scripted client).** The client-UI "hidden" state is cosmetic. If `GET /api/sessions/{id}/items/{k}` returns the full item payload including `choices[].is_correct`, `solution_rationale`, `step_hints[]` regardless of `mode=hidden`, then a scripted client (curl + auth cookie) fetches answers trivially. **Required**: the API must serve a **redacted item projection** while `attempt.mode == hidden_reveal` and `attempt.revealed_at == null`. Specifically strip `choices[].is_correct`, `solution_*`, `canonical_answer`, `step_hints`. Server then returns the full projection only on `POST /api/sessions/{id}/items/{k}/reveal` — an explicit event that is also what the UI animates off. If this is not enforced server-side this whole feature is pedagogy theater. Ship-blocker.

**Photo upload DoS / rate-limit evasion.** Endpoint has 20MB cap (good) and rate limit (unspecified in brief). Autoresearch iter-05 flagged this. Attacks: (a) burst 100 × 20MB uploads in 10s to exhaust vision-model quota / finops budget (ADR-0050 Q5 $3.30/student/month cap); (b) near-max-size images carefully crafted to be the slowest possible to OCR (high-entropy noise + many false text regions) — single-digit QPS costs $$$. Required: **per-student** cap (e.g. 5 photo-diagnoses/hour, 20/day), **per-session** cap (3/session), **per-tenant** cost ceiling that trips a circuit breaker, and PRR-233-style cost-per-request telemetry. Evasion vector today: student creates N sessions, each hits per-session cap but aggregate goes unbounded. Required: per-student wins over per-session.

## 2. Session replay (same photo, different session)

A student can re-upload byte-identical JPEG across sessions. Because misconception data is session-scoped (ADR-0003, 30-day retention), each re-upload is processed fresh — **no deduplication, no "we already saw this image" cache**. Abuse paths: (a) a successful prompt-injection image that worked once can be retried indefinitely across rotating sessions, defeating per-session canary (each session generates a new canary, so a recorded leak can't be "blacklisted" by hash alone); (b) cost-amplification (same image, N sessions, N vision-model calls). **Required**: perceptual-hash (pHash) the incoming image, keep a 30-day moderation-result cache keyed by `(tenant_id, pHash)`, reject / cheap-path images whose pHash has ever tripped moderation in the last 30d. This does NOT violate session-scoping because the cache stores moderation verdicts, not student-specific content. Also: stamp each photo with a per-tenant HMAC of `(student_id, timestamp, content_sha)` into the moderation log so replay across students is visible.

## 3. Section 8 position — Q1 framing

**Kill broad framing.** §8.1 Q1 framing: narrow-only. Reasons: (a) autoresearch designed the defense-in-depth assuming a constrained "diagnose this wrong answer" context — broad framing widens the attention surface by ~10x; (b) broad framing removes the conversational prior ("this should be math about problem X") that makes schema-enforcement meaningful; (c) cost ceiling — broad framing blows through ADR-0050 Q5 $3.30 cap at moderate use; (d) broad framing enables the multi-turn escalation attack (iter-02 §2.4) by design. Narrow framing reduces the attack from "open LLM tutor" to "diagnose-this-error" and lets us scope moderation/injection defenses accordingly. If product wants broad framing, it belongs in a v2 task after iter-02's layers 1–6 are all productized and fuzz-tested for a quarter.

## 4. Recommended new PRR tasks

- **PRR-NEW-SIM-A** (P0, M, redteam+backend): productize autoresearch iter-02 layers 1–6 as real code — image re-encode JPEG-85, per-session canary, Pydantic vision-output schema, LaTeX+multilingual-ignore-word allowlist, dual-LLM routing, CAS backstop wiring. File under `src/llm-acl/` and `Cena.Infrastructure/Security/`. No feature-flag for Q1 photo UX until this ships. Acceptance: all 10 iter-02 test cases green as integration tests.
- **PRR-NEW-SIM-B** (P0, S, redteam): multilingual prompt-injection regex suite — EN/HE/AR "ignore", "system prompt", "pretend", "you are now", "forget", Unicode bidi U+202E/U+202D, zero-width joiners. Add to `InjectionDetector` and use it on both photo `surrounding_text` and all Q3 freeform fields before LLM ingestion.
- **PRR-NEW-SIM-C** (P0, M, redteam+moderation): CSAM dual-moderation — image classifier + vision-caption-then-text-moderate. Add drawn/sketch dataset to classifier eval set; block publish until recall ≥ 95% on hand-drawn depictions. Runbook for Israeli-law reporting.
- **PRR-NEW-SIM-D** (P0, S, redteam+api): server-side redacted item projection while `hidden_reveal` mode active. Contract test: `GET items/{k}` with mode=hidden MUST NOT contain `is_correct`, `solution_*`, `canonical_answer`, `step_hints`. Reveal endpoint is a domain event, not a UI toggle.
- **PRR-NEW-SIM-E** (P0, S, redteam+finops): per-student photo rate-limit (5/hr, 20/day), per-session (3), per-tenant circuit-breaker tied to finops budget; return `429` with structured error code, not a generic "try again."
- **PRR-NEW-SIM-F** (P1, S, redteam+privacy): pHash replay cache — 30-day `(tenant, pHash) → moderation_verdict` table; reject repeats that previously tripped CSAM/injection; dedupe cost for benign repeats.
- **PRR-NEW-SIM-G** (P1, M, redteam): fuzz test suite — AFL/atheris-style fuzzing of the photo-upload pipeline (malformed JPEG, ICC-profile bombs, polyglot PDF/JPG, progressive-JPEG truncation, SVG disguised as PNG), and a corpus of 200 real-world prompt-injection photos (text-on-paper, EXIF, white-on-white print, bidi). Reuse the CSV-hardening pattern from PRR-021.
- **PRR-NEW-SIM-H** (P0, S, redteam+content-eng): rubric grader quarantine — dual-LLM split per autoresearch iter-02 §4.6 for Q3. Grader outputs structured `{score, rubric_hit_ids}` only; no grader free-text to student. Ship-blocker for Q3 humanities.
- **PRR-NEW-SIM-I** (P1, XS, redteam): shipgate scanner — grep CI for `v-html` on any new user-freeform field (mirrors PRR-NEW-F from multi-target findings); fail build on hit.

## 5. Blockers (non-negotiable ship-gates)

1. **Q1 broad framing off the table** until autoresearch iter-02 layers 1–6 are productized and covered by integration tests. Narrow framing is acceptable only alongside PRR-NEW-SIM-A/B/C/E all green.
2. **Hide-then-reveal server-side redaction** (PRR-NEW-SIM-D). Client-side hiding is not a security control. No ship without the redacted projection + contract test.
3. **CSAM dual-moderation** (PRR-NEW-SIM-C). Hand-drawn sketch recall ≥ 95% or the photo endpoint stays behind a feature flag.
4. **Rubric grader quarantine** (PRR-NEW-SIM-H). No ship of Q3 humanities free-form with a grader that can emit free-text to students.
5. **Photo rate-limit as per-student + per-tenant circuit-breaker** (PRR-NEW-SIM-E). Per-session alone is trivially evaded by session-rotation.
6. **Session replay pHash cache** (PRR-NEW-SIM-F) before broad framing ever ships (P1 if narrow-only).
7. **Multilingual injection regex** (PRR-NEW-SIM-B). Hebrew/Arabic coverage is required the day we ship to Israeli Bagrut students — ship-blocker for Q1 + Q3 both.

## Questions back to decision-holder

1. Confirm Q1 is narrow-only for v1. If product wants broad, it's a v2 epic, not a v1 task.
2. Is there existing productized code for autoresearch iter-02 layers 2–5 that I missed? If yes, point me at files so I can drop SIM-A's scope.
3. Q2 — which of options A/B/C is authoritative? The redacted projection (SIM-D) is required for all three, but the default behavior affects scheduler + UX.
4. Q3 humanities at Launch — if grader quarantine (SIM-H) slips, does humanities ship MC-only (§8 open-q 5)? My redteam recommendation: MC-only is safer than shipping unquarantined free-text grading.
5. CSAM hand-drawn-recall target — 95% my call, what's legal saying? Ministry reporting obligations may push it higher.
