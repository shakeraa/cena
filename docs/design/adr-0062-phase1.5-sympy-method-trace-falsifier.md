# ADR-0062 Phase 1.5 — SymPy method-trace falsifier

- **Status**: Architecture brief — implementation-ready
- **Date**: 2026-05-03
- **Author**: claude-subagent-phase15-phase2-design
- **Scope**: Implements ADR-0062 §Phasing "Phase 1.5 — falsifier"
- **Owning ADR**: [ADR-0062](../adr/0062-concept-extraction-and-multi-skill-mastery.md)
- **Constraint anchor**: [ADR-0002](../adr/0002-sympy-correctness-oracle.md) — SymPy is the sole correctness oracle. The falsifier MUST NOT change correctness verdicts; it can only adjust the *confidence* attached to a concept claim.

## §1 Problem statement

The Hybrid extractor (Phase 1) lets an LLM emit `concepts: [{skill, role, rationale}]` for a question. The LLM is fluent and often confidently wrong about *why* a question tests a skill — it will tag a polynomial differentiation problem as "uses chain rule" because the prompt mentions a composite function shape, even when the canonical solution does plain power-rule term-by-term differentiation. The platform has a free, deterministic, already-trusted source of truth for what a problem actually requires: SymPy. Today SymPy is invoked only for verification of equivalence/step/solve and returns a Boolean plus a simplified expression — it does not surface *what techniques it used*, so we cannot cross-check the LLM's rationale against the actual solution path.

Pedagogical failure mode this catches: a curator confirms "chain-rule, integration-by-parts" on a question whose solution is purely algebraic; supporting-concept nudges (Phase 2) then push positive mastery signals on chain-rule for students who got it right by power-rule. The student's chain-rule posterior drifts upward without evidence. The falsifier prevents this by *downweighting* concept claims the SymPy method trace doesn't support, before the curator confirm step.

## §2 Current state

