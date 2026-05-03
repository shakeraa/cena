# TASK-PRR-209: Admin — content-coverage heatmap (topic × difficulty × methodology × track)

**Priority**: P1
**Effort**: M — 4-6 days
**Lens consensus**: persona-educator, persona-enterprise, persona-a11y, persona-finops, persona-sre
**Source docs**: Epic PRR-E, prr-201 (projection)
**Assignee hint**: claude-subagent-admin-ui (admin Vue)
**Tags**: source=pre-release-review-2026-04-20, epic=epic-prr-e, lens=educator+enterprise+a11y
**Status**: Not Started
**Source**: Epic PRR-E, 2026-04-20
**Tier**: mvp

---

## Goal

Admin + teacher-facing heatmap that reads prr-201's coverage projection and shows per-cell ready-variant count against the prr-210 SLO. Drives authoring priorities. Teacher role sees a cohort-scoped read-only view; content-author and super-admin see authoring-scope with drill-through to the template editor (prr-202) or curator-task queue.

## Files

- `src/admin/full-version/src/views/apps/coverage/CoverageHeatmap.vue` (new)
- `src/admin/full-version/src/views/apps/coverage/CoverageCellDrawer.vue` (new) — drill-through per cell
- `src/admin/full-version/src/pages/apps/coverage/index.vue` (new)
- `src/api/Cena.Admin.Api/Coverage/CoverageStatusEndpoint.cs` (from prr-201)
- `src/admin/full-version/tests/unit/CoverageHeatmap.spec.ts`

## Non-negotiable references

- ADR-0001 (tenant isolation) — teacher view scoped to teacher's cohort tenant; cross-tenant access = 403.
- Role matrix — content-author + super-admin see authoring-scope; teacher sees cohort read-only.
- k-anonymity floor k=10 (per prr-026) when surfacing aggregates to teacher role — cells with < 10 students suppressed in aggregate view.
- prr-211 shipgate scanner — no cell label copy may introduce banned vocabulary (no "streaks", no "behind schedule" shame copy).

## Definition of Done

- Heatmap axes: rows = (subject × topic); columns = difficulty × methodology × track (four columns per difficulty: Halabi-4u, Halabi-5u, Rabinovitch-4u, Rabinovitch-5u).
- Cell state: green (≥ SLO), yellow (1 ≤ count < SLO), red (count = 0). Shape/pattern distinguishes state (not color alone).
- Cell click: drawer shows (a) current ready count, (b) in-waterfall-stage count, (c) drop reasons from last waterfall run, (d) linked template (if one exists) → prr-202 editor, (e) linked curator task (if one exists) → curator queue.
- Filter bar: (subject, topic, methodology, track, status); preserves filter in URL.
- Read model latency: p99 ≤ 200ms for the full grid (heatmap is a projection, not a live query).
- Teacher view: read-only, cohort-scoped, k-anonymity floor applied; no template/curator drill-through affordance.
- Axe CI pass.
- Full admin-web build clean.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-admin-ui --result "<branch>"`

---

## Multi-persona lens review (embedded)

- **persona-educator**: teacher-facing cohort view owned here; methodology + track axes owned here.
- **persona-enterprise**: tenant isolation + role matrix enforced. Owned here.
- **persona-a11y**: shape + color for cell state; keyboard-navigable grid; aria-labels describing (topic, difficulty, methodology, track, count). Owned here.
- **persona-finops**: no LLM calls in this view — heatmap is read-only over projection.
- **persona-sre**: projection-backed p99 ≤ 200ms; rebuild cadence documented.

## Related

- Parent epic: [EPIC-PRR-E](./EPIC-PRR-E-question-engine-ux-integration.md)
- Depends on: prr-201 (projection + endpoint)
- Adjacent: prr-210 (SLO definition)

## Implementation Protocol — Senior Architect

See [epic file](./EPIC-PRR-E-question-engine-ux-integration.md#implementation-protocol--senior-architect).
