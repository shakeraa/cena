# Knowledge Graph Access Control Contract

> **Status:** Specification
> **Applies to:** All graph-serving endpoints (GraphQL, SignalR, sharing links)

---

## Access Control Matrix

| Viewer | What They See | Data Source | Freshness | Auth Scope |
|--------|--------------|-------------|-----------|------------|
| **Student (own)** | Full interactive graph: all concepts, mastery overlay, prerequisite paths, methodology indicators | Actor in-memory state (L1 cache) via SignalR push | **Real-time** (< 1s) | JWT role=STUDENT, self-only |
| **Student (shared link)** | Read-only snapshot: mastery colors, concept names, total mastered count. NO attempt history, NO methodology details | Pre-rendered snapshot (see Sharing below) | **Point-in-time** (snapshot at share time) | Public link with HMAC token (no login required) |
| **Teacher (own class)** | Per-student mastery overlay for students in their class. Concept-level detail: P(known), attempts, last active. Class-wide heatmap aggregation | GraphQL `studentKnowledgeGraph(studentId)` — async projection (TeacherDashboard read model) | **Near-real-time** (< 5s via GraphQL subscription) | JWT role=TEACHER, scoped to `school_id` + assigned class IDs |
| **Teacher (not their class)** | ❌ DENIED | N/A | N/A | Resolver rejects: teacher.class_ids does NOT include student's class |
| **Parent (own child)** | Simplified graph: mastery % per subject, top 5 struggling concepts, weekly progress delta. NOT the full interactive graph (too complex for parents) | GraphQL `childProgress(childId)` — ParentProgress read model | **Near-real-time** (< 10s) | JWT role=PARENT, scoped to `student_ids` claim |
| **Parent (not their child)** | ❌ DENIED | N/A | N/A | Resolver rejects: parent.student_ids does NOT include queried student |
| **Admin** | Full access to any student's graph + analytics aggregates | Direct actor query or read model | Real-time or near-real-time | JWT role=ADMIN |
| **Anonymous (no login)** | ❌ DENIED (except shared links) | N/A | N/A | 401 Unauthorized |

---

## Graph Data by Role

### Student View (full interactive graph)

```graphql
# What the student queries (already in graphql-schema.graphql)
query MyKnowledgeGraph($subject: Subject!) {
  myKnowledgeGraph(subject: $subject) {
    nodes {
      id
      name          # Hebrew / Arabic / English
      masteryState {
        pKnown
        isMastered
        lastAttemptedAt
        methodology   # Current active methodology for this concept
      }
    }
    edges {
      fromId
      toId
      type          # PREREQUISITE | RELATED
    }
  }
}
```

**Data served from:** StudentActor in-memory state → SignalR `KnowledgeGraphUpdated` event on every mastery change.

### Teacher View (class-scoped student graphs)

```graphql
# Teacher drills into one student's graph
query StudentKnowledgeGraph($studentId: ID!, $subject: Subject!) {
  studentDetail(studentId: $studentId) {
    knowledgeGraph(subject: $subject) {
      nodes {
        id
        name
        masteryState {
          pKnown
          isMastered
          totalAttempts     # Teacher sees attempt count
          lastAttemptedAt
        }
      }
      edges { fromId, toId, type }
    }
    # Teacher also sees:
    recentSessions(last: 5) { startedAt, duration, questionsAttempted }
    stagnationConcepts { conceptId, stagnationScore }
  }
}
```

**Data served from:** TeacherDashboard async projection (Marten). Updated via NATS events.

**Auth enforcement:**
```
# GraphQL resolver middleware
@auth(requires: TEACHER)
studentDetail(studentId) {
  # Check: is this student in one of the teacher's classes?
  if studentId NOT IN teacher.assignedStudentIds:
    throw ForbiddenError("Not authorized to view this student")
}
```

### Parent View (simplified, child-scoped)

