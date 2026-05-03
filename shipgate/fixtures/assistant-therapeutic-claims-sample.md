# Ship-gate positive-test fixture: assistant therapeutic claims (prr-073)

**Purpose**: This file deliberately contains every pattern in
`scripts/shipgate/assistant-therapeutic-claims.yml`. The CI test suite
(`tests/shipgate/assistant-therapeutic-claims.spec.mjs`) runs the scanner
against this fixture and asserts that EVERY rule fires at least once.

**Do not clean this file.** Do not rephrase. Each line below is a trap.
This file is whitelisted in
`scripts/shipgate/assistant-therapeutic-claims-whitelist.yml`.

---

## prr-073 — assertion of emotional state (English, BANNED)

- I understand your anxiety about the Bagrut unit.
- Variant: I understand your overwhelm right now.
- Variant: I understand your stress about this topic.
- You're feeling overwhelmed by this question, aren't you?
- Variant: you feel anxious about this unit.
- Variant: you are depressed about your results.
- Variant: you might feel panicked by the next step.
- This sounds difficult emotionally — let's slow down.
- Variant: that seems hard emotionally for you.
- Variant: this feels stressful mentally.
- Variant: it must be tough emotionally right now.
- Let me help with your stress about the Bagrut.
- Variant: I can help with your anxiety during this session.
- Variant: I'll help with your overwhelm in this unit.
- I'm here for you emotionally whenever you need it.
- I know how you feel about this difficult topic.
- Variant: I know what you're going through with this material.

## prr-073 — clinical diagnosis vocabulary (English, BANNED)

- You might have an anxiety disorder based on these responses.
- You're showing signs of clinical depression.
- Variant: this looks like depressive symptoms.
- You sound like you have ADHD from these attention patterns.
- Variant: you might have attention-deficit disorder.
- You seem to have dyslexia based on your reading pace.
- Variant: sounds like dyscalculia from your arithmetic errors.
- You're on the spectrum, it seems.
- Variant: you look autistic from how you process this problem.
- You might have OCD about these step checks.
- Variant: sounds obsessive-compulsive.
- You could have PTSD from that previous exam.
- Variant: this looks like post-traumatic stress.

## Hebrew variants (verify with native-speaker team before enforcement)

- אני מבין את החרדה שלך לקראת המבחן.
- אני מבין את הלחץ שלך.
- אתה מרגיש מוצף מהיחידה הזאת.
- אתה חש חרד לגבי השאלה.
- הסיבה היא דיכאון ממושך.
- זו הפרעת חרדה חמורה.
- הפרעת קשב וריכוז אצלך ברורה.

## Arabic variants (verify with native-speaker team before enforcement)

- أفهم قلقك من امتحان البجروت.
- أفهم توترك في هذه الوحدة.
- تشعر بالإرهاق من هذا السؤال.
- أنت تشعر بالقلق من هذه الخطوة.
- السبب هو الاكتئاب المستمر.
- هذا اضطراب القلق المزمن.
- اضطراب نقص الانتباه واضح هنا.
