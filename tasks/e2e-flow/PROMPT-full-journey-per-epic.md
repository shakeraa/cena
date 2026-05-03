# New-session prompt: full E2E flow journey per epic

**Goal**: Drive a real Chromium browser through every flagship workflow in
each `tasks/e2e-flow/EPIC-E2E-{A..L}*.md` epic. Click real buttons. Collect
console errors, page errors, failed network requests, and container logs.
Don't substitute HTTP-level shortcuts for UI clicks. Aggregate everything
into a per-epic diagnostic report and ship gaps you find.

---

## Copy-paste prompt for the new session

> Read `tasks/e2e-flow/PROMPT-full-journey-per-epic.md` and execute it. For
> every epic A through L, drive the flagship workflow in a real browser
> with real `getByTestId(...).click()` interactions, collect Chrome
> console + page errors + failed network requests + relevant container
> logs, fix every issue you find, and produce a per-epic diagnostic
> report. The reference implementation is
> `src/student/full-version/tests/e2e-flow/workflows/student-full-journey.spec.ts` —
> it covers EPIC-A + EPIC-B end-to-end and shows the diagnostic-collection
> pattern. Reproduce that pattern for the remaining epics.
>
> Memory you must honour:
>
> - `feedback_real_browser_e2e_with_diagnostics.md` — real button clicks,
>   not API shortcuts; collect console + page errors + network failures +
>   container logs every run.
> - `feedback_no_stubs_production_grade.md` — fix root causes, not
>   workarounds.
> - `feedback_destroy_containers_not_node_modules.md` — `docker compose
>   down -v` is fine; never delete `node_modules`.
> - `feedback_full_sln_build_gate.md` — build full sln before merging.
> - `feedback_always_merge_to_main.md` — every passing journey ships to
>   `origin/main` immediately.
> - `feedback_honest_not_complimentary.md` — when something half-works,
>   say so explicitly. No "should work" / "spec runnable" claims you
>   haven't actually verified live.

---

## Pre-flight (before driving any journey)

```bash
# 1. Stack must be up + healthy
docker compose -f docker-compose.yml -f docker-compose.app.yml up -d
docker ps --format 'table {{.Names}}\t{{.Status}}' | grep -E "(student|admin|actor|nats|postgres|firebase|redis)"

# 2. Seed Firebase + bridge to Marten
docker exec cena-firebase-emulator /seed/seed-dev-users.sh
./scripts/seed-marten-from-firebase.sh

# 3. Confirm dev env vars are set (rebuild if not)
docker exec cena-student-api env | grep -E "CENA_E2E_TRUSTED_REGISTRATION|CENA_TEST_PROBE_TOKEN"
# Expected:
#   CENA_E2E_TRUSTED_REGISTRATION=true
#   CENA_TEST_PROBE_TOKEN=dev-only-test-probe-token-do-not-ship

# 4. Verify endpoints are live
curl -sS -o /dev/null -w "on-first-sign-in: %{http_code}\n" \
  -X POST http://localhost:5050/api/auth/on-first-sign-in \
  -H "Content-Type: application/json" -d '{"tenantId":"x"}'  # expect 401
curl -sS -o /dev/null -w "test-probe (no token): %{http_code}\n" \
  "http://localhost:5050/api/admin/test/probe?type=studentProfile&tenantId=x&id=u"  # expect 404
```

If any of these fail: rebuild the affected service
(`docker compose ... build student-api && up -d --no-deps student-api`)
and re-run the pre-flight.

---

## Per-epic journey checklist

### EPIC-E2E-A — Auth & Onboarding ✅ COVERED

Reference: `student-full-journey.spec.ts` (end-to-end already wired) +
`student-register.spec.ts` (regression-style).

Journey steps already automated:
- /register → age-gate-dob → age-gate-next click
- credentials form fill (display-name, email, password)
- auth-submit click
- waits for /onboarding to render
- onboarding-page visibility

