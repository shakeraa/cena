# PP-011: Honest Labeling of IRT Calibration (Rasch, not 2PL) + Minimum N

- **Priority**: Medium — prevents misleading psychometricians and incorrect confidence
- **Complexity**: Senior engineer — rename + add N threshold logic
- **Source**: Expert panel review § IRT & CAT (Dr. Yael)

## Problem

### 1. Misleading 2PL label

`IrtCalibrationPipeline` in `src/actors/Cena.Actors/Services/IrtCalibrationPipeline.cs` labels itself as "Rasch/2PL" but the discrimination parameter is always fixed at 1.0 unless an external prior is provided (line 136-138). There is no EM algorithm, no marginal MLE, and no iterative discrimination estimation. This is Rasch with optional externally-supplied discrimination priors — labeling it "2PL" misleads reviewers into thinking actual 2PL estimation is happening.

### 2. Minimum N too low

The minimum response threshold for empirical calibration is N=10 (line 119). Baker & Kim (2004, "Item Response Theory: Parameter Estimation Techniques") establish:
- Stable Rasch estimates require N >= 200
- Stable 2PL estimates require N >= 500
- At N=10, standard error on difficulty is approximately +/- 0.5 logits — practically useless for item selection

## Scope

### 1. Rename and document honestly

- Rename the class header comment from "Rasch/2PL" to "Rasch (with optional external discrimination priors)"
- Add a doc comment explaining that true 2PL calibration requires an EM algorithm (Bock & Aitkin 1981) and is a future enhancement
- The `Discrimination` field on `IrtItemParameters` should document: "Default 1.0 (Rasch). External calibration tools (e.g., Python girth) can supply 2PL estimates that are stored here."

### 2. Tiered calibration confidence

Replace the binary N=10 threshold with a tiered system:

```csharp
public enum CalibrationConfidence
{
    Default,         // N < 30: use Rasch default (b=0, a=1, c=0), flag as uncalibrated
    LowConfidence,   // 30 <= N < 100: logit-based Rasch estimate, wide SE
    Moderate,        // 100 <= N < 200: stable Rasch estimate
    High,            // N >= 200: stable Rasch, eligible for 2PL if external tool provides
    Production       // N >= 500: full confidence, 2PL trusted
}
```

Items below N=30 should use `IrtItemParameters.RaschDefault` and be flagged so the CAT algorithm does not trust their difficulty estimate.

### 3. Add confidence to IrtItemParameters

```csharp
public record IrtItemParameters(
    string QuestionId,
    double Difficulty,
    double Discrimination,
    double GuessParameter,
    int ResponseCount,
    double FitResidual,
    CalibrationConfidence Confidence,  // NEW
    DateTimeOffset CalibratedAt
);
```

## Files to Modify

- `src/actors/Cena.Actors/Services/IrtCalibrationPipeline.cs` — rename, add tiered confidence
- `src/actors/Cena.Actors/Assessment/ConstrainedCatAlgorithm.cs` — use confidence to weight item selection (prefer high-confidence items for ability estimation)
- `src/api/Cena.Admin.Api/ItemBankHealthService.cs` — include calibration confidence in health dashboard

## Acceptance Criteria

- [ ] No mention of "2PL" in code comments unless qualified with "external calibration only"
- [ ] `CalibrationConfidence` enum exists and is populated on every `IrtItemParameters`
- [ ] Items with N < 30 are flagged as uncalibrated and use Rasch defaults
- [ ] CAT algorithm prefers items with Moderate+ confidence for ability estimation
- [ ] Item bank health dashboard shows confidence distribution (how many items at each tier)
