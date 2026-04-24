# TASK-E2E-E-02: Parent digest — WhatsApp path (Meta direct, PRR-429)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-E](EPIC-E2E-E-parent-console.md)
**Tag**: `@parent @whatsapp @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/parent-digest-whatsapp.spec.ts`

## Journey

Parent opted into WhatsApp → scheduler triggers → `IWhatsAppSender` (meta backend) sends → parent receives (captured by test mock).

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Outbound | Meta API call captured with correct `tenant_id` metadata |
| DB | Delivery row recorded |
| Bus | `ParentDigestDeliveredV1` with `channel=whatsapp` |
| Template | Utility template used (not marketing) |

## Regression this catches

WhatsApp sent when opt-in = email-only; marketing template instead of utility; cross-family template mix-up.

## Done when

- [ ] Spec lands
- [ ] Meta mock server reused with PRR-430 webhook tests
