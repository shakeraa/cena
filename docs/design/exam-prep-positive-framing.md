# Exam-prep positive-framing reference copy

**Status**: Reference artefact — governed by [ADR-0048 (exam-prep time framing)](../adr/0048-exam-prep-time-framing.md).
**Audience**: Designers, copywriters, and engineers proposing any new exam-prep surface (student home, session, progress, parent surface).
**Pair with**: [`scripts/shipgate/banned-mechanics.yml`](../../scripts/shipgate/banned-mechanics.yml) — the CI-enforced rule pack that catches the banned patterns at PR time.

This document pairs anti-pattern copy (time-pressure mechanic, banned by CI) with target copy (time-awareness, allowed). Each pair includes the rationale so the reviewer can argue the call-site in a PR without re-deriving it.

All math examples are wrapped in `<bdi dir="ltr">` when embedded inside RTL (he/ar) pages — see `feedback_math_always_ltr.md` and the standing rule: KaTeX/equations render left-to-right even inside right-to-left flows.

---

## Anchor principle

**Time-awareness (ALLOWED): the student is informed about time. Time-pressure (BANNED): time is mobilised to coerce action.**

Six-axis test (see ADR-0048 for the full matrix):

1. Is the number static (ALLOWED) or a live-ticking counter (BANNED)?
2. Is the frame progress-based (ALLOWED) or deadline-based (BANNED)?
3. Is the styling neutral (ALLOWED) or red/amber/pulsing (BANNED)?
4. Is the voice honest-and-specific (ALLOWED) or urgent/coercive (BANNED)?
5. Is the affect informational (ALLOWED) or anxiety-inducing by design (BANNED)?
6. Does the intensity track mastery (ALLOWED) or the deadline (BANNED)?

One "BANNED" axis flips the whole surface.

---

## 1. Home-surface readiness widget

**Anti-pattern copy (BANNED)**

> Crisis Mode active — Bagrut in 42 days! You are 18% below trajectory. Tap to enter focus mode now!

**Target copy (ALLOWED)**

> Your Bagrut is on 2026-05-14. Your current accuracy on Unit 3 is <bdi dir="ltr">72%</bdi> (95% CI <bdi dir="ltr">68–76%</bdi>) over 180 attempts. 2 of 8 Unit 3 sub-skills mastered. Keep practising.

**Why**

- Anti-pattern has a red-tier label ("Crisis"), a countdown, scarcity framing, and a call-to-action coupled to the deadline.
- Target reports the exam date as a plain fact, the mastery number honestly with CI (R-28 posture), and invites continued practice without coercion. No countdown, no urgency colour, no scarcity language.
- Passes every rule in `banned-mechanics.yml`: no `crisis`, no `countdown`, no `days remaining`, no `last chance`, no `hurry`, no `only N left`.

---

## 2. Session-end performance card

**Anti-pattern copy (BANNED)**

> You ran out of time on 3 questions. Only 7 days left to master this topic — last chance before Bagrut!

**Target copy (ALLOWED)**

> You worked through 12 questions. 9 correct, 3 needed a hint. Your Unit 5 accuracy moved from <bdi dir="ltr">64%</bdi> to <bdi dir="ltr">69%</bdi> (95% CI <bdi dir="ltr">62–76%</bdi>). Next session suggestion: one more 20-minute Unit 5 drill, then move to Unit 6.

**Why**

- Anti-pattern hits three bans: `run out of time`, `only N left`, `last chance`.
- Target reports what happened, how the mastery number moved (honest + CI), and proposes a specific next action tied to the mastery signal — not to the deadline. "Next session suggestion" is progress-based, not deadline-based.

---

## 3. Streak-style "engagement" replacement

**Anti-pattern copy (BANNED)**

> Don't lose your 14-day streak! Keep your streak going with a quick review session.

**Target copy (ALLOWED)**

> 4 of 5 planned sessions this week. 3 review items are ready — tap to practise.

**Why**

- Anti-pattern is a classic GD-004 streak-loss mechanic. Rejected per `docs/engineering/shipgate.md` and CLAUDE.md non-negotiable #3.
- Target is the Apple-Fitness-rings-style positive-frame cadence signal explicitly allowed by the ship-gate policy: shows progress against a plan the student set, never punishes for a missed day, never uses a loss-aversion verb.
- Note: the Cena codebase's `src/student/full-version/src/plugins/fake-api/handlers/student-notifications/index.ts` was historically the one hit for "Keep your streak going"; it has already been rewritten to the positive-frame copy above.

