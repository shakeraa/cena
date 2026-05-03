# Cena Review v2 — Re-Verification + Expanded Authorities (7 agents)

Evidence-based re-audit of the Cena project. This version adds (a) a
fix-verification preflight that validates previously-closed findings, and
(b) two new authorities — Child Safety & Privacy Compliance, and QA/Tests
& Regression — to cover domains that the v1 5-agent sweep missed.

Spawn ALL 7 agents in ONE message via Claude Code's Task tool with
run_in_background: true. Do NOT poll status after spawning — wait for
results. Findings without evidence are discarded at merge.

---

## Stack ground truth (do not rediscover)

- **Backend**: .NET, DDD with bounded contexts, Marten (Postgres event
  store), NATS bus between Cena.Admin.Api.Host / Cena.Student.Api.Host
  and the Actor Host, SignalR for realtime. **NOT GraphQL. NOT raw
  WebSockets.**
- **Admin UI**: Vuexy Vue 3 at `src/admin/full-version/`, Firebase Auth
  project `cena-platform`, dev port 5174. Primary color `#7367F0` is
  **LOCKED** — do not flag it. Fix contrast via usage pattern only.
- **Student web**: Vue, separate host.
- **Mobile**: Flutter (planned, not built — do not review Flutter code).
- **Languages**: English primary, Arabic + Hebrew secondary (Hebrew
  hideable outside Israel). RTL must actually render RTL.
- **Question bank**: event-sourced, versioned, multi-lang stored AS
  versions, AI-generated prompts persisted in event payloads, auto
  quality gate.
- **Users**: learners are **minors** — child-safety law applies (COPPA,
  GDPR-K, UK-ICO Children's Code, FERPA for US schools, Israel Privacy
  Protection Law). This is a hard constraint, not a nice-to-have.
- **Task queue**: `.agentdb/kimi-queue.db` via `node .agentdb/kimi-queue.js`.

## Non-negotiable rules (user-enforced, from memory)

1. **NO stubs.** Hardcoded objects, `Ok(new {})`,
   `NotImplementedException`, `// TODO` in a happy path, canned AI
   responses — all P0.
2. **Labels must match the data.** Button text, API route, handler name,
   DB column, projection field — all must describe the same thing.
3. **Verify E2E.** Query the DB, hit the endpoint, render the UI, compare
   payloads against schemas, confirm tenant scoping actually filters.
4. **Fix-forward.** Every P0/P1 finding MUST be filed as a task in
   `.agentdb/kimi-queue.db`. "Documented as remaining issue" is forbidden.
5. **Root cause, not symptom.** Trace the full data flow before fixing.
6. **No fake fixes.** A label change that hides a bug is P0 on re-verify.

## Evidence requirements (enforced at merge)

Every finding must include at least ONE of:
- `grep` / ripgrep output with file:line
- curl/HTTP request + actual response body
- DB query + actual row output
- Screenshot via Playwright or Chrome DevTools MCP
- Git blame SHA + commit subject
- Accessibility audit output (axe, Lighthouse)
- Published citation (pedagogy only): authors, year, venue, DOI or ISBN
- Test file path + test name + pre-fix failure proof (QA agent only)

Findings marked "looks like", "probably", "might be", or "should
investigate" are discarded.

---

## PHASE 0 — Re-verification preflight (coordinator, runs BEFORE spawning)

This phase runs in the main Claude Code session. Do NOT delegate it to a
sub-agent. It must complete before Phase 1 agents are spawned.

### 0.1 Load prior state

Confirmed reality (as of 2026-04-11):
- Prior merged report: `docs/reviews/cena-review-2026-04-11.md`
- Per-agent evidence files (where the reproduction commands live):
  `docs/reviews/agent-1-arch-findings.md`,
  `docs/reviews/agent-2-security-findings.md`,
  `docs/reviews/agent-3-data-findings.md`,
  `docs/reviews/agent-4-pedagogy-findings.md`,
  `docs/reviews/agent-5-ux-findings.md`
- Prior review screenshots: `docs/reviews/screenshots/`
- Prior findings enqueued as `FIND-<lens>-<nnn>` (e.g. `FIND-arch-001`,
  `FIND-sec-001`, `FIND-data-001`, `FIND-pedagogy-001`, `FIND-ux-001`)
- All 53 FIND-* tasks from the prior run currently have `status=done`

