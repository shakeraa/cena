# WEB-006: Teacher Dashboard — Class Overview, Knowledge Gap Heatmap, Assignments

**Priority:** P1 — primary web client use case
**Blocked by:** WEB-001 (scaffold), WEB-003 (GraphQL), WEB-004 (state)
**Estimated effort:** 4 days
**Contract:** `contracts/frontend/graphql-schema.graphql` (ClassRoom, ConceptGap, AssignmentCompletion types)

---

## Context
The teacher dashboard is the primary web interface. Teachers see a class overview with per-student mastery/streak/XP, a knowledge gap heatmap identifying concepts where students struggle, and assignment completion tracking. Real-time updates via GraphQL subscriptions show mastery events as they happen during class. The dashboard uses the `ClassRoom`, `ConceptGap`, and `AssignmentCompletion` types from the GraphQL schema.

## Subtasks

### WEB-006.1: Class Overview Page
**Files:**
- `src/web/src/features/teacher/ClassOverviewPage.tsx` — page component
- `src/web/src/features/teacher/components/StudentTable.tsx` — sortable student list
- `src/web/src/features/teacher/components/ClassStatsBar.tsx` — summary stats
- `src/web/src/features/teacher/components/LiveActivityFeed.tsx` — real-time mastery events

**Acceptance:**
- [ ] Route: `/teacher/class/:classRoomId`
- [ ] Uses `useClassOverview(classRoomId)` GraphQL hook
- [ ] `ClassStatsBar`: displays `studentCount`, `averageMastery` (0-100%), `averageStreak` (days)
- [ ] `StudentTable`: columns = Name, Mastery (%), Streak (days), XP, Last Active, Risk Level
- [ ] Sortable by any column (default: Last Active DESC) using `StudentSortField` enum
- [ ] Relay cursor pagination with "Load more" button
- [ ] Risk level badge: green (active, progressing), yellow (stagnating or inactive 3+ days), red (inactive 7+ days)
- [ ] `LiveActivityFeed`: subscribes to `onClassMasteryUpdate(classRoomId)`, shows "Alice mastered Addition!" events in real-time
- [ ] Inactive students section: students not active in 7+ days (from `inactiveStudents(daysThreshold: 7)`)
- [ ] Empty state: no students enrolled -> show enrollment instructions

**Test:**
```typescript
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ClassOverviewPage } from '@/features/teacher/ClassOverviewPage';

test('renders class stats bar', async () => {
  render(<ClassOverviewPage />, { wrapper: createTeacherWrapper({ classRoomId: 'class-1' }) });

  await screen.findByText('30 Students');
  expect(screen.getByText('65% Average Mastery')).toBeInTheDocument();
  expect(screen.getByText('4.2 Day Avg Streak')).toBeInTheDocument();
});

test('student table is sortable by mastery', async () => {
  render(<ClassOverviewPage />, { wrapper: createTeacherWrapper({ classRoomId: 'class-1' }) });

  const masteryHeader = await screen.findByRole('columnheader', { name: /mastery/i });
  await userEvent.click(masteryHeader);

  const rows = screen.getAllByRole('row');
  // First student row (after header) should have highest mastery
  expect(within(rows[1]).getByText(/95%/)).toBeInTheDocument();
});

test('live feed shows mastery events', async () => {
  const { container } = render(<ClassOverviewPage />, { wrapper: createTeacherWrapper({ classRoomId: 'class-1' }) });

  simulateSubscriptionEvent('onClassMasteryUpdate', {
    classRoomId: 'class-1', studentId: 's1', studentName: 'Alice',
    conceptId: 'c1', conceptName: 'Addition',
    previousMastery: 0.6, newMastery: 0.88, justMastered: true,
    timestamp: new Date().toISOString(),
  });

  await screen.findByText(/Alice.*mastered.*Addition/);
});
```

---

