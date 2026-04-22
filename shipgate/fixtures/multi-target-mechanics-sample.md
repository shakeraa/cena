# Ship-gate positive-test fixture: multi-target mechanics (PRR-224)

**Purpose**: This file deliberately contains every pattern in
`scripts/shipgate/multi-target-mechanics.yml`. The CI test suite
(`tests/shipgate/multi-target-mechanics.spec.mjs`) runs the scanner against
this fixture and asserts that EVERY rule fires at least once.

**Do not clean this file.** Do not rephrase. Each line below is a trap.
This file is whitelisted in
`scripts/shipgate/multi-target-mechanics-whitelist.yml`.

---

## PRR-224 #1 — multi-target countdown/days-until framing (BANNED)

- Only 14 days until your Bagrut — review now!
- Variant: 30 days until the SAT.
- Variant: 42 days until your Psychometric exam.
- Variant: 7 days until Math Bagrut.
- Variant: 3 days until Physics exam.
- Urgency: only 5 days — Bagrut approaches!
- Urgency: only 12 days — SAT test window.
- Loss-aversion: don't miss your Bagrut!
- Loss-aversion: don't miss your SAT prep window.
- Loss-aversion: don't miss your PET.
- Loss-aversion: don't miss your Math Bagrut.

## PRR-224 #2 — per-target "falling behind" shaming (BANNED)

- You're falling behind in Physics — catch up before the exam.
- Variant: falling behind in Math compared to last week.
- Variant: you are falling behind in Bagrut Biology.
- Variant: falling behind in SAT prep.
- Variant: falling behind in Psychometric this month.
- Variant: you're behind on Physics.
- Variant: you are behind on Bagrut Math.
- Variant: you are behind in Chemistry this semester.
- Variant: you're behind in your Bagrut History.

## PRR-224 #3 — cross-target cohort-shaming (BANNED)

- Other students studying Physics are ahead of you.
- Variant: other students taking SAT are further along than you.
- Variant: other students preparing for PET are farther along.
- Cohort pressure: your cohort is ahead in Math Bagrut.
- Cohort pressure: your peers are ahead of you in Physics.
- Cohort pressure: your classmates are ahead in Chemistry.
- Pace-pressure: at this pace you won't finish your Bagrut in time.
- Pace-pressure: at this pace you will not complete Math Bagrut.
- Pace-pressure: at this pace you can't master Physics.
- Pace-pressure: at this pace you won't pass the SAT.

## PRR-224 #4 — identifier-name bans (code-shape traps)

```ts
// BANNED: countdown/days-until/days-left identifiers in production code
const daysUntilExam = 14               // ident-days-until
const days_until_bagrut = 30           // ident-days-until
const DAYS_UNTIL_SAT = 42              // ident-days-until
const daysLeftToStudy = 7              // ident-days-left
const days_left_for_bagrut = 3         // ident-days-left
const countdown = startCountdown()     // ident-countdown
const examCountdown = 14               // ident-countdown (matches \w*)
const count_down_timer = 10            // ident-countdown
const Countdown = class {}             // ident-countdown
const COUNTDOWN_SECONDS = 60           // ident-countdown
const streakCount = 5                  // ident-streak
const streak_count = 5                 // ident-streak
const StreakCount = 5                  // ident-streak
const dayStreakCount = 12              // ident-streak (matches day[Ss]treak\w*)
const day_streak = 7                   // ident-streak
const DayStreak = 7                    // ident-streak
const streakDays = 11                  // ident-streak
const streak_days = 11                 // ident-streak
const consecutiveSessions = 3          // ident-streak
const consecutive_sessions = 3         // ident-streak
const consecutiveDays = 4              // ident-streak
const consecutive_days = 4             // ident-streak
const deadlinePressure = 0.8           // ident-deadline-pressure
const deadline_pressure = 0.5          // ident-deadline-pressure
const urgencyLevel = "high"            // ident-deadline-pressure
const urgency_level = "high"           // ident-deadline-pressure
const urgencyScore = 9                 // ident-deadline-pressure
const urgency_score = 9                // ident-deadline-pressure
```

## Hebrew variants (verify with native-speaker team before enforcement)

- 14 ימים עד הבגרות — days until Bagrut.
- 30 ימים עד הפסיכומטרי — days until Psychometric.
- 7 ימים עד המבחן — days until exam.
- אל תחמיץ את הבגרות — don't miss your Bagrut.
- אל תפספס את המבחן — don't miss your exam.
- אל תפסיד את הפסיכומטרי — don't miss your Psychometric.
- אתה מפגר בפיזיקה — you're falling behind in Physics.
- אתם מפגרים במתמטיקה — you're falling behind in Math.
- את מפגרת בבגרות — you're falling behind in Bagrut.
- החברים מקדימים בפיזיקה — classmates are ahead in Physics.
- בני הכיתה לפניך בפסיכומטרי — peers are ahead of you in Psychometric.
- המחזור מקדימים בבגרות — cohort is ahead in Bagrut.

## Arabic variants (verify with native-speaker team before enforcement)

- 14 يوم حتى البجروت — days until Bagrut.
- 30 أيام على الامتحان — days until exam.
- 7 أيام قبل البسيخومتري — days until Psychometric.
- لا تفوّت بجروتك — don't miss your Bagrut.
- لا تفوت امتحانك — don't miss your exam.
- لا تخسر البجروت — don't miss Bagrut.
- أنت متأخر في الفيزياء — you are falling behind in Physics.
- انت متأخرة في الرياضيات — you are falling behind in Math (feminine).
- أنت متأخرون في البجروت — you are falling behind in Bagrut (plural).
- زملاؤك متقدمون في الفيزياء — peers are ahead in Physics.
- الطلاب الآخرون يسبقونك في الرياضيات — other students are ahead in Math.
- صفّك أمامك في البجروت — your class is ahead in Bagrut.

## PRR-264 — Hide-reveal timer / auto-hide bans

Identifier-level (BANNED):
const autoHideOptionsAfter = 10        // hide-reveal-auto-hide-identifier
const autoHideAfterSeconds = 8         // hide-reveal-auto-hide-identifier
const autoHideMs = 5000                // hide-reveal-auto-hide-identifier
const autoHideTimer = null             // hide-reveal-auto-hide-identifier
const optionsRevealTimer = 0           // hide-reveal-reveal-timer-identifier
const revealTimer = null               // hide-reveal-reveal-timer-identifier
const revealCountdown = 5              // hide-reveal-reveal-countdown-identifier
scheduledHideAt: Date.now()            // hide-reveal-scheduled-hide-identifier
scheduled_hide_at                      // hide-reveal-scheduled-hide-identifier

English copy (BANNED):
- Options disappear in 10 seconds — pick fast!
- Choices hide in 5 seconds if you do not answer.
- Answers vanish in 8 seconds.
- You have 10 seconds before the options hide.
- You have 12 seconds until the reveal locks.
- You have 6 seconds to pick before options vanish.
- Hurry before options hide forever!
- Hurry up or answers disappear.

Hebrew copy (BANNED — verify with reviewer):
- האפשרויות ייעלמו בעוד 10 שניות.
- התשובות נעלמות בעוד 5 שניות.
- יש לך 8 שניות לפני שהאפשרויות ייסגרו.

Arabic copy (BANNED — verify with native speaker):
- الخيارات ستختفي خلال 10 ثوان.
- الإجابات تختفي في 5 ثوان.
- لديك 8 ثوان قبل إخفاء الخيارات.
