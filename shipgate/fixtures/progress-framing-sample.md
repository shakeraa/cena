# Ship-gate positive-test fixture: progress framing / scores-as-identity (prr-091)

**Purpose**: This file deliberately contains every pattern in
`scripts/shipgate/progress-framing.yml`. The CI test suite
(`tests/shipgate/progress-framing.spec.mjs`) runs the scanner against this
fixture and asserts that EVERY rule fires at least once.

**Do not clean this file.** Do not rephrase. Each line below is a trap.
This file is whitelisted in
`scripts/shipgate/progress-framing-whitelist.yml`.

---

## prr-091 — scores-as-identity (English, BANNED)

- You're a B-level student based on your performance.
- Variant: you are an A-level student this week.
- Variant: you're a C+ level student overall.
- Your rank is 12 in the cohort.
- Variant: your ranking moved up two places.
- Variant: your class rank is 5th this week.
- Variant: your cohort rank dropped overnight.
- You are in the top 10% of your class.
- Variant: top 25 % of test-takers nationwide.
- You are #1 in your class this week.
- Variant: you are number one in the cohort today.
- Variant: no. 1 in class for the month.
- Check out the leaderboard to see where you stand.
- Variant: you've moved up on the leaderboards.
- Ranked 3 of 28 in your class.
- Variant: ranked 1 out of 30 in the unit.
- Beat your classmates on this week's challenge!
- Variant: beat your peers in the sprint.
- Variant: beat the class to the finish line.
- You're better than 85% of students in this unit.
- Variant: better than 70 % of classmates in mastery.

## prr-091 — uncalibrated progress numbers (BANNED without CI)

- Your accuracy is 73% right now.
- Variant: 82% accurate over the last week.
- Variant: 65% correct in this unit.
- Variant: 90% mastered overall.
- Mastery of 72% achieved on this topic.
- Variant: 54% mastered of Unit 3.
- Variant: mastery of 88% on Newton's laws.

## Hebrew variants (verify with native-speaker team before enforcement)

- הדירוג שלך עלה השבוע.
- 10% המובילים בכיתה.
- לוח המובילים מוצג כאן.
- טבלת הדירוג השבועית.

## Arabic variants (verify with native-speaker team before enforcement)

- ترتيبك في الصف الثالث.
- أفضل 10% في الوحدة.
- لوحة المتصدرين هذا الأسبوع.
- قائمة المتصدرين الشهرية.
