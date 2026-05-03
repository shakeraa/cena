# ADR-0038 — Event-sourced right-to-be-forgotten via crypto-shredding

- **Status**: Accepted
- **Date**: 2026-04-20
- **Decision Makers**: Shaker (project owner), Architecture
- **Task**: prr-003a
- **Related**: [ADR-0003](0003-misconception-session-scope.md), [ADR-0001](0001-multi-institute-enrollment.md), `pre-release-review/conflicts.md` C-03

---

## Context

GDPR Art. 17 ("right to erasure") and Israeli PPL §14 both require that a controller erase personal data on a data subject's valid request. Cena's state model is event-sourced on top of Marten, which treats the event store as append-only — Marten has no supported API for selective event deletion within a stream, and "just delete the row" breaks any downstream projection replay.

The current `RetentionWorker.cs:334-357` implementation is not a hard-delete. It filters events at read time using a retention horizon, leaves every PII-bearing event physically on disk indefinitely, and cannot honour an ad-hoc per-subject erasure request — only time-based expiry for narrow categories. That is a textbook Art. 17 violation: a data subject who demands erasure today cannot be served by a worker that only deletes events 30 days later via generic retention rules.

We evaluated four design options:

- **A. Crypto-shredding** — encrypt PII fields inside events with a per-subject key; store the key in a key vault; erasure = destroy the key. Events remain, payloads become ciphertext that is mathematically irretrievable.
- **B. Event-stream rewrite** — on erasure, stream the entire aggregate, drop or redact the offending events, write a fresh stream under a new stream ID. Breaks Marten's append-only invariant; every subscriber must be taught to follow stream renames.
- **C. Out-of-band PII store** — keep a pointer in the event, put PII in a sidecar table, delete the row in the sidecar on erasure. Breaks event replay determinism; projections replayed from backup produce different read models than at original time.
- **D. Aggregate rebuild + new stream** — on erasure, zero-out the aggregate's state, re-derive a new minimal stream from audit metadata only. Deletes history, not just PII; destroys fraud-investigation trail.

## Decision

Adopt **Option A — crypto-shredding**, wired through a new `SubjectKeyStore` abstraction. Both `RetentionWorker` and a new `ErasureWorker` call `SubjectKeyStore.Delete(subjectId)` to effect erasure; no event rows are deleted. The Marten event store remains append-only and its replay semantics are preserved.

Per-subject keys are derived via HKDF from a root key held in a KMS/HSM (AWS KMS in production, a dev-only local keyring in CI/dev). The HKDF `info` parameter binds the derived key to a specific subject ID and a specific purpose label, so a single root key compromise does not automatically compromise every subject's data, and re-deriving the same subject key on an unrelated host is not a path to bypass erasure.

### Rejected alternatives

- **B — stream rewrite** was rejected because it violates Marten's append-only contract. Every projection subscriber would have to learn a "stream X is superseded by stream Y" rename protocol, which does not exist in the current infrastructure. Migration would touch every projection in `src/shared/Cena.Infrastructure/Projections/` and every Marten async daemon consumer — vastly larger blast radius than option A. Event replay from a pre-erasure backup would also re-materialise the deleted event by design, leaving a post-restore re-erasure step anyway, so option B shares option A's restore caveat while adding new ones.
- **C — sidecar PII store** was rejected because it destroys event replay determinism. A projection rebuilt from events after a sidecar delete produces a different read model than the original, which breaks any audit-trail argument we need to make to an inspector — the events say X happened, the projection says it didn't.
- **D — aggregate rebuild** was rejected because it is over-broad. A student account holds events that are evidence of platform fraud (e.g. systematic cheating patterns, consent-forgery by a parent, teacher misconduct visible in message traffic). Option D deletes those alongside the PII, not only the PII. We need erasure of *personal data* while preserving structural audit. If PPL legal review later concludes physical deletion is required (see open question 1 below), we fall back to D with a documented acknowledgement that the fraud trail is lost.

## Field classification policy

Within the event schema, a field is PII (and therefore gets encrypted with the subject key) if it satisfies any of:

1. It contains free-form text the subject authored (`StudentAnswer`, chat messages, notes),
2. It contains an identifier that directly names a natural person (`StudentId` where not already hashed, parent email, phone number),
3. It is a structured attribute that, combined with fields already in the event, is re-identifying for a single subject (purpose strings on consent events, accommodation certificates).

Non-PII fields stay plaintext. In particular `ProblemId`, timestamps, enum fields (e.g. `DetectedRuleType`), and integer counters do not carry PII and are left in the clear. This matters for read-model rebuilds that need to run in aggregate without needing every subject key to be live.

Concrete first-pass classification applied to events currently in the system:

| Event | Encrypted fields | Plaintext fields |
|---|---|---|
| `MisconceptionDetected_V1` | `StudentAnswer`, `StudentId` (if not hashed) | `ProblemId`, `BuggyRuleId`, `DetectedAt`, `SessionId` |
| `ConsentGranted_V1` / `ConsentWithdrawn_V1` (prr-155) | `subjectId`, `purpose`, `grantorActorId` | `occurredAt`, event type tag |
| `StudentAccommodationChanged_V1` (prr-044) | `studentSubjectId`, `certificateRef`, `authorizationSource` | `accommodationKind`, `changedAt`, `scope` |
| `MentorChatMessageSent_V1` | `MessageBody`, `AuthorSubjectId` | `SessionId`, `SentAt` |

The full classification table lives alongside the event definitions; any new event type requires a PII classification review at definition time (architecture test forthcoming).

## Key lifecycle

