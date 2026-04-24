# Syllabus & Corpus Strategy

> **Status:** Specification
> **Applies to:** Content Authoring Context, tasks/content/CNT-001 through CNT-006
> **Critical question this answers:** Where does the curriculum data actually come from?

---

## 1. Source Materials

### 1.1 Official Bagrut Exam Papers (Primary Source)

| Attribute | Value |
|---|---|
| **Source** | Israel Ministry of Education (משרד החינוך) |
| **Availability** | Publicly available — published annually on the Ministry website after each exam session |
| **URL** | https://meyda.education.gov.il/sheeloney_bagrut/ |
| **Coverage** | 2010-2026 (15 years of exams per subject) |
| **Format** | PDF (Hebrew, RTL), some with scoring rubrics |
| **Legal status** | **Public domain** — government documents published for public use |
| **Languages** | Hebrew (primary), Arabic (parallel versions exist for Arab-sector schools) |
| **Subjects available** | Math (3/4/5 units), Physics, Chemistry, Biology, Computer Science |

**What we extract:**
- Question phrasing patterns ("הוכח כי...", "חשב את...", "מצא את...")
- Difficulty distribution (which Bloom levels appear at which frequency)
- Concept coverage (which topics appear in exams, how often)
- Distractor patterns in MCQs (common wrong answers and why students choose them)
- Scoring rubrics and partial credit rules
- Multi-part question structure (a→b→c dependency chains)

### 1.2 Approved Textbooks (Secondary Source)

| Subject | Primary Textbook | Publisher | Status |
|---|---|---|---|
| Math (5 units) | **גבע 5** (Geva 5) | Geva Publishing | Standard textbook in most Israeli high schools |
| Math (4 units) | **גבע 4** | Geva Publishing | |
| Math (3 units) | **גבע 3** | Geva Publishing | |
| Physics | **Halliday/Resnick** (Hebrew translation) + **פיזיקה לבגרות** | Various | |
| Chemistry | **כימיה לבגרות** | Various | |
| Biology | **ביולוגיה לבגרות** | Various | |
| CS | **מדעי המחשב** (various) | Various | |

**Legal considerations:**
- Textbooks are **copyrighted** — we CANNOT reproduce textbook content directly
- **Fair use for education** (שימוש הוגן): Israeli copyright law (חוק זכויות יוצרים, 2007) Section 19 allows limited use for education, criticism, and research
- **Our approach:** We use textbooks as a STYLE GUIDE (terminology, pedagogical approach, topic ordering) — we do NOT copy questions or explanations verbatim
- **LLM extraction:** Kimi reads the textbook to learn the STYLE, then generates ORIGINAL questions in that style. The generated content is original, not copied.
- **Risk mitigation:** Education advisor (licensed Bagrut teacher) validates that generated content doesn't inadvertently reproduce copyrighted questions

### 1.3 Ministry of Education Syllabus Documents

| Document | What It Provides |
|---|---|
| **תוכנית הלימודים** (Curriculum plan) | Official topic list per subject per unit level |
| **הנחיות הערכה** (Assessment guidelines) | How exams are structured, scoring policies |
| **חוזרי מנכ"ל** (Director-General circulars) | Policy changes, emphasis shifts, new topics |

**Availability:** All publicly available on the Ministry of Education website.
**Legal status:** Public domain government documents.

### 1.4 Teacher-Contributed Materials

