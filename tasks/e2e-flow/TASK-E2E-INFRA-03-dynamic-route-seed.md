# TASK-E2E-INFRA-03: Dynamic-route seed fixture — plant ids for `[id]`-route coverage

**Status**: Proposed
**Priority**: P0 (blocks ~20 % of admin coverage + per-cell student tests)
**Epic**: Shared infra
**Tag**: `@infra @seed @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/fixtures/dynamic-seed.ts` (new) + per-spec usage
**Prereqs**: `INFRA-01` bus probe (✅ shipped); admin test probe (queue id `t_57d2a2cb8b10`)

## Why this exists

Both EPIC-G admin smoke and the new `EPIC-X-student-pages-smoke.spec.ts` skip every `[id]` route (`/apps/experiments/[id]`, `/apps/user/view/[id]`, `/apps/mastery/student/[id]`, `/apps/moderation/review/[id]`, `/apps/questions/edit/[id]`, `/apps/tutoring/sessions/[id]`, `/parent/dashboard?child=…`, `/tutor/[threadId]`, etc.). That's roughly 12 admin + 5 student dynamic routes — all uncovered.

We can't fix this without a way to plant the right shape of data. Hardcoded ids drift; ad-hoc seeding per spec is fragile.

## What to build

`src/student/full-version/tests/e2e-flow/fixtures/dynamic-seed.ts` exposing a typed seeder:

```ts
export interface DynamicSeed {
  /** Plant a question + return its id; cleaned up at test end. */
  question(opts?: Partial<QuestionShape>): Promise<string>

  /** Plant a tutor thread for the current student; return threadId. */
  tutorThread(opts?: { studentId: string; messageCount?: number }): Promise<string>

  /** Plant a learning session and return sessionId + studentId. */
  learningSession(opts?: { studentId: string }): Promise<{ sessionId: string; studentId: string }>

  /** Plant an experiment; return experimentId. */
  experiment(opts?: { name?: string; variants?: number }): Promise<string>

  /** Plant a moderation queue item; return its id. */
  moderationItem(opts?: { kind: 'question' | 'tutor-message'; status?: 'pending' | 'approved' }): Promise<string>

  /** Plant a parent + child pair; return parentUid + childUid. */
  parentChildPair(): Promise<{ parentUid: string; childUid: string }>

  /** Drop everything seeded by this fixture instance. */
  cleanup(): Promise<void>
}
```

Backed by:

1. Direct admin-api calls for things the admin path already supports (questions, experiments, moderation items)
2. Direct Marten writes via a new admin probe endpoint (`POST /api/admin/test/seed/{type}`) gated by `CENA_TEST_PROBE_TOKEN` — matches the existing read-side probe pattern
3. Firebase emu for parent + child user creation

## Boundary considerations

- Seed each fixture call with a **per-test correlation id** so cleanup is precise (no test deletes another test's data)
- Idempotent — calling `question()` twice returns two distinct ids
- Per-tenant scoped — uses the `tenant` fixture so we don't pollute the default tenant

## Done when

- [ ] Fixture lands in `tests/e2e-flow/fixtures/dynamic-seed.ts`
- [ ] Backend probe endpoints added under `/api/admin/test/seed/*` with token gate
- [ ] Each helper has a unit test in `Cena.Admin.Api.Tests` for the probe endpoint
- [ ] At least one dynamic-route spec uses each helper to prove the integration:
  - `EPIC-G-experiments.spec.ts` plants an experiment, drives `/apps/experiments/[id]`
  - `EPIC-X-student-tutor.spec.ts` plants a thread, drives `/tutor/[threadId]`
  - `EPIC-X-student-session.spec.ts` plants a learning session, drives `/session?id=...`
- [ ] `tests/e2e-flow/README.md` documents the contract

## Why a separate seed pipeline (not just direct curl in each spec)

- Avoids 200-line setup blocks at the top of every dynamic-route spec
- Centralizes cleanup — when a probe-endpoint shape changes, one fixture updates instead of N
- Per-test isolation: each test gets its own seeded ids, so parallelism doesn't cross-pollute
