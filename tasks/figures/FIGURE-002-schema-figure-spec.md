# FIGURE-002: Schema — `figure_spec` on QuestionDocument + event + upcaster

## Goal
Extend the event-sourced Question aggregate so every question can carry a structured, parametric `figure_spec`. Add the Marten document field, the event, the upcaster for existing rows, and the DTOs that cross the API boundary.

## Depends on
FIGURE-001 (ADR) must be merged first — body types are driven by the ADR's chosen stack.

## Context
- Question state lives in `src/actors/Cena.Actors/Questions/QuestionState.cs`, events in `src/actors/Cena.Actors/Events/QuestionEvents.cs`, projections in `QuestionListProjection.cs`, upcasters in `src/actors/Cena.Actors/Configuration/EventUpcasters.cs`, Marten config in `MartenConfiguration.cs`, infrastructure document in `src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs`.
- DTO contracts in `src/api/Cena.Api.Contracts/Admin/QuestionBank/QuestionBankDtos.cs`.
- Existing upcaster pattern in `EventUpcasters.cs` is the reference for evolving the stream backwards-compatibly.
- Quality gate tests live in `src/api/Cena.Admin.Api.Tests/QualityGate/QualityGateTestData.cs` — extend them.

## Schema shape (discriminated union)
```csharp
// Cena.Api.Contracts
public abstract record FigureSpec(string Type);

public sealed record FunctionPlotSpec(
    string Expression,           // LaTeX: "x^2 - 4x + 3"
    double XMin, double XMax,
    double YMin, double YMax,
    IReadOnlyList<FigureMarker> Markers,
    string? Caption,
    string AriaLabel             // MUST be populated by authoring/AI flow
) : FigureSpec("function_plot");

public sealed record PhysicsDiagramSpec(
    PhysicsBodyType Body,        // InclinedPlane | Pulley | FreeBody | CircularMotion | CircuitSchematic
    IReadOnlyList<PhysicsForce> Forces,
    IReadOnlyDictionary<string, double> Parameters,   // e.g. {"angle_deg": 30, "mu_k": 0.2}
    CoordinateFrame Frame,       // Inertial | NonInertial
    string? Caption,
    string AriaLabel
) : FigureSpec("physics_diagram");

public sealed record GeometryConstructionSpec(
    string JsxGraphJson,         // JSXGraph declarative spec
    string? Caption,
    string AriaLabel
) : FigureSpec("geometry");

public sealed record RasterFigureSpec(
    string CdnUrl,
    int WidthPx, int HeightPx,
    string? Caption,
    string AriaLabel,            // MANDATORY for a11y
    string? SourceAttribution    // "Bagrut 5-unit 2024 winter, q. 3b"
) : FigureSpec("raster");
```

## Work to do
1. Add `FigureSpec` hierarchy + supporting records to `Cena.Api.Contracts/Admin/QuestionBank/QuestionBankDtos.cs`.
2. Add `FigureSpec?` field to `QuestionState`, `QuestionDocument`, and the read projection.
3. Add event `QuestionFigureSpecUpdated(Guid QuestionId, FigureSpec? Spec)` to `QuestionEvents.cs`.
4. Add event handler to `QuestionState.cs`.
5. Add upcaster in `EventUpcasters.cs` that returns `Spec = null` for historical `QuestionCreated` events that lack it.
6. Marten schema registration in `MartenConfiguration.cs` — JSONB column, index on `Spec->>'Type'` for filtering.
7. Write round-trip tests in `src/actors/Cena.Actors.Tests/Events/` — one per figure type, plus one "null/legacy" test.
8. Wire into admin `GET /api/admin/questions/{id}` and `PUT` endpoints in `AdminApiEndpoints.cs` and `QuestionBankService.cs`. No stubs — real JSON serialization.
9. Wire into student `GET /api/sessions/{id}/question/{qid}` so the client can receive it.
10. Do NOT write the editor UI or the renderer yet — that's FIGURE-003 / FIGURE-006.

## Non-negotiables (from `docs/research/cena-sexy-game-research-2026-04-11.md`)
- `AriaLabel` is **not optional** — authoring UI and AI generation must fail if missing.
- No personally-identifying data in figure specs (Edmodo precedent — Track 8).
- Every spec MUST be reproducible by a deterministic parametric pipeline so variants can regenerate.

## DoD
- Build + tests green
- New round-trip tests pass for all four figure types
- Backward compat: loading an existing legacy question returns `FigureSpec = null`, serializes cleanly
- No behavioral changes visible to the student yet (field carried through but not rendered)

## Reporting
Complete with branch name and list of files touched.
