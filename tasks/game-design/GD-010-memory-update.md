# GD-010: Memory update — ship-gate ban, ADR refs, misconception scope rule

## Goal
Update project memory files so future agents (Claude Code, sub-agents, Kimi) load the research-derived non-negotiables on session start instead of re-reading the full research synthesis.

## Source
`docs/research/cena-sexy-game-research-2026-04-11.md` — non-negotiables from the synthesis.

## Files to update
1. `~/.claude/projects/-Users-shaker-edu-apps-cena/memory/MEMORY.md` — add three new memory entries:
   - **ship-gate ban** — reference GD-004 and the banned-terms list
   - **SymPy as correctness oracle** — reference the ADR from GD-001
   - **misconception session scope** — reference the ADR from GD-002
2. `/Users/shaker/edu-apps/cena/CLAUDE.md` — add a new "Design non-negotiables" section that enumerates the three rules with one-line rationale + pointer to the research doc
3. `/Users/shaker/edu-apps/cena/AGENTS.md` — same block, mirrored for cross-CLI agents (Kimi, Codex)

## New memory entries to write
Format matches existing entries in `memory/MEMORY.md`:
- `feedback_shipgate_banned_terms.md`
- `project_sympy_correctness_oracle.md`
- `project_misconception_session_scope.md`

Each is a short (< 300 word) explainer with the rule, the source, and the consequence of violation.

## DoD
- Three new memory files exist
- `MEMORY.md` index updated
- `CLAUDE.md` + `AGENTS.md` updated
- A fresh session start (restart Claude Code) surfaces the new rules within the first auto-loaded context

## Reporting
Complete with branch + list of files touched.