```bash
mkdir -p .claude/worktrees/reverify-preflight

# Locate the most recent merged review
ls -1t docs/reviews/cena-review-*.md | head -1

# Dump all tasks, filter to FIND-* in post-processing (kimi-queue list
# does not support --title-prefix; use --json and grep)
node .agentdb/kimi-queue.js list --status all --json \
  | jq '[.[] | select(.title | startswith("FIND-"))]' \
  > .claude/worktrees/reverify-preflight/prior-findings.json

# Closed subset
node .agentdb/kimi-queue.js list --status done --json \
  | jq '[.[] | select(.title | startswith("FIND-"))]' \
  > .claude/worktrees/reverify-preflight/closed-findings.json
```

### 0.2 Build a verification manifest

For each closed FIND-* task, join three sources of truth:

1. **Queue row** — `node .agentdb/kimi-queue.js show <task-id> --json` gives
   title, body, priority, worker, result string, timestamps.
2. **Per-agent findings file** — grep `docs/reviews/agent-*-findings.md`
   for the `FIND-<lens>-<nnn>` ID to pull the evidence block (file:line,
   grep commands, curl commands, screenshots referenced). The v1 run
   stored these at `docs/reviews/agent-1-arch-findings.md` (arch lens),
   `agent-2-security-findings.md` (sec), `agent-3-data-findings.md`
   (data), `agent-4-pedagogy-findings.md` (pedagogy), and
   `agent-5-ux-findings.md` (ux).
3. **Fix commit** — from the task result string or branch name, locate
   the merge commit to `main` and get its diff for the `qa` agent.

Write to `.claude/worktrees/reverify-preflight/verification-manifest.yaml`:

```yaml
- finding_id: FIND-sec-001
  prior_task_id: t_27a595bd9212
  prior_severity: p0                    # "critical" in queue → p0
  lens: sec
  closed_at: <timestamp from queue row>
  closed_by: <worker from queue row>
  fix_commit_sha: <from git log --grep "FIND-sec-001">
  fix_files: [src/.../LeaderboardService.cs]
  original_evidence:
    - type: grep
      command: "rg '\\$@\"' src/Cena.Admin.Api/LeaderboardService.cs"
      expected_before: "7 matches"
      expected_after:  "0 matches"
  reproduction_steps:
    - step: 1
      action: |
        Re-run the grep command and count matches.
      expect: 0
    - step: 2
      action: |
        Inspect every surviving SQL call in LeaderboardService.cs for
        parameter binding (NpgsqlParameter or LINQ-to-Marten).
      expect: "all reads use parameters or LINQ"
```

Repeat for all 53 closed FIND-* tasks. The manifest is the input to
Phase 0.3.

### 0.3 Run the verification gate

For each entry in the manifest, the coordinator (not a sub-agent) runs
the reproduction command and classifies the result:

| Label | Meaning | Action |
|---|---|---|
| `verified-fixed` | Bug is gone, evidence confirms | Log, move on |
| `regressed` | Was fixed, is broken again | **New P0 task**, reference prior FIND-id |
| `partially-fixed` | One code path fixed, another still broken | **New P1 task** |
| `moved` | Fix pushed the bug elsewhere | **New P1 task**, cross-reference |
| `fake-fix` | Label/symptom changed, root cause intact | **New P0 task**, flag as regression-of-trust |

Write results to
`docs/reviews/reverify-<YYYY-MM-DD>-preflight.md` with counts per label
and a table of all `fake-fix` / `regressed` items — these are the most
important findings of the entire review.

### 0.4 Only then spawn Phase 1

Phase 1 agents receive the preflight report as context in their prompt so
they don't re-discover already-verified fixes. Agents should treat
`verified-fixed` areas as *lower-priority for drill-down*, not skipped —
surrounding code may still have new bugs.

---

## PHASE 1 — The 7 agents (ONE message, parallel, background)

Agents are named by **lens**, matching the prior review convention:
`arch`, `sec`, `data`, `pedagogy`, `ux`, **`privacy`** (new), **`qa`** (new).

Each agent runs in its own worktree:
```bash
git worktree add .claude/worktrees/review-<lens> \
  -b claude-subagent-<lens>/cena-reverify-<date> origin/main
```

Each agent must read `.agentdb/AGENT_CODER_INSTRUCTIONS.md` first and
accept the preflight report at `docs/reviews/reverify-<date>-preflight.md`
as input context.

Finding IDs MUST follow the prior convention: `FIND-<lens>-<nnn>`
where `<lens>` ∈ {arch, sec, data, pedagogy, ux, privacy, qa}. Numbering
continues from the highest existing ID in that lens (e.g. if the prior
review ended at `FIND-arch-012`, v2 starts at `FIND-arch-013`).

