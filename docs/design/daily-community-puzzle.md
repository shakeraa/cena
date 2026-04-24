# Daily Community Puzzle — Design Spec

> Replaces: Point 6 "Daily Wordle" (original game-design proposal)
> Reason: Wordle retention IS loss aversion (Duolingo blog confirmation). Ship-gate violation.
> Pattern: Brilliant-style shared community challenge, not personal streak.

## Core mechanic

One shared math/physics problem per day, drawn from the question bank. All students who participate see the same problem. No streak, no chain, no penalty for skipping.

### What it IS

- A single problem published daily at 00:00 UTC
- Students can attempt it any time during the day (or not — no notification nagging)
- After solving (or after 24h), students see **community results**: "42% chose answer B — here's why the right approach uses the quadratic formula"
- Archive of past puzzles, browsable without penalty or unlock gates
- Problem difficulty cycles through levels: Mon/Thu easy, Tue/Fri medium, Wed/Sat hard, Sun review

### What it is NOT

- No personal streak counter (ship-gate: banned)
- No "X days in a row" badge or indicator
- No "don't break the chain" copy anywhere
- No loss-aversion notification ("you're about to lose your streak")
- No competitive leaderboard (only aggregate stats)
- No XP or currency reward (only mastery credit per normal BKT pipeline)

## UI surface

### Student web (`/daily-puzzle`)

```
┌─────────────────────────────────────────────┐
│  Today's Community Puzzle  •  April 13      │
│                                             │
│  [Problem card — same as QuestionCard.vue]  │
│                                             │
│  [Submit]                                   │
│                                             │
│  ─── After submit or 24h ───               │
│                                             │
│  Community Results:                         │
│  • 847 students attempted today             │
│  • 62% got it right                         │
│  • Most common approach: factoring          │
│  • Most common mistake: sign flip in step 3 │
│  (linked to misconception remediation)      │
│                                             │
│  [Browse past puzzles →]                    │
└─────────────────────────────────────────────┘
```

### Notifications

- Default OFF (per ship-gate)
- If opted in: one daily notification at student's preferred time, max 1/day
- Copy: "Today's community puzzle is ready" (neutral, no urgency)
- Never: "Don't miss today's puzzle" / "Your streak is at risk"

## Data model

The daily puzzle uses the existing `QuestionDocument` schema — no new infrastructure.

```csharp
public record DailyPuzzleConfig(
    DateOnly Date,
    string QuestionId,         // FK to QuestionDocument
    string TrackId,            // which curriculum track
    DifficultyLevel Difficulty,
    DateTimeOffset PublishedAt
);

public record DailyPuzzleResult(
    DateOnly Date,
    int TotalAttempts,
    int CorrectCount,
    Dictionary<string, int> AnswerDistribution,  // answer → count
    string? MostCommonMisconception              // buggy-rule ID
);
```

## Community results aggregation

- Results computed from anonymous aggregate counts (k-anonymity, k ≥ 10 per ADR-0003)
- No individual student answers are visible to other students
- Misconception identification uses the catalog (MISC-001) at the aggregate level only
- Results published after the puzzle closes (24h) or when the student submits (whichever is later)

## Legal compliance

| Rule | Status |
|------|--------|
| No streak counter | Compliant — no streak anywhere |
| No loss-aversion | Compliant — no penalty for skipping |
| No variable-ratio reward | Compliant — no random drops |
| Ship-gate scanner | All copy must pass `scripts/shipgate/scan.mjs` |
| Misconception data | Session-scoped per ADR-0003 — aggregate stats only |
| Notification opt-in | Default OFF per ship-gate |
