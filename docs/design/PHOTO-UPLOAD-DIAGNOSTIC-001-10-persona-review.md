# Photo-Upload Diagnostic — 10-Persona Review

**Date**: 2026-04-22
**Status**: Discussion / open for triage
**Owner**: Claude Code coordinator
**Companion**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](./BUSINESS-MODEL-001-pricing-10-persona-review.md)

---

## What's being reviewed

A new student-facing feature: when a student gets a wrong answer on a practice item, they can upload a phone photo of their handwritten solution and receive a targeted diagnostic.

**Proposed pipeline:**
1. Photo upload (from phone camera, via PWA)
2. OCR extracts handwritten math (existing Mathpix-style layer from Phase 1A ingestion pipeline)
3. LLM parses the extracted text into a canonical step sequence
4. SymPy CAS verifies each step-to-step transition — per [ADR-0002](../adr/0002-sympy-correctness-oracle.md)
5. First failing transition is returned with an LLM-narrated explanation mapped to a misconception template
6. Photo deleted within minutes; misconception data session-scoped, 30-day retention per [ADR-0003](../adr/0003-misconception-session-scope.md)

**Cost estimate:** ~$0.02–0.04/upload (OCR ~$0.005 + Sonnet ~$0.015–0.03 + CAS free).
**Commercial scope:** Premium tier = unlimited with soft cap; Plus = capped at ~20/mo; Basic = not available.

---

## Review methodology

Ten personas covering end-user, guardian, educator, research, compliance, engineering, ML-safety, accessibility, support, and finance angles. Each asked to name the single most important failure mode for their lens, and a specific mitigation.

---

### 1. 11th-grade student — just got a wrong answer

Wants the diagnosis to be *specific and actionable*. "You distributed the minus sign incorrectly in step 3" is gold. "There seems to be an error somewhere" is useless — they could tell that.

**Critical failure mode:** **silent mis-OCR.** If the system reads "x²" as "x2" and then tells them their step is wrong when it's actually right, trust collapses in one use. Worse: the student may "correct" correct work based on a false diagnosis.

**Mitigation:** Confidence threshold on OCR. Below threshold, show the extracted text in editable form with an "is this what you wrote?" preview. Student confirms or edits before CAS runs. Never silently commit to a mis-reading.

---

### 2. Parent

The first question is privacy, not price. "Where does the photo go? Who sees it? Can I delete it?"

**Critical failure mode:** legalese privacy policy. A parent who has to scroll 3 pages of T&C to understand "is my kid's photo safe" will default to not uploading.

**Mitigation:** One-card privacy disclosure shown before the first upload, in plain Hebrew/Arabic/English:
- "Photos are deleted within 5 minutes."
- "Step analysis is kept for 30 days for your child's learning history."
- "Never used to train AI. Never shared with third parties."
- "You can delete everything anytime."

Needs parental consent flow for accounts under 16. Do this once at onboarding, not per upload.

---

### 3. Math teacher

Mixed reaction. Upside: students get feedback between classes. Downside: **if the AI diagnosis contradicts my classroom instruction, whose authority wins in the student's mind?**

**Critical failure mode:** a black-box "you were wrong" narration with no mathematical audit trail. The teacher can't verify or contest it, and students may start trusting the AI over the teacher.

**Mitigation:** Expose the CAS-verification chain in an expandable "show my work" view — the actual SymPy transformation tree showing the expected vs. detected expression at each step. Teachers can inspect the evidence, and so can mathematically-curious students. Makes the system *auditable*, not oracular. Also: acknowledge classroom authority — if a teacher-assigned item has a teacher-provided solution path, prefer that path over AI-derived paths.

---

### 4. Math education researcher

Research on post-hoc error diagnosis is mixed. Students who are *shown* where they went wrong without doing the *work of finding it* may patch the surface error and repeat the deep misconception on the next problem.

**Critical failure mode:** Feature degenerates into a homework-answer-checker. Students upload every wrong answer, read the diagnosis, never internalize the pattern.

**Mitigation:** Insert a **reflection step** before full explanation. When the system detects the first wrong step, show: "I see the error. Try again — here's a hint if you want it." One student retry before the full narration unlocks. Turns the feature from an answer-checker into a scaffold. The cost (minor friction) is small; the pedagogical gain is large. Backed by productive-failure literature.

---

### 5. Privacy / data-protection officer (Israeli Privacy Law + GDPR-aligned)

Photos of minors are sensitive. Handwriting may be argued as biometric-adjacent under some interpretations. Incidental PII (student name in margin, school logo) is almost guaranteed.