### Agent `arch` — System, Contract & Event-Schema Architect

Map every bounded context, REST route, NATS subject, SignalR hub, Marten
projection. Cross-reference declared vs wired. **Expanded for v2**: also
audit event schema evolution since event-sourced aggregates cannot
retroactively change event shape without a replay strategy.

Hunt for:
- Endpoints registered but never called by any UI
- UI calling endpoints that don't exist server-side
- NATS publishers with no subscribers (and vice versa)
- Event types duplicated across bounded contexts (leakage)
- Controllers bypassing aggregates and hitting `IDocumentSession` directly
- Aggregates writing events no projection reads
- `throw new NotImplementedException`, `return Ok(new {})`, `// TODO`,
  `// STUB`, `FIXME`, `canned`, `fake` in any endpoint
- Missing tenant scoping on any query touching Marten
- **NEW**: Event schema changes since last review without a versioning
  strategy (upcaster, dual-write, replay)
- **NEW**: Feature flags / kill-switches for newly-fixed code paths so
  rollback is possible without redeploy

Output: `docs/reviews/agent-arch-reverify-<date>.md`

### Agent `sec` — Security, Auth, Infra & Observability

Trace every auth path: Firebase ID token → backend verification → tenant
resolution → row-level filtering. Validate secrets, CORS, rate limits.
**Expanded for v2**: observability — can the fixes be watched in prod?

Hunt for:
- State-mutating endpoints reachable without `[Authorize]`
- Tenant ID sourced from request body/query/header instead of verified
  JWT claim
- Firebase ID tokens accepted but not verified against `cena-platform`
- SignalR hubs without connect-time auth
- Hardcoded secrets, connection strings, API keys (check git log too)
- CORS wildcards outside Development environment
- Missing rate limits on AI-backed endpoints (cost exposure)
- SQL injection vectors even through Marten LINQ
- **NEW**: Fixed bugs that have no structured log emitted on the error
  path — a silent re-regression would not be detectable
- **NEW**: Missing metrics on hot paths (request count, error rate,
  latency p50/p95/p99)
- **NEW**: No alerting rule for the specific regression class that was
  just fixed
- **NEW**: Error-budget burn — fixes that increased error rate
- **NEW**: REV-001: Firebase service account key rotation status
  (known pending item in memory)

Must produce: actual curl output showing auth behavior for each sensitive
route, plus a log/metrics coverage matrix.

Output: `docs/reviews/agent-sec-reverify-<date>.md`

### Agent `data` — Data, Performance, Projections & Cost

Read every Marten projection, every query, every `IAsyncDocumentSession`
usage. Prove the event-sourced question bank replays correctly.
**Expanded for v2**: cost guardrails on AI endpoints.

Hunt for:
- N+1: list endpoints that fetch a parent then iterate children
- Projections that diverge from their source event streams (pick 3
  largest, rebuild, diff)
- Missing indexes on tenant_id, question_id, student_id, user_id
- Write aggregates that also read (CQRS violation)
- AI prompts stored as plain strings instead of versioned event payloads
- Question versions that cannot be replayed to reconstruct current state
- Unbounded queries (no pagination / no take limit)
- **NEW**: AI-backed endpoints without per-tenant and global rate caps
- **NEW**: AI-backed endpoints without cost metering / token accounting
- **NEW**: AI prompts cached by content hash? Same prompt hitting the
  LLM twice in a session is a cost bug
- **NEW**: Firebase Auth verification doing a network call per request
  instead of using a public-key cache
- **NEW**: Fixes that added queries but no matching indexes
- **NEW**: Performance regression vs. the numbers in the prior review

Must produce: EXPLAIN ANALYZE output for top 10 hot queries, row counts
per tenant, event stream lengths, projection rebuild timings, AI call
volume per endpoint per day, cost-per-learner-per-month estimate.

Output: `docs/reviews/agent-data-reverify-<date>.md`

### Agent `pedagogy` — Pedagogy, Learning Science & i18n/Content

Audit every learner-facing feature against CITED research. Highest
hallucination risk — enforce citations hard. **Expanded for v2**: i18n
correctness (EN/AR/HE) including RTL and the Hebrew-hideable flag.

Every pedagogical claim MUST cite a real published source: authors, year,
venue, DOI or ISBN. If the agent cannot cite, it marks the finding
"UNSOURCED — discard" and moves on.