When extending: add password-reset, role-claim invalidation
(promotion path), and parent-bind UI flow once /parent/bind?token=...
ships in the SPA.

### EPIC-E2E-B — Subscription & Billing ✅ PARTIALLY COVERED

Reference journey covers Plus annual happy path. Extend with:
- B-02: declined card → /subscription/cancel UI
- B-03: cancel back from checkout
- B-04: tier upgrade UI flow
- B-06: cancel-at-period-end
- B-08: bank transfer (IL track)

For each: drive `/account/subscription` UI buttons, capture the
`SubscriptionStatusDto` after each transition, assert via probe.

### EPIC-E2E-C — Student Learning Core (NEW)

Drive the actual session-start UI:
1. /home → "Start Session" button click
2. /session — first question renders, an answer choice is clickable
3. Click answer → assert correctness feedback DOM appears
4. Hint ladder click — each hint reveals
5. Submit session → mastery delta visible on /home

Collect:
- Every API call to /api/sessions, /api/hints, /api/me/mastery
- Bus events on `cena.events.student.{uid}.{answer_evaluated|hint_delivered|mastery_updated}`

### EPIC-E2E-D — AI Tutoring (NEW)

Drive `/tutor` chat UI:
1. /tutor → input field + submit
2. Type a math question → submit
3. SSE stream populates response
4. CAS verification badge appears
5. Follow-up question

Collect: token-budget exhaustion path (D-06), CAS sidecar down (J-01).

### EPIC-E2E-E — Parent Console (NEW)

Drive parent UI as a parent-role user:
1. parent1@cena.local /login flow
2. /parent/dashboard → child cards visible
3. Digest preferences form interaction
4. Time-budget setting (E-06)
5. Right-to-erasure click (E-08)

Note: The seed creates `parent1@cena.local` but the parent-bind to a
specific child requires the invite flow (A-04). Either run A-04 first
to create the binding, or extend `seed-marten-from-firebase.sh` to seed
a parent + bound-child pair.

### EPIC-E2E-F — Teacher & Classroom (NEW)

Sign in as teacher1@cena.local, drive:
1. /apps/teacher/heatmap — concept heatmap visible
2. Click cell → drill-down student list
3. Click student → student detail page
4. K-floor enforcement: try to drill below 10 students → blocked

### EPIC-E2E-G — Admin Operations (NEW)

Switches host: drives admin SPA at localhost:5174.
1. admin@cena.local /login on admin SPA
2. /apps/users → user list visible
3. Promote student → role change applied
4. /apps/moderation queue interactions
5. CAS-override workflow

### EPIC-E2E-H — Multi-Tenant Isolation (NEW)

Two-tenant matrix:
1. Create user in tenant A via on-first-sign-in
2. Create user in tenant B via on-first-sign-in
3. As A, fetch /api/me/profile, /api/admin/users — assert no B data
4. As A, /api/admin/test/probe?tenantId=B&id=A's-uid → expect found:false

The A-01 spec already covers cross-tenant defence on the probe;
extend to /api/admin/users + /api/me/parent-dashboard.

### EPIC-E2E-I — GDPR / COPPA Compliance (NEW)

Drive consent UI:
1. /register → age-gate path for age 13-15 (parental consent flow)
2. Parent email entry → emu OOB code
3. Verify token → registered with parental_consent_given=true
4. /api/me/consent should reflect the verification

### EPIC-E2E-J — Resilience & Failure Modes (NEW)

Container chaos:
1. Stop CAS sidecar → tutor responses degrade gracefully (no 500s in
   user-visible UI)
2. Stop NATS → outbox accumulates, restart drains
3. Stop Firebase emulator → /login surfaces a clean error, no JS
   throws

For each: verify the chrome console doesn't fill with uncaught
exceptions while the dependency is down.

### EPIC-E2E-K — Offline / PWA (NEW)

Drive offline mode:
1. Start a session
2. `await context.setOffline(true)`
3. Continue answering — answers queue locally
4. setOffline(false) → queue flushes, server state matches client

