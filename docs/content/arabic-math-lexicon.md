# Arabic Math Lexicon — Levantine Register

**Status:** DRAFT — pre-review. Not yet locked. Do not use as the sole source of truth for production content generation until Prof. Amjad (or a qualified Levantine-Arabic Bagrut math educator) signs off.
**Audience:** Content authors, AI prompt engineers, QA reviewers, translation specialists.
**Register:** Modern Standard Arabic (MSA) with Levantine (شامي) conventions where they differ from Egyptian or Gulf norms. When a term has regional split, the Levantine form is primary.
**Scope (initial):** Israeli Bagrut 5-unit math (805) syllabus — 8 topic families. 4-unit (804) and 3-unit (801) lexicons are downstream once this ships.
**Partner languages:** Hebrew (Israeli Ministry standard, as rendered in Bagrut exam booklets) and English (reference).

---

## Why this doc exists

Cena's market wedge is Arabic-first Bagrut prep (docs/research/tracks/track-7-israeli-bagrut-market.md). The wedge collapses the moment a student sees a term rendered inconsistently across lessons, hints, or exam-simulation surfaces. Two failure modes we are fixing:

1. **Intra-Cena drift** — one AI prompt produces `متعدد الحدود`, another produces `كثيرة الحدود` for "polynomial," and the student loses trust.
2. **Ministry notation mismatch** — the Bagrut exam is in Hebrew with specific terminology. If a student learns a concept in Arabic but the exam's Hebrew phrasing points to a slightly different meaning, the bilingual cognitive-load tax (Sweller/Ayres/Kalyuga 2011, already cited in project docs) gets worse, not better.

This lexicon is the single source of truth that:

- **Locks** the canonical Arabic term for each 5-unit syllabus concept.
- **Maps** each term to its Ministry-standard Hebrew counterpart verbatim (so exam-simulation mode renders the exact phrasing).
- **Tags** review status so authors know which terms are approved vs. under review vs. draft.

---

## Review lifecycle

Every term moves through three states:

| State | Meaning | Who can set | What uses it |
|-------|---------|-------------|--------------|
| `DRAFT` | Engineering's best guess, not yet reviewed | Cena engineering | Development only; flag in content-gen pipeline |
| `PROF_AMJAD_REVIEW` | Under expert review | Prof. Amjad (or substitute reviewer) | Pilot content only; ship-gate warning on production use |
| `LOCKED` | Approved, production-safe | Prof. Amjad sign-off | All production content pipelines |

Engineering-side enforcement:

- `src/shared/Cena.Infrastructure/Localization/TerminologyLexicon.cs` (to ship in phase 1B) will load this file and expose each term's status to content-generation services.
- CI check (to ship in phase 1B): any production content-generation call that uses a non-`LOCKED` term must be flagged as a build warning, and a `LOCKED_ONLY=true` production mode must refuse the generation entirely.

---

## Terminology tables

Each row: **Arabic (canonical, Levantine/MSA) | Hebrew (Ministry-standard) | English (reference) | Status | Notes**.

### 1. Algebra — foundations

| Arabic | Hebrew | English | Status | Notes |
|--------|--------|---------|--------|-------|
| متغير | משתנה | variable | DRAFT | Standard across registers |
| ثابت | קבוע | constant | DRAFT | |
| معامل | מקדם | coefficient | DRAFT | |
| حد (pl. حدود) | איבר (pl. איברים) | term (in a polynomial) | DRAFT | Note the plural shift: Arabic collective vs Hebrew plural construct |
| متعدد الحدود | פולינום | polynomial | DRAFT | Levantine-preferred over كثيرة الحدود; reject the alternative |
| معادلة | משוואה | equation | DRAFT | |
| متباينة | אי-שוויון | inequality | DRAFT | |
| جذر (pl. جذور) | שורש (pl. שורשים) | root (of equation) | DRAFT | |
| حل | פתרון | solution | DRAFT | |

### 2. Functions

| Arabic | Hebrew | English | Status | Notes |
|--------|--------|---------|--------|-------|
| دالة (pl. دوال) | פונקציה (pl. פונקציות) | function | DRAFT | |
| مجال التعريف | תחום הגדרה | domain | DRAFT | Literal: "domain of definition" — match Ministry phrasing |
| المدى | טווח | range (codomain) | DRAFT | Ministry uses `טווח`; Arabic textbooks sometimes use `مجال القيم` — reject |
| نقطة تقاطع | נקודת חיתוך | intersection point | DRAFT | |
| نقطة قصوى (عظمى / صغرى) | נקודת קיצון (מקסימום / מינימום) | extremum (max / min) | DRAFT | |
| تزايد / تناقص | עלייה / ירידה | increasing / decreasing | DRAFT | |
| دالة زوجية / فردية | פונקציה זוגית / אי-זוגית | even / odd function | DRAFT | |