- **`IQuestionConceptExtractor.ExtractAsync`** (`src/actors/Cena.Actors/Mastery/Extraction/IQuestionConceptExtractor.cs`) returns `ExtractionResult(IReadOnlyList<QuestionConcept> Concepts, string Strategy)`. The Phase 1 implementation in flight (Hybrid: rules → LLM fallback) populates `QuestionConcept.Rationale` (LLM's "show your work" string) and `QuestionConcept.Confidence` (extractor self-reported 0..1).
- **`QuestionConceptsExtracted_V1`** (`src/actors/Cena.Actors/Events/ConceptExtractionEvents.cs:70-75`) is the audit event. Stream-keyed on `QuestionId`. Emitted by `BagrutDraftPersistence.cs:200-216`. Persisted concepts ride straight onto `QuestionListProjection.Apply(QuestionConceptsExtracted_V1, …)` at `src/actors/Cena.Actors/Questions/QuestionListProjection.cs:104-116`.
- **SymPy invocation** flows through `ICasRouterService.VerifyAsync` (`src/actors/Cena.Actors/Cas/CasRouterService.cs:58`) → `ISymPySidecarClient.VerifyAsync` (`src/actors/Cena.Actors/Cas/SymPySidecarClient.cs:69-143`) → NATS subject `cena.cas.verify.sympy` → `dispatch()` in `docker/sympy-sidecar/sympy_worker.py:259-276`.
- **Current SymPy response shape** (`SymPyResponse` in `SymPySidecarClient.cs:183-188`, `Response` in `sympy_worker.py:64-79`): `{success, simplifiedA, simplifiedB, error}`. There is no method-trace field on either side.
- **Current variant generation pipeline** invokes the CAS router for `Equivalence` / `StepValidity` / `Solve` operations (e.g. `CasGatedQuestionPersister`). None of those calls currently surface what techniques SymPy used.
- The `Tier` field on `QuestionConcept` already accepts `"hybrid"` and the strategy id can already be `"rules_v1+llm_haiku4_5_v1"` — the audit string can be extended to `"…+sympy_falsifier_v1"` without a schema change.

## §3 Design — interfaces, contracts, file layout

The falsifier is a *post-extractor decorator*: it wraps `IQuestionConceptExtractor`. The Hybrid extractor stays as-is; the falsifier reads its output, calls SymPy for a method trace, downweights any concept whose rationale is not supported by the trace, and emits a sibling audit event.

### New types

```
src/actors/Cena.Actors/Mastery/Extraction/
  ISymPyMethodTraceClient.cs        # NEW — thin client over the new sidecar op
  SymPyMethodTrace.cs                # NEW — DTO returned by the sidecar
  IConceptFalsifier.cs               # NEW — pure-function falsifier interface
  RationaleMatchFalsifier.cs         # NEW — default implementation
  FalsifyingConceptExtractor.cs      # NEW — decorator over IQuestionConceptExtractor
src/actors/Cena.Actors/Events/
  ConceptExtractionEvents.cs         # EXTENDED — adds QuestionConceptFalsifierVerdict_V1
docker/sympy-sidecar/
  sympy_worker.py                    # EXTENDED — new op MethodTrace
src/actors/Cena.Actors/Cas/
  CasContracts.cs                    # EXTENDED — new CasOperation.MethodTrace member
  ISymPySidecarClient.cs             # NEW method TraceMethodAsync (additive)
```

### Sidecar contract (Python side)

New op code `OP_METHOD_TRACE = 5`. Request reuses `CasVerifyRequest` but `expressionA` carries the *problem expression* (e.g. the LaTeX for `d/dx [(x^2+1)^3]`). Response shape adds three fields:

```json
{
  "success": true, "simplifiedA": "<sympy result>", "simplifiedB": null, "error": null,
  "methodTrace": {
    "techniques": ["polynomial-differentiation", "chain-rule"],
    "operatorsUsed": ["Derivative", "Pow", "Add"],
    "treeDepth": 4
  }
}
```

`techniques` is a closed enum on the Python side (initial set: `polynomial-differentiation`, `chain-rule`, `product-rule`, `quotient-rule`, `integration-by-parts`, `u-substitution`, `partial-fractions`, `trig-identity`, `polynomial-factoring`, `quadratic-formula`, `completing-the-square`, `system-elimination`, `system-substitution`, `numerical-evaluation`, `unknown`). Every entry maps 1:1 to a `TechniqueCode` constant on the .NET side. `unknown` means SymPy didn't recognise the canonical reduction path; the falsifier treats `unknown` as **fail-open** (no downweight).

Implementation on the Python side: detect techniques by inspecting the AST of the problem and the simplification path. Concrete heuristics (deterministic, no ML):
- `chain-rule` ↔ `Derivative` node whose argument contains a non-`Symbol` subtree (composite function).
- `product-rule` ↔ `Derivative(Mul(f, g))` where both `f` and `g` contain the diff variable.
- `integration-by-parts` ↔ `Integral(Mul(f, g))` where SymPy's internal heuristic chose `manualintegrate` IBP.
- `polynomial-factoring` ↔ `factor(expr) != expr` and the result has more terms than input.
- Other entries follow the same shape: deterministic AST/operator inspection, no ML.

If any heuristic raises, the entry is silently dropped; if all fail, `techniques: ["unknown"]` (fail-open).

### .NET-side interfaces

```csharp
public sealed record SymPyMethodTrace(
    IReadOnlyList<string> Techniques,        // canonical TechniqueCode strings
    IReadOnlyList<string> OperatorsUsed,
    int TreeDepth);

public interface ISymPyMethodTraceClient
{
    /// Returns null when SymPy is unavailable or returned a non-Ok status.
    /// NEVER throws — fail-open semantics so the falsifier degrades to a no-op.
    Task<SymPyMethodTrace?> TraceMethodAsync(string problemLatex, CancellationToken ct);
}

public interface IConceptFalsifier
{
    /// Pure function. Given the LLM concepts and a SymPy method trace,
    /// returns a new list with adjusted confidences. Order preserved.
    /// Caller decides whether to persist.
    IReadOnlyList<QuestionConcept> Falsify(
        IReadOnlyList<QuestionConcept> concepts,
        SymPyMethodTrace trace,
        out FalsifierVerdict verdict);
}

public sealed record FalsifierVerdict(
    string FalsifierVersion,             // "rationale_match_v1"
    IReadOnlyList<ConceptAdjustment> Adjustments,
    string TraceTechniquesJoined);       // for the audit event payload

public sealed record ConceptAdjustment(
    string SkillCode,
    double ConfidenceBefore,
    double ConfidenceAfter,
    string Reason);                      // "supported", "unsupported_by_trace", "unmappable_rationale"

/// Decorator. Composition order from DI: FalsifyingConceptExtractor wraps HybridConceptExtractor.
public sealed class FalsifyingConceptExtractor : IQuestionConceptExtractor { … }
```

### New event (audit-only sibling)

```csharp
public sealed record QuestionConceptFalsifierVerdict_V1(
    string QuestionId,
    string FalsifierVersion,                          // "rationale_match_v1"
    IReadOnlyList<ConceptAdjustmentRecord> Adjustments,
    string TraceTechniques,                           // comma-joined for human reads
    string TraceOperators,                            // comma-joined
    int TraceTreeDepth,
    DateTimeOffset Timestamp);

public sealed record ConceptAdjustmentRecord(
    string SkillCode, double Before, double After, string Reason);
```

The original `QuestionConceptsExtracted_V1` *already* carries the post-falsifier confidences (the decorator returns the adjusted list). The verdict event is a sibling for forensics: "what % of LLM-suggested concepts survived the falsifier?" — answered by streaming the verdict events, no need to re-run extraction.

### Falsifier algorithm (RationaleMatchFalsifier v1)

Per concept:
1. If `concept.Tier == "rules"` or `concept.Tier == "curator"` → unchanged. Falsifier targets only LLM-emitted claims.
2. If `concept.Rationale.Length < 10` chars → unchanged (nothing to falsify; rationale is too thin).
3. Map the rationale to a `TechniqueCode` by normalised substring match against the canonical technique-name lexicon (lowercase, `-` and ` ` interchangeable, language: English; Hebrew/Arabic rationales are skipped — `unmappable_rationale`, no downweight). The lexicon table lives next to `RationaleMatchFalsifier` and is the only place with locale strings.
4. If the mapped technique is in `trace.Techniques` → `Reason="supported"`, no change.
5. If the mapped technique is **not** in `trace.Techniques` AND `trace.Techniques` is non-empty AND does NOT contain `"unknown"` → `Reason="unsupported_by_trace"`, **multiplicative downweight**: `ConfidenceAfter = ConfidenceBefore * 0.5`, floored at 0.0. Multiplicative chosen over veto so a high-confidence (0.9) claim becomes 0.45 (still above the curator-review threshold), and a low-confidence (0.4) claim becomes 0.20 (below the threshold). Veto would be too strong for a v1 heuristic; additive would let high-confidence wrong claims survive.
6. If the rationale is unmappable (locale, ambiguity, no lexicon hit) → `Reason="unmappable_rationale"`, no change. **Fail-open**.

### DI registration changes

`Cena.Actors.Mastery.Extraction.ExtractionServiceRegistration` (existing or to-be-added in Phase 1) gains:

```csharp
services.AddOptions<FalsifierOptions>().BindConfiguration("Cena:Concepts:Falsifier");
services.TryAddSingleton<ISymPyMethodTraceClient, SymPyMethodTraceClient>();
services.TryAddSingleton<IConceptFalsifier, RationaleMatchFalsifier>();

// Decorator wiring: only when the flag is on.
if (configuration.GetValue<bool>("Cena:Concepts:Falsifier:Enabled"))
{
    services.Decorate<IQuestionConceptExtractor, FalsifyingConceptExtractor>();
}
```

`services.Decorate` is from `Scrutor` (already in the dependency tree per `Directory.Packages.props`; verify before implementing — open question §10.3).

`MartenConfiguration.cs` adds:

```csharp
opts.Events.AddEventType<Cena.Actors.Events.QuestionConceptFalsifierVerdict_V1>();
```

`BagrutDraftPersistence.cs` (the sole caller emitting `QuestionConceptsExtracted_V1` today) gains a parallel `Events.Append(id, verdict)` after extraction completes. The decorator returns the verdict via an out-parameter on a new method `IQuestionConceptExtractor.TryGetLastFalsifierVerdict(out FalsifierVerdict?)` so the caller can attach it without a separate round-trip — ALTERNATIVE: pass the verdict back through `ExtractionResult` (cleaner, touches the existing record). **Decision**: extend `ExtractionResult` with `FalsifierVerdict? Verdict` (default null). It's an additive field, all existing tests already use named-parameter construction, the change is mechanical.

## §4 Acceptance criteria

A coder agent has shipped Phase 1.5 when ALL of these hold:

1. **Sidecar trace op**: `POST` to NATS subject `cena.cas.verify.sympy` with `{"operation": 5, "expressionA": "(x**2+1)**3"}` returns `{success: true, methodTrace: {techniques: ["chain-rule", …], …}}` within 200ms p99 on dev hardware.
2. **`SymPyMethodTraceClient.TraceMethodAsync(unparseable)`**: returns `null`, never throws, logs at Warning.
3. **`SymPyMethodTraceClient.TraceMethodAsync` when sidecar circuit-breaker open**: returns `null` immediately (<5ms), no NATS call.
4. **`RationaleMatchFalsifier.Falsify`** with `concept.Tier="rules"`: returns the same list, every adjustment has `Reason="supported"` and `Before==After`.
5. **`RationaleMatchFalsifier.Falsify`** with LLM concept whose rationale is `"applies the chain rule via composite function"` and trace techniques `["polynomial-differentiation"]`: returns concept with `ConfidenceAfter == ConfidenceBefore * 0.5`, `Reason="unsupported_by_trace"`.
6. **`RationaleMatchFalsifier.Falsify`** with rationale in Hebrew (`"שימוש בנגזרת מורכבת"`): `Reason="unmappable_rationale"`, no change. The Hebrew rationale path is explicit (locale-detected via simple Unicode-range check), not silent.
7. **`FalsifyingConceptExtractor.ExtractAsync`** with the falsifier flag OFF: behaves identically to the wrapped extractor (decorator is bypassed at DI time, not at runtime).
8. **End-to-end (using a real Postgres + the Python sidecar in docker-compose-test)**: persisting a Bagrut draft via `BagrutDraftPersistence.PersistAsync` writes BOTH `QuestionConceptsExtracted_V1` (with adjusted confidences) AND `QuestionConceptFalsifierVerdict_V1` to the same stream, in order, in a single `SaveChangesAsync`.
9. **Curator-facing surface**: a downweighted concept does NOT disappear from `GET /api/admin/ingestion/items/{id}/concepts`. The curator sees the post-falsifier confidence and can still pick or reject. (Falsifier never deletes.)
10. **No correctness change**: a CAS step-validity / equivalence verdict on the same expression returns the same Boolean before and after falsifier wiring. (Pin via existing `CasRouterServiceTests`.)
11. **Strategy string**: extracted event's `ExtractionStrategy` is `"rules_v1+llm_haiku4_5_v1+sympy_falsifier_v1"` when the falsifier ran; unchanged when it didn't.

## §5 Test plan

| Test | Type | What it pins | Notes |
|---|---|---|---|
| `RationaleMatchFalsifierTests.SupportedRationale_Unchanged` | unit | concept with rationale matching trace stays at original confidence | |
| `RationaleMatchFalsifierTests.UnsupportedRationale_HalvedConfidence` | unit | the 0.5 multiplicative downweight | |
| `RationaleMatchFalsifierTests.RulesTier_NeverDownweighted` | unit | rules-tier and curator-tier concepts pass through untouched | |
| `RationaleMatchFalsifierTests.UnknownTraceTechniques_FailsOpen` | unit | when SymPy returned `["unknown"]`, no concepts adjusted | |
| `RationaleMatchFalsifierTests.HebrewRationale_Unmappable_FailsOpen` | unit | Unicode-range detect → `unmappable_rationale` reason | |
| `RationaleMatchFalsifierTests.EmptyRationale_NoOp` | unit | rationales <10 chars skipped | |
| `RationaleMatchFalsifierTests.LowConfidenceClaim_StaysAboveZero` | unit | floored at 0.0, never negative | |
| `SymPyMethodTraceClientTests.SidecarReturnsTechniques_Parsed` | unit (mocked NATS) | wire shape parses correctly | |
| `SymPyMethodTraceClientTests.SidecarTimeout_ReturnsNull` | unit | fail-open, no exception bubble | |
| `SymPyMethodTraceClientTests.CircuitBreakerOpen_ImmediateNull` | unit | reuses `SymPySidecarClient` breaker; doesn't add a second one | |
| `FalsifyingConceptExtractorTests.FlagOff_BypassedAtDi` | architecture | when flag off, DI returns the inner extractor directly | mirrors `CasBindingStartupCheck` style |
| `FalsifyingConceptExtractorTests.SidecarUnavailable_PassesThrough` | unit | trace=null → all concepts unchanged + verdict logged | |
| `BagrutDraftPersistenceFalsifierIntegrationTests.AppendsBothEvents` | integration (real Postgres) | both events on the same stream, atomic | needs `RequiresPostgresFact` |
| `SymPySidecarMethodTraceCanaryTests.PolyDerivative_NoChainRule` | conformance (real sidecar) | the canonical `(x^2)' = 2x` returns `["polynomial-differentiation"]`, NOT `["chain-rule"]` | extends `SymPySandbox.CanarySuiteTests` |
| `SymPySidecarMethodTraceCanaryTests.CompositeDerivative_HasChainRule` | conformance (real sidecar) | `((x^2+1)^3)'` returns `["chain-rule", …]` | |

Integration tests requiring Postgres use the existing `RequiresPostgresFact` attribute (see `feedback_container_state_before_build.md`) — they auto-skip when the docker compose stack isn't up, never fail spuriously.

Tests requiring the Python sidecar use the existing `SymPySandbox.CanarySuite` infrastructure: the test fixture probes `cena.cas.health.sympy` once, skips the test class cleanly when no reply within 2s.

## §6 Migration / rollout plan

Single feature flag, three steps.

**Flag**: `Cena:Concepts:Falsifier:Enabled` (env `CENA_CONCEPTS_FALSIFIER_ENABLED`). Default `false`. Documented in `docs/engineering/feature-flags.md` (new entry, pattern matches `Cena:Variants:BagrutSeedToLlmEnabled`).

**Step 1 (ship dark)**: merge with flag default `false`. The decorator is not registered. The sidecar gets the new `MethodTrace` op but nobody calls it. The `QuestionConceptFalsifierVerdict_V1` event type is registered with Marten but never appended. Zero behavior change for users / curators.

**Step 2 (canary on staging)**: flip `Cena:Concepts:Falsifier:Enabled=true` on staging. Run the calibration corpus (first 200 Bagrut items per ADR-0062 §Phasing) and inspect the verdict events. Acceptance criteria: ≥80% of LLM-suggested concepts have `Reason="supported"` (i.e. the falsifier is not over-aggressively downweighting). If <80%, the lexicon needs work — open a follow-up brief.

**Step 3 (production)**: flip in production after staging signal is green for ≥48h.

**Reverse path**: flag flip back to `false`. The verdict events that were already written remain on disk; the projection ignores them (it never read them; only `QuestionConceptsExtracted_V1` flows into `QuestionListProjection`). Adjusted confidences in already-shipped extracted events stay as they were — this is intentional, the audit trail is immutable. To re-run extraction without falsifier, the caller would re-extract; that's a separate operator action, not part of the rollback.

**No DB migration needed**. Event types are append-only; no schema or projection rebuild is required.

## §7 Failure modes + degradation

The falsifier sits at the curator-facing seam (between extraction and the curator review UI). It MUST NOT throw out of `IQuestionConceptExtractor.ExtractAsync` — that would block draft persistence.

| Failure mode | Behavior | Why |
|---|---|---|
| SymPy sidecar unreachable (NATS timeout) | `TraceMethodAsync` returns `null`. Falsifier sees null trace → skips all adjustments. Concepts pass through unchanged. Verdict event records `FalsifierVersion="rationale_match_v1"` and `Adjustments=[]`. | **Fail-open**. SymPy is the correctness oracle, not the precision oracle; we don't block extraction on its absence. |
| SymPy circuit breaker open | Same as above (the existing breaker in `SymPySidecarClient` short-circuits to null). | Fail-open. |
| SymPy returns malformed JSON | Logged at Error, treated as `null` trace. | Fail-open. |
| SymPy returns `techniques: ["unknown"]` | All concepts unchanged. | Fail-open per §3 algorithm. |
| Rationale is in Hebrew/Arabic | Locale-detected, marked `unmappable_rationale`, no change. | Fail-open. The lexicon is English-only in v1; non-English rationales are surfaced to the curator unfiltered. |
| Rationale is empty / whitespace | `unmappable_rationale`, no change. | Fail-open. |
| `Falsify(concepts, trace)` itself throws | Caught by `FalsifyingConceptExtractor`, logged at Error with full context, returns the inner extractor's result unchanged + a synthetic verdict event with `FalsifierVersion="rationale_match_v1_failed"` and the exception type in `TraceTechniques`. | Fail-open at the decorator boundary. The audit trail captures the failure. |
| Verdict event append fails (Marten error) | Logged at Error. The extraction event still appended. | Audit gap is acceptable; correctness path is unaffected. |

The **only** path where the falsifier mutates user-visible state is §5 of the algorithm (multiplicative downweight). Every other path is a no-op. There is no scenario where the falsifier raises a concept's confidence, adds a concept, or removes a concept.

## §8 Cost + telemetry

**Cost analysis**. Phase 1 already runs SymPy at `CasGatedQuestionPersister` and on every step verification at student session time. The falsifier adds one MORE SymPy call **per question, at extract time**. Question extraction happens at draft-persist time + variant-generate time; at the ADR-0062 §Cost projection of 5,000 items/year, this is 5,000 additional SymPy calls/year. Each call is ~50ms wall-clock + ~0.1 SymPy CPU-seconds. Annual additional CPU: ~10 minutes. Annual additional NATS bytes: ~5MB. **Conclusion**: cost is in the noise floor and does not need budgeting. SymPy CAS calls remain unmetered (no $ cost — runs in our own container).

**Telemetry to ship with the feature**:

```
cena.concepts.falsifier.invocations.total{outcome="supported|adjusted|unmappable|sidecar_unavailable"}
cena.concepts.falsifier.adjustments.total{reason}
cena.concepts.falsifier.duration.ms                  (Histogram)
cena.concepts.falsifier.trace.techniques.count       (Histogram, 0–10)
cena.concepts.falsifier.curator_override_after_adjustment.total
  # Increments when a curator confirms a concept the falsifier downweighted.
  # If this metric ever exceeds the no-adjustment override rate, the falsifier
  # is hurting more than helping → kill the feature.
```

**Kill criteria** (to prevent the falsifier from quietly hurting precision):
- If `curator_override_after_adjustment / total_adjustments > 0.30` over 30 days → falsifier is over-aggressively downweighting; flip the flag off and revisit the lexicon.
- If `sidecar_unavailable / invocations > 0.05` over 7 days → SymPy sidecar reliability problem; not a falsifier-specific issue but should pause rollout while it's investigated.

## §9 Out of scope

Explicit list of what this brief deliberately does NOT solve. Future-self should not have to dig through git history.

1. **Veto semantics.** The falsifier downweights, never vetoes. ADR-0062 §Open Questions includes "additive, multiplicative, or veto" — this brief picks multiplicative for v1 and defers veto to Phase 3 telemetry.
2. **Hebrew/Arabic rationale lexicon.** v1 lexicon is English-only. If telemetry shows ≥30% of rationales come back unmappable due to locale, a follow-up brief adds Hebrew + Arabic technique strings.
3. **LLM-side method extraction.** We could ask the LLM itself "what techniques does this problem require" instead of asking SymPy. We don't, because (a) LLM is the entity we're cross-checking, so using it as ground truth defeats the purpose, (b) SymPy is free and deterministic. This is a deliberate design choice, not a future enhancement.
4. **Per-track lexicon.** A 4yu Bagrut and a 5yu Bagrut might use different terminology for the same technique. v1 ignores `TrackHint`; if curator-override telemetry by track diverges, a follow-up brief adds track-specific lexicons.
5. **Fine-grained operator-tree comparison.** v1 only compares the *technique label*. We could compare the actual SymPy operator tree against an LLM-emitted operator tree. We don't — LLM operator-tree compliance is too brittle for a v1 heuristic.
6. **Confidence floor below the curator-review threshold.** If a downweight pushes confidence below e.g. 0.3, we could auto-suppress the concept from the curator UI. We don't; the curator always sees everything. UI changes are out of scope.
7. **Reapplying the falsifier on existing extracted events.** A bulk re-run job over historical extractions is a separate work item.

## §10 Open questions

Each is bounded and answerable.

1. **Does `Scrutor.Decorate` work with a `TryAddSingleton`-registered base?** It does in standard configurations, but the Cena DI uses `TryAddSingleton<IQuestionConceptExtractor, RulesOnlyConceptExtractor>()` today — verify the decorator wiring works against `TryAdd`. Implementer should run `FalsifyingConceptExtractorTests.FlagOff_BypassedAtDi` first.
2. **Is `Scrutor` actually in `Directory.Packages.props`?** Need to verify before assuming. If not, either add the package (small) or hand-roll the decorator (trivial — three lines). Implementer should grep `Directory.Packages.props` for `Scrutor` and decide.
3. **Does the Python sidecar's existing parser robustly handle the new `OP_METHOD_TRACE`?** The dispatch table at `sympy_worker.py:259-276` returns `Response(False, error="unsupported operation code")` for unknown ops — verify that until the new code is deployed, .NET-side `TraceMethodAsync` correctly interprets the `success: false` reply as "tracing not supported", not as "trace empty". Edge case is at deploy-time only (mid-rollout where .NET ships first).
4. **Should the verdict event also capture the LLM rationale verbatim?** Currently the rationale is in the extracted event. Duplicating it would let an audit query answer "what did the LLM claim, what did SymPy say, what did the curator pick" from a single event. Pro: single-event audit. Con: PII / token-cost duplication. Open question — punt to implementer's preference, not load-bearing.
5. **Is `polynomial-differentiation` the right name, or should it be `power-rule`?** Curators write "power rule" in Hebrew/English. Lexicon synonym map needs a quick curator review before shipping the English lexicon.

---

```text
SELF-CHECK
  baseline:                NOT MEASURED — this is a docs-only brief; no test counts to baseline against.
  refusals-considered:     refused to expand sidecar response with arbitrary metadata; refused veto semantics in v1; refused to use LLM as the cross-check oracle.
  meta-patterns-noticed:   the codebase has a recurring pattern of "decorator-with-flag-default-off + sibling audit event"; this brief reuses it deliberately so the next reviewer recognizes it.
  deletion-vs-addition:    nothing deleted. Brief is purely additive.
  commit-message-lines:    no commit this turn (will land via the architecture-briefs branch commit).
  honesty-led-with:        the v1 lexicon being English-only is a real precision floor; surfaced in §9 and §10 unprompted.
  smoke-evidence:          label not claimed.
```
