# SESSION-UX-002: Progressive Disclosure + Natural Session Boundaries

## Design

### Progressive disclosure levels

The session UI reveals complexity based on student proficiency (BKT+ effective mastery):

| Level | BKT P(known) | UI elements visible | Rationale |
|-------|-------------|---------------------|-----------|
| **Novice** | < 0.20 | Worked example + full hint ladder + simplified figure | Sweller worked-example effect |
| **Developing** | 0.20–0.50 | Faded example + 2 hints + standard figure | Renkl fading transition |
| **Competent** | 0.50–0.80 | 1 hint available + full figure detail | Aleven help-abuse mitigation |
| **Expert** | > 0.80 | No scaffolding, full figure, step input only | Sweller expertise-reversal |

### Natural session boundaries

Sessions end naturally, never abruptly:

1. **Completion signal**: after answering the last question in a set, show "Session complete" with mastery summary
2. **Time-based soft cap**: at 25 minutes, show "Good stopping point" suggestion (not mandatory)
3. **Mastery milestone**: when a skill crosses the progression threshold (0.85), celebrate and offer to continue or stop
4. **Energy check**: after 10 consecutive questions, brief pause with "How are you feeling?" (no data collection, just UX breathing room)

### What we do NOT do

- No forced session length (the student chooses when to stop)
- No "one more question" dark pattern
- No countdown timer creating urgency (ship-gate violation)
- No penalty for stopping mid-session (progress is auto-saved)
- No streak counter tracking consecutive sessions (ship-gate violation)

### Implementation

- `ScaffoldingLevel` on `SessionQuestionDto` already drives the hint ladder (FIND-pedagogy-006)
- `FigureThumbnail.vue` (FIG-MOBILE-001) already has `visibleAtLevel` support
- Session soft cap: new `useSessionTimer` composable with 25-min suggestion
- Mastery celebration: new `MasteryMilestone.vue` modal triggered by `CrossedProgressionThreshold` from BKT+