### 3. Calculus

| Arabic | Hebrew | English | Status | Notes |
|--------|--------|---------|--------|-------|
| نهاية | גבול | limit | DRAFT | |
| مشتقة | נגזרת | derivative | DRAFT | Note: Ministry uses `נגזרת`, not the Hebrew cognate `דיפרנציאל` |
| اشتقاق | גזירה | differentiation | DRAFT | |
| قاعدة السلسلة | כלל השרשרת | chain rule | DRAFT | |
| تكامل | אינטגרל | integral | DRAFT | |
| تكامل محدد / غير محدد | אינטגרל מסוים / לא מסוים | definite / indefinite integral | DRAFT | |
| مشتقة ثانية | נגזרת שנייה | second derivative | DRAFT | |
| نقطة انعطاف | נקודת פיתול | inflection point | DRAFT | |

### 4. Trigonometry

| Arabic | Hebrew | English | Status | Notes |
|--------|--------|---------|--------|-------|
| زاوية | זווית | angle | DRAFT | |
| قطاع دائري | גזרה | circular sector | DRAFT | |
| راديان | רדיאן | radian | DRAFT | |
| جيب | סינוס | sine | DRAFT | Symbol: sin (ASCII) — math must always render LTR per project rule |
| جيب التمام | קוסינוס | cosine | DRAFT | |
| ظل | טנגנס | tangent | DRAFT | |
| متطابقة مثلثية | זהות טריגונומטרית | trig identity | DRAFT | |
| معادلة مثلثية | משוואה טריגונומטרית | trig equation | DRAFT | |

### 5. Geometry (analytic)

| Arabic | Hebrew | English | Status | Notes |
|--------|--------|---------|--------|-------|
| مستقيم | ישר | line | DRAFT | |
| قطعة مستقيمة | קטע | line segment | DRAFT | |
| مستوى إحداثي | מערכת צירים | coordinate plane | DRAFT | |
| ميل | שיפוע | slope / gradient | DRAFT | |
| دائرة | מעגל | circle | DRAFT | |
| قطع ناقص | אליפסה | ellipse | DRAFT | |
| قطع زائد | היפרבולה | hyperbola | DRAFT | |
| قطع مكافئ | פרבולה | parabola | DRAFT | |
| مستقيم مماس | משיק | tangent line | DRAFT | |
| مسافة بين نقطتين | מרחק בין שתי נקודות | distance between two points | DRAFT | |

### 6. Statistics & probability

| Arabic | Hebrew | English | Status | Notes |
|--------|--------|---------|--------|-------|
| احتمال | הסתברות | probability | DRAFT | |
| متغير عشوائي | משתנה מקרי | random variable | DRAFT | |
| توزيع احتمالي | התפלגות | probability distribution | DRAFT | |
| توزيع طبيعي | התפלגות נורמלית | normal distribution | DRAFT | |
| متوسط / وسط حسابي | ממוצע | mean / arithmetic average | DRAFT | |
| انحراف معياري | סטיית תקן | standard deviation | DRAFT | |
| تباين | שונות | variance | DRAFT | |
| احتمال شرطي | הסתברות מותנית | conditional probability | DRAFT | |
| حدث | מאורע | event | DRAFT | |
| فضاء العينة | מרחב מדגם | sample space | DRAFT | |

### 7. Sequences & series

| Arabic | Hebrew | English | Status | Notes |
|--------|--------|---------|--------|-------|
| متتالية | סדרה | sequence | DRAFT | |
| متتالية حسابية | סדרה חשבונית | arithmetic sequence | DRAFT | |
| متتالية هندسية | סדרה הנדסית | geometric sequence | DRAFT | |
| الحد النوني | האיבר הכללי / איבר כללי | nth term (general term) | DRAFT | Hebrew phrasing: "the n-th term" |
| مجموع جزئي | סכום חלקי | partial sum | DRAFT | |
| سلسلة | טור | series | DRAFT | Do NOT translate `سلسلة` as `streak` in any copy (GD-004 ship-gate) — the ship-gate allowlist has `سلسلة` as an electrical/math term only |