### WEB-006.2: Knowledge Gap Heatmap
**Files:**
- `src/web/src/features/teacher/KnowledgeGapPage.tsx` — page component
- `src/web/src/features/teacher/components/GapHeatmap.tsx` — heatmap visualization
- `src/web/src/features/teacher/components/ConceptGapCard.tsx` — detail card

**Acceptance:**
- [ ] Route: `/teacher/class/:classRoomId/gaps`
- [ ] Uses `useKnowledgeGapAnalysis(classRoomId, filter)` GraphQL hook
- [ ] `GapHeatmap`: grid of concepts colored by `studentsBelowThreshold` (green < 20%, yellow 20-50%, red > 50%)
- [ ] Each cell shows concept name and `averageMastery` percentage
- [ ] Clicking a cell opens `ConceptGapCard` with detail: `studentsAttempted`, `dominantErrorType`, `studentsBelowThreshold`
- [ ] `dominantErrorType` displayed with icon and label: PROCEDURAL (gear), CONCEPTUAL (brain), MOTIVATIONAL (heart)
- [ ] Filter controls: `masteryThreshold` slider (default 0.85), `minAttempts` input (default 3), `topic` dropdown
- [ ] Uses `KnowledgeGapFilter` input type from schema
- [ ] Empty state: no gaps found -> "All students are on track!"
- [ ] `ConceptGap` fields from schema: `concept { id, name, displayName }`, `studentsBelowThreshold`, `averageMastery`, `studentsAttempted`, `dominantErrorType`

**Test:**
```typescript
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { KnowledgeGapPage } from '@/features/teacher/KnowledgeGapPage';

test('renders heatmap with concept cells', async () => {
  const mockGaps = [
    { concept: { id: 'c1', name: 'Fractions', displayName: 'Fractions' },
      studentsBelowThreshold: 0.6, averageMastery: 0.45, studentsAttempted: 25, dominantErrorType: 'CONCEPTUAL' },
    { concept: { id: 'c2', name: 'Decimals', displayName: 'Decimals' },
      studentsBelowThreshold: 0.15, averageMastery: 0.78, studentsAttempted: 28, dominantErrorType: null },
  ];

  render(<KnowledgeGapPage />, { wrapper: createTeacherWrapper({ gaps: mockGaps }) });

  const fractions = await screen.findByText('Fractions');
  expect(fractions.closest('[data-severity]')).toHaveAttribute('data-severity', 'high');

  const decimals = screen.getByText('Decimals');
  expect(decimals.closest('[data-severity]')).toHaveAttribute('data-severity', 'low');
});

test('clicking gap cell shows detail card', async () => {
  render(<KnowledgeGapPage />, { wrapper: createTeacherWrapper({ gaps: mockGaps }) });

  await userEvent.click(await screen.findByText('Fractions'));
  expect(screen.getByText('25 students attempted')).toBeInTheDocument();
  expect(screen.getByText(/conceptual/i)).toBeInTheDocument();
});
```

---

### WEB-006.3: Assignment Completion Tracking
**Files:**
- `src/web/src/features/teacher/AssignmentsPage.tsx` — page component
- `src/web/src/features/teacher/components/AssignmentProgressBar.tsx` — per-concept progress
- `src/web/src/features/teacher/components/AssignmentTable.tsx` — tabular view

**Acceptance:**
- [ ] Route: `/teacher/class/:classRoomId/assignments`
- [ ] Uses `useAssignmentCompletion(classRoomId, conceptIds)` GraphQL hook
- [ ] Teacher selects concepts from a concept picker (search with full-text index)
- [ ] `AssignmentCompletion` fields from schema: `conceptId`, `conceptName`, `totalStudents`, `masteredCount`, `inProgressCount`, `notStartedCount`, `averageMastery`, `estimatedRemainingMinutes`
- [ ] `AssignmentProgressBar`: stacked bar showing mastered (green) / in-progress (yellow) / not-started (gray)
- [ ] `AssignmentTable`: columns = Concept, Mastered, In Progress, Not Started, Avg Mastery, Est. Remaining
- [ ] Sort by any column
- [ ] Refresh button to re-fetch completion status
- [ ] `estimatedRemainingMinutes` displayed as "~2h 15m" or "< 1h"

