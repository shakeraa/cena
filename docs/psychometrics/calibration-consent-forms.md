# Calibration study — consent forms (DRAFT)

> **🚨 DRAFT — Legal counsel + DPO review required before any enrolment 🚨**
>
> Engineering first-pass. These forms must be translated + legally reviewed
> before they meet a real student or parent.

- **Task**: RDY-080
- **Study**: Cena θ → Bagrut calibration (see `calibration-study-design.md`)
- **Consent bucket**: **academic-performance-sharing** — separate from
  the general Cena consent the student / parent granted at sign-up.

## 1. Why a separate consent bucket

A student's actual Bagrut scaled score is:
- **High-sensitivity academic data** under UK-GDPR + Israel PPL
  Amendment 2024 § 17
- **Not needed** for the platform to function — Cena tutors equally
  well whether or not the student shares their Bagrut score
- Used here **solely** to calibrate a statistical mapping

Under GDPR's data-minimisation principle (Art. 5(1)(c)) and the
Israeli PPL's "specific purpose" requirement, this cannot ride on the
general platform-use consent. A distinct, purpose-specific consent is
required.

## 2. What participants are consenting to

Each of these is a separate checkbox, independently grantable:

1. **Provide my actual Bagrut scaled score** to Cena after the exam
   results are released. The score may be provided by me (the
   student), my parent/guardian, or via my Ministry transcript upload.
2. **Allow Cena to retain the linking** between my Cena θ snapshots
   (already collected under platform consent) and my Bagrut score
   for up to **24 months**, after which both are deleted absent
   renewed consent.
3. **Allow Cena to use the linkage in aggregate** for the calibration
   study — my individual data is never identified in any output.
4. **Allow Cena to contact me at T+12 weeks** post-exam for one
   follow-up survey on perceived usefulness. One survey, one email,
   no further contact without re-opted.

## 3. What participants are NOT consenting to

Called out explicitly so participants understand what's excluded:

- **No re-sharing** of the Bagrut score with anyone outside Cena's
  calibration team
- **No identification** of the student in any report or paper
- **No use for anything other than calibration** — not marketing, not
  "look, Cena raises your Bagrut", not product demos
- **No fine-tuning or training** of Anthropic / any ML model on the
  student's answer data — this is excluded by the general consent
  (`MlExcluded` tag, ADR-0003) and is not re-admitted here
- **No effect on the student's Cena experience** — calibration
  participants see the same product as non-participants

## 4. Withdrawal

At any point, for any reason or no reason, a participant may:
- Withdraw from the calibration study (keeps general Cena access)
- Request deletion of the Bagrut score + linked θ snapshots
  (fulfilled within 30 days)
- Revert to general platform consent only

Withdrawing does not affect the student's access to Cena, their
Bagrut exam, or any relationship with Cena.

## 5. Consent-holder mapping

| Student age | Who grants consent |
|---|---|
| 18+ | Student directly |
| 16–17 | Parent / guardian + student assent (age-appropriate explanation of the study) |
| Under 16 | Excluded from calibration study (higher risk, insufficient counterbalancing value) |
| Under 13 | Excluded absolutely (COPPA + Israel PPL under-13 + protocol gating) |

## 6. Arabic-language consent copy (draft — for Levantine-Arab translator review)

```
عزيزي ولي الأمر / الطالب الكريم،

نقوم في منصة "سِنا" (Cena) بدراسةٍ علميةٍ تهدف إلى التحقّق من مدى دقّة
مقياس القدرة الذي تستخدمه المنصة (نسمّيه θ) مقارنةً بعلامة البجروت
الفعلية في امتحان الرياضيات. هذه الدراسة منفصلةٌ تماماً عن استخدامك
العادي للمنصة، وهي اختيارية 100%.

ما هو المطلوب منك إن وافقت:
- أن تُطلعنا على علامة البجروت التي حصلت عليها بعد صدور النتائج.
- أن تسمح لنا بالاحتفاظ بالعلاقة بين بياناتك على منصّة سِنا (التي
  جمعناها أصلاً) وبين العلامة، لمدّة أقصاها 24 شهراً.
- أن نتواصل معك مرّةً واحدةً فقط بعد 12 أسبوعاً من الامتحان بسؤالٍ
  قصيرٍ عن تجربتك.

ما الذي لن يحدث:
- لن نكشف هويّتك في أيّ تقريرٍ أو ورقةٍ بحثية.
- لن نشارك علامتك مع أيّ جهةٍ خارج فريق الدراسة.
- لن نستخدم بياناتك لتدريب أيّ نموذج ذكاءٍ اصطناعيّ.
- لن يتأثّر استخدامك للمنصّة بطريقةٍ أو بأخرى إن وافقت أو رفضت.

لك الحقّ في الانسحاب من الدراسة في أيّ وقتٍ دون إبداء أسباب، ودون أيّ
تأثيرٍ على استخدامك لمنصّة سِنا.

الموافقة (اختيارية ومستقلّة):
☐ أوافق على مشاركة علامة البجروت الخاصّة بي مع فريق الدراسة.
☐ أوافق على الاحتفاظ بالعلاقة بين بياناتي على المنصّة وعلامتي لمدّة
   أقصاها 24 شهراً.
☐ أوافق على التواصل لإجراء استطلاعٍ واحدٍ بعد الامتحان بـ 12 أسبوعاً.

اسم الطالب: __________________
اسم ولي الأمر (للقاصرين): __________________
التوقيع: __________________
التاريخ: __________________
```

