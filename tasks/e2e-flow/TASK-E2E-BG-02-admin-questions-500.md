# TASK-E2E-BG-02: Backend gap — `GET /api/admin/questions/{languages,list}` returns 500

**Status**: Proposed (surfaced 2026-04-27 by EPIC-G admin smoke matrix)
**Priority**: P1
**Epic**: Backend gap fixes (EPIC-E2E-G admin journey)
**Tag**: `@backend-gap @admin-api @questions @p1`
**Owner**: admin-api maintainers
**Surfaced by**: `EPIC-G-admin-pages-smoke.spec.ts` allowlist — both `/apps/questions/languages` and `/apps/questions/list` show 500s on first paint

## Evidence

```
GET /api/admin/questions/languages → 500 Internal Server Error
GET /api/admin/questions list      → 500 Internal Server Error
```

The questions area is one of the highest-touch admin surfaces — it's where Bagrut PDF ingestion (RDY-057) pipes parametric templates into the bank, where translators land for `RDY-004a` translation QA, and where MOD-006 moderation rotates batches. **Both list endpoints down means the entire questions area is unusable today.**

## What to investigate

1. Reproduce:
   ```bash
   TOKEN=$(curl -s -X POST "http://localhost:9099/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key" -H 'Content-Type: application/json' -d '{"email":"admin@cena.local","password":"DevAdmin123!","returnSecureToken":true}' | jq -r .idToken)
   curl -i -H "Authorization: Bearer $TOKEN" "http://localhost:5052/api/admin/questions"
   curl -i -H "Authorization: Bearer $TOKEN" "http://localhost:5052/api/admin/questions/languages"
   ```
2. `docker logs cena-admin-api` filtered for `/api/admin/questions`.
3. Likely root causes (questions touch a wide surface):
   - Marten projection on `QuestionDocument` mis-registered — same pattern as the recent `StudentProfileSnapshot` alias collision claude-code just fixed
   - Marten doc alias collision against an event-sourced version (`QuestionDocument` vs `QuestionDraftDocument`)
   - The `RDY-027 glossary validation` worktree changed query shape but the read handler wasn't updated

## Definition of done

- [ ] Root cause + PR linked
- [ ] Both `GET /api/admin/questions` and `GET /api/admin/questions/languages` return 200 with empty `[]` when bank is empty
- [ ] Pagination contract in the response is consistent with `/apps/questions/list` SPA expectations (the SPA destructures `data.items` + `data.total` per the admin-api convention)
- [ ] Unit test in `Cena.Admin.Api.Tests` for the empty-bank case AND a multi-question case
- [ ] EPIC-G admin smoke allowlist entries for `/apps/questions/languages` and `/apps/questions/list` REMOVED
- [ ] Smoke matrix passes 0 console-error on those two routes

## Blast radius if not fixed

- `RDY-004a` translation QA pipeline integration (claude-1 task referenced in 13d-old directives) cannot ship without /api/admin/questions/languages
- `MOD-006` moderation rotation can't see the raw question list
- Bagrut ingestion (G-01) writes to a table whose admin view 500s, so curators can't review what the pipeline produced
