# COPPA Compliance Statement — Cena Platform (DRAFT)

> **🚨 DRAFT — counsel review required 🚨**
>
> Engineering first-pass for legal counsel review. Not authoritative.
> Required before onboarding any US-resident students under 13.

## Scope

This statement describes Cena's compliance posture with respect to the
US Children's Online Privacy Protection Act (COPPA, 15 U.S.C. §§
6501–6505) and the FTC's COPPA Rule (16 C.F.R. Part 312), as updated
by the 2025 rule amendments on AI-related data practices.

**Current pilot scope**: Israel + UK primary. US students are NOT in
the pilot. This statement is forward-looking; it documents the
posture we're building toward and is not yet operationally tested
against a US user cohort.

## 1. Personal information collected from children under 13

The following data is collected from students identified as under 13:

| Category | Source | Retention |
|---|---|---|
| Email address | Parent-verified account creation | Account lifetime |
| First name | Onboarding | Account lifetime |
| Birth year (not full DOB) | Onboarding age gate | Account lifetime |
| Academic performance | In-product use | 12 months |
| Tutor conversation turns | In-product use | 90 days |
| Misconception inferences | Derived (ADR-0003) | 30 days |
| Stuck-type labels | Derived (ADR-0036) | 30 days |
| Self-assessment (affective) | Onboarding (RDY-057) | 90 days |
| Focus / attention metrics | In-product use | 90 days |

**Not collected from under-13 students**:
- Full date of birth
- Home address, phone number
- Photographs / videos
- Voice recordings (planned for v2; COPPA treatment will need a
  separate verifiable parental consent flow)
- Persistent identifiers for cross-site behavioural advertising
- Location data beyond timezone

## 2. Parental consent mechanism

Cena uses **verifiable parental consent** via the following mechanisms
(whichever applies based on FTC-approved methods):

1. **Signed consent form** — PDF signed by parent + attached at
   enrollment (the "sliding scale" knowledge-based auth for
   low-risk disclosure).
2. **Credit-card / government-ID verification** for higher-risk
   disclosures (e.g., human-tutor messaging, per ADR-0036 notes
   on distinct-consent-axis for human routing).
3. **Email + knowledge-based authentication** matching FTC § 312.5
   requirements.

Parents can:
- Review the data Cena has collected about their child
- Request deletion
- Revoke consent (account closure within 30 days)
- Inspect without fee (up to 1 request per 90 days)

Parent contact: **privacy@cena.edu** (counsel: confirm this mailbox
is active + monitored).

## 3. Direct notice to parents

Before collecting any personal information from a child, Cena:

1. Notifies the parent via email at the address provided during
   registration.
2. Describes what information will be collected, how it will be used,
   and who it will be shared with.
3. Includes a link to this COPPA compliance statement + the
   children's privacy policy (`docs/legal/privacy-policy-children.md`).
4. Requires the parent to consent before any non-login data is
   transmitted.

## 4. Operator obligations

Cena is a COPPA "operator" and:

- **Does not condition participation** on a child providing more
  information than is reasonably necessary to participate.
- **Does not disclose personal information to third parties** except
  as listed below.
- **Maintains reasonable security** per § 312.8 (SOC 2 Type II
  target; TLS 1.2+ in transit; encryption at rest on production DBs;
  access controls with least-privilege).

## 5. Third-party disclosure

Personal information from under-13 students may be disclosed to:

| Recipient | Purpose | Basis |
|---|---|---|
| Anthropic (Claude API) | AI tutor inference | Processor under DPA (see `dpa-anthropic-draft.md`); parental consent at registration |
| Firebase (Google) | Authentication + push notifications | Processor under Firebase DPA |
| PostgreSQL host (cloud provider TBD) | Primary data store | Processor under the cloud DPA |

**No advertising networks, data brokers, or unrelated third parties.**

## 6. AI-specific rule compliance (2025 FTC amendments)

The 2025 amendments require **separate verifiable parental consent**
for certain AI-related data practices. Cena's stance:

| Practice | Covered by base consent? | Separate consent needed? |
|---|---|---|
| AI tutor processes student free-text | Yes (disclosed in base notice) | **No**, per current reading of § 312.5(a)(2) |
| Student data used to train Anthropic's models | **Prohibited** (DPA § 7) | N/A — prohibited outright |
| Disclosure of student data to a human teacher inside the student's assigned classroom | Yes (FERPA school-official exception) | **No** |
| Disclosure of student data to a human teacher OUTSIDE the student's classroom (cross-school escalation) | No | **Yes** — separate consent required per RDY-062 pending investigation |
| Processing "child-mental-state" affective data (self-assessment) | Partial | **Opt-in required**; retention ≤ 90 days default; never to tutor per ADR-0037 |

## 7. Data retention + deletion

- Default retention schedule per §1 above.
- Parent-initiated deletion request: data purged within 30 days.
- Account closure: all student data purged within 30 days except
  where retention is legally required (e.g., audit logs of consent
  grants/revocations — retained 7 years for regulatory evidence).

## 8. Safe harbor / self-certification

Cena is **not yet certified** under any FTC-approved COPPA safe
harbor program. Counsel recommends: evaluate iKeepSafe, kidSAFE, or
ESRB programs post-pilot.

## 9. Governance

- **DPO / Privacy Officer**: TBD (counsel to recommend)
- **Annual review cadence**: COPPA compliance statement + underlying
  controls reviewed annually, or upon material product change.
- **Parent complaints handling**: privacy@cena.edu, 15-day
  acknowledgement target, 30-day resolution target.

## 10. What counsel must fill in / validate

- [ ] Parental-consent mechanism specifics (which method(s) we accept)
- [ ] Safe-harbor enrollment decision
- [ ] DPA exhibits matching this document's §5 table
- [ ] Retention-period justification for each category (privacy
  minimisation principle — are these the minimum needed?)
- [ ] "AI-specific rule" §6 reading against the 2025 FTC amendments
  final text
- [ ] Incident-response notification obligation (72-hour to FTC? To
  state AGs?)
- [ ] Sign-off on parent-rights workflow (how does the SPA surface
  "delete my child's data")

## References

- FTC COPPA Rule: 16 C.F.R. Part 312
- FTC AI-related amendments 2025 — pending final citation
- Cena's children's privacy policy: `docs/legal/privacy-policy-children.md`
- Cena's general privacy policy: `docs/legal/privacy-policy.md`
- DPA (Cena ↔ Anthropic draft): `docs/legal/dpa-anthropic-draft.md`

---

**Status**: DRAFT — awaiting counsel review.
**Last touched**: 2026-04-19 (engineering draft)