**Critical failure modes:**
1. Data residency — if Mathpix/OCR vendor processes in US-East, that's cross-border transfer of minor's biometric-adjacent data. Non-trivial compliance exposure.
2. Training use — if the vendor's ToS allows them to use images for model improvement, default-no is mandatory.
3. Consent granularity — one-time blanket consent at signup is legally weaker than per-feature consent with revocation.

**Mitigation:**
- Data Processing Agreement with OCR vendor, **EU or Israel residency** required. If Mathpix doesn't offer it, evaluate alternatives (e.g., in-house handwriting OCR, Pix2Text with data-residency control).
- Explicit "no training, no sharing" clause in vendor DPA.
- Parental consent registration for under-16 accounts.
- Auto-redaction of faces / names / school identifiers from photos before OCR.
- Israeli Database Registration filing if required (likely yes for minor photo data).

This is a real compliance tail, not a blocker. Budget 4–6 weeks of legal/vendor-negotiation work before public launch.

---

### 6. Engineering lead

The ingestion path was built for clean print-quality PDFs. Student handwriting is structurally different: multi-row layouts with arrows, erased/crossed-out steps, work-in-the-margin, skipped mental steps, mixed-script (Hebrew labels around English equations), phone-camera skew and glare.

**Critical failure modes:**
1. LLM hallucinates intermediate steps the student didn't actually write, then CAS-verifies those hallucinated steps as correct — giving a false "you're fine" verdict.
2. Student skips steps mentally (goes from step 2 to step 5 on paper) — CAS flags the leap as invalid when the student is actually correct, just concise.
3. Screenshot-of-different-problem attacks (student uploads a random photo to see if the system breaks).

**Mitigation:**
- **Ship v1 with a narrow scope: detect the first wrong step, not narrate the full derivation.** Whole-derivation narration has too many hallucination attack surfaces.
- CAS verification of every extracted step against the original problem (not just step-to-step); if extracted steps don't trace back to the posed problem, reject with "I couldn't connect this to the problem you're solving."
- Rate-limit + image-similarity checks to catch abuse.
- Accept that step-skipping will cause false-wrong flags; surface them as "I couldn't follow between step 2 and step 3 — can you show me what happened there?" rather than "step 3 is wrong."

---

### 7. ML/AI researcher

CAS catches the *mathematical* break. But the LLM's narration has to explain *why* — and that's where hallucination lives. CAS says "expected (x-2), got (x+2)." LLM narrates "you forgot to multiply by 2." Wrong reason; right detection.

**Critical failure mode:** Correct error detection, wrong pedagogical narration. Student "fixes" a non-error, develops a new misconception.

**Mitigation:** Don't let the LLM narrate freeform. Build a **curated misconception taxonomy** — e.g., sign-flip distributive, minus-as-subtraction, premature cancellation, factoring errors, quadratic-formula sign errors, fraction-over-fraction errors, etc. Each CAS-detected break type maps to 1–3 candidate misconception templates. The LLM picks which template best matches the student's observed work, from a *closed* set. If none match well, show a conservative "let me check this with your teacher" message rather than fabricate. Research consistently shows targeted misconception matching > generic narration.

---

### 8. Accessibility expert

OCR accuracy is not uniform across student populations. Dysgraphia, ADHD-pattern messy work, left-hand smudging on RTL-oriented paper, Arabic/Hebrew script intermixing, mathematical dyslexia symptoms — all systematically degrade OCR.

**Critical failure mode:** Premium feature works for the tidy half of students and fails for the other half — accessibility gap hard-coded into the paid tier. At scale this is both an equity issue and a discrimination-claim exposure.

**Mitigation:**
- Track per-student OCR confidence quality as a metric. If a given student's OCR consistently falls below threshold, surface a **typed-steps alternative** as the default ("here, just type your steps and I'll check them — faster than uploading a photo") without calling attention to their handwriting.
- Never tell a student their handwriting is "unreadable." Reframe: "I couldn't pick up all of this — want to type just step 3?"
- Include dysgraphia-friendly fallback in accessibility audit per existing ADR chain.

---

### 9. Customer support lead

At 10k students × 20 uploads/mo = 200k uploads/mo. Even a 0.5% wrong-diagnosis rate = 1,000 tickets/mo. At current support staffing, that's a disaster.

**Critical failure mode:** No dispute workflow. A parent whose kid was told they were wrong (and was actually right) files an angry ticket; support has no way to inspect the OCR, CAS trace, or LLM decision, so they can only apologize generically. Repeat offense → NPS collapse.