Hunt for:
- Assessment items with no alignment to a stated learning objective
- Feedback that says only "correct/incorrect" with no explanation
  (violates Black & Wiliam 1998 formative-assessment research)
- Difficulty progression with no scaffolding (Vygotsky ZPD)
- "Spaced repetition" claims with no actual spacing algorithm in code
- Mastery thresholds that are product guesses, not research-backed
- Gamification that rewards speed over correctness
  (Deci 1971 — undermining intrinsic motivation)
- Cognitive load violations on mobile viewports (Sweller 1988)
- **NEW**: Hardcoded English strings (not extracted to i18n catalogs)
- **NEW**: Arabic/Hebrew content rendered LTR
- **NEW**: Date/number/currency locale not switching with UI language
- **NEW**: Pluralization bugs (Arabic has 6 plural forms; English has 2)
- **NEW**: The "hide Hebrew outside Israel" flag actually working —
  test with geo spoofed both ways
- **NEW**: Question bank multi-lang versions diverging in content meaning
  (translation drift across versions)

Must produce: screenshots of each feature in EN, AR, HE via Playwright MCP,
file:line of implementation, source citation backing each critique.

Output: `docs/reviews/agent-pedagogy-reverify-<date>.md`

### Agent `ux` — UX, Accessibility & Broken-Workflow Auditor

Walk every user journey end-to-end as a real user using the Playwright or
Chrome DevTools MCP. Click every button. Submit every form. Assert every
response. **Expanded for v2**: WCAG 2.2 AA hard-required.

Start with the admin app at http://localhost:5174 and the student web
host.

