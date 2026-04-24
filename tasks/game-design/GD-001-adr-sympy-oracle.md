# GD-001: ADR — Symbolic math engine is the sole source of truth for tutor correctness

## Goal
Produce `docs/adr/ADR-XXX-sympy-correctness-oracle.md` that binds the platform to a single rule: **the LLM explains, the computer-algebra system verifies**. No LLM output that claims a numerical or symbolic answer is shown to a student until a deterministic CAS has confirmed it.

## Source
`docs/research/cena-sexy-game-research-2026-04-11.md` — Track 9 findings (Khanmigo 2023 arithmetic-error incident; MathDial 2023 answer-leakage ≈40% within 3 turns; VanLehn 2011 d≈0.76 for step-based ITS).

## Required decisions
1. CAS choice: SymPy (Python sidecar service) vs MathNet (in-process .NET) vs Maxima. Recommend SymPy for symbolic breadth + coverage; quantify the cost of a Python service in Cena's stack.
2. Transport: in-process, NATS request/reply, or HTTP sidecar.
3. What "verify" means: equivalence, equality under substitution, numerical tolerance, simplification normal form.
4. How rejection flows back to the LLM for repair (one retry, then human review).
5. Answer-mask at decode: LLM token stream MUST mask computed answers until CAS approves.
6. Turn budget: max exchanges per student question before hand-off to human or cooldown.
7. Observability: every CAS call logged with hash + outcome; audit trail accessible to admin.

## DoD
- ADR merged
- Cross-references FIGURE-007 quality gate rules and LLM-010 Socratic tutor
- Non-compliant code paths inventoried as follow-up tasks

## Reporting
Complete with branch + CAS choice + sidecar cost estimate.
