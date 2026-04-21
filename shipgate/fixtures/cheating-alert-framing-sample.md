# Ship-gate positive-test fixture: cheating-alert framing (prr-144)

**Purpose**: This file deliberately contains every pattern in
`scripts/shipgate/cheating-alert-framing.yml`. The CI test suite
(`tests/shipgate/cheating-alert-framing.spec.mjs`) runs the scanner against
this fixture and asserts that EVERY rule fires at least once.

**Do not clean this file.** Do not rephrase. Each line below is a trap.
This file is whitelisted in
`scripts/shipgate/cheating-alert-framing-whitelist.yml`.

---

## prr-144 — student-facing cheating copy (English, BANNED)

- Cheating detected on question 4.
- Variant: cheating flagged by the anti-cheat system.
- Variant: cheater detected!
- Variant: cheating alert on your session.
- Variant: cheating identified — take action.
- Variant: cheating suspected in this attempt.
- You are cheating on this unit. (bare-label near 'you')
- Variant: warning: cheating on your account.
- You have been flagged for academic integrity.
- Variant: flagged for an academic integrity violation.
- Variant: flagged for academic integrity review.
- Plagiarism alert on your submission.
- Variant: plagiarism detected in your attempt.
- Variant: plagiarism flagged by the reviewer.
- Variant: plagiarized warning.
- Honor code violation recorded.
- Variant: honor code breach detected.
- Variant: honour code infraction logged.
- Academic dishonesty suspected on this session.
- Variant: academic dishonestly logged.
- You cheated on the last question.
- Variant: you copied from another student.
- Variant: you plagiarized the answer.
- Variant: you were caught sharing answers.
- You copied from a classmate on question 7.
- Variant: copied answers from a peer.
- Variant: copied from another student in your cohort.
- Variant: copied from a friend during the session.

## Hebrew variants (verify with native-speaker team before enforcement)

- זוהתה העתקה בשאלה הזאת.
- זוהתה רמאות בבחינה.
- זוהתה הונאה במערכת.
- חוסר יושר אקדמי בתלמיד.
- חוסר יושרה אקדמית בחשבון.
- העתקת מחבר בשאלה 4.
- העתקת מתלמיד בבחינה.
- העתקת תשובות מחברה בבית.

## Arabic variants (verify with native-speaker team before enforcement)

- تم اكتشاف غش في الامتحان.
- تم اكتشاف تزوير في الإجابة.
- عدم النزاهة الأكاديمية.
- عدم الأمانة الأكاديمية.
- نقلت من زميلك في الصف.
- نقلت الإجابات من صديقك.
- غششت من طالب آخر.
