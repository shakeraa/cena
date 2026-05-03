# RDY-047: ADR-0032 Layering Claim — Correct or Enforce

- **Priority**: Medium — ADR accuracy
- **Complexity**: Low
- **Effort**: 1-2 hours

## Problem

ADR-0032 §16 states CAS primitives moved to `Cena.Actors.Cas` as a separable layer. In reality they live in a `Cas/` folder inside the single `Cena.Actors` project. There is no `Cena.Actors.Cas` assembly. The stated layering rationale is not physically enforced.

## Scope

Pick one and execute:

- (a) Create `src/actors/Cena.Actors.Cas/Cena.Actors.Cas.csproj`, move the Cas files, update references everywhere
- (b) Amend ADR-0032 §16 to say "`Cena.Actors.Cas` **namespace** inside `Cena.Actors`" — remove the "enforceable separation" claim

Default: (b) now; (a) only when another adapter needs the invariant without pulling the full aggregates surface.

## Acceptance

- [ ] ADR and code agree on project structure