**Mitigation:**
- "This diagnosis seems wrong" button on every diagnostic result. One click opens a dispute flow.
- Support portal view that shows: original photo, OCR output, CAS chain, LLM narration, misconception template selected. Support can inspect the evidence chain and override with credit/apology.
- Weekly review of disputed diagnoses → feedback into misconception taxonomy → regression test.
- Auto-credit for disputes confirmed as system error (avoids human-in-loop bottleneck on low-stakes cases).

---

### 10. CFO

At ~$0.03/upload × 20 uploads/student/mo = $0.60/student — easily within Premium margin. **But the abuse case is where this breaks.**

**Critical failure mode:** A student uploads 500 photos/month using the feature as an all-homework answering machine. 500 × $0.03 = $15/student → Premium margin for that user erased.

**Mitigation:**
- Soft cap at 100 diagnostic uploads/month on Premium (median student likely does 20–30).
- Above 100, don't hard-block. Surface: "you've had a busy month — 120 diagnostics used! Want to book a 1:1 tutor session?" Converts the heavy user from a cost center into either an upsell or a user who self-moderates.
- Hard cap at 300/mo with "contact support" UX (for legitimate heavy-use edge cases like exam-week cram).
- Monitor upload-distribution tail weekly; investigate users with >200/mo for account-sharing or abuse.

---

## Synthesis — what changes in the v1 design

| # | Change | Trigger persona |
|---|--------|-----------------|
| 1 | OCR confidence threshold + editable "is this what you wrote?" preview before CAS | #1 (student), #8 (accessibility) |
| 2 | One-card privacy disclosure (Hebrew/Arabic/English) + parental consent flow under 16 | #2, #5 |
| 3 | Expandable "show my work" view surfacing the CAS chain — makes the system teacher-auditable | #3 (teacher) |
| 4 | Reflection step: first wrong-step detected → "try again — hint available" before full narration | #4 (education research) |
| 5 | OCR vendor DPA with EU/Israel residency + no-training clause; Israeli DB registration | #5 (compliance) |
| 6 | v1 scope: **detect the first wrong step only.** No whole-derivation narration until v2 | #6 (engineering), #7 (ML safety) |
| 7 | LLM narration sourced from a closed misconception taxonomy, never freeform | #7 (ML safety) |
| 8 | Typed-steps alternative surfaced as default when OCR quality for that student is consistently low | #8 (accessibility) |
| 9 | "This diagnosis seems wrong" dispute flow + support audit view + auto-credit on confirmed errors | #9 (support) |
| 10 | Soft cap 100/mo on Premium → upsell to tutor session; hard cap 300/mo | #10 (CFO) |

## Open flags for user triage

- **Vendor DPA is on critical path.** If Mathpix (or current OCR) doesn't offer Israel/EU residency + no-training, need vendor swap before public launch. Audit within 2 weeks.
- **Misconception taxonomy doesn't exist yet.** Building a curated closed-set mapping of CAS-break-types → misconception templates is 4–6 weeks of math-education SME work. Can't ship v1 without it (per persona #7).
- **Accessibility OCR stratification** needs measurement before public launch — run the OCR layer against a representative handwriting corpus including dysgraphic samples.
- **Pedagogical A/B design.** Is the reflection-step pattern actually producing better learning? Instrument retention of the misconception-fix 3 problems later and compare to non-reflection cohort.

## v1 vs. v2 split

**v1 (ship first):**
- Photo upload, OCR, CAS verification
- First-wrong-step detection with curated misconception template
- Reflection step ("try again")
- Dispute flow, support audit view
- Soft + hard caps
- Privacy card + consent flow
- Typed-steps fallback

**v2 (after data):**
- Multi-step narration of whole derivations
- Teacher assignment integration (grade handwritten work)
- OCR model fine-tuning on collected-with-consent handwriting samples
- Misconception-pattern longitudinal report in parent dashboard

## Cost projection revisited after this review

Per-upload cost unchanged (~$0.03 variable). **Fixed costs added by this review:**
- Vendor DPA negotiation + legal: one-time ~$15–25k
- Misconception taxonomy build: ~$30–40k (4–6 weeks SME + eng)
- Support portal + dispute flow: ~2 dev-weeks
- Parental consent + privacy card: ~1 dev-week + legal review
- Accessibility OCR stratification study: ~2 weeks research + data collection

**Total one-time cost to launch:** ~$70–90k + 4–6 weeks calendar on the critical-path items (vendor DPA + taxonomy).

**Verdict:** feature is defensible and valuable, but the "just plug into the existing ingestion path" framing understates the surrounding work by ~70%. Worth building, but plan it as an epic, not a sprint.
