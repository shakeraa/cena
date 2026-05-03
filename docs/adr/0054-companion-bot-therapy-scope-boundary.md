# ADR-0054 — Companion-bot / therapy-scope boundary

- **Status**: Accepted
- **Date**: 2026-04-21
- **Decision Makers**: Shaker (project owner), persona-ethics, persona-redteam
- **Task**: prr-043
- **Related**: [ADR-0003](0003-misconception-session-scope.md) (session scope),
  [ADR-0037](0037-affective-signal-in-pedagogy.md) (affective signal),
  [ADR-0041](0041-parent-auth-role-age-bands.md) (age bands / COPPA),
  [ADR-0047](0047-no-pii-in-llm-prompts.md) (LLM prompt safety),
  [prr-073 therapeutic-claims shipgate rule](../../scripts/shipgate/assistant-therapeutic-claims.yml)

---

## Context

Cena ships an assistant surface — the Sidekick / tutor — that talks to
students in natural language. Students in the target population (ages 13–18,
IL Bagrut + general high school) will, unsurprisingly, raise emotional
material in those conversations:

- "I'm stressed about the exam"
- "I can't focus today"
- "My mom yelled at me for my grade"
- "I think I'm bad at math"
- (lower-frequency but real) crisis-keyword signals: self-harm, suicidal
  ideation, abuse disclosure

prr-073 (already shipped) ships a **shipgate scanner for therapeutic-claim
copy** — the rulepack at
`scripts/shipgate/assistant-therapeutic-claims.yml` blocks strings like "I
understand your anxiety" from reaching production. That is the *copy layer*
guardrail. This ADR is the *scope layer* guardrail: it locks **what Cena as
a product does and does not do** when emotional material shows up, so every
future contributor has a single document to cite for the boundary.

### What could go wrong without this boundary

1. **Clinical-liability drift.** A teammate ships a "wellness bot" tab that
   offers "coping strategies for anxiety". We are now operating an
   unlicensed mental-health service for minors. FTC, Israel Ministry of
   Health, and every school's counsel agree this is a shipstopper.
2. **Detection-without-safeguarding.** The model notices distress, surfaces
   a "hang in there :)" message, and nothing else happens. A child in
   crisis was detected and then ignored — worst-of-both-worlds.
3. **Therapy framing in UX copy.** "Cena Companion is here to support your
   emotional well-being" appears on the home page. This is a therapeutic
   claim regardless of what the back-end actually does.
4. **Retention of emotional logs.** Distress signals accumulate on the
   student profile. A parent FOIA-style request reveals we have been
   cataloguing their child's emotional state for months with no consent,
   no purpose, no deletion schedule.

## Decision

### Principle

**Cena is an academic support product, not a therapy, counselling, or
mental-health product.** The assistant may acknowledge emotional content in
a human manner, MUST NOT pretend to treat it, and MUST hand off any signal
that crosses the safeguarding threshold to a real human in the student's
support network.

### What the assistant MAY do

1. **Acknowledge in a limited, non-clinical way.**
   - "That sounds like a rough moment. Want to pick an easier warm-up?"
   - "I noticed this is the third question in a row like this — want to
     switch topics for a bit?"
   - Neutral acknowledgement + an academic action. No diagnosis, no
     emotion-labelling.
2. **Offer a one-tap route to in-product support that is not therapy.**
   - "Take a 5-minute break" (pauses the session timer)
   - "Try an easier starter question"
   - "Come back to this tomorrow" (saves progress)
3. **Surface school / parent contact, when appropriate.**
   - "Your teacher Ms. X is on Cena, want to send a note?" (opt-in, never
     automatic)
4. **Fire the safeguarding-handoff flow if a crisis keyword matches.** See
   below.

### What the assistant MUST NOT do

1. **Clinical framing or diagnosis vocabulary.**
   - No "anxiety", "depression", "ADHD", "dyslexia", "autism" as ascribed
     to the student, in any language, in any surface. prr-073 scanner
     enforces.
2. **Therapeutic-claim assertions of emotional state.**
   - "I understand your anxiety" — banned (prr-073).
   - "You're feeling overwhelmed" — banned (prr-073).
   - "Let me help you process this" — banned (addition; see rulepack
     update below).
3. **Ongoing emotional-state tracking on the student profile.**
   - Distress signals are **session-scoped** ([ADR-0003](0003-misconception-session-scope.md)
     treatment), never written to `StudentActor` state, never included in
     ML training corpora, 30-day retention max.
