# Data Processing Agreement — Cena ↔ Anthropic (DRAFT)

> **🚨 DRAFT — NOT LEGALLY BINDING 🚨**
>
> This is a **first-pass draft** prepared by engineering for **legal counsel
> review**. It is NOT executed, NOT binding, and MUST be reviewed + rewritten
> by qualified counsel before signature. Filed as a starting point to save
> billable hours. Counsel owns the final text.
>
> **Reviewers required**: Cena's outside counsel (privacy/data-protection
> specialty) + Anthropic's DPA team.

## 1. Purpose

Cena ("Controller") processes personal data about minors (students aged
4-18) residing primarily in Israel and the UK. Cena uses Anthropic's
Claude API ("Processor") to power the AI tutoring feature described in
§2 below.

This Data Processing Agreement ("DPA") sets out the terms under which
Anthropic processes personal data on behalf of Cena, in compliance with:

- UK GDPR (as retained by the UK) and the UK Data Protection Act 2018
- EU GDPR (Regulation (EU) 2016/679) where applicable
- Israel Privacy Protection Law 5741-1981 (as amended 2024)
- US Children's Online Privacy Protection Act (15 U.S.C. § 6501–6505)
  where US students are onboarded (currently not in scope for pilot;
  listed for forward compatibility)
- FTC v. Edmodo consent decree (aligning boundaries on "Affected Work
  Product" for ML training on child data)

## 2. Subject matter + duration of processing

**Subject matter**: Real-time natural-language tutoring interactions
between Cena students and the Claude API. Cena sends a prompt (including
student-authored free text) and receives a completion. Completions are
post-filtered by Cena's CAS oracle (SymPy) before display to the
student.

**Duration**: For the term of the commercial services agreement
between Cena and Anthropic. Processing of an individual record is
bounded by Cena's retention schedule (§6 below).

## 3. Nature + purpose of processing

- Generate pedagogical scaffolding text given a student's attempt on a
  math problem.
- Generate follow-up hints / worked-example continuations bounded by
  Cena's CAS-verified answer set.
- No processing for ad targeting, profiling, or product-improvement
  training. See §7 for the explicit no-training restriction.

## 4. Types of personal data + categories of data subjects

### Data subjects

- Students aged 4-18 (primary; under-13 where relevant)
- Teachers (adult users with administrative access)
- Parents (adult users with guardian access; email-only)

### Data types transmitted to Processor

| Category | Examples | Special-category? |
|---|---|---|
| Identifier (pseudonymous) | `studentAnonId` (HMAC hash) | No |
| Academic | Student's attempt text on a math problem; tutor conversation turns | No |
| Affective / behavioural | Inferred misconceptions (ADR-0003 scope); stuck-type diagnosis labels (ADR-0036) | **Possibly** — may be treated as "data on mental wellbeing" under some regimes |
| Free-text (student) | Onboarding self-assessment (RDY-057) — never sent to Claude per ADR-0037 | Prohibited from sharing per ADR-0037 |

### Data types NOT transmitted

- Real name, email, school name, phone, address, date of birth
  (scrubbed by `TutorPromptScrubber` before any prompt reaches the
  Processor)
- Government IDs, parent names (same scrubber)
- Location data beyond timezone
- Financial / payment data

## 5. Obligations of the Processor

Processor (Anthropic) shall:

1. **Act only on documented instructions** from Controller, including
   the `/prompt` structure Controller publishes via the API.
2. **Ensure persons processing data commit to confidentiality** —
   standard employment contract language.
3. **Implement appropriate technical + organizational measures**
   meeting Art. 32 GDPR:
   - TLS 1.2+ in transit
   - Encryption at rest on logs + retained requests
   - Access controls with least-privilege + annual review
   - SOC 2 Type II or equivalent
4. **Engage sub-processors only with prior authorization** and pass
   through equivalent DPA terms. Current authorised sub-processors
   listed at ANTHROPIC_SUBPROCESSORS_URL (to be filled by counsel).
5. **Assist Controller in responding to data-subject requests**
   (access, rectification, deletion) within 30 days of request.
6. **Notify Controller of personal data breaches** without undue
   delay and in any case within 48 hours of awareness.
7. **Delete or return personal data** at Controller's choice within
   30 days of termination of the commercial agreement, subject to
   Anthropic's retention for legal/audit purposes per §6.

## 6. Retention

- **At the Processor**: Cena requests Anthropic's "zero data retention"
  (ZDR) tier where available. Where ZDR is not available, retention
  not to exceed 30 days from request.
- **At the Controller**: Per Cena's retention schedule (see
  `docs/legal/privacy-policy.md`):
  - Tutor conversation turns: 90 days
  - Misconception data (ADR-0003): 30 days
  - Stuck-type diagnoses (ADR-0036): 30 days
  - Self-assessment data (ADR-0037): 90 days or until opt-out

