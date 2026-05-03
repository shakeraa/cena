## Summary

<!-- What does this PR do? Keep it brief. -->

## Dark-pattern ship-gate checklist

> **Legal basis**: FTC v. Epic, Edmodo, COPPA 2025, ICO v. Reddit £14.47M, Israel PPL Am. 13.  
> All items must be checked before merge. See `docs/engineering/shipgate.md` for details.

- [ ] No new streak counter or streak-like mechanic
- [ ] No new variable-ratio reward (loot box, random drop, mystery reward)
- [ ] No new loss-aversion copy ("don't break", "you'll lose", "running out of time")
- [ ] No new confetti / haptics / audio tied to non-learning events
- [ ] No notification nagging — all notifications require explicit user opt-in
- [ ] No leaderboard scoped beyond co-op study pods
- [ ] No default-on social features (matchmaking, friend suggestions, public profiles)

## Test plan

<!-- How was this tested? -->

- [ ] Unit tests pass
- [ ] Build succeeds
- [ ] Ship-gate scanner passes (`node scripts/shipgate/scan.mjs`)