- **Derivation.** `subjectKey = HKDF-SHA256(rootKey, salt=installId, info="cena.subject." + subjectId)`. Deterministic given a live root key — a subject who re-enrols under the same subject ID can decrypt their own historical data until they demand erasure.
- **Storage.** Derived keys are not persisted. They are materialised on demand from the root key and cached for the duration of an in-process request only. The one durable artefact per subject is an `erased` tombstone flag in `SubjectKeyStore`; on `Delete`, we flip the tombstone and refuse all future derivations for that subject.
- **Rotation.** The root key rotation policy is deliberately out of scope for this ADR — rotation interacts with backup semantics in non-trivial ways and deserves its own decision. A follow-up ADR will cover it.
- **Revocation.** Revocation is definitional: `SubjectKeyStore.Delete(subjectId)` flips the tombstone. The subject's ciphertext remains on disk but is no longer decryptable by this installation.

## Audit trail

Every erasure is an append-only audit event recording: a hash of the subject ID (never the ID itself), the wall-clock timestamp, the authorisation source (data-subject request, PPL supervisor order, regulator, internal admin), and the operator/actor that executed the flip. PII content is never reproduced in the audit line. The audit stream itself is stored under a separate Marten tenant and is not covered by subject-level crypto-shred — it is itself legally required record-keeping.

## Backup / restore interaction

Restoring a backup taken before an erasure silently re-materialises the subject's decryptable data, because the restored backup contains both the ciphertext events and the pre-deletion `SubjectKeyStore` row. **Restoring a pre-erasure backup therefore requires a post-restore re-erasure runbook step** that reapplies every tombstone recorded after the backup snapshot. This is a known, documented operational constraint of crypto-shredding; it cannot be designed out at this layer. Runbook: `docs/runbooks/post-restore-re-erasure.md` (to be authored alongside first production restore drill).

## Read-path contract

Any decryption call that encounters a destroyed key, a missing key, or a tombstoned subject returns the sentinel string `[erased]` (for strings) or `null` (for nullable reference types). Callers MUST NOT surface a stack trace, a decryption error, or a "key not found" condition to the end user. Admin tooling that reads erased records sees `[erased]` and is expected to display it as a UI affordance rather than an error. Projections that ignore `[erased]` continue to work; projections that depend on decrypted content degrade to "no data" rather than crash.

## Migration plan

Events persisted before this ADR are unencrypted. We do not retroactively rewrite those events — retroactive rewrite re-introduces option B's append-only violation and blocks deployment. Instead:

1. New event writes from the moment this ADR lands on main go through the encryption pipeline and honour erasure.
2. Pre-ADR events remain in the clear, gated behind a per-event `Schema >= 2` flag so read code can distinguish.
3. Pre-ADR events naturally expire under ADR-0003's 30-day retention window for session-scoped data, and under regulatory caps (COPPA 90 days max, PPL bounded by purpose) for everything else.
4. The transition window is treated as **acceptable residual risk** and documented as such. If the transition window takes longer than 90 days to clear naturally, we revisit.

## Consequences

### Positive

- Marten's append-only contract is preserved; no existing projection or daemon is broken.
- A single key deletion erases the subject across every stream in one atomic operation — no need to hunt down copies across event, projection, and snapshot storage.
- Fraud-investigation audit structure survives erasure: the shape of what happened is still reconstructable, only the personally-identifying contents are not.
- Reversible within the retention window if a subject withdraws an erasure request before the tombstone is processed (we delete the tombstone, the key derivation resumes working). Outside the window there is no recovery path, which is exactly the legal requirement.

### Negative

- Requires per-field PII classification decisions at event-design time, and a code-review culture that enforces them. Classification is judgment work; a future architecture test will cover the obvious cases (any field named `*Answer`, `*Body`, `*Email`, `*Phone` must be encrypted) but cannot cover all.
- Introduces KMS/HSM infrastructure dependency on the hot read path. We mitigate with in-process caching of derived keys, but a KMS outage degrades reads for encrypted fields to `[erased]` — same failure mode as a genuine erasure. This is acceptable because it degrades cleanly rather than crashing.
- Backup-restore-then-re-erasure is a non-trivial operational drill. The runbook has to exist and be rehearsed.

### Neutral

- Marten schema unchanged. Encryption is a wrapper at the JSON serialiser level, not a new column.

## Open questions / pending items

1. **PPL physical-deletion interpretation.** Israeli PPL §14 could be interpreted as requiring actual physical removal of personal data from all media — not merely cryptographic destruction. If legal review confirms that reading, we fall back to option D (aggregate rebuild) for PPL-scope subjects and accept the audit-trail loss. Tracked separately.
2. **Root-key rotation ADR.** Rotation interacts with long-lived ciphertext in non-obvious ways (rotating the root key without re-encrypting old ciphertext means every old subject becomes un-decryptable — worse than erasure, since it's accidental). A dedicated ADR will specify the rotation cadence and the re-encryption strategy. Placeholder: `docs/adr/NNNN-key-rotation-for-crypto-shredding.md`.

## References

- [ADR-0003](0003-misconception-session-scope.md) — session-scope boundary that this ADR operationalises at the crypto layer.
- [ADR-0001](0001-multi-institute-enrollment.md) — multi-institute enrollment; cross-institute erasure inherits from this ADR (one subject, one key, erased in every institute at once).
- `pre-release-review/conflicts.md` C-03 — the conflict record that triggered this decision.
- `docs/tasks/pre-release-review/TASK-PRR-003a.md` — task body.
- GDPR Art. 17 ("right to erasure").
- Israeli PPL §14 (subject rights).
- Marten documentation on append-only event stores and projection replay semantics.