**Test:**
```typescript
import { render, screen } from '@testing-library/react';
import { AssignmentsPage } from '@/features/teacher/AssignmentsPage';

test('renders assignment completion for concepts', async () => {
  const mockCompletion = [
    { conceptId: 'c1', conceptName: 'Addition', totalStudents: 30,
      masteredCount: 20, inProgressCount: 7, notStartedCount: 3,
      averageMastery: 0.78, estimatedRemainingMinutes: 45 },
    { conceptId: 'c2', conceptName: 'Subtraction', totalStudents: 30,
      masteredCount: 15, inProgressCount: 10, notStartedCount: 5,
      averageMastery: 0.62, estimatedRemainingMinutes: 120 },
  ];

  render(<AssignmentsPage />, { wrapper: createTeacherWrapper({ completion: mockCompletion }) });

  await screen.findByText('Addition');
  expect(screen.getByText('20 / 30')).toBeInTheDocument(); // Mastered
  expect(screen.getByText('~45m')).toBeInTheDocument();
  expect(screen.getByText('~2h')).toBeInTheDocument();
});

test('progress bar shows correct proportions', async () => {
  render(<AssignmentsPage />, { wrapper: createTeacherWrapper({ completion: mockCompletion }) });

  const bar = await screen.findByTestId('progress-c1');
  // 20/30 mastered = 66.7%
  expect(bar.querySelector('[data-segment="mastered"]')).toHaveStyle({ width: '66.67%' });
});
```

**Edge cases:**
- Class with 200+ students -> pagination prevents memory issues
- No students have attempted a concept -> notStartedCount = totalStudents, no progress bar
- Real-time update arrives during page view -> subscription updates the relevant row
- Teacher has multiple classrooms -> classroom selector at top of dashboard

---

## Integration Test

```typescript
test('teacher dashboard full flow', async () => {
  render(<TeacherDashboard />, { wrapper: createTeacherWrapper({ classRoomId: 'class-1' }) });

  // 1. Class overview loads
  await screen.findByText('30 Students');

  // 2. Navigate to knowledge gaps
  await userEvent.click(screen.getByRole('link', { name: /knowledge gaps/i }));
  await screen.findByText('Fractions'); // Gap concept

  // 3. Navigate to assignments
  await userEvent.click(screen.getByRole('link', { name: /assignments/i }));
  await screen.findByText('Addition');

  // 4. Live update arrives
  simulateSubscriptionEvent('onClassMasteryUpdate', {
    classRoomId: 'class-1', studentId: 's1', studentName: 'Bob',
    conceptId: 'c1', conceptName: 'Addition',
    previousMastery: 0.8, newMastery: 0.92, justMastered: true,
    timestamp: new Date().toISOString(),
  });

  // Assignment completion updates
  await screen.findByText('21 / 30'); // Bob mastered, count increased
});
```

## Rollback Criteria
- If heatmap performance is poor with 500+ concepts: paginate concepts or use canvas renderer
- If real-time subscriptions cause too many re-renders: debounce subscription handler to 1s
- If GraphQL queries are slow: add Redis caching layer for class overview

## Definition of Done
- [ ] All 3 subtasks pass their tests
- [ ] `npm test -- --filter teacher` -> 0 failures
- [ ] Class overview with sortable paginated student table
- [ ] Knowledge gap heatmap with severity coloring and detail cards
- [ ] Assignment completion with stacked progress bars
- [ ] Real-time subscription updates reflected in UI
- [ ] All GraphQL types match schema exactly
- [ ] Responsive layout (desktop 1024px+)
- [ ] PR reviewed by frontend lead + UX designer
