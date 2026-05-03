# Descoped tasks — rationale

These 25 task files were moved here by the 2026-04-20 reorganization pass (Option D — MVP slice + honest de-scope).

## Rule applied

A task was descoped if **all** of the following were true:

1. Priority is P2.
2. Zero downstream tasks reference its output/files (no dependency edges into it from tasks.jsonl, retired.md, or conflicts.md).
3. Not guarded by a privileged lens: none of `persona-privacy`, `persona-redteam`, `persona-ministry` appeared in `lens_consensus`. Those three lenses carry legal/compliance weight and the senior-architect policy is to never descope them silently.
4. No non-negotiable reference (`ADR-0001`, `ADR-0002`, `ADR-0003`, `#8 no stubs`, `500-LOC rule`, `RDY-080`, `GD-004`, `ADR-026`, `evidence-integrity`, `dark-patterns`).
5. Title matches at least one of the descope-marker families:
   - pure copy / rename / framing-only edits
   - "retire proposal X" that merely removes a doc without a code change
   - meta/audit items ("re-audit", "re-bucket", "carry-forward critique", "synthesis note")
   - vanity/polish with no user-visible privacy, safety, or integrity consequence
   - Epic-D narrow-copy rules where adding the rule **is** the Epic-D work (the task file is redundant with the Epic)

## Full list

See [`../../../pre-release-review/reviews/descoped-log.md`](../../../pre-release-review/reviews/descoped-log.md) for the authoritative per-task reason table (ID, title, lens, reason).

## Reverting a descope

If a future session decides a descoped task must ship pre-launch, `git mv` the file back to `tasks/pre-release-review/` and flip its `**Tier**:` frontmatter line to `mvp`. Update `tasks.jsonl` similarly (change `"tier":"descoped"` → `"tier":"mvp"`) and regenerate the README counts.