### 8. Vectors

| Arabic | Hebrew | English | Status | Notes |
|--------|--------|---------|--------|-------|
| متجه (pl. متجهات) | וקטור (pl. וקטורים) | vector | DRAFT | |
| طول / مقدار متجه | אורך / גודל וקטור | magnitude of a vector | DRAFT | |
| اتجاه | כיוון | direction | DRAFT | |
| جداء عددي (نقطي) | מכפלה סקלרית | dot product (scalar product) | DRAFT | |
| جداء اتجاهي | מכפלה וקטורית | cross product (vector product) | DRAFT | |
| متجه الوحدة | וקטור יחידה | unit vector | DRAFT | |
| متجهات متوازية | וקטורים מקבילים | parallel vectors | DRAFT | |
| متجهات عمودية | וקטורים ניצבים | perpendicular vectors | DRAFT | |

---

## Variable-name conventions (carries from ArabicMathNormalizer.cs)

Mathematics context (matches `src/shared/Cena.Infrastructure/Localization/ArabicMathNormalizer.cs`):

| Arabic | Latin (Ministry + international) | Meaning |
|--------|----------------------------------|---------|
| س | x | independent variable |
| ص | y | dependent variable |
| ع | z | third coordinate |
| ن | n | natural-number index |
| م | m | integer parameter |
| ل | l | (context-dependent) |
| ك | k | integer index |
| ر | r | radius, ratio |
| ت | t | time (in math context) |

Physics context overrides (`ت` → `a` for acceleration) — see ArabicMathNormalizer.cs line 55+.

Student input in Arabic characters is normalized to Latin via `ArabicMathNormalizer.Normalize(input, context)` before entering the CAS. Never display math in Arabic characters to a student in exam-simulation mode — Ministry exams are Latin-numeral + Latin-variable.

---

## Review checklist (for the expert reviewer)

Before any term moves from `DRAFT` → `PROF_AMJAD_REVIEW` → `LOCKED`, verify:

- [ ] Term is the dominant form in Levantine Bagrut textbooks used in Nazareth, Haifa, and Jerusalem-area Arab schools.
- [ ] Hebrew counterpart matches the form printed in the most recent 5-unit Bagrut exam (moed-a and moed-b of the past 3 years).
- [ ] English reference is unambiguous (not a false cognate).
- [ ] Plural form is specified when the term has a frequently-used plural in math prose.
- [ ] Any regional competitor (Egyptian, Gulf) is explicitly rejected with a note if the student might encounter it in other materials.

---

## Change protocol

- Add a new row in `DRAFT` status any time engineering or content-gen surfaces a new term.
- Move a row to `PROF_AMJAD_REVIEW` only after the engineering team has proposed a canonical form.
- Move a row to `LOCKED` only when the expert signs off in writing (PR comment or signed doc). Record the reviewer and date in the Notes column.
- Never silently update a `LOCKED` row. A change to a locked term requires a new review cycle.

---

## Related files

- `src/shared/Cena.Infrastructure/Localization/ArabicMathNormalizer.cs` — Arabic-to-Latin input normalization (already shipped for ARABIC-001 + PP-014/PP-015).
- `src/shared/Cena.Infrastructure/Localization/TerminologyLexicon.cs` — phase 1B, will load this file into a typed lookup.
- `tasks/readiness/RDY-068-f2-arabic-first-ux.md` — task definition.
- `docs/research/cena-user-personas-and-features-2026-04-17.md` — F2 rationale (Amir + Tariq + Mahmoud personas).
- `docs/research/cena-panel-review-user-personas-2026-04-17.md` — Prof. Amjad's panel review with the terminology-drift concern.
- `docs/research/tracks/track-7-israeli-bagrut-market.md` — market wedge sizing.

---

## Phase tracking

- **Phase 1A (2026-04-19, this commit):** lexicon scaffolding doc (this file) + locale-inference composable (`useLocaleInference.ts`) + unit tests + onboarding-store default wiring. All terms are `DRAFT`.
- **Phase 1B (next):** `TerminologyLexicon.cs` domain loader + CI gate for non-LOCKED terms in production content paths.
- **Phase 2:** Prof. Amjad review pass. First batch (Algebra + Functions) to `LOCKED` after review.
- **Phase 3:** 4-unit and 3-unit lexicon expansion once 5-unit is locked.
