---
audience: parent
version: v1.0.0 2026-04-21
effective_from: 2026-04-21
supersedes: privacy-policy.md (unversioned draft 2026-04-11)
doc_id: cena-privacy-policy-parent-v1.0.0
reading_level: adult
jurisdictions:
  - GDPR (EU 2016/679)
  - COPPA 2025 Final Rule (US 16 CFR Part 312)
  - PPL Amendment 13 (Israel 5741-1981)
  - FERPA (US 20 USC 1232g) — reference only, institutional customers carry FERPA obligations
status: counsel-review-pending
---

# Cena Privacy Policy — Parent and Guardian Edition

**Status**: draft pending final counsel review. All legal claims are tagged with
their source regulation so counsel can trace each statement back to a specific
rule.

**For**: parents, legal guardians, and any adult acting as a consent authority
on behalf of a learner under 18.

**If you are the learner and you are 13 or older**, read the
[student edition](privacy-policy-student.md) instead. That document is written
for your reading level and covers the subset of rights that belong directly to
you under ADR-0041.

## 1. Who we are and how to contact us

"Cena" (the "Service") is the adaptive learning platform operated by
Cena Platform Ltd.

- **Controller identity** — Cena Platform Ltd. is the data controller for all
  personal data described in this policy.  \[GDPR Art. 4(7), Art. 13(1)(a)\]
- **Data Protection Officer** — contact `dpo@cena.example` for GDPR Art. 37
  inquiries, rights requests, and breach notifications.
  \[GDPR Art. 37(7), Art. 38(4)\]
- **School-tenant controllers** — where Cena is deployed inside an institute,
  the institute is a joint controller for the educational records it holds
  inside Cena. Your school's privacy officer handles FERPA-scope inquiries.
  \[FERPA §99.3, GDPR Art. 26\]

## 2. Legal bases we rely on

Every processing activity on Cena is tied to a specific legal basis.

- **Parental consent for minors under 13 (COPPA VPC)** — operators must obtain
  verifiable parental consent before collecting personal information from
  children under 13. Cena obtains this through the Parent aggregate consent
  flow described in section 5. \[COPPA 16 CFR §312.5(b)\]
- **GDPR Art. 6(1)(a) consent** — applies to optional processing (marketing
  nudges, cross-tenant benchmarking). You can withdraw at any time under
  GDPR Art. 7(3); withdrawal does not affect prior lawful processing.
- **GDPR Art. 6(1)(b) contract** — account authentication and session
  continuity are necessary to deliver the Service to you and your child.
  These do not require separate consent and cannot be withdrawn without
  terminating the account.
- **GDPR Art. 6(1)(f) legitimate interest** — security logging and abuse
  prevention, balanced against your child's fundamental rights per the
  Art. 6(1)(f) balancing test. The balancing test is documented and available
  on DPO request.
- **Israel PPL §8** — data subjects with a connection to Israel rely on PPL
  Amendment 13's reinforced consent-and-purpose framework, which Cena applies
  as a floor (never weaker than GDPR).

## 3. What personal data we collect

We collect the minimum data needed to run an adaptive learning platform
(GDPR Art. 5(1)(c) data minimisation).

| Category | Source | Retention | Legal basis |
|---|---|---|---|
| Account identifiers (email, display name) | Account creation | Account lifetime + 30 days | Contract (Art. 6(1)(b)) |
| Date of birth | Account creation | Account lifetime | Age verification (COPPA, ADR-0041) |
| Learning activity records | Learner interactions | Educational record retention set by institute, default 7 years | Contract + institute FERPA obligations |
| Mastery estimates | Derived from activity | Rebuilt on demand; no separate retention | Contract |
| Misconception signals | Derived during a session | **Session-scoped, 30-day hard cap** (ADR-0003) | Legitimate interest (safety), never for training |
| AI tutor conversation turns | Learner interactions | 90 days; 30 days on request | Contract + safety |
| Device and usage data | HTTP + instrumentation | 30 days | Security, abuse prevention (Art. 6(1)(f)) |

