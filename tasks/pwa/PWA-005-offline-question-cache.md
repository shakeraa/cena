# PWA-005: Offline Question Cache + IndexedDB Storage

## Goal
Implement an IndexedDB-backed question cache so students can review previously loaded questions, their completed steps, and mastery data when offline. This is not "full offline mode" (CAS verification requires the server) — it's "review what you've already done, even without network."

## Context
- Architecture doc: `docs/research/cena-mobile-pwa-approach.md` §2.2
- Depends on: PWA-001 (Service Worker foundation)
- CAS verification is server-side — students cannot answer new questions offline
- But they CAN: review completed questions, see their mastery map, review step solutions, see hints they unlocked
- Israeli periphery schools (Negev, Galilee) have intermittent connectivity — students study on buses

## Scope of Work

### 1. IndexedDB Schema
Use `idb` (lightweight IndexedDB wrapper, ~1.2KB gzipped):

```typescript
interface CenaOfflineDB {
  questions: {
    key: string;            // questionId
    value: {
      questionId: string;
      questionData: QuestionDto;
      figureAssets: string[];   // URLs of cached figure images/SVGs
      completedSteps: StepResultDto[];
      lastAccessed: number;
      cachedAt: number;
    };
    indexes: {
      'by-accessed': number;
    };
  };
  mastery: {
    key: string;            // 'current'
    value: {
      skills: Record<string, SkillMasteryDto>;
      updatedAt: number;
    };
  };
  sessions: {
    key: string;            // sessionId
    value: {
      sessionId: string;
      summary: SessionSummaryDto;
      completedAt: number;
    };
    indexes: {
      'by-date': number;
    };
  };
}
```

### 2. Cache Population Strategy
- **Questions**: Cache each question as the student completes it (after all steps verified). Include the question data, completed steps with feedback, and figure asset URLs.
- **Mastery**: Update mastery snapshot on every BKT update received via SignalR. Single record, always overwritten.
- **Sessions**: Cache session summary when a session ends. Include total questions, accuracy, time spent, skills practiced.
- **Figure assets**: The Service Worker (PWA-001) handles figure caching via `StaleWhileRevalidate`. IndexedDB stores the URLs so the offline review page knows which figures to reference.

### 3. Cache Limits & Eviction
- **Questions**: Max 50 questions. LRU eviction based on `lastAccessed`.
- **Sessions**: Max 30 session summaries. Oldest evicted first.
- **Mastery**: Single record, always current.
- **Total budget**: ~2MB estimated (questions are JSON, not heavy).
- **Eviction runs**: After every new cache write, check limits and evict if needed.

### 4. Offline Review UI
Create `src/student/full-version/src/views/OfflineReview.vue`:

- Route: `/review` (accessible from bottom nav)
- Shows: list of cached questions grouped by topic, with completion status
- Each question card shows: topic, difficulty, date completed, accuracy (steps correct/total)
- Tap a question → see the full question with completed steps and feedback
- Mastery map: show cached mastery data with "Last updated: X ago" label
- Session history: list of recent sessions with summary stats
- **When online**: same page works, but data is fresh from API (not from cache)
- **When offline**: data from IndexedDB, with a subtle "Viewing cached data" indicator

### 5. Cache Freshness
- On app start (when online): refresh mastery snapshot from API, update IndexedDB
- On session end: cache the completed session and any new questions
- Do NOT prefetch questions the student hasn't seen — this leaks the question pool and creates security issues (§25)
- Display `lastUpdated` timestamps on all cached data so the student knows how fresh it is

## Files to Create/Modify
- `src/student/full-version/src/services/offlineDb.ts` — IndexedDB schema, CRUD operations
- `src/student/full-version/src/composables/useOfflineCache.ts` — reactive cache state
- `src/student/full-version/src/views/OfflineReview.vue` — offline review page
- `src/student/full-version/src/components/CachedQuestionCard.vue` — question display for cached data
- `src/student/full-version/src/router/index.ts` — add `/review` route

## Non-Negotiables
- **Never prefetch unseen questions** — this is a security boundary (students could inspect IndexedDB to see upcoming questions)
- **Cache only completed questions** — in-progress questions are handled by PWA-004 (draft persistence)
- **IndexedDB, not localStorage** — localStorage is 5MB max and synchronous; IndexedDB is async and has no practical limit
- **LRU eviction must work** — test with 51 questions and verify the oldest is evicted
- **Accessible offline indicator** — screen reader must announce "viewing cached data" when offline

## Acceptance Criteria
- [ ] Complete a question online → go offline → navigate to `/review` → question is visible with all steps
- [ ] Mastery map shows cached data when offline, with "last updated" timestamp
- [ ] 51st question evicts the least recently accessed question
- [ ] IndexedDB total size stays under 5MB with 50 questions cached
- [ ] Online → offline → online transition is seamless (no page reload needed)
- [ ] `/review` page works both online and offline
- [ ] All text i18n'd (Arabic + Hebrew)
- [ ] RTL layout correct on review page

## Testing Requirements
- **Unit**: `offlineDb.ts` — test CRUD, LRU eviction, storage limits
- **Unit**: `useOfflineCache.ts` — test reactive state updates
- **Integration**: Playwright — complete 3 questions, go offline, verify review page shows them
- **Manual**: Real device — complete questions, enable airplane mode, verify review

## DoD
- PR merged to `main`
- IndexedDB schema documented in PR
- Offline review screenshot (Arabic) attached to PR
- LRU eviction test passing

## Reporting
Complete with: `branch=<worker>/<task-id>-pwa-offline-cache,cache_max=<n>,idb_size_kb=<n>`
