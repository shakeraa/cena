# Ship-gate positive-test fixture: positive-framing-extended (EPIC-PRR-D tail)

**Purpose**: This file deliberately contains every pattern in
`scripts/shipgate/positive-framing-extended.yml`. The CI test suite
(`tests/shipgate/positive-framing-extended.spec.mjs`) runs the scanner against
this fixture and asserts that EVERY rule fires at least once.

**Do not clean this file.** Do not rephrase. Each line below is a trap.
This file is whitelisted in
`scripts/shipgate/positive-framing-extended-whitelist.yml`.

---

## prr-163 — cohort-context copy lockdown (BANNED)

- Your cohort is failing this term — catch up.
- Your class is behind the national cohort.
- The school is struggling this semester.
- The class has been falling behind on derivatives.
- Your school ranks 42nd in the district.
- The cohort is below average on Unit 3.
- Your class is under the benchmark this month.
- You're in the worst-performing class in the grade.
- Variant: lowest-performing cohort in the district.

## prr-166 — F3 rubric "lost points" framing (BANNED)

- You lost 4 points on step 3.
- Variant: you lost 2 points on the integration step.
- Points deducted for the missing constant of integration.
- Variant: points taken away on the algebra simplification.
- 3 points off for the sign error.
- That cost you a point on this rubric row.
- Variant: this approach will cost you 2 points.
- Your error here cost you points overall.

## prr-167 — F6 language-proficiency inference (BANNED)

- Your English proficiency is B2.
- Variant: student's English level is intermediate.
- Your Hebrew proficiency is advanced.
- Variant: the student's Hebrew fluency is native.
- Your Arabic proficiency is A2.
- Variant: the student's Arabic level is beginner.
- English-level B1 detected for this student.
- Hebrew-level intermediate detected.
- Arabic-level A2 detected.
- Your English is B2 grade.
- Your Arabic is C1 fluency.

## prr-168 — Focus-ritual "easier" mood-adjustment copy (BANNED)

- Here's an easier problem while you recover.
- Variant: here is an easier question to warm up with.
- Let's try something easier.
- Variant: let's try easier for a minute.
- Stepping down the difficulty for a moment.
- Variant: dialing back the difficulty.
- Variant: turning down the difficulty.
- This one will be easier for you.
- Variant: this problem is easier for you.

## prr-170 — Inflated interleaving effect-size claims (BANNED)

- Interleaving has d=0.7 on cumulative retention.
- Variant: interleaved practice shows d=0.8 in meta-analyses.
- Variant: interleaving effect size 0.8 in primary studies.
- Interleaved practice has a large effect on retention.
- Variant: interleaving has a transformative effect on learning.

## prr-171 — Journey path animation (BANNED)

- .journey-path { @keyframes glide-in { ... } }
- .journey-path { transition: all 0.3s ease; }
- .journey-path { animation: slide-up 0.4s ease-in; }

## prr-172 — Misconception-tag blame framing (BANNED)

- You made a mistake on the exponent rule.
- Variant: you made an error on simplification.
- Variant: you made the mistake of flipping the sign.
- You got it wrong on step 2.
- Variant: you got this wrong.
- Variant: you got the answer wrong.
- You were wrong about the derivative rule.
- Variant: you are wrong on this rubric row.
- Your error was applying the chain rule too early.
- Variant: your mistake was on step 3.

## prr-177 — Idle-pulse Stuck? Ask button (BANNED)

- .stuck-ask { @keyframes idle-pulse { ... } }
- #stuck-ask { animation: pulse 2s infinite; }
- .stuck-ask { pulse: 2s ease infinite; }

## prr-178 — "Challenge Round" session-type label (BANNED)

- Start a Challenge Round to test yourself.
- Variant: this week's Challenge Round is about derivatives.
- See this week's Challenge Rounds on the menu.

## Hebrew variants (verify with native-speaker team before enforcement)

- הכיתה שלך נכשלת השבוע.
- איבדת 4 נקודות על שלב 3.
- עשית טעות בכלל המעריכים.
- סיבוב אתגר חדש זמין עכשיו.

## Arabic variants (verify with native-speaker team before enforcement)

- صفك يفشل هذا الفصل.
- فقدت 4 نقاط على الخطوة 3.
- ارتكبت خطأ في قاعدة الأسس.
- جولة التحدي جديدة متاحة الآن.
