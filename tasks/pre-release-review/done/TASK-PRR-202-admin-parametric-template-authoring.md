# TASK-PRR-202: Admin — parametric template authoring (CRUD + slot constraints + live preview)

**Priority**: P1
**Effort**: M — 4-6 days
**Lens consensus**: persona-educator, persona-enterprise, persona-a11y, persona-redteam
**Source docs**: `docs/research/cena-question-engine-architecture-2026-04-12.md:§12` (Admin authoring), `docs/research/cena-question-engine-architecture-2026-04-12.md:§4.1`
**Assignee hint**: claude-subagent-admin-ui (admin Vue)
**Tags**: source=pre-release-review-2026-04-20, epic=epic-prr-e, lens=educator+enterprise+a11y
**Status**: Not Started
**Source**: Epic PRR-E, 2026-04-20
**Tier**: mvp

---

## Goal

Authoring UI in the admin app for parametric templates consumed by prr-200's compiler. CRUD list + detail editor with slot constraint DSL, live preview rendering 3 randomly-seeded variants from the current template, CAS verdict per preview variant, and publish workflow gated on "at least one preview variant Verified".

## Files

- `src/admin/full-version/src/views/apps/questions/templates/TemplateList.vue` (new)
- `src/admin/full-version/src/views/apps/questions/templates/TemplateEditor.vue` (new)
- `src/admin/full-version/src/views/apps/questions/templates/SlotConstraintEditor.vue` (new)
- `src/admin/full-version/src/views/apps/questions/templates/TemplatePreviewPanel.vue` (new)
- `src/admin/full-version/src/pages/apps/questions/templates/index.vue` (new)
- `src/admin/full-version/src/pages/apps/questions/templates/[id].vue` (new)
- `src/api/Cena.Admin.Api/Templates/TemplateCrudEndpoints.cs` (new) — list/get/create/update/publish
- `src/admin/full-version/tests/unit/TemplateEditor.spec.ts` (new)

## Non-negotiable references

- ADR-0002 (SymPy oracle) — preview hits the live CAS sidecar, not a stub.
- ADR-0032 (CAS-gated ingestion) — publish path funnels through the same gate as human-authored questions.
- Role matrix — content-author + super-admin only; teacher role 403.
- LaTeX sanitization (§28 engine doc) — all LaTeX fields go through the sanitizer before SymPy sees them.

## Definition of Done

- Template list view: paginated, filterable by (subject, topic, difficulty, methodology, track).
- Template editor: stem (LaTeX), slots (name, type, constraints), answer-derivation strategy, distractor strategy, methodology, track, subject, topic.
- Slot constraint DSL supports: integer range, integer set, rational with bounds, non-zero, coprime, "discriminant non-negative", "product-of-two-distinct".
- Live preview panel: renders 3 seeded variants with per-variant CAS verdict and quality-gate score; seeds are persisted on the template so "preview again" is reproducible.
- Publish action: disabled until at least one preview variant is CAS-Verified and QG ≥ 85; visible reason when disabled.
- Keyboard-navigable, focus-trapped modal for slot editor; aria-labels on every constraint input.
- Constraint DSL parsed via a whitelist grammar — no dynamic-code-execution surface, no `Function` constructor, no runtime-string-to-code path (persona-redteam).
- Admin-supplied LaTeX sanitized before render/CAS (§28 engine doc pipeline).
- Tenant isolation: templates are global content (no tenant scope), but only content-author + super-admin roles access the UI.
- Full sln clean build; Vitest unit tests on TemplateEditor.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-admin-ui --result "<branch>"`

---

## Multi-persona lens review (embedded)

- **persona-educator**: methodology + track required fields — cannot publish without them. Owned here.
- **persona-enterprise**: role matrix enforced; audit log entry on every publish (uses existing audit infra). Owned here.
- **persona-a11y**: editor must meet the admin-app a11y bar (focus indicators, tab order, aria-labels on all slot constraints). Owned here. Axe CI rule added.
- **persona-redteam**: whitelist-grammar DSL parser (no dynamic-code-execution surface); admin-authored LaTeX sanitized before render/CAS. Owned here.

## Related

- Parent epic: [EPIC-PRR-E](./EPIC-PRR-E-question-engine-ux-integration.md)
- Depends on: prr-200 (compiler + schema)
- Consumer: prr-201 (waterfall reads templates), prr-209 (admin heatmap shows template coverage)

## Implementation Protocol — Senior Architect

See [epic file](./EPIC-PRR-E-question-engine-ux-integration.md#implementation-protocol--senior-architect).
