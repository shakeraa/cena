# Ship-gate positive-test fixture: banned mechanics

**Purpose**: This file deliberately contains every pattern in
`scripts/shipgate/banned-mechanics.yml`. The CI test suite
(`tests/shipgate/banned-mechanics.spec.mjs`) runs the scanner against this
fixture and asserts that EVERY rule fires at least once.

**Do not clean this file.** Do not rephrase. Each line below is a trap.
This file is whitelisted in
`scripts/shipgate/banned-mechanics-whitelist.yml`.

---

## prr-006 — Crisis Mode naming (REJECTED)

- Crisis Mode is a banned label.
- Variant: crisis-mode flag set to true.
- Variant: Crisis framing near the Bagrut exam is banned.
- Variant: Crisis tier red badge near the exam.

## prr-006 — Countdown framing (REJECTED)

- The Bagrut countdown widget is banned.
- Variant: exam-countdown card.
- Variant: study countdown timer.
- Variant: test countdown banner.
- Bare: countdown framing in any copy.
- Variant: 42 days remaining before the exam.
- Variant: 7 days until the test.
- Variant: 3 days left to prep.
- Urgency variant: you might run out of time.
- Urgency variant: time is running out for this unit.
- Urgency variant: we're almost out of time.
- Urgency variant: last chance to master this topic.

## GD-004 — Streak copy (HARD BAN)

- Practice streaks are banned.
- Variant: practice streak counter.
- Loss-aversion: keep your streak alive.
- Loss-aversion: don't break your streak.
- Loss-aversion: don't break the chain.

## prr-019 — Outcome-prediction copy (REJECTED)

- Predicted Bagrut score: banned label.
- Variant: predicted Bagrut based on mastery.
- Variant: predicted exam score widget.
- Variant: exam readiness score (banned outcome-prediction framing).

## Hebrew variants (verify with native-speaker team before enforcement)

- מצב משבר — Crisis Mode.
- ספירה לאחור — countdown.
- 5 ימים שנותרו — days remaining.
- 42 ימים עד הבגרות — days until Bagrut.
- הזדמנות אחרונה — last chance.
- הזמן אוזל — time is running out.
- ציון הבגרות החזוי — predicted Bagrut score.

## Arabic variants (verify with native-speaker team before enforcement)

- وضع الأزمة — Crisis Mode.
- العد التنازلي — countdown.
- الأيام المتبقية — days remaining.
- الفرصة الأخيرة — last chance.
- الوقت ينفد — time is running out.
- علامة البجروت المتوقعة — predicted Bagrut score.
