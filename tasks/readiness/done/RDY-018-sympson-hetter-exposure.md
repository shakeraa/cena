# RDY-018: Full Sympson-Hetter Exposure Control

- **Priority**: High — same items shown to every student without proper exposure control
- **Complexity**: Senior engineer — psychometric algorithm
- **Source**: Expert panel audit — Yael (Psychometrics)
- **Tier**: 2
- **Effort**: 1-2 weeks

## Problem

`ConstrainedCatAlgorithm.cs` has basic exposure tracking but the full Sympson-Hetter algorithm is missing:
- No cumulative exposure tracking across students (who saw which items, how many times)
- No probability thresholding (accept selected item with P=exposure_param, reject and re-select)
- No per-item exposure targets (literature recommends 0.1-0.2 per item, not uniform 25% cap)
- No historical exposure analytics

Current `ComputeExposureParameter` returns 1.0 for low-exposure items and `maxRate/currentRate` for high-exposure — an oversimplification.

## Scope

### 1. Cumulative exposure tracking

- Store per-item exposure count in a Marten document (`ItemExposureDocument`)
- Track: item ID, total administrations, total eligible students, exposure rate
- Update on every item administration

### 2. Full Sympson-Hetter implementation

Per Sympson & Hetter (1985):
- Each item has an exposure control parameter (0 < p_i <= 1)
- When CAT selects an item, administer with probability p_i
- If rejected (1 - p_i), select next-best item
- Periodically recalibrate p_i to maintain target exposure rate (e.g., 0.20)

### 3. Configurable target exposure rate

- Default: 0.20 (each item seen by at most 20% of students)
- Configurable per item bank or per exam configuration
- Higher targets acceptable for small item banks (< 50 items)

### 4. Exposure analytics in health dashboard

- Distribution chart: exposure rate per item (histogram)
- Over-exposed items list (> 2x target)
- Under-used items list (never administered)

## Files to Modify

- `src/actors/Cena.Actors/Assessment/ConstrainedCatAlgorithm.cs` — full S-H implementation
- New: `src/shared/Cena.Infrastructure/Assessment/ItemExposureDocument.cs`
- `src/api/Cena.Admin.Api/ItemBankHealthService.cs` — exposure analytics
- `src/actors/Cena.Actors.Host/Program.cs` — register exposure document

## Acceptance Criteria

- [ ] Per-item exposure tracked across all student sessions
- [ ] Sympson-Hetter probability thresholding implemented
- [ ] Items rejected with probability (1 - p_i), next-best selected
- [ ] Exposure control parameters recalibrated periodically
- [ ] Target exposure rate configurable (default 0.20)
- [ ] Health dashboard shows exposure distribution + flagged items
- [ ] No item exceeds 2x target exposure rate after calibration