---

## 4. Parent surface — the one legitimate date-rendering case (opt-in only)

**Anti-pattern copy (BANNED — even on parent surface)**

> 62 days until your child's Bagrut. They are behind trajectory — urgent action required!

**Target copy (ALLOWED — only if parent has opted in per ADR-0048 §Per-family informational opt-in)**

> Bagrut exam on 2026-05-14. Your child's last week's practice cadence was 4 of 5 planned sessions. Mastery across the 3 active Unit focus areas: <bdi dir="ltr">68% / 71% / 82%</bdi> (95% CI <bdi dir="ltr">±5%</bdi> each).

**Why**

- Anti-pattern uses `N days until` + coercive "urgent action required" copy.
- Target uses date-as-fact, reports the honest cadence + mastery numbers, and leaves action to the parent's judgement. Never styled with alert colours; parent can hide the surface at any time.
- **Student surface still never shows this.** The opt-in flows only to the parent surface. Students see the home-surface widget from §1.

---

## 5. Accommodations surface tone (already adopted — ADR-0040)

**Target copy (reference for other surfaces)**

> Extended-time accommodation active: +25% per question. Large-print paper scheduled for Bagrut day. Calculator type approved: scientific (non-graphing).

**Why this is the tone target**

ADR-0040 already locked the accommodations tone as "honest + specific + supportive." Other exam-prep surfaces should match this voice: concrete, short, factual, respectful. No cheering, no alarms, no nicknaming the student's situation ("Crisis Mode", "Champion Mode", etc.).

---

## 6. Multi-locale variants

Every permitted copy sample above has equivalent target phrasings in Hebrew and Arabic. **Hebrew and Arabic numbers and math expressions are wrapped in `<bdi dir="ltr">…</bdi>`** so percentages, dates, and equations render LTR inside the RTL page flow.

### Hebrew target (§1 example)

> הבגרות שלך ב-<bdi dir="ltr">2026-05-14</bdi>. הדיוק הנוכחי שלך ביחידה 3 הוא <bdi dir="ltr">72%</bdi> (רווח סמך <bdi dir="ltr">95% CI 68–76%</bdi>) על פני 180 ניסיונות. <bdi dir="ltr">2 מתוך 8</bdi> תת-מיומנויות של יחידה 3 נרכשו. המשך לתרגל.

- Must NOT contain: `ספירה לאחור`, `ימים שנותרו`, `ימים עד`, `הזדמנות אחרונה`, `הזמן אוזל`, `מהרו`, `מהרי`, `נותרו רק N`, `מצב משבר`, `ציון הבגרות החזוי`.

### Arabic target (§1 example)

> بجروتك في <bdi dir="ltr">2026-05-14</bdi>. دقتك الحالية في الوحدة 3 هي <bdi dir="ltr">72%</bdi> (فاصل ثقة <bdi dir="ltr">95% CI 68–76%</bdi>) على مدى 180 محاولة. <bdi dir="ltr">2 من 8</bdi> مهارات الوحدة 3 تم إتقانها. استمر في التدرب.

- Must NOT contain: `العد التنازلي`, `الأيام المتبقية`, `الفرصة الأخيرة`, `الوقت ينفد`, `أسرع قبل…`, `فقط N يوم`, `وضع الأزمة`, `علامة البجروت المتوقعة`.
- The adverbial `بسرعة` ("with speed/velocity") which appears legitimately in physics content is NOT banned — see `banned-mechanics.yml` `ar-hurry` rule comment.

---

## 7. How to ship a new exam-prep surface

1. Draft the copy.
2. Check against §1–§6 above: which pattern does it match? Is it the target or anti-pattern side?
3. Run `node scripts/shipgate/rulepack-scan.mjs --pack=mechanics` locally against your branch.
4. If CI flags something you believe is legitimate (e.g. a physics question that contains `running` in the sense of "running a program"), propose a whitelist entry with a one-line justification in `scripts/shipgate/banned-mechanics-whitelist.yml` — reviewed per-PR.
5. If in doubt on a boundary case, read [ADR-0048](../adr/0048-exam-prep-time-framing.md) — it has the six-axis test and the alternatives that were considered and rejected.

---

## 8. Change log

- **2026-04-20** — initial authoritative pairs (prr-006). Six worked examples covering home, session, streak-replacement, parent, accommodations tone, and multi-locale. Cross-linked from ADR-0048 and whitelisted in `banned-mechanics-whitelist.yml`.
