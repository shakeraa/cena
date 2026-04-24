# Gource-Style Knowledge Graph Visualization

> **Status:** Specification
> **Inspiration:** [Gource](https://gource.io/) — software repo visualization that shows commit history as an animated organic tree
> **Applies to:** Mobile (Flutter), Web (Canvas), Shared links (pre-rendered video)

---

## Concept

Instead of a static graph with colored nodes, the knowledge graph is a **living, animated visualization** that grows organically over time — like Gource visualizes a git repo's history.

### Key Visual Properties (from Gource)

| Gource Feature | Cena Adaptation |
|---|---|
| **Files appear as nodes** | Concepts appear as glowing nodes when first encountered |
| **Directories are branches** | Subject areas (Algebra, Trigonometry, Calculus) are branches radiating from center |
| **Commits animate in sequence** | Learning events (attempts, mastery) animate in chronological order |
| **User avatar moves between files** | Student's "cursor" (a glowing dot) moves between concepts as they study |
| **Color = language/type** | Color = subject (Math=teal, Physics=amber, Chemistry=green) |
| **Idle files fade** | Concepts not reviewed recently fade (decay visualization) |
| **Branches grow organically** | New concepts push outward, force-directed physics |
| **Time-lapse** | Replay an entire semester of learning in 60 seconds |

---

## Three Modes

### Mode 1: Live Session Graph (real-time, during studying)

The graph updates in real-time as the student answers questions:

```
Student answers question about "Derivatives"
  → "Derivatives" node pulses (scale 1.0 → 1.3 → 1.0, green glow)
  → Mastery bar on node fills up (0.70 → 0.78)
  → Student cursor (glowing dot) slides from previous concept to "Derivatives"
  → If mastery crosses 0.85: node EXPLODES into full color + particle effect
  → Prerequisite edge to "Limits" briefly highlights (showing connection)
  → Newly unlocked concepts ("Integration") fade IN at the frontier
```

**Performance target:** 60fps on mid-range Android, using CustomPainter + quadtree (per MOB-006).

### Mode 2: Time-Lapse Replay (Gource-style, viewable anytime)

Replay the student's entire learning journey as a cinematic animation:

```
Timeline: September 2026 → March 2027 (one semester)

00:00 — Empty graph. Just "Numbers" and "Basic Algebra" as seed nodes.
00:05 — First session: student cursor appears, moves to "Linear Equations"
         Node materializes with a pop animation. Mastery bar starts filling.
00:10 — Day 2: "Quadratic Equations" branch grows from "Linear Equations"
         Prerequisite edge draws in with a flowing animation.
00:15 — Week 2: Algebra branch spreads. 15 nodes visible. Colors brighten.
00:20 — Week 4: First mastery! "Linear Equations" explodes green.
         Particle trail connects to "Quadratic Equations" (now unlocked).
00:30 — Month 2: Trigonometry branch appears. Student cursor splits time
         between Algebra (amber glow = active review) and Trig (new).
00:45 — Month 4: 100 concepts mastered. Graph is a sprawling tree.
         Faded nodes = concepts due for spaced repetition review.
         Bright nodes = recently mastered.
00:55 — Month 6: Full graph. 200+ mastered. Beautiful organic structure.
01:00 — End. Zoom out to show entire tree. Statistics overlay:
         "שרה mastered 247 concepts in 6 months"
```

**Implementation:** Pre-rendered using Rive animation (on-device) or Remotion (server-side for sharing).

### Mode 3: Shared Replay (exportable video/GIF)

Student exports their time-lapse as a video or animated GIF for social sharing:

```
Output: 15-second or 60-second video
Format: MP4 (H.264) or animated WebP
Resolution: 1080×1920 (Instagram Stories) or 1920×1080 (YouTube)

Overlay:
  - Student first name (only) in corner
  - Running counter: "Concepts mastered: 0 → 247"
  - Running XP counter
  - Streak flame icon
  - "Powered by Cena" watermark
  - "Download at cena.app" CTA at end

Privacy:
  - First name only (no last name, school, class)
  - No attempt counts or error patterns visible
  - No methodology indicators
  - No behavioral data
  - Student must explicitly tap "Create Video" (not auto-generated)
```

---

## Technical Architecture

### Data Flow for Time-Lapse

```
Marten Event Store (source of truth)
  │
  │ Replay all events for student, ordered by timestamp:
  │   ConceptAttempted → shows cursor moving, mastery bar filling
  │   ConceptMastered  → shows explosion effect
  │   MasteryDecayed   → shows node fading
  │   MethodologySwitched → shows brief methodology icon flash
  │   SessionStarted/Ended → shows session boundary markers
  │
  ▼
TimelapseDataBuilder (server-side service)
  │
  │ Produces: TimelapseScript
  │   - Ordered list of (timestamp, event_type, concept_id, mastery_delta)
  │   - Graph layout computed at each keyframe
  │   - Camera path (zoom/pan decisions based on where activity is)
  │
  ▼
Renderer (two options):
  │
  ├─► On-device (Rive/Flutter Canvas)
  │   - Interactive: user can scrub timeline
  │   - Plays at 1x, 2x, 5x, 10x speed
  │   - Uses same CustomPainter from MOB-006
  │   - Performance: fine for < 500 events
  │
  └─► Server-side (Remotion / FFmpeg)
      - For video export (Instagram/YouTube format)
      - Uses Remotion React components → MP4
      - Triggered by "Create Video" button in app
      - Queued job, takes 30-60 seconds to render
      - Result uploaded to S3, CDN link returned to client
```

### TimelapseScript Model

```dart
/// A script that drives the Gource-style time-lapse animation.
/// Built from replaying the student's event stream.
class TimelapseScript {
  final String studentId;
  final DateTime startDate;
  final DateTime endDate;
  final int totalEvents;
  final List<TimelapseKeyframe> keyframes;
  final Duration totalDuration;       // e.g., 60 seconds for playback
  final TimelapseStats stats;         // final counts for overlay
}

class TimelapseKeyframe {
  final DateTime eventTimestamp;      // Real time of the event
  final double playbackTime;          // 0.0 - 1.0 normalized to total duration
  final String conceptId;
  final TimelapseEventType type;      // appeared, attempted, mastered, decayed
  final double masteryDelta;          // +0.08 or -0.05
  final double newMastery;            // P(known) after this event
  final Offset? cameraTarget;         // Where to pan the camera
  final double? cameraZoom;           // Zoom level at this keyframe
}

enum TimelapseEventType {
  conceptAppeared,    // First time concept is visible on graph
  attempted,          // Student answered (mastery bar change)
  mastered,           // Crossed 0.85 threshold (explosion effect)
  decayed,            // HLR timer fired, recall dropped (fade effect)
  sessionBoundary,    // Session start/end marker
  methodologySwitched, // Brief icon flash
}

class TimelapseStats {
  final int conceptsMastered;
  final int totalAttempts;
  final int sessionsCompleted;
  final int currentStreak;
  final int totalXp;
  final Duration totalStudyTime;
}
```

---

## Animation Specifications

### Node Lifecycle

| State | Visual | Animation |
|---|---|---|
| **Appeared** (first encounter) | Small gray dot, semi-transparent | Fade in + scale from 0 → 1 (200ms ease-out) |
| **In progress** | Yellow/amber circle, mastery bar filling | Bar animates on each attempt (150ms) |
| **Mastered** | Full green circle, bright glow | EXPLOSION: scale to 1.5x + particle burst + settle to 1.0 (500ms) |
| **Decayed** | Fading green → yellow border, opacity drops | Slow fade over 2 seconds, border becomes dashed |
| **Reviewed** (after decay) | Yellow → green again | Quick flash (100ms) + bar refill |

### Edge Animations

| Trigger | Visual |
|---|---|
| **Prerequisite discovered** | Edge draws from parent → child with flowing dot animation (like electricity) |
| **Both nodes mastered** | Edge becomes solid green (both ends achieved) |
| **Prerequisite decayed** | Edge becomes dashed (dependency weakened) |

### Camera Behavior (Gource-style)

```
Camera follows the student's activity:
  - Pan to whichever concept was just attempted (smooth ease, 300ms)
  - Zoom in when activity is concentrated in one area
  - Zoom out when student switches subjects
  - During time-lapse: camera path pre-computed from keyframe positions
  - Double-tap: zoom to fit entire graph
  - Pinch: manual zoom override
```

### Physics

```
Force-directed layout with Gource-like feel:
  - Concepts repel each other (Coulomb force)
  - Prerequisites attract (spring force)
  - Same-subject concepts attract weakly (cluster)
  - Mastered concepts settle faster (higher damping)
  - New concepts have high initial velocity (they "pop" into place)
  - Layout runs in isolate (not UI thread)
  - Physics pauses when user is panning/zooming
```

---

## Integration with Existing Architecture

| Component | Role |
|---|---|
| `StudentActor` | Source of events for time-lapse script |
| `Marten Event Store` | Replays events to build TimelapseScript |
| `CurriculumGraphActor` | Provides base graph layout and prerequisite edges |
| `DiagramCacheService` | Caches pre-computed TimelapseScript for replay |
| `Remotion Worker` | Server-side video rendering for export |
| `S3 + CDN` | Stores and serves exported videos |
| `MOB-006 CustomPainter` | On-device renderer for interactive time-lapse |
| `GraphQL` | `myTimelapse(subject, startDate, endDate)` query |

---

## Who Can See What (extends kg-access-control.md)

| Viewer | Live Graph | Time-Lapse Replay | Video Export |
|---|---|---|---|
| **Student (own)** | ✅ Full interactive | ✅ Full replay | ✅ Can export |
| **Teacher (class)** | ✅ Read-only view | ✅ Can replay student's journey | ❌ Cannot export student's video |
| **Parent (child)** | ✅ Simplified view | ✅ Can replay (simplified: mastery events only, no errors) | ❌ Cannot export |
| **Shared link** | ❌ Static snapshot | ❌ No replay | ✅ Student shares their own video |
| **Admin** | ✅ Full | ✅ Full | ✅ For support/debugging |
