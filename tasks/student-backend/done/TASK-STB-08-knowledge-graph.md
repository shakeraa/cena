# TASK-STB-08: Knowledge Graph & Concept Annotations

**Priority**: HIGH — blocks the knowledge graph and all concept-detail pages
**Effort**: 3-4 days
**Depends on**: [STB-00](TASK-STB-00-me-profile-onboarding.md)
**UI consumers**: [STU-W-10](../student-web/TASK-STU-W-10-knowledge-graph.md), [STU-W-13](../student-web/TASK-STU-W-13-diagrams.md)
**Status**: Not Started

---

## Goal

Expose the concept catalog as a graph API, add pathfinding between concepts, and provide student-private annotations for concepts and diagrams.

## Endpoints

| Method | Path | Purpose | Rate limit | Auth |
|---|---|---|---|---|
| `GET` | `/api/content/concepts?subject=&depth=` | List concepts with prerequisites, successors, related, and the student's current mastery | `api` | JWT |
| `GET` | `/api/content/concepts/{id}` | Concept detail: description, aliases, example questions, linked diagrams | `api` | JWT |
| `POST` | `/api/knowledge/path` | Shortest prerequisite path between two concepts, weighted by mastery deficits | `api` (30/min) | JWT |
| `POST` | `/api/me/concept-annotations` | Create or update a private annotation on a concept | `api` | JWT |
| `GET` | `/api/me/concept-annotations?conceptId=` | Read student's annotations | `api` | JWT |
| `DELETE` | `/api/me/concept-annotations/{id}` | Delete an annotation | `api` | JWT |
| `POST` | `/api/me/diagram-annotations` | Create or update a diagram annotation | `api` | JWT |
| `GET` | `/api/me/diagram-annotations?diagramId=` | Read diagram annotations | `api` | JWT |
| `DELETE` | `/api/me/diagram-annotations/{id}` | Delete a diagram annotation | `api` | JWT |
| `GET` | `/api/content/diagrams/{id}/teacher-layer` | Teacher annotation layer if one exists | `api` | JWT |
| `POST` | `/api/content/diagrams/{id}/export` | Render diagram + annotations to PDF/PNG/SVG server-side | `api` (10/day) | JWT |

## Data Access

- **Reads**:
  - `ConceptDocument` (new catalog) with `prerequisites`, `successors`, `related` arrays
  - `ConceptMasteryProjection` (extend existing mastery projection with join-key)
  - `StudentConceptAnnotationDocument` (new)
  - `StudentDiagramAnnotationDocument` (new)
  - `TeacherDiagramAnnotationLayerDocument` (new)
- **Writes**: annotation documents; event sourcing not required for private per-student notes (CRUD is fine)
- **Graph traversal**: concept graph is small enough (< 10k nodes) to traverse in-memory in the handler via Dijkstra; no external graph DB needed
- **Statement timeout**: concept list is paginated and indexed on subject; pathfinding is pure CPU, no DB

## Hub Events

None for v1 — annotations are private and low-frequency.

## Contracts

Add to `Cena.Api.Contracts/Dtos/Knowledge/`:

- `ConceptDto` with mastery field, prerequisites, successors, related
- `ConceptDetailDto` with description, aliases, example questions, linked diagrams
- `PathRequestDto` — `{ fromConceptId, toConceptId }`
- `PathResponseDto` — `{ path: [conceptId], totalMasteryDeficit, estimatedSessions }`
- `ConceptAnnotationDto` — `{ id, conceptId, text, color, createdAt, updatedAt }`
- `DiagramAnnotationDto` — `{ id, diagramId, strokes[], labels[], coordinates, createdAt }`
- `TeacherAnnotationLayerDto`

## Auth & Authorization

- Firebase JWT
- `ResourceOwnershipGuard` on all annotation endpoints
- Teacher annotation layer readable by any student in the teacher's class(es)
- Diagram export enforces the same read authorization as diagram fetch

## Cross-Cutting

- Concept list cacheable privately with ETag + `Cache-Control: private, max-age=300`
- Concept detail cacheable the same way
- Path results not cached (pairwise, low reuse)
- Annotations invalidate cache on write
- Diagram export uses a background job; POST returns `{ jobId }`, client polls or listens for `ExportReady` event (future; v1 can synchronous-stream up to 10 MB)
- Handler logs with `correlationId`

## Definition of Done

- [ ] All 11 endpoints implemented and registered in `Cena.Student.Api.Host`
- [ ] DTOs in `Cena.Api.Contracts/Dtos/Knowledge/`
- [ ] Concept list returns correct mastery for current student
- [ ] Pathfinding returns the optimal prerequisite path and handles no-path (404 with reason)
- [ ] Concept annotations CRUD working
- [ ] Diagram annotation coordinates stored as normalized floats, not pixels
- [ ] Teacher annotation layer loads if present; returns 404 cleanly if not
- [ ] Diagram export returns a valid PDF / PNG / SVG matching the requested format
- [ ] Integration tests for all endpoints including concurrent annotation writes
- [ ] OpenAPI spec updated
- [ ] TypeScript types regenerated
- [ ] Mobile lead review: mobile will consume the same endpoints when its knowledge-graph surface ships

## Out of Scope

- Teacher annotation authoring — admin tool
- Public concept pages for SEO — future
- Concept diff view (what changed this week) — server just returns current state; client derives diff
- Collaborative concept annotations — private only for v1
