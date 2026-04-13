# ADR-0002 — Symbolic math engine is the sole source of truth for tutor correctness

- **Status**: Proposed
- **Date proposed**: 2026-04-13
- **Deciders**: Shaker (project owner), claude-code (coordinator)
- **Supersedes**: none
- **Related**: [ADR-0001](0001-multi-institute-enrollment.md), [Track 9 research](../research/tracks/track-9-socratic-ai-tutoring.md), [Game design synthesis](../research/cena-sexy-game-research-2026-04-11.md)
- **Task**: GD-001 (`t_1e403143f266`)

---

## Context

Cena's AI tutor (`ClaudeTutorLlmService`) currently delegates all mathematical reasoning to the LLM. The system prompt instructs the model to "use the Socratic method" and "offer hints rather than solutions," but these are prompt-level guardrails only — no architectural enforcement exists.

Research establishes two critical failure modes in LLM-based math tutoring:

1. **Arithmetic errors**: Khanmigo (Khan Academy's GPT-4 tutor) shipped with arithmetic errors in 2023, prompting Khan Academy to add a calculator tool + ground-truth solver wrapper. LLMs are probabilistic text generators, not symbolic reasoners. They will produce plausible-looking but incorrect algebra (VanLehn 2011, Macina et al. MathDial 2023).

2. **Answer leakage**: MathDial (EMNLP 2023) documents that GPT-based tutors reveal the final answer within 3 turns ~40% of the time, even with Socratic prompting. Students quickly learn to ask "is this right?" and get the answer confirmed or corrected without doing the work.

Step-based ITS (intelligent tutoring systems) that reason at the individual solution step level achieve d ≈ 0.76 — statistically indistinguishable from expert human tutors (VanLehn 2011). Answer-only tutors achieve d ≈ 0.31. The delta is the difference between a marginal product and a transformative one.

---

## Decision

**The LLM explains; the computer-algebra system verifies. No LLM output that claims a numerical or symbolic result is shown to a student until a deterministic CAS has confirmed it.**

### Decision 1 — CAS choice: SymPy (Python sidecar) + MathNet (in-process fallback)

| Engine | Strengths | Weaknesses |
|--------|-----------|------------|
| **SymPy** (Python) | Full symbolic algebra, calculus, trig, equation solving; 20+ years of active development; standard in academic ITS research | Requires Python process; ~50ms cold-start per request; adds Python to the deployment |
| **MathNet.Symbolics** (.NET) | In-process, zero latency; native to Cena's stack | Symbolic algebra only (no calculus, no equation solving, no simplification to normal form) |
| **Maxima** | Mature CAS, strong simplification | Heavy process, LISP runtime, poor containerization story |

**Primary**: SymPy as an HTTP sidecar service (FastAPI or gRPC, containerized). SymPy covers the full Bagrut 4-unit/5-unit + AP Calc + SAT math curriculum. All step verification, equation equivalence, and final-answer checks route through SymPy.

**Fallback**: MathNet.Symbolics for in-process arithmetic validation (basic expression equivalence, numerical checks). Used only when the SymPy sidecar is unreachable (circuit breaker) and the check is simple enough for MathNet to handle. If MathNet cannot verify, the response is held and flagged for human review.

**Cost**: One additional container (~128MB RAM, ~0.25 vCPU idle). Estimated $8–15/month on Fly.io or Railway at Cena's current scale. Negligible relative to LLM API costs.

### Decision 2 — Transport: NATS request/reply

The SymPy sidecar registers on NATS subject `cas.verify.{operation}` (e.g. `cas.verify.equivalence`, `cas.verify.step`, `cas.verify.simplify`). The Actor Host sends a request with a JSON payload and receives a structured verdict.

Rationale: Cena already uses NATS for Actor Host ↔ Admin API communication (NatsBusRouter). Adding another NATS subject is zero additional infrastructure.

```
LLM generates candidate response
  → TutorService extracts mathematical claims
    → NATS request: cas.verify.equivalence { student: "x=3", canonical: "x=3" }
    → SymPy sidecar responds: { equivalent: true, method: "substitution", confidence: 1.0 }
  → If all claims verified: stream response to student
  → If any claim rejected: re-prompt LLM with rejection reason (one retry)
  → If retry also rejected: hold response, flag for human review
```

### Decision 3 — What "verify" means

The CAS performs four types of verification:

| Operation | Definition | Example |
|-----------|-----------|---------|
| **Equivalence** | Two expressions are mathematically equal under all valid substitutions | `x²-1` ≡ `(x+1)(x-1)` |
| **Step validity** | A transformation from expression A to expression B preserves equality | `2x+4=0` → `x+2=0` (valid: divided both sides by 2) |
| **Numerical tolerance** | Two numerical results are within ε (default 1e-9) | `√2` ≈ `1.41421356` |
| **Normal form** | An expression is in canonical simplified form | `x²+2x+1` → `(x+1)²` (not yet simplified) |

### Decision 4 — Rejection repair flow

```
Student submits answer/step
  → CAS verifies against canonical trace
  → If CORRECT: LLM explains why it's correct, scaffolds next step
  → If INCORRECT:
      → CAS identifies the specific error (e.g. "sign flip in step 3")
      → LLM generates a targeted remediation using the misconception catalog
      → Response is CAS-verified before delivery
  → If LLM response contains a mathematical claim:
      → Extract all claims (regex + AST parse)
      → CAS-verify each claim
      → If any claim fails: re-prompt LLM with the failure (1 retry max)
      → If retry fails: suppress the mathematical claim, deliver only the pedagogical text
      → Flag for human review
```

### Decision 5 — Answer-mask at decode

Before any LLM response is streamed to the student, a post-processing filter:

1. Pre-computes the canonical final answer via CAS
2. Scans the LLM output for the final answer (exact match, LaTeX-equivalent match, numerical-tolerance match)
3. If found and the reveal gate is not satisfied: replaces the answer with `[solution step hidden — try working through it]`
4. Logs the masking event for observability

The reveal gate is satisfied only when: (a) the student has attempted the problem at least once, AND (b) the hint ladder has been exhausted or the student explicitly requests the answer after engaging with at least 2 ladder rungs.

### Decision 6 — Turn budget

| Problem type | Min turns before any reveal | Max turns before cooldown |
|-------------|---------------------------|--------------------------|
| Standard | 3 | 12 |
| Boss fight | 6 | 20 |
| Step-solver | 2 per step | 8 per step |

After max turns, the tutor offers to show the full worked solution (faded worked example pattern) and moves the student to a similar-but-different problem. This is enforced structurally in `TutorMessageService`, not by prompt.

### Decision 7 — Observability

Every CAS call is logged with:
- Request hash (SHA-256 of input expressions)
- Operation type (equivalence / step / tolerance / normal-form)
- Verdict (pass / fail / error / timeout)
- Latency (ms)
- Whether the result was used to mask LLM output
- Student session ID (for audit trail, session-scoped per ADR privacy rules)

Metrics exposed via OpenTelemetry:
- `cena.cas.requests.total` (counter, by operation + verdict)
- `cena.cas.latency.ms` (histogram)
- `cena.cas.mask.total` (counter — how often answer-masking fires)
- `cena.cas.fallback.total` (counter — MathNet fallback activations)

Admin dashboard: CAS health panel showing pass/fail rates, latency p50/p95, and masking frequency.

---

## Non-compliant code paths (follow-up tasks)

These existing code paths violate the "CAS verifies all mathematical claims" rule and must be retrofitted:

| File | Issue | Remediation |
|------|-------|-------------|
| `src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs` | LLM response streamed directly to student with no CAS verification | Insert CAS verification step between LLM response and delivery |
| `src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs:160-178` | System prompt says "Socratic" but no architectural enforcement | Add answer-mask filter + turn budget counter |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Answer correctness checked by `IsCorrect` field on question, not CAS | Route answer checks through CAS for mathematical questions |
| `src/actors/Cena.Actors/Services/HintGenerator.cs` | Hints generated without CAS validation of mathematical content | CAS-verify all hints containing math before delivery |
| `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs` | `/answer` endpoint accepts answer and returns correctness without CAS | Add CAS verification for math question types |

---

## Downstream task cross-references

- **CAS-001** (`t_66da32a5e647`): Build the SymPy sidecar + MathNet fallback
- **CAS-002** (`t_299a195273b6`): Step verifier API endpoint + NATS integration
- **CAS-003** (`t_042a298ac521`): 500-pair SymPy ↔ MathNet conformance suite
- **CAS-BIND-001** (`t_eeddcd506753`): QuestionCasBinding — lock question to authoring CAS
- **CAS-LLM-001** (`t_426568611b77`): CAS-verify all mathematical expressions in LLM output
- **FIGURE-007** (`t_faa8109c6380`): Quality gate rules for figures (cross-ref: figure equilibrium checks via CAS)
- **STEP-001–005**: Step solver components that depend on CAS for step verification

---

## Evidence

| Claim | Source | Effect size |
|-------|--------|-------------|
| Step-based ITS ≈ human tutors | VanLehn 2011, Ed. Psych. 46(4) | d ≈ 0.76 vs d ≈ 0.79 (human) |
| Answer-only tutors underperform | VanLehn 2011 | d ≈ 0.31 |
| LLMs leak answers within 3 turns | Macina et al. MathDial, EMNLP 2023 | ~40% leakage rate |
| Faded worked examples | Renkl & Atkinson 2003, Ed. Psych. 38(1) | d ≈ 0.4–0.6 |
| Misconception-targeted remediation | Koedinger et al. 1997; Heffernan 2014 | d ≈ 0.2–0.4 per buggy rule |
| LLMs produce arithmetic errors | Khan Academy Khanmigo 2023 incident | Qualitative (public acknowledgment) |

---

## Consequences

**Positive**:
- Mathematical correctness is deterministic, not probabilistic
- Answer leakage is architecturally prevented, not prompt-hoped
- Step-level reasoning enables d ≈ 0.76 tutoring effectiveness
- Audit trail for every correctness claim shown to students
- Legal defensibility: "our math was verified by a computer algebra system" vs "our AI said so"

**Negative**:
- Adds a Python sidecar to the deployment (~$8–15/month)
- Adds ~50ms latency per CAS call (multiple calls per response)
- Requires mathematical claim extraction from LLM output (regex + heuristic AST parsing)
- MathNet fallback path is less capable — some checks will fail-closed

**Risks**:
- SymPy sidecar availability becomes a critical dependency (mitigated by MathNet fallback + circuit breaker)
- Claim extraction may miss edge cases (mitigated by conservative masking — when in doubt, mask)
- Students may find the turn budget frustrating (mitigated by research-backed thresholds + faded worked example escape hatch)