## 7. Hebrew-language consent copy (draft — for legal review)

```
הורה / תלמידה יקרים,

בפלטפורמת "Cena" אנחנו עורכים מחקר פסיכומטרי לאימות מידת הדיוק של
מדד היכולת שהפלטפורמה מפיקה (קוראים לו θ) אל מול ציון הבגרות במתמטיקה
בפועל. המחקר הזה נפרד לחלוטין מהשימוש הרגיל שלך בפלטפורמה,
והשתתפות בו היא וולונטרית ב-100%.

מה נבקש אם תסכים/י:
- לשתף עמנו את ציון הבגרות שלך לאחר פרסום התוצאות.
- להרשות לנו לשמור את הקישור בין הנתונים שלך ב-Cena (שכבר נאספו
  בהסכמה הקיימת) ובין הציון, לתקופה של עד 24 חודשים.
- להיענות לפנייה יחידה 12 שבועות לאחר הבחינה עם שאלון קצר.

מה לא יקרה:
- זהותך לא תיחשף בשום דו"ח או פרסום.
- הציון שלך לא יועבר לאף גורם מחוץ לצוות המחקר.
- המידע שלך לא ישמש לאימון שום מודל בינה מלאכותית.
- השימוש שלך בפלטפורמה לא יושפע באף צורה — בין אם תסכים/י ובין אם לא.

יש לך זכות לפרוש מהמחקר בכל עת, ללא צורך בהנמקה וללא השפעה כלשהי
על השימוש שלך ב-Cena.

הסכמה (אופציונלית ועצמאית לכל סעיף):
☐ אני מסכימ/ה לשתף את ציון הבגרות שלי עם צוות המחקר.
☐ אני מסכימ/ה לשמירת הקישור בין הנתונים בפלטפורמה לבין הציון לתקופה
   של עד 24 חודשים.
☐ אני מסכימ/ה ליצירת קשר בשאלון אחד, 12 שבועות לאחר הבחינה.

שם התלמיד/ה: __________________
שם ההורה (לקטינים): __________________
חתימה: __________________
תאריך: __________________
```

## 8. English-language consent copy (reference; participants receive
the language of their jurisdiction)

```
Dear student / parent,

Cena is running a psychometric study to check how accurately our
internal ability measure (we call it θ) lines up with your actual
Bagrut math exam score. This study is separate from your normal use
of Cena, and it is 100% voluntary.

If you agree, we will ask you to:
- Share your Bagrut scaled score with us after results are released.
- Let us keep the linkage between your Cena data (already collected
  under general platform consent) and the score for up to 24 months.
- Respond to a single survey 12 weeks post-exam.

What we will NOT do:
- Identify you in any report or paper.
- Share your score with anyone outside the study team.
- Use your data to train any AI model.
- Change your Cena experience based on whether you participate.

You may withdraw at any time, for any reason, without any effect on
your Cena access or your relationship with Cena.

Consent (each item is independent):
☐ I agree to share my Bagrut scaled score with the study team.
☐ I agree to the linkage being retained for up to 24 months.
☐ I agree to be contacted once, 12 weeks post-exam, for one survey.

Student name: __________________
Parent / guardian name (for minors): __________________
Signature: __________________
Date: __________________
```

## 9. Operational notes (not part of the consent text)

- The consent flow is a separate route inside the student SPA
  (`/research/calibration`) that is never auto-offered — students
  opt in via a banner or a parent-console CTA
- Each granted consent bucket is recorded as an
  `AcademicConsentGrantedV1` event in the student's stream with the
  three independent booleans from §6 of the consent copy
- Withdrawal is a one-click path from `/research/calibration/withdraw`
  that emits `AcademicConsentWithdrawnV1` and triggers the 30-day
  deletion pipeline
- The parent-console view shows the consent state for each minor the
  parent is linked to, and supports per-minor withdrawal without
  affecting the sibling's consent

## References

- Study design: `calibration-study-design.md`
- Scaffolding: `src/shared/Cena.Domain/Psychometrics/Calibration/`
- General parental consent: `docs/compliance/parental-consent.md`
- DPA: `docs/legal/dpa-anthropic-draft.md`

---
**Status**: DRAFT — legal + DPO review required before enrolment.
**Last touched**: 2026-04-19 (engineering draft)
