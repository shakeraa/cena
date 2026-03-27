# Cena Platform — Master Task Plan

## Structure
Each domain has its own task file with strict acceptance criteria.
Tasks are ordered by dependency — later tasks depend on earlier ones.

## Domain Files
| File | Domain | Technology | Tasks |
|------|--------|-----------|-------|
| `01-data-layer.md` | Data | PostgreSQL, Marten v7, Neo4j, Redis | 12 tasks |
| `02-actor-system.md` | Backend | Proto.Actor .NET 9, Event Sourcing | 14 tasks |
| `03-llm-layer.md` | LLM | Python FastAPI, Claude, Kimi | 10 tasks |
| `04-mobile-app.md` | Mobile | Flutter/Dart, Riverpod, Drift | 16 tasks |
| `05-frontend-web.md` | Frontend | React, TypeScript, GraphQL | 8 tasks |
| `06-infrastructure.md` | DevOps | AWS, NATS, CI/CD, Monitoring | 10 tasks |
| `07-content-pipeline.md` | Content | Neo4j, Kimi batch, Expert review | 6 tasks |
| `08-security-compliance.md` | Security | GDPR, Auth, PII, Pen testing | 8 tasks |
| `09-mastery-engine.md` | Mastery | BKT, HLR, MIRT, Neo4j, Python trainers | 18 tasks |
| `10-focus-engine.md` | Focus & Resilience | Mobile sensors, microbreaks, affect detection | 12 tasks |

## Stages
1. **Foundation** (Weeks 1-4): Data layer + actor skeleton + LLM ACL
2. **Core Loop** (Weeks 5-8): Session flow + BKT + item selection + offline sync
3. **Intelligence** (Weeks 9-12): Stagnation detection + methodology switching + MCM
4. **Mobile** (Weeks 5-12): Flutter app, parallel with backend
5. **Polish** (Weeks 13-16): Gamification, accessibility, Arabic, A/B testing
6. **Launch Prep** (Weeks 17-18): Load testing, security audit, Hebrew LLM quality gate

## Acceptance Criteria Standard
Every task MUST have:
- [ ] **Definition of Done** — specific, testable condition
- [ ] **Test** — automated test that proves the criteria is met
- [ ] **Contract reference** — link to the contract file it implements
- [ ] **Blocked by** — explicit dependencies on other tasks

---

## ⛔ MANDATORY: NO STUBS, NO MOCKS, NO FAKE CODE

**This rule applies to EVERY task in EVERY domain. No exceptions.**

### What is FORBIDDEN:
- `throw UnimplementedError(...)` or `throw new NotImplementedException()`
- `// TODO: implement` with empty method bodies
- `pass  # placeholder` in Python
- `return null;` or `return default;` as placeholders
- Mock objects in place of real implementations (mocks ONLY in test files, NEVER in source)
- Hardcoded return values pretending to be real logic
- `Console.WriteLine("Not implemented yet")`
- Empty `build()` methods in Flutter widgets
- Abstract classes with `...` bodies that should be concrete

### What is REQUIRED:
- **Real logic** that processes real data and produces real results
- **Real database queries** (not in-memory fakes) against PostgreSQL/Neo4j/Redis
- **Real LLM API calls** (with proper error handling, not mocked responses)
- **Real WebSocket connections** (not simulated message passing)
- **Real event sourcing** — events persisted to Marten, replayed on activation
- **Real BKT calculations** using the actual Corbett & Anderson formula
- **Real stagnation detection** with the 5 normalized signals
- **Real Flutter widgets** that render actual UI, not placeholder boxes

### How to verify (CI gate):
```bash
# This MUST pass before any PR is merged:
grep -rn "UnimplementedError\|NotImplementedException\|TODO.*implement\|STUB\|MOCK\|placeholder\|pass  #" src/ lib/
# Assert: 0 matches in source code (test code is exempt)
```

### Why this matters:
Stubs create technical debt that compounds. A stub in the BKT service means
the session actor can't be tested with real data. A stub in the Flutter
widget means the offline sync can't be validated end-to-end. One stub
breaks the entire chain.

**If you can't implement it fully, don't implement it at all. File a
blocking dependency instead.**

---

## 🎯 MANDATORY: Act as Senior Professional Developer

**This rule applies to EVERY task. The coding agent must behave as a
senior full-stack developer with 10+ years of experience.**

### Mindset Requirements

1. **Understand before coding.** Read the contract, the related docs, and
   the architect reviews BEFORE writing a single line. Understand WHY the
   design is the way it is, not just WHAT to implement.

2. **Best practices are non-negotiable:**
   - SOLID principles (especially Single Responsibility and Dependency Inversion)
   - DRY — but don't over-abstract prematurely
   - Clean Code: meaningful names, small functions, no magic numbers
   - Error handling at every boundary (network, DB, file, user input)
   - Logging with context (structured, correlation IDs, appropriate levels)
   - Input validation at system boundaries (never trust client data)
   - Security by default (sanitize, escape, validate, authorize)

3. **Think about edge cases FIRST:**
   - What happens if the database is down?
   - What happens if the LLM times out?
   - What happens if the student's device loses network mid-operation?
   - What happens with 0 items? 1 item? 10,000 items?
   - What happens with Hebrew RTL + LaTeX LTR mixed content?
   - What happens when the actor passivates mid-operation?

4. **Suggest improvements.** If you see a better approach than what the
   contract specifies, DOCUMENT IT as a comment with rationale:
   ```
   // CONTRACT DEVIATION: Using ConcurrentDictionary instead of Dictionary
   // because multiple child actors may read this concurrently during
   // stagnation detection. Original contract assumed single-threaded access.
   ```

5. **Performance awareness:**
   - BKT update MUST be < 1 microsecond (it's on the hot path)
   - Knowledge graph render MUST be < 8ms per frame (60fps budget)
   - Event persistence MUST be < 50ms (including snapshot check)
   - Never allocate on the hot path (use pre-allocated buffers)
   - Profile before optimizing — measure, don't guess

6. **Test like a user, not a developer:**
   - End-to-end scenarios: "Student opens app → starts session → answers
     5 questions → goes offline → answers 3 more → reconnects → syncs →
     teacher sees updated graph"
   - Failure scenarios: "Network drops mid-sync → app crashes → student
     reopens → no data lost"
   - Concurrency: "Two devices logged in as same student → both submit
     answers → server resolves correctly"

7. **Code review yourself before submitting:**
   - Would a senior engineer approve this PR?
   - Is every public method documented?
   - Are there any commented-out code blocks? (DELETE THEM)
   - Are there any `print()` or `Console.WriteLine()` debug statements? (DELETE THEM)
   - Does the code handle the error cases from the architect reviews?

### Domain Knowledge Required

Before implementing ANY task, the agent MUST read:
- The referenced contract file(s) in full
- The relevant architect review (`contracts/REVIEW_*.md`)
- The `docs/architecture-design.md` section for the bounded context
- The `docs/system-overview.md` for user-facing behavior

**An implementation that works but doesn't match the domain model is REJECTED.**
Example: if the contract says "mastery threshold = 0.85 for progression,
0.95 for prerequisite gates" — implementing with a single threshold is wrong
even if tests pass.
