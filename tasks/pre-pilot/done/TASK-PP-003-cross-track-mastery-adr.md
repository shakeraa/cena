# PP-003: Decide Cross-Track Mastery Sharing Model (ADR Decision) (CRITICAL)

- **Priority**: Critical — blocks TENANCY-P2a and meaningful multi-track enrollment
- **Complexity**: Architect level — requires psychometric analysis + architectural decision
- **Blocks**: TENANCY-P2a (mastery re-key), multi-track enrollment UX, cross-enrollment analytics
- **Source**: Expert panel review § BKT+ & Mastery (Dr. Rami), § Tenancy (Dr. Rami)

## Problem

TENANCY-P2a (mastery state re-key per ADR-0002 model) failed because the cross-track mastery sharing model was never decided. The VERIFY-0001 literature review is complete (`docs/research/VERIFY-0001-transfer-of-learning.md`) but the architectural decision between three options was never made.

A student enrolled in both Bagrut 806 (4-unit) and 036 (5-unit) tracks currently has completely independent mastery states. Mastering quadratic equations in 806 does not affect their 036 mastery at all. This creates a poor student experience (they must "re-prove" skills they already know) and wastes learning time.

## Decision Options

### Option A: Full Sharing
- Skills with identical `SkillId` across tracks share mastery state
- If you master "quadratic-equations" in 806, you have the same mastery in 036
- Risk: different tracks may teach the same skill at different depths — 4-unit quadratics are simpler than 5-unit quadratics
- Transfer of learning research (Barnett & Ceci 2002) suggests near-transfer is reliable but far-transfer is weak

### Option B: Discounted Sharing (0.7x multiplier)
- Cross-track mastery transfer is discounted: if P(known) = 0.85 in track A, it seeds as 0.85 * 0.7 = 0.595 in track B
- The discount factor accounts for depth differences between tracks
- Requires a per-skill-pair discount matrix (expensive to maintain but accurate)
- Research basis: Perkins & Salomon (1992) transfer taxonomy

### Option C: Independent Silos
- No cross-track sharing at all
- Simple to implement and reason about
- Students must re-prove all skills when switching or adding tracks
- Worst student experience but zero risk of mastery inflation

## Deliverables

1. Review VERIFY-0001 literature findings with the psychometrics team
2. Analyze the actual skill overlap between Bagrut tracks 036, 806, and 807 (how many skills share identical IDs)
3. Make a decision (A, B, or C) and document it as an ADR amendment to ADR-0001
4. Design the data model for the chosen option:
   - Option A: shared `MasteryState` keyed by `(StudentId, SkillId)` — track-independent
   - Option B: primary `MasteryState` per track + `CrossTrackSeed` projection that computes discounted transfer
   - Option C: `MasteryState` keyed by `(StudentId, SkillId, TrackId)` — current default
5. Unblock TENANCY-P2a implementation

## Files Affected

- `docs/adr/0001-multi-institute-enrollment.md` — add Decision 2 amendment
- `src/actors/Cena.Actors/Services/BktPlusCalculator.cs` — mastery state key structure
- `src/actors/Cena.Actors/Services/SkillTrackMasteryService.cs` — cross-track query logic
- `src/shared/Cena.Infrastructure/Documents/EnrollmentDocument.cs` — mastery key fields

## Acceptance Criteria

- [ ] ADR-0001 amended with Decision 2 (cross-track mastery model)
- [ ] Decision includes quantitative analysis of skill overlap between tracks
- [ ] Data model for chosen option is specified with C# record types
- [ ] TENANCY-P2a is unblocked with clear implementation instructions