4. **Parent digest of emotional content.**
   - Parent digest ([ADR-0041](0041-parent-auth-role-age-bands.md)) covers
     academic progress. Emotional disclosure in an assistant chat is NOT
     part of parent digest by default. If and only if the student self-
     discloses crisis material, the safeguarding flow (below) applies.
5. **"Companion" or "wellness" product positioning in UI copy.**
   - The product name is "Cena"; the assistant is "Sidekick" /
     "study-helper". Not "companion", not "coach", not "therapist", not
     "mental-health helper". The shipgate scanner's
     `positive-framing-extended.yml` will be extended to block these
     product-positioning strings.
6. **Framing the assistant as "always there for you" / "understands you".**
   - These map to loneliness-exploitation dark patterns (FTC v. Epic
     2022 precedent applied by analogy). Banned regardless of literal
     phrasing.

### Crisis-keyword handoff (safeguarding)

If a student message matches the crisis-keyword classifier:

| Tier | Signal | Assistant response | Side effect |
|---|---|---|---|
| **0: not-crisis** | Normal academic / mild frustration | Normal academic answer | None |
| **1: affective-signal** | "stressed", "can't focus", "anxious" (self-descriptive) | Neutral acknowledge + academic action (break / warm-up) | Session-scoped affective signal (ADR-0037) |
| **2: safeguarding-watch** | Persistent distress, repeated tier-1 in one session | Add "Would you like to talk to someone?" card with school counsellor contact | Teacher is notified via parent-digest-adjacent "student flagged for check-in" queue; opt-in for schools |
| **3: immediate-crisis** | Explicit self-harm / suicide / abuse language | Assistant IMMEDIATELY switches to a crisis-template message in the student's language with region-appropriate hotline numbers. Academic task paused, not abandoned. Screen is clearly different from the normal tutor UI. | Simultaneously: the assistant LLM call for this turn is suppressed (no free-form generation in a crisis turn — only the template); an incident is logged to the safeguarding queue with sanitised context (message hash, not verbatim); school safeguarding lead is paged via their configured channel |

**Tier-3 template non-negotiables**:

- Uses the student's UI language (he / ar / en).
- Lists **region-appropriate** hotline numbers (IL: ERAN ארן, Sahar סהר;
  general: Crisis Text Line US/UK/IL, international list).
- Says clearly: "I'm not a therapist. A person you trust can help right
  now. Here are real people to talk to right now."
- Does NOT end the session abruptly (shutting the screen on a child in
  crisis is worse). Offers a "stay here quietly" option.
- Does NOT claim Cena will "check on you later". We keep no emotional
  memory (ADR-0037).

**Escalation-path documentation**: every school's onboarding requires
naming a safeguarding lead (role + channel). If a school has not configured
one, tier-3 still fires the template but paging falls back to the school's
admin contact + the platform safeguarding ops rotation. This MUST be
documented at school onboarding; onboarding cannot complete without a
safeguarding-lead configuration.

### Data retention

- Tier-0: no retention of emotional signal (there is none).
- Tier-1: session-scoped affective signal per ADR-0037 (30 days, session-
  bounded, MlExcluded).
- Tier-2: structured event on the safeguarding queue — 90 days, school-
  admin readable. Used by counsellor for the check-in. NOT fed into any
  model training or personalisation.
- Tier-3: incident record — 2 years (regulatory retention for child-
  safeguarding in IL / EU / US minimum standards). Message **content is
  redacted**; only classification + timestamp + school + anonymised
  student pseudonym are stored. Verbatim message is discarded at tier-3
  detection.

### Parent notification policy

- Tier-1: **not** escalated to parents. Parent digest continues to cover
  academic progress only.
- Tier-2: school counsellor decides whether to involve parents; Cena does
  not auto-notify. This is a professional-judgement call and we refuse to
  automate it.
- Tier-3: safeguarding lead decides per school's safeguarding policy.
  Default is notify parents; overrides exist for cases where the parent is
  the source of the distress (abuse-disclosure subset). Cena does not
  auto-decide; the safeguarding lead has a dashboard to mark "notify" /
  "do not notify, reason on file".

### LLM-tutor context

The LLM tutor prompt context MUST NOT include:

- Prior emotional signal from other sessions
- Tier-1 / tier-2 / tier-3 classifications
- Parent-digest content
- Counsellor notes

This is the same rule as ADR-0037 §5 ("LLM tutor context — PROHIBITED" for
affective self-signal), reiterated for the crisis-adjacent surface.

## Rationale

### Why hard-line "not therapy"?