Hunt for:
- Buttons that fire no network request at all
- Buttons that fire a request but the UI ignores the error
- Forms that accept input and silently drop it
- "Save" that returns 200 but doesn't persist (verify via DB query)
- Loading spinners that never resolve
- Empty states that never transition to populated
- Labels in one language while data is in another
- Firebase Auth redirects that loop
- SignalR connections that drop and don't reconnect
- Label drift: button text ≠ API route ≠ DB column being written
- **NEW — WCAG 2.2 AA**:
  - Contrast ratios (respect the #7367F0 lock — fix via usage, not palette)
  - Keyboard navigation: every interactive element reachable via Tab
  - Focus visible and not trapped
  - ARIA roles, labels, and live regions on dynamic content
  - Screen-reader dry-run (VoiceOver or NVDA equivalent) of the 3 most
    critical learner flows
  - Form errors announced, not just colored
  - Touch targets ≥ 44×44 CSS px on mobile
  - Reduced-motion support
  - Hidden content not reachable by assistive tech
- **NEW**: Lighthouse accessibility audit ≥ 95 on admin + student entry
  pages (artifact attached)
- **NEW**: axe-core DOM scan on each of the top 10 pages (artifact
  attached)

Must produce: screenshots per step, network request dumps, console error
logs, mobile viewport (375x812) screenshots, Lighthouse reports,
axe-core output.

Output: `docs/reviews/agent-ux-reverify-<date>.md`

### Agent `privacy` — Child Safety, Privacy & Compliance (NEW in v2)

Cena serves minors. This agent audits the platform against child-focused
privacy and safety law and produces a compliance gap report.

Frameworks in scope:
- **COPPA** (US, under 13)
- **GDPR-K / Article 8** (EU, age of digital consent varies 13–16 by
  member state)
- **UK ICO Age-Appropriate Design Code** (Children's Code, 15 standards)
- **FERPA** (US schools handling student education records)
- **Israel Privacy Protection Law 5741-1981** + 2024 amendments
- **Saudi PDPL** and **UAE Federal Decree-Law 45/2021** if marketed there

Hunt for:
- Account creation flow without verifiable parental consent where
  required
- Data minimization violations: fields collected that aren't strictly
  necessary for the learning function
- Retention policy absent or exceeding what's documented
- Advertising SDKs, tracking pixels, third-party analytics that profile
  minors (Google Analytics, Meta Pixel, TikTok Pixel, Hotjar, etc.)
- Behavioral profiling / nudge mechanics that target minors
  (ICO Children's Code standard 12)
- Default settings that are NOT high-privacy for minors
  (ICO standard 3 — "privacy by default")
- Geolocation collected without justification
- Chat / UGC features without moderation or reporting
- Data shared cross-border without a lawful basis
- Right-to-erasure endpoint missing or broken
- Data subject access request (DSAR) endpoint missing or broken
- Breach notification runbook missing
- DPIA (Data Protection Impact Assessment) artifact missing
- Firebase Auth configuration: email enumeration, password reset,
  MFA policy appropriate for a minor-serving product
- Terms of Service and Privacy Policy readable at a child-appropriate
  level (ICO standard 7 — "transparency")
- Age gate present and not trivially bypassable
- Processor agreements (DPAs) with every vendor touching student data
- US state laws: SOPIPA (CA), NY Ed Law 2-d, Illinois SOPPA, Texas HB 18
  — spot-check if US launch is in scope

Must produce:
- Compliance gap matrix (framework × requirement × status × evidence)
- Screenshot of the age gate and consent flow
- Network tab dump showing every third-party call on the
  unauthenticated home page and the post-login learner page
- List of every DB column storing learner PII with its retention and
  purpose

Output: `docs/reviews/agent-privacy-reverify-<date>.md`

### Agent `qa` — QA, Tests & Regression Suite (NEW in v2)

A fix without a test will regress. This agent's job is to prove every
closed finding from Phase 0 has a test that would have caught it, and
that the test actually fails on the pre-fix commit.

Hunt for:
- Closed findings with ZERO test added in the fix commit
- Tests added but not wired into CI
- Tests that pass on the buggy commit (don't actually exercise the bug)
- Tests marked `[Skip]`, `[Ignore]`, `.only`, `.skip`, `xit`, `xdescribe`
- Assertion-free tests ("it runs without crashing")
- Integration tests that mock the thing they're supposed to integrate
- Contract tests absent between Admin API ↔ Actor Host ↔ Student API
- NATS publisher/subscriber pair with no integration test
- Event replay tests absent for event-sourced aggregates
- Projection rebuild tests absent
- E2E test suite absent or not run in CI
- Coverage drop vs the prior review's baseline
- Flaky tests (re-run 5× and check determinism)
- Tests dependent on wall-clock time without a clock abstraction
- Tests dependent on DB state without seed isolation
- Firebase Auth not mocked in unit tests (real network calls)

For every Phase 0 `verified-fixed` finding:
1. Locate the test added in the fix commit
2. Check out the pre-fix parent commit
3. Cherry-pick the test onto it
4. Run the test — it MUST fail
5. If it passes on the buggy commit, the test is a fake test → P0

Must produce:
- Test coverage matrix (finding_id → test_file → pre-fix fail proof)
- CI pipeline dump showing which test suites actually run
- Flaky test report
- Coverage diff vs prior review

Output: `docs/reviews/agent-qa-reverify-<date>.md`

---

## Finding schema (all agents use this)

```yaml
- id: FIND-<lens>-<nnn>                # e.g. FIND-privacy-007, FIND-qa-003
  severity: p0 | p1 | p2 | p3
  category: contract | security | perf | pedagogy | ux | a11y |
            privacy | compliance | test | stub | label-drift |
            regression | fake-fix | observability | cost | i18n |
            event-schema
  file: path/to/file.cs
  line: 123
  related_prior_finding: FIND-sec-001   # if this is a regression
  framework: COPPA | GDPR-K | WCAG-2.2-AA | ICO-Children | FERPA | null
  evidence:
    - type: grep | curl | screenshot | db-query | git-blame |
            citation | lighthouse | axe | test-run
      content: |
        <the actual proof output>
  finding: <one sentence>
  root_cause: <what design decision led here>
  proposed_fix: <concrete change, not "investigate">
  test_required: <specific test case that must exist before this closes>
  task_body: |
    <ready-to-enqueue task with goal, files, DoD, reporting reqs>
```

## Task enqueue (each agent, for each P0/P1)

The queue CLI uses `enqueue` (verified via `--help`). Title is positional.
Priority is mapped: P0 → `critical`, P1 → `high`.

```bash
# Write the task body to a file first — multi-line bodies are unsafe on
# the command line and --body-file is the supported path.
cat > /tmp/find-<lens>-<nnn>.md <<'EOF'
<task_body from the YAML, including goal / files / DoD / reporting>
EOF

node .agentdb/kimi-queue.js enqueue \
  "FIND-<lens>-<nnn>: <one-liner>" \
  --body-file /tmp/find-<lens>-<nnn>.md \
  --priority <critical|high> \
  --assignee unassigned \
  --tags "reverify,<lens>,<category>"
```

Regression and fake-fix findings get an additional tag `regression` and
MUST reference the original finding ID in the body under a
`related_prior_finding:` field so the coordinator can cross-link them in
the merge step.

---

## PHASE 2 — Coordinator merge (main session, after all 7 return)

Do NOT spawn an 8th agent for merging. The main Claude Code session does
this:

1. Read all 7 agent reports from `docs/reviews/agent-*.md`
2. Read the Phase 0 preflight report
3. Deduplicate cross-agent findings (same file:line = merge)
4. Cross-link causal chains:
   "FIND-arch-013 is the root cause of FIND-ux-015 and FIND-privacy-004"
5. **Regression & fake-fix section first** — these are the report's
   headline. Everything else is secondary.
6. Priority matrix: blast radius × fix cost × legal exposure
   (legal exposure is a 10× multiplier for `privacy` lens findings)
7. Write `docs/reviews/cena-review-<YYYY-MM-DD>.md` containing:
   - Executive summary (under 300 words)
   - **Regressions & fake-fixes** (from Phase 0 preflight)
   - P0/P1 counts by lens and by framework
   - Top 10 ranked by impact
   - Compliance status per framework (`privacy` lens)
   - Test coverage delta vs prior review (`qa` lens)
   - Task IDs for every enqueued item
   - Link to the preflight report
8. Post to coordination topic:
   ```bash
   node .agentdb/kimi-queue.js send --from claude-code \
     --topic coordination --kind status \
     --subject "cena re-verify complete" \
     --body "R regressions, F fake-fixes, N p0, M p1 enqueued; \
             report at docs/reviews/cena-review-<date>.md"
   ```
9. Clean up worktrees:
   ```bash
   for lens in arch sec data pedagogy ux privacy qa; do
     git worktree remove .claude/worktrees/review-$lens
   done
   rm -rf .claude/worktrees/reverify-preflight
   ```

---

## Definition of done

- [ ] Phase 0 preflight report exists with verification verdict per prior
      finding
- [ ] All 7 agent reports exist under `docs/reviews/`
- [ ] Every regression and fake-fix has a NEW P0 task in the queue
- [ ] Every P0 and P1 finding has a corresponding task in
      `.agentdb/kimi-queue.db`
- [ ] Zero findings without evidence
- [ ] Zero pedagogy claims without a cited source
- [ ] Zero compliance findings without a framework citation
- [ ] Merged report at `docs/reviews/cena-review-<YYYY-MM-DD>.md`
- [ ] Coordination message posted
- [ ] All review worktrees removed

## Prerequisites (coordinator checks BEFORE Phase 0)

1. Admin UI running at http://localhost:5174 (`ux`, `privacy` need it)
2. Student web host running at http://localhost:5175 (`ux`, `privacy` need it)
3. Postgres + Marten reachable (`arch`, `data`, `qa` need it)
4. Prior review present at `docs/reviews/cena-review-2026-04-11.md`
   plus `docs/reviews/agent-*-findings.md` (Phase 0 needs them)
5. 53 FIND-* tasks present in `.agentdb/kimi-queue.db` with `status=done`
   (already verified: queue `stats` shows them under `kimi-coder` and
   `claude-code` workers)
6. Playwright MCP or Chrome DevTools MCP connected
7. `jq` installed (Phase 0 filters queue JSON)
8. axe-core and Lighthouse available in the `ux` worktree
9. CI pipeline config accessible to `qa` agent
10. Git log searchable for `FIND-*` commit messages (used to find fix SHAs)

## What this review is NOT

- Not a style/formatting audit
- Not a dependency bump survey
- Not a "rewrite everything" exercise
- Not an excuse to flag `#7367F0` (locked) or propose GraphQL (banned)
- Not a substitute for a formal external pentest or DPIA — the `sec`
  and `privacy` lenses produce input TO those, not replacements FOR them

---

## Version history

- **v1** (`cena-review-v1-discovery.md`) — first-pass 5-agent discovery
  audit. Use when there is no prior review to re-verify against.
- **v2** (this file) — adds Phase 0 fix-verification preflight, the
  `privacy` agent (Child Safety & Privacy Compliance), the `qa` agent
  (QA/Tests & Regression), and folds observability, cost, i18n,
  WCAG 2.2 AA, event schema evolution into existing lenses. Uses
  lens-name IDs (`FIND-<lens>-<nnn>`) to match v1's convention. Use for
  every run after v1.
- **v2 patches** (2026-04-11) — corrected queue CLI command from `add`
  → `enqueue`, switched body passing to `--body-file`, locked ID
  convention to `FIND-<lens>-<nnn>`, and grounded Phase 0 against the
  real 53 closed FIND-* tasks produced by the v1 run.
