# Ship-gate positive-test fixture: error message blame framing (prr-142)

**Purpose**: This file deliberately contains every pattern in
`scripts/shipgate/error-message-blame.yml`. The CI test suite
(`tests/shipgate/error-message-blame.spec.mjs`) runs the scanner against
this fixture and asserts that EVERY rule fires at least once.

**Do not clean this file.** Do not rephrase. Each line below is a trap.
This file is whitelisted in
`scripts/shipgate/error-message-blame-whitelist.yml`.

---

## prr-142 — blame-framed error copy (English, BANNED)

- Wrong! Please try again.
- Variant: "Wrong!" banner.
- Incorrect! Check your work.
- Variant: "Incorrect!" red toast.
- You failed the exercise.
- Variant: you have failed this quiz.
- Variant: you failed the unit.
- You got it wrong — see the answer below.
- Variant: you got this wrong.
- Variant: you got that wrong again.
- Your incorrect answer was 42.
- Variant: that's a wrong answer.
- Variant: bad answer, try another.
- Try harder next time — that was careless.
- You missed it again on step 3.
- Variant: you missed that again.
- You made a mistake on the last step.
- Variant: you made another mistake there.
- That's a bad answer — reconsider.
- Variant: a terrible attempt at step 2.
- Variant: a poor response overall.
- That's not good enough, unfortunately.
- Variant: your answer is not good enough yet.

## Hebrew variants (verify with native-speaker team before enforcement)

- שגוי! נסה שוב.
- לא נכון!
- טעות!
- נכשלת במשימה.
- תשובה שגויה.
- תשובה לא נכונה.
- תשובה לא טובה.
- תתאמץ יותר!
- השתדל חזק יותר.

## Arabic variants (verify with native-speaker team before enforcement)

- خطأ!
- غير صحيح!
- غلط!
- فشلت في التمرين.
- رسبت في الوحدة.
- إجابة خاطئة.
- جواب غير صحيح.
- اجتهد أكثر!
- حاول بجد.
