# FIGURE-005: Backend — `PhysicsDiagramService` (programmatic SVG generator)

## Goal
Server-side .NET service that takes a `PhysicsDiagramSpec` and produces a publication-ready SVG (reference quality: the right-hand image in the GPAI "Text to Technical Diagram" screenshot — inclined plane with force vectors, LaTeX labels, clean axes). SVGs are generated once per spec, cached on CDN, served instantly to students.

## Depends on
- FIGURE-001 (ADR — locks programmatic SVG vs TikZ vs Matplotlib)
- FIGURE-002 (schema defines `PhysicsDiagramSpec`)

## Context
- Reference: user showed `gpai` inclined-plane schematic with: mass box on incline, angle θ, gravity vector `mg`, components `mg sin θ` / `mg cos θ`, normal `N`, friction `F_f = −μ_k N sgn(v_x) x̂`, optional non-inertial frame with Coriolis + centrifugal labels, coordinate axes (Y up, X right), LaTeX-typeset labels.
- **MUST NOT use a text-to-image model** (Nano Banana, DALL-E, GPAI-style). Image-gen models hallucinate forces and labels → educationally wrong → fails quality gate.
- Mobile model (`src/mobile/lib/features/diagrams/models/diagram_models.dart`) already anticipates batch-generated SVG → S3/CDN. This service is the generation side of that contract.
- Ingestion pipeline exists: `src/api/Cena.Admin.Api/IngestionPipelineService.cs` + `IngestionPipelineCloudDir.cs`. Reuse its storage conventions.

## Work to do
1. Create `src/api/Cena.Admin.Api/PhysicsDiagramService.cs`:
   - Input: `PhysicsDiagramSpec`
   - Output: SVG string + content hash (used as cache key)
   - Body types in v1: `InclinedPlane`, `FreeBody`, `Pulley`, `Vector2D`
   - Body types in v2 (separate tasks): `CircuitSchematic`, `CircularMotion`, `WavePattern`
2. Rendering primitives (pure C# SVG builder — no external binary needed):
   - Coordinate frame (axes, ticks, origin label)
   - Inclined plane (triangle with surface texture, angle arc, angle label)
   - Mass box (rectangle with center-of-mass dot and label)
   - Force vector (arrow with head, tail at body center, length proportional to magnitude, color per type: gravity=red, normal=green, friction=orange, applied=blue, inertial=dashed grey)
   - Component decomposition (dashed construction lines for `mg sin θ` / `mg cos θ`)
   - Angle arc with degree symbol
   - Math label (embed KaTeX-rendered SVG as `<foreignObject>` or pre-rendered `<text>` with Unicode — pick one in the ADR; prefer pre-rendered SVG paths from KaTeX for font independence)
3. Publication-ready typography:
   - Single typeface for math (Latin Modern / STIX / Asana Math) — whichever KaTeX uses
   - Crisp 1px strokes
   - Vector arrow heads matched to stroke width
   - `viewBox` set so the SVG scales cleanly in any container
   - Dark-mode variant: generate both `<svg class="light">` and `<svg class="dark">` via CSS vars — or emit two SVGs per spec and let the client pick
4. Content-addressed caching:
   - Hash the spec → cache key
   - Store generated SVG in same cloud dir as `IngestionPipelineCloudDir`
   - Serve via existing CDN pathway
5. API endpoint: `POST /api/admin/figures/physics` → returns `{ cdnUrl, ariaLabel, widthPx, heightPx }`. Admin authoring + AI generation both call it.
6. `AriaLabel` generation: deterministic text description from the spec ("Inclined plane at 30 degrees. A 5 kilogram mass rests on the surface. Gravity acts downward. Normal force acts perpendicular to the surface. Kinetic friction acts up the slope."). Localized for EN/AR/HE via existing i18n.
7. Unit tests:
   - `src/api/Cena.Admin.Api.Tests/Figures/PhysicsDiagramServiceTests.cs`
   - One test per body type → render + check SVG is valid XML + contains expected force labels + `aria-label` non-empty
   - Snapshot test: golden SVG for a canonical inclined-plane spec, fails if output drifts
   - Perf test: 1000 renders < 5 seconds (programmatic SVG, no LaTeX shell-out)

## Non-negotiables
- **No image-gen model calls.** This is a deterministic vector pipeline. Correctness verified by unit test, not by "looks right".
- No PII in the cache key or file path
- SymPy (or equivalent numeric check) verifies that the force magnitudes sum to the stated acceleration — test asserts `Σ F − ma = 0` for any spec that claims to be at equilibrium or with known `a`
- File size: each SVG < 30KB

## Do NOT
- Use Skia, ImageMagick, or a headless browser to rasterize — SVG only
- Ship v1 with circuits or molecular diagrams — they're in later tasks
- Write the admin editor UI — that's FIGURE-006

## DoD
- Service + endpoint + 4 body types working
- Golden snapshots committed (one per body type)
- Perf budget met
- An admin user can POST a spec via curl and get back a CDN URL that renders a publication-ready SVG in the browser

## Reporting
Complete with branch, list of files, golden-snapshot paths, and one screenshot of the canonical inclined-plane render beside the GPAI reference.