| Source | What It Provides |
|---|---|
| **Education advisor** (licensed Bagrut teacher, part-time) | Real classroom experience: which concepts students struggle with, common errors, effective teaching sequences |
| **Teacher worksheets** | Additional practice problems (with teacher's permission) |
| **Exam post-mortems** | Analysis of which questions students got wrong and why (anonymized) |

**Legal status:** Contributed under work-for-hire agreement with the education advisor.

### 1.5 Arabic Curriculum Sources

| Source | Status |
|---|---|
| Arabic Bagrut exams (بجروت) | Published by Ministry of Education, same public availability |
| Arabic textbooks | Parallel Hebrew textbooks translated/adapted for Arab-sector schools |
| CET Arabic materials | CET (Center for Educational Technology) publishes Arabic learning resources |

**Key difference:** Arabic Bagrut exams are identical in content to Hebrew exams but phrased in Arabic. The syllabus is the same — only the language differs.

---

## 2. Corpus Ingestion Pipeline

```
STAGE 0: One-time per subject (runs before any content generation)

┌──────────────────────────────────────────────────────┐
│ Input Sources                                         │
│                                                        │
│ 📄 15 years of Bagrut PDFs (public domain)            │
│ 📚 Textbook style analysis (fair use — style only)    │
│ 📋 Ministry syllabus documents (public domain)        │
│ 👨‍🏫 Education advisor notes (work-for-hire)            │
│ 🔤 Arabic parallel sources                            │
└──────────────┬───────────────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────┐
│ Corpus Analysis (Kimi K2.5, 256K context)            │
│                                                        │
│ For each source:                                       │
│   1. Terminology index (Hebrew ↔ English ↔ Arabic)   │
│   2. Question style patterns                          │
│   3. Difficulty distribution (Bloom levels)            │
│   4. Distractor patterns (common wrong answers)       │
│   5. Scoring rubrics (partial credit rules)           │
│   6. Cross-concept connections                        │
│                                                        │
│ Output: corpus_analysis_{subject}.json                │
└──────────────┬───────────────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────┐
│ Stored in S3                                          │
│                                                        │
│ s3://cena-content/corpus/                             │
│   ├── math_5u/                                        │
│   │   ├── corpus_analysis.json      (style guide)    │
│   │   ├── terminology_index.json    (He/En/Ar terms)  │
│   │   ├── bagrut_examples.json      (categorized Q's) │
│   │   └── misconceptions.json       (per-concept)     │
│   ├── physics/                                        │
│   └── ...                                             │
└──────────────────────────────────────────────────────┘
```

### What the Corpus Analysis Produces (per subject)

```json
{
  "subject": "mathematics",
  "unit_level": 5,
  "corpus_version": "2026-03-27",
  "sources_analyzed": [
    { "type": "bagrut_exam", "years": "2010-2025", "count": 32 },
    { "type": "textbook", "name": "Geva 5", "chapters": 18 },
    { "type": "syllabus", "version": "2024 update" }
  ],
  "terminology_index": {
    "derivative": { "he": "נגזרת", "ar": "مشتقة", "en": "derivative" },
    "chain_rule": { "he": "כלל השרשרת", "ar": "قاعدة السلسلة", "en": "chain rule" }
  },
  "question_patterns": {
    "prove": { "he": "הוכח כי...", "ar": "أثبت أن...", "frequency": 0.15 },
    "calculate": { "he": "חשב את...", "ar": "احسب...", "frequency": 0.35 },
    "find": { "he": "מצא את...", "ar": "أوجد...", "frequency": 0.25 }
  },
  "bloom_distribution": {
    "recall": 0.15,
    "comprehension": 0.25,
    "application": 0.35,
    "analysis": 0.20,
    "synthesis": 0.05
  },
  "concept_coverage": {
    "derivatives_basic": { "exam_frequency": 0.95, "avg_difficulty": 0.4 },
    "chain_rule": { "exam_frequency": 0.80, "avg_difficulty": 0.65 },
    "integration_by_parts": { "exam_frequency": 0.70, "avg_difficulty": 0.75 }
  }
}
```

---

## 3. How Content Flows Through the System

```
Corpus (S3) → Kimi extracts graph → Neo4j stores graph → Expert reviews
     │                                                          │
     │                                                          ▼
     │                                              Questions generated
     │                                              (Kimi K2.5 batch)
     │                                                          │
     │                                                          ▼
     │                                              Expert review (8-15 weeks)
     │                                                          │
     │                                                          ▼
     │                                              QA pass (automated)
     │                                                          │
     │                                                          ▼
     └──── Corpus style guide used ────────────► Publication
           throughout generation                     │
                                                     ├── Neo4j (production graph)
                                                     ├── S3 (Protobuf artifact)
                                                     ├── NATS (CurriculumPublished)
                                                     └── Actors hot-reload
```

---

## 4. Legal & Licensing Summary

| Source | License | Can We Use? | How We Use It |
|---|---|---|---|
| Bagrut exam papers | **Public domain** (government) | Yes, freely | Direct analysis + style extraction |
| Ministry syllabus | **Public domain** (government) | Yes, freely | Topic structure, concept ordering |
| Textbooks (Geva, etc.) | **Copyright** (publisher) | **Style only** (fair use) | Learn terminology and phrasing patterns — NEVER copy questions |
| Teacher materials | **Work-for-hire** (advisor contract) | Yes, per contract | Contributed under employment agreement |
| eSelf/CET research | **Published research** | Citations only | Reference in competitive analysis, not content |

### Copyright Risk Mitigation

1. **All generated content is original** — Kimi generates new questions inspired by Bagrut style, not copied
2. **SymPy validates math** — ensures correctness independent of any source
3. **Education advisor reviews** — licensed teacher confirms content is original and Bagrut-appropriate
4. **Plagiarism check** — generated questions are checked against a database of real Bagrut questions to ensure no verbatim copying
5. **Arabic content** — generated independently using Arabic terminology index, not translated from Hebrew

---

## 5. Timeline & Effort

| Subject | Nodes | Corpus Ingestion | Graph Extraction | Question Gen | Expert Review | Total |
|---|---|---|---|---|---|---|
| **Math 5-unit** | ~2,000 | 1 week | 5-10 days | 3-5 days | 8-15 weeks | **12-18 weeks** |
| **Physics** | ~1,500 | 1 week | 4-8 days | 2-4 days | 6-12 weeks | **9-15 weeks** |
| **Chemistry** | ~1,200 | 1 week | 3-6 days | 2-3 days | 5-10 weeks | **8-13 weeks** |

**Critical path:** Expert review is the bottleneck. 1 expert at 20 hrs/week reviews ~100-150 concepts/week. With 2 experts: ~8 weeks for Math.

**MVP launch:** Math 5-unit only (50% content at launch, expanding while live — per `fundraising-playbook.md`).

---

## 6. Arabic Corpus Specifics

Arabic Bagrut students take the same exams in Arabic. The corpus strategy is:

1. **Arabic Bagrut PDFs** — same public availability as Hebrew
2. **Arabic terminology index** — 30+ MSA math terms (from `contracts/llm/prompt-templates.py`)
3. **Arabic question patterns** — "أثبت أن..." / "احسب..." / "أوجد..." (from Arabic exams)
4. **Generation** — Kimi generates Arabic questions using the Arabic style guide, NOT by translating Hebrew questions
5. **Expert review** — Arabic-speaking Bagrut teacher (separate from Hebrew advisor)

**Why NOT translate:** Machine translation of math questions introduces subtle errors (e.g., pronoun gender in Arabic affects equation references). Original generation in Arabic is more reliable.