We do **not** collect: biometric data, device fingerprints beyond the narrow
abuse-prevention set, location data, contacts, or media library content. If
this changes, a version bump of this policy with at least 30 days' advance
notice is required before the new category is collected.
\[GDPR Art. 13(3), ICO guidance on re-use for new purposes\]

## 4. Special handling of children under 13

Cena treats all accounts where the date of birth places the learner under 13
as subject to full COPPA protections.

- **No account without parental consent** — the Under13 age band is
  authorization-gated by `AgeBandAuthorizationRules.CanActorGrant`; a
  self-grant by an Under13 learner is refused at the aggregate command layer
  and audited as a denial.  \[COPPA §312.5\]
- **No marketing, no behavioural profiling** — `MarketingNudges` and
  `LeaderboardDisplay` are structurally refused for Under13 regardless of
  parent consent, matching COPPA's §312.8 operational-security floor.
- **No sharing with third parties beyond what is necessary to operate the
  Service** — see section 7 for the short, closed list.
- **Right to review** — you may at any time request and receive the personal
  information Cena has collected from your child, require deletion, and refuse
  further collection. This is the COPPA §312.6(a) parental review right.

## 5. Your consent authority and your child's evolving rights

Cena's consent model is age-banded, matching the evidence-based age gradients
in ADR-0041 and the legal frameworks we are subject to.

- **Under 13 (Under13 band)** — you grant and revoke all non-contract
  processing on your child's behalf. Your child cannot grant consent on their
  own and cannot override your refusal.
- **13 through 15 (Teen13to15 band)** — you retain durable-data consent
  authority (GDPR Art. 8(1) Member State digital-age-of-consent floor; varies
  by country but never lower than 13). Your child may revoke purposes you
  granted, per ADR-0041 "students may always withdraw their own consent".
- **16 through 17 (Teen16to17 band)** — your child holds self-determination
  over non-safety purposes (PPA minor-dignity analysis). You retain
  visibility for safety-duty purposes only. Attempts by you to grant
  non-safety purposes on their behalf are refused.
- **18 and above (Adult band)** — you have no consent authority unless
  your adult child has nominated you through a separate adult-to-adult
  agency contract.

Withdrawing consent is the same shape as granting it: one click, no dark
patterns, no "last chance" mechanics. \[ICO v. Reddit Feb 2026 enforcement
re: withdrawal friction, GDPR Art. 7(3)\]

## 6. What your child sees