## 7. No ML training — critical restriction

Processor (Anthropic) **shall NOT**, under any circumstance:

1. Use Controller's transmitted data to train, fine-tune, RLHF, or
   otherwise improve any model that Anthropic makes available to
   other customers or to the public.
2. Use Controller's transmitted data to produce any "Affected Work
   Product" per the FTC v. Edmodo consent decree (model outputs
   trained on children's data).
3. Manually review transmitted data except for: (a) abuse / safety
   classifier triggers, (b) Controller-initiated bug reports.

Anthropic's existing enterprise DPA language on this axis is
acceptable; this §7 MUST be re-confirmed against current terms at
signature time.

## 8. International data transfers

Where personal data is transferred outside the UK/EEA:

- Controller + Processor rely on UK IDTA (International Data Transfer
  Agreement) or the EU Standard Contractual Clauses (2021 module 2),
  attached as Annex 1.
- For Israeli data subjects: Israel has an adequacy finding from the
  European Commission; no additional instrument required for transfer
  to the UK/EEA. US transfers rely on the Israel-US data transfer
  framework (Amended Privacy Law 2024 § 36).

## 9. Audit rights

Controller may audit Processor's compliance with this DPA:

- Upon reasonable notice (≥30 days)
- Not more than once per year unless a breach has occurred
- Through a qualified independent auditor acceptable to Processor
- At Controller's expense, except where the audit reveals material
  non-compliance

## 10. Liability

As per the main commercial services agreement. This DPA does not
alter the liability caps or carve-outs therein, except that Processor
warrants:

- Unlimited liability for: wilful misconduct, breach of §7 (no
  ML-training restriction), breach of §4 scrubbing expectations.
- Capped liability (per the main SA) for: all other matters.

## 11. Governing law

- **Primary**: England & Wales (aligns with UK-GDPR).
- **Secondary**: Israel Privacy Protection Law for Israeli data
  subjects (Law 5741-1981).

Disputes subject to arbitration per the main commercial agreement.

## 12. Schedule — items counsel must fill in

- [ ] Anthropic legal entity name + registered address
- [ ] Cena legal entity name + registered address
- [ ] Effective date
- [ ] Notification contacts (breach + DSR) on both sides
- [ ] List of authorised sub-processors (Annex A)
- [ ] SCCs / IDTA module 2 attached as Annex 1
- [ ] Anthropic's current ZDR offering confirmation
- [ ] Edge cases: what happens if Anthropic's Acceptable Use Policy
  classifier triggers + reviews a student's free-text?

## Engineering notes (not part of legal text)

- The `TutorPromptScrubber` at
  `src/actors/Cena.Actors/Tutor/TutorPromptScrubber.cs` is the
  operational guarantee for §4. Every prompt-build path MUST run
  through it. An architecture test exists; a breach opens GDPR Art
  5(1)(c) data-minimization exposure.
- The CAS oracle (ADR-0002) is NOT a privacy control — it's a
  correctness control. Don't conflate.
- Anthropic's current enterprise API supports a `no_log` / ZDR
  setting documented at [anthropic.com/docs/build-with-claude/retention].
  Counsel should confirm it covers the enterprise plan Cena is on.

---

**Status**: DRAFT — awaiting counsel review.
**Last touched**: 2026-04-19 (engineering draft)
**Owner for counsel review**: TBD (needs to be assigned)
