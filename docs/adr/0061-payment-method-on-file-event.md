# ADR-0061 — Payment-method-on-file event + encryption namespace

Status: Accepted
Date: 2026-04-29
Supersedes: —
Related: ADR-0038 (crypto-shred + per-subject keys), ADR-0057 (parent-keyed subscription aggregate), Phase 1D / Phase 1D-fix-2

## Context

The trial-then-paywall flow collects a Stripe SetupIntent at trial start so that conversion-to-paid can charge the saved card without re-prompting the user. Two design questions follow:

1. Where do we persist the Stripe `pm_…` payment-method id between trial-start and conversion?
2. How do we encrypt that id so that GDPR right-to-be-forgotten (RTBF) renders it unrecoverable while we keep the rest of the parent's append-only event stream intact?

The Phase 1D shipping baseline left the question unanswered (`paymentMethodId` discarded). Phase 1D-fix shipped a one-way SHA-256 hash, which is unrecoverable by design (incorrect: conversion needs the original `pm_…` to call Stripe). Phase 1D-fix-2 ships the design recorded here.

## Decision

### Event

We introduce `SubscriptionPaymentMethodAttached_V1` on the parent's existing `subscription-{parentSubjectIdEncrypted}` stream:

```
SubscriptionPaymentMethodAttached_V1(
    string ParentSubjectIdEncrypted,
    string PaymentMethodIdEncrypted,
    string FingerprintHash,
    DateTimeOffset AttachedAt,
    PaymentMethodAttachSource Source);
```

`PaymentMethodAttachSource` is an enum: `TrialStartSetupIntent`, `AccountBillingSetupIntent`, `CheckoutCompletion`. Source-tagging lets analytics distinguish the entry channel without inspecting the originating endpoint.

### Encryption

`PaymentMethodIdEncrypted` is wire-format AES-GCM-256 ciphertext produced by `EncryptedFieldAccessor.EncryptAsync(rawPmId, namespaceKey, ct)` — same primitive used everywhere else in the codebase that encrypts PII (see ADR-0038). The plaintext is the Stripe `pm_…` id; the ciphertext is the on-stream payload. Conversion-to-paid will call `EncryptedFieldAccessor.TryDecryptAsync(ciphertext, namespaceKey, ct)` to recover the original.

### Encryption namespace

The subject id used for key derivation is the parent's stream-key form — i.e. the same `parentSubjectIdEncrypted` that keys the `subscription-{…}` stream. This is the canonical id within the Subscriptions bounded context (see ADR-0057 §2 "parent-keyed").

The Consent bounded context, by contrast, uses plaintext subject ids (Firebase UID) as the key namespace (ConsentCommands convention). The two bounded contexts intentionally use different namespaces because:

1. The Subscriptions context never sees the plaintext form on its hot path — events on `subscription-{…}` carry only the wire-encrypted parent id. Using anything other than the wire form would force a cross-context lookup on every encrypt/decrypt.
2. RTBF is processed by the dedicated `ErasureWorker`, which is responsible for tombstoning the key for whichever id form was used to encrypt. The worker reads the deletion request's `StudentId` field; for parent-keyed subscriptions, the request must specify the wire-encrypted parent id, not the Firebase UID. This is enforced by the parent-erasure pathway's request constructor.

This is a bounded-context design choice, not a limitation.

### Persistence atomicity

`SubscriptionPaymentMethodAttached_V1` is appended via `ISubscriptionAggregateStore.AppendManyAsync(parentId, [trialEvent, pmAttachedEvent], ct)` so that the trial-start and the payment-method capture either both land or neither does. Partial commit would leave a trial without the captured card, breaking conversion.

### Idempotency

The endpoint reads the parent aggregate's `LastAttachedPaymentMethodFingerprintHash` and skips emitting a second `SubscriptionPaymentMethodAttached_V1` if the same fingerprint is already on file (re-run with the same SetupIntent → no event noise). The fingerprint hash is a one-way digest of Stripe's `card.fingerprint` and is acceptable as a comparison key.

### Read-model surfaces

`StudentEntitlementView.HasPaymentMethodOnFile` — boolean flag exposed to consumers. The raw ciphertext is intentionally NOT surfaced on the view (PCI-DSS scope minimisation: only the existence bit, never the id).

`StudentEntitlementProjection` updates `StudentEntitlementDocument.HasPaymentMethodOnFile` for every linked student of the parent on each `SubscriptionPaymentMethodAttached_V1`. The projection load-and-merges so the flag survives event re-ordering on replay.

## Alternatives considered

1. **Hash-only persistence** (Phase 1D-fix iteration 1): rejected — conversion cannot reuse a one-way hash to charge Stripe.
2. **Plaintext persistence** (no encryption): rejected — violates ADR-0038; PCI-DSS scope explosion.
3. **Encrypt under Firebase UID** (Consent-context convention): rejected — would require a wire-form ↔ plaintext lookup table on every encrypt/decrypt; violates Subscriptions' bounded-context independence.
4. **Stripe Customer-id only, fetch pm_id at conversion**: viable but adds a Stripe round-trip at conversion-time; we already have the pm_id at SetupIntent verify time, persisting it is cheaper.

## Consequences

- Conversion-to-paid in Phase 3 will decrypt the persisted pm_id via `EncryptedFieldAccessor.TryDecryptAsync(pmEvent.PaymentMethodIdEncrypted, parentId, ct)` and pass the plaintext to `Stripe.SubscriptionService.CreateAsync(...)`.
- RTBF on a parent must enqueue the erasure request with the wire-encrypted parent id. The `ErasureWorker` then tombstones the key, rendering the persisted pm-id ciphertext undecryptable per ADR-0038 read-path contract.
- The new event must be registered in `SubscriptionMartenRegistration.RegisterSubscriptionsContext` (already done).
- `StudentEntitlementProjection` must handle the new event (already done).