```graphql
query ChildProgress($childId: ID!) {
  childProgress(childId: $childId) {
    # NOT the full graph — simplified view
    subjectSummaries {
      subject
      masteryPercentage     # e.g., 67%
      conceptsMastered
      conceptsTotal
      weeklyDelta           # +5% this week
    }
    strugglingConcepts(top: 5) {
      conceptName
      pKnown
      suggestedAction       # "Review algebra basics"
    }
    riskLevel               # GREEN / YELLOW / RED
  }
}
```

**Data served from:** ParentProgress async projection.

**Auth enforcement:**
```
@auth(requires: PARENT)
childProgress(childId) {
  if childId NOT IN parent.student_ids:
    throw ForbiddenError("Not authorized to view this child")
}
```

---

## Knowledge Graph Sharing (Viral Acquisition)

### How It Works

1. Student taps "Share My Graph" button in the knowledge graph screen
2. Client generates a **static snapshot** of current mastery state:
   - Concept names (Hebrew/Arabic)
   - Mastery colors (green/yellow/gray)
   - Total mastered count, XP, streak
   - NO attempt history, NO methodology, NO behavioral data
3. Snapshot uploaded to S3 as a pre-rendered image (PNG) + metadata JSON
4. Share link generated: `https://cena.app/graph/{shareToken}`
   - `shareToken` = HMAC-SHA256(studentId + timestamp + secret)
   - Expires after 7 days (configurable)
5. Link opens:
   - If app installed: deep link → opens graph in read-only mode
   - If not installed: web page with static graph image + "Download Cena" CTA

### Shared Graph Data (privacy-safe subset)

```json
{
  "shareToken": "abc123...",
  "createdAt": "2026-04-01T10:00:00Z",
  "expiresAt": "2026-04-08T10:00:00Z",
  "studentFirstName": "שרה",         // First name only, no last name
  "subject": "math",
  "stats": {
    "conceptsMastered": 147,
    "conceptsTotal": 2000,
    "currentStreak": 12,
    "totalXp": 4500,
    "level": 7
  },
  "graphSnapshot": {
    "imageUrl": "https://cdn.cena.app/shares/{token}.png",
    "thumbnailUrl": "https://cdn.cena.app/shares/{token}-thumb.png"
  }
}
```

**What is NOT shared:**
- Full mastery probabilities (P(known) per concept)
- Attempt history or response times
- Error patterns or methodology data
- Behavioral signals (backspace count, session abandonment)
- Parent/teacher/school information

### Share Link Web Fallback

For users who don't have the app:

```
https://cena.app/graph/{shareToken}
→ Server renders HTML page with:
  - Static graph image (pre-rendered PNG)
  - "שרה mastered 147/2000 math concepts"
  - "Download Cena" button → App Store / Play Store
  - Open Graph meta tags for social media previews
```

---

## Real-Time vs Near-Real-Time

| Consumer | Update Mechanism | Latency | Why |
|----------|-----------------|---------|-----|
| **Student (own graph)** | SignalR `KnowledgeGraphUpdated` push | < 1s | In-session, must feel instant |
| **Teacher (live class)** | GraphQL subscription `onClassMasteryUpdate` | < 5s | During lesson, should feel responsive |
| **Teacher (dashboard)** | GraphQL query (async projection) | < 10s | Browsing, not real-time critical |
| **Parent** | GraphQL query (async projection) | < 30s | Weekly check-ins, not time-sensitive |
| **Shared link** | Static snapshot | Point-in-time | One-time render at share time |

---

## Privacy Constraints

1. **Student ↔ Student:** Students CANNOT see each other's graphs (no direct query)
2. **Leaderboard:** Opt-in only, anonymized by default, shows effort metrics (XP) not mastery (per product-research.md)
3. **Shared links:** First name only, no school, no class, no behavioral data
4. **Teacher:** Scoped to assigned classes only (not school-wide unless admin)
5. **Parent:** Scoped to linked children only (via Firebase `student_ids` claim)
6. **GDPR:** Graph overlay is part of student PII → crypto-shredded on deletion
7. **Minors:** All graph data classified as PII per Israeli privacy law
