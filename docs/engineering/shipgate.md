# Ship-gate: dark-pattern ban enforcement

## Policy

Cena prohibits engagement mechanics that exploit loss aversion, variable-ratio reward schedules, artificial urgency, or social pressure on minors. This is not a design preference — it is a legal floor established by:

| Enforcement | Date | Key ruling |
|-------------|------|-----------|
| FTC v. Epic | 2022 | $245M — default-on social for minors |
| FTC v. Edmodo | 2023 | "Affected Work Product" — models trained on child data deleted |
| FTC COPPA Final Rule | 2025 | Explicit data minimization + notification consent for minors |
| ICO v. Reddit | Feb 2026 | £14.47M — per-user behavioral profiles of minors = profiling |
| Israel PPL Amendment 13 | In force | Applies to all Cena users in Israel |

## What is banned

1. **Streak counters** that can go to zero (loss aversion)
2. **Variable-ratio rewards** on answer correctness (slot-machine pattern)
3. **Loss-aversion copy**: "don't break", "you'll lose", "keep the chain", FOMO urgency
4. **Guilt/shame push notifications**: "your tutor misses you"
5. **Confetti/haptics/audio** tied to non-learning events (reward inflation)
6. **Default-on social matchmaking** or public leaderboards ranking minors
7. **Streak-freeze currency** of any kind

## What is allowed

- Positive-frame daily cadence signal (Apple Fitness rings style — shows progress, no punishment for missing)
- Co-op study pods (opt-in, no ranking)
- Community puzzles (shared challenge, no personal streak)
- Session completion celebration (brief, proportional)

## CI enforcement

The scanner at `scripts/shipgate/scan.mjs` runs on every PR via CI. It:

1. Scans locale files (en.json, ar.json, he.json) for banned terms
2. Scans Vue templates and TypeScript source for banned patterns
3. Scans C# backend code for banned patterns
4. Checks against the allowlist at `scripts/shipgate/allowlist.json`

### Allowlist

If a legitimate use is flagged (e.g. a physics question about "streak currents"), add an entry to `scripts/shipgate/allowlist.json`:

```json
{
  "file": "path/relative/to/repo/root",
  "line": 42,
  "term": "streak",
  "justification": "Physics term: electrical streak discharge"
}
```

Allowlist entries are reviewed in PR. The justification field is mandatory.

## PR template

Every PR includes a dark-pattern checklist (`.github/PULL_REQUEST_TEMPLATE.md`). All items must be checked before merge.