### EPIC-E2E-L — Accessibility / i18n (NEW)

Run with `locale = 'ar'` and `locale = 'he'`:
1. Re-run epic A journey with each locale
2. Assert `document.dir === 'rtl'`
3. Assert no raw i18n key strings visible (`pricing.tier.plus.title` etc.)
4. axe-core scan on each landing page

---

## Diagnostic collection (every epic)

Each journey spec MUST do all of:

```typescript
// 1. Console listener
page.on('console', msg => {
  consoleEntries.push({
    type: msg.type(),
    text: msg.text(),
    location: msg.location()?.url ? `${msg.location().url}:${msg.location().lineNumber}` : undefined,
  })
})

// 2. Uncaught exception listener
page.on('pageerror', err => {
  pageErrors.push({ message: err.message, stack: err.stack })
})

// 3. Network failure listener
page.on('response', async resp => {
  if (resp.status() >= 400) {
    failedRequests.push({
      method: resp.request().method(),
      url: resp.url(),
      status: resp.status(),
      body: await resp.text().catch(() => '<navigation flushed>').then(s => s.slice(0, 800)),
    })
  }
})
```

After each journey, also collect container logs:

```bash
# Capture per-container logs since the journey started.
SINCE="$(date -u -v-2M '+%Y-%m-%dT%H:%M:%SZ')"   # 2 minutes back
for c in cena-student-api cena-actor-host cena-admin-api cena-nats cena-postgres cena-firebase-emulator; do
  echo "── $c ──"
  docker logs --since "$SINCE" "$c" 2>&1 | grep -iE "ERR|WRN|exception|fail" | grep -vE "Marten\.Sessions" | head -30
done
```

(Filter `Marten.Sessions` warnings — pre-existing config noise; not a real error.)

---

## Per-epic deliverable

For every epic:
1. **Spec file** at `src/student/full-version/tests/e2e-flow/workflows/EPIC-{A..L}-journey.spec.ts` mirroring the diagnostic-collection pattern from `student-full-journey.spec.ts`.
2. **Live run** confirming the journey passes end-to-end, with console + page errors + network failures + container errors all reported as zero (or with explicit allowlist for known issues that are tracked in the queue).
3. **Per-epic report** in `tasks/e2e-flow/REPORT-EPIC-{A..L}.md` summarizing:
   - which UI buttons were clicked
   - which API endpoints fired
   - which bus events were observed
   - any console / page / network / container issues found
   - PR / commit links for fixes
4. **Commit + push to origin/main** (every epic that ships green = its own commit).

---

## Stop conditions (when to escalate, not push through)

- **Browser doesn't open**: Docker stack issue. Run pre-flight, restart containers as needed. Stop and ask the user before deleting `node_modules` (you won't, but in case the impulse arises).
- **Spec needs a backend that doesn't exist** (e.g. EPIC-E2E-D's tutor SSE handler): enqueue a backend task with the same diagnostic detail PRR-436 / TASK-E2E-A-04-BE used. Don't fake it.
- **Cross-tenant defence fails**: this is a P0 ship-gate. Stop the run, surface to the user, fix the root cause before continuing.
- **Race condition between SPA navigation and Playwright response cache** (the `tier-card-plus-cta` problem): use `page.route('**://destination/**', r => r.abort())` to suppress the navigation; never substitute the click with `page.request.post`.

---

## What success looks like

When this work is done:
- 12 journey specs (one per epic) under `tests/e2e-flow/workflows/EPIC-*-journey.spec.ts`.
- All 12 pass live with zero console errors, zero page errors, zero unexpected 4xx/5xx, and clean container logs.
- 12 reports under `tasks/e2e-flow/REPORT-EPIC-*.md` that a coordinator can scan in 2 minutes per epic.
- Every gap discovered along the way either fixed or queued — no silent "spec passes but I haven't run it" claims.

That's a real "drive Cena like a student" pass.
