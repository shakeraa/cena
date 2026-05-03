# SESSION-UX-001: Session Start with Topic Choice + Personalized Suggestion

## Design

### Flow

1. Student opens a session (from mastery map node or "Start learning" button)
2. **Topic picker**: shows available topics in current track, sorted by:
   - Topics needing refresh (BKT+ decay below 0.40)
   - Topics in progress (0.20–0.80 mastery)
   - New topics (prerequisites met, not yet started)
3. **Personalized suggestion**: "Recommended: Quadratic Equations — you haven't practiced in 5 days"
4. Student picks a topic or accepts the suggestion
5. Session starts with the selected topic + adaptive question selection (BKT+)

### Suggestion algorithm

```
1. Find all skills where effectiveMastery < refreshThreshold (0.40) AND pLearned >= 0.80
   → These are "needs refresh" — highest priority
2. Find skills where 0.20 < effectiveMastery < 0.80
   → These are "in progress" — sorted by days since last practice (longest first)
3. Find skills where all prerequisites are met AND never attempted
   → These are "ready to start" — sorted by prerequisite depth (shallowest first)
4. Return top suggestion from the first non-empty group
```

### UI elements

- Topic cards with mastery ring (from MasteryMap.vue)
- "Recommended" badge on suggested topic
- "Continue where you left off" option if a session was interrupted
- Subject filter chips (algebra, calculus, physics) per track

### Data requirements

- `GET /api/me/mastery-summary` — returns per-skill effective mastery
- BKT+ calculator provides effectiveMastery with forgetting
- Skill prerequisite graph provides ordering