At the Teen13to15 band and above, your child has a
[student-visible parent-view](#) showing what you see about them. At the
Teen16to17 band they may veto non-safety visibility purposes. We surface this
transparency right because
minor-dignity research (cited in ADR-0041) shows that opaque parent
surveillance erodes the developmental autonomy that gives education its
value. \[PPA minor-dignity §4b, UNCRC Art. 16\]

## 7. Who we share data with

Cena processes data with the following categories of recipients:

1. **Your institute** — every data point tied to your child that is generated
   inside the institute's tenant is visible to the institute's authorized
   administrators. Tenant scoping is enforced at the HTTP layer
   (`ADR-0001` multi-institute enrollment). A parent at institute X cannot
   read a child at institute Y and vice versa.
2. **Subprocessors under GDPR Art. 28** — Anthropic (AI tutor model provider)
   under the Cena–Anthropic DPA. The DPA is available on request and names
   the subprocessing purpose, the data types, the storage regions, and the
   return-or-destroy clause.
3. **Law enforcement, only under a lawful order** — Cena does not volunteer
   your child's data. Orders that do not meet the GDPR Art. 6(1)(c) or
   jurisdictional equivalent test are refused and documented.

We do **not** sell personal data in any jurisdiction, including the CCPA
"sale" and CPRA "share" meanings. \[CCPA §1798.140(ad), CPRA §1798.140(ah)\]

## 8. International transfers

Personal data may be processed in AWS us-east-1 (default) or eu-west-1
(institute-elected). Transfers from the EEA are covered by:

- **Standard Contractual Clauses (2021/914/EU)** — signed, on file, available
  on request.
- **Transfer Impact Assessment** — updated annually, last reviewed
  2026-01-15.

\[Schrems II decision C-311/18, EDPB Recommendations 01/2020\]

## 9. Your rights

You may exercise the following rights by contacting the DPO. Decisions are
returned within 30 days (GDPR Art. 12(3)); extensions are requested in writing
with a reason.

- **Access** (GDPR Art. 15) — ask what we have about you or your child.
- **Rectification** (GDPR Art. 16) — correct inaccurate or incomplete data.
- **Erasure / right to be forgotten** (GDPR Art. 17 + ADR-0038 crypto-shred
  implementation) — we tombstone the encryption keys for your child's stream;
  ciphertext becomes permanently undecryptable.
- **Restriction of processing** (GDPR Art. 18).
- **Data portability** (GDPR Art. 20) — JSON export of your child's personal
  data, available from the parent console.
- **Object to processing** (GDPR Art. 21).
- **Not be subject to solely-automated decisions** (GDPR Art. 22) — Cena's
  difficulty selection is a pedagogical recommendation; no admission,
  grading, or disciplinary consequence is decided solely by the platform.
- **COPPA parental review** (§312.6) — identical scope to Art. 15 + 17 for
  Under13.
- **PPL §13 access and correction** — the Israeli equivalent of Art. 15–16,
  with the Israeli supervisory authority as the appeal body.
- **Complaint to a supervisory authority** — your Member State DPA, the
  Israeli PPA, or the FTC for COPPA. Contact details are on the DPO's page.

## 10. How long we keep data

- **Educational records** — default 7 years or the institute's policy,
  whichever is shorter. Institutes can shorten this under their own policies.
- **Consent audit trail** — retained for the longer of 7 years or 1 year
  beyond account closure (so that the audit is reconstructible on a complaint
  after the account is gone).
- **Misconception session data** — **30 days maximum**, per ADR-0003. Never
  attached to a student profile. Never used for ML training.

When data reaches its retention cap, it is crypto-shredded (ADR-0038),
leaving ciphertext that is mathematically unrecoverable.

## 11. Security measures

- Encryption at rest (AES-256) and in transit (TLS 1.2+).
- Per-subject key derivation with crypto-shred on erasure (ADR-0038).
- Session revocation list with tokens revocable within 60 seconds of a
  compromise signal.
- Row-level tenant isolation; every cross-tenant probe is refused with
  `CENA_AUTH_IDOR_VIOLATION` and logged at WARNING level.
- Annual penetration test. Most recent report: 2026-01-30.
- Breach notification within 72 hours of confirmed exposure (GDPR
  Art. 33(1)).

## 12. Changes to this policy

- Each publish increments the `version` field in the front matter using
  SemVer-style ordering.
- Material changes (new processing categories, new subprocessors, shortened
  retention that removes a right) require at least **30 days** advance
  notice to accepting parents and a re-acceptance gate.
- Non-material changes (typography, citation corrections) do not require
  re-acceptance; the previous acceptance record remains valid.
- The acceptance record carries the exact `version` string the parent
  accepted; older accepted versions remain queryable in the consent audit
  export.

## 13. If something goes wrong

- **Contact the DPO** — `dpo@cena.example`.
- **File a complaint** — your Member State DPA (GDPR), the Israeli PPA, or
  the FTC (COPPA). Cena does not retaliate against complaint filers and
  never requires you to waive a complaint right to continue using the
  Service.  \[GDPR Art. 77, PPL §23B, FTC Act §5\]
- **Legal counsel's notes on this document** — kept in an internal register
  and released on GDPR Art. 15 request covering your own data. Counsel
  comments on minors other than your own are withheld under institutional
  confidentiality.

---

**Citation key** — every bracketed reference above ties back to a specific
section of the cited regulation or an internal Cena ADR. A counsel review
who disagrees with a claim should cite the paragraph number so we can
update the exact line (policy-as-code).

**Internal references** —
ADR-0001 (multi-institute enrollment),
ADR-0003 (misconception session-scope retention),
ADR-0038 (event-sourced right to be forgotten),
ADR-0041 (parent auth role + age-band matrix),
ADR-0042 (ConsentAggregate bounded context).