The regulatory environment around AI mental-health products is hostile and
correctly so. WHO 2024 guidance on AI in mental health is explicit: any
product that implies treatment must meet clinical-trial, licensing, and
safeguarding standards. Cena cannot meet those — we are an academic
product. The only safe lane is: we do not claim to, we do not act as if we
do, we hand off when emotional material crosses the safeguarding line.

### Why a tiered response instead of "always escalate"?

Always-escalating devalues the signal. A student who says "I'm stressed
about the exam" is almost always fine; treating it as a tier-3 incident
triggers paging, parent notification, school intervention, and the student
learns never to say that word. Tiering lets us act proportionally:
acknowledge at tier-1, watch at tier-2, escalate at tier-3.

### Why suppress LLM free-form generation at tier-3?

LLMs have produced harmful responses to crisis messages in published
evaluations. A deterministic template in the student's language with real
hotline numbers is strictly safer than any generated response, no matter
how well-prompted. This is a place where the "LLM explains, humans verify"
principle becomes "LLM does not explain in a crisis, template does".

### Why retain tier-3 for 2 years with redaction?

Regulatory safeguarding standards (IL, EU, US) expect records of crisis
disclosures for child-protection review. Redacting content while retaining
classification lets us comply with "we had X many tier-3 events in the
last year" audit questions without retaining the trauma text.

### 03:00 Bagrut morning failure runbook

Symptom: assistant produced a therapeutic-tone message that someone
screenshotted.

Steps:

1. `GET /api/admin/shipgate/scan-logs?rulepack=assistant-therapeutic-claims`
   — did the scanner miss? (pre-deploy gate should have blocked.)
2. If yes: the template wasn't in the rulepack. Add the rule, redeploy
   scanner, scan the repo for other violations of the new rule, open a
   PR.
3. If no: a free-form LLM output escaped the copy gate. Check LLM tier
   assignment; crisis-adjacent surfaces (tier-3) should never run
   free-form generation.
4. If a real crisis was misclassified: page the safeguarding ops
   rotation. Root-cause the classifier, not only the specific message.

## Consequences

### Positive

- A single document every future "should Cena have a wellness feature?"
  proposal can be evaluated against.
- Safeguarding-handoff path is mandatory at school onboarding, not an
  afterthought.
- Tier-3 template is deterministic, auditable, and culturally localised.
- Regulator-defensible: we do not claim therapy, we do not act as therapy,
  we have a handoff, we have retention policy, we have redaction.

### Negative

- Some pedagogy features that feel "helpful" will be scoped out
  (e.g. "help the student process their feelings about failure"). Those
  features become OUT OF SCOPE, full stop, not deferred.
- Tier-3 template requires per-region hotline catalogue maintenance; we
  own that refresh.

### Neutral

- Shipgate rulepack grows (new patterns for "companion", "wellness", "let
  me help you process").

## Implementation seams

- **Copy layer**: extend
  `scripts/shipgate/assistant-therapeutic-claims.yml` with
  "companion-positioning" + "wellness-positioning" rules (separate task).
- **Scope layer** (this ADR): assistant surface code must route messages
  through a `CrisisClassifier` before LLM call. Classifier output →
  tier-0…3.
- **Tier-3 template**: language-localised template set in
  `src/shared/Cena.Infrastructure/Safeguarding/CrisisTemplates/` —
  separate task, not in this ADR's scope.
- **Onboarding**: school onboarding flow must require safeguarding-lead
  config. Separate task (ties to school admin onboarding).
- **Retention**: safeguarding queue retention + redaction policy —
  separate task, uses event-sourcing RTBF primitives (ADR-0038).
- **Parent-digest policy** (ADR-0041) clarified: emotional content is
  never in the digest by default.

## Open items (deferred with task IDs)

- Hotline catalogue per region — separate task.
- Classifier accuracy evaluation — needs adversarial red-team before prod.
- Shipgate rulepack extension for "companion"/"wellness" — separate task.
- Safeguarding-lead onboarding UX — separate task.

## References

- WHO, *Ethics and governance of artificial intelligence for health: large
  multi-modal models*, 2024 (WHO guidance specifically calls out
  mental-health LLM products as high-risk).
- FTC v. Epic Games (2022) — $245M settlement, dark patterns against
  minors.
- Israel PPL Amendment 13 (in force) — data-protection rules for minors.
- Reid, Bernstein & Paruthi, *AI-augmented mental health tools in schools:
  harms catalogue*, 2025.
- Crisis hotlines: ERAN ארן (IL), Sahar סהר (IL), Crisis Text Line (global).
