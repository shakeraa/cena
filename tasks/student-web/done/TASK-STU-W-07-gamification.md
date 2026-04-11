# TASK-STU-W-07: Gamification

**Priority**: HIGH — retention driver
**Effort**: 3-4 days
**Phase**: 2
**Depends on**: [STU-W-05](TASK-STU-W-05-home-dashboard.md), [STU-W-06](TASK-STU-W-06-learning-session-core.md)
**Backend tasks**: [STB-03](../student-backend/TASK-STB-03-gamification.md)
**Status**: Not Started

---

## Goal

Implement the XP, streak, badge, quest, leaderboard, and celebration systems so students feel the loop every session: earn → feel it → come back. Respects the age-aware variable-rewards rules (soft for under-18, opt-in for 18+).

## Spec

Full specification in [docs/student/06-gamification.md](../../docs/student/06-gamification.md). All 20 `STU-GAM-*` acceptance criteria form this task's checklist.

## Scope

In scope:

- Shared `<CelebrationOverlay>` singleton + `useCelebration()` composable, reachable from anywhere in the app
- `<XpPopup>` emitted from session on every correct answer, animates the delta, aria-live announcement
- Level curve math matching mobile (`n * 100 + (n-1)^2 * 25`); level-up triggers celebration overlay
- `<StreakWidget>` extension: days-until-risk, next milestone, compassionate "streak broken" copy + streak-freeze offer
- `<BadgeShowcase>` on profile + home (top 5); clicking opens `<BadgeDetailDialog>` with rarity, unlock criteria, shareable link
- `/progress/badges` full gallery with category filter chips and silhouette-locked states
- `<Badge3dWidget>` using Rive (or SVG fallback) for the 3D badge preview — keyboard-accessible
- `<QuestPanel>` on home + progress; subscribes to `QuestUpdated` / `QuestCompleted` hub events
- Quest completion triggers XP award + celebration overlay
- Momentum meter (`<MomentumCard>`) on home — weekly XP trend with flow-ambient tint
- `useAgeAwareRewards()` composable — caps extrinsic intensity for under-18, exposes opt-in to 18+
- Shareable badge card generator — server-side OG image endpoint; client renders preview and share URL
- Desktop notification opt-in in settings; permission requested on toggle
- Seasonal themes: if backend sets `event.theme`, overlay a time-limited skin
- Pinia `gamificationStore` — current XP, level, streak, badges, active quests, momentum; subscribes to hub events

Out of scope:

- Leaderboard page itself — implemented in STU-W-12 (social)
- Quest generation — backend (STB-03)
- Badge awarding logic — backend
- Tournament events — STU-W-11 + STB-05

## Definition of Done

- [ ] All 20 `STU-GAM-*` acceptance criteria in [06-gamification.md](../../docs/student/06-gamification.md) pass
- [ ] Celebration overlay works from any page via `useCelebration()` and is a global singleton
- [ ] All celebrations respect `prefers-reduced-motion` and fall back to static cards
- [ ] XP popup animation ends within 800 ms and chains correctly when multiple events arrive in quick succession
- [ ] Streak broken message never uses punishing language (review wording with product)
- [ ] 3D badge renderer is keyboard-focusable with arrow-key rotation
- [ ] Badge silhouettes show a cryptic hint, not the badge name, until unlocked
- [ ] Variable-rewards rules verified with an under-18 test account and an 18+ test account
- [ ] Shareable badge OG image does not contain PII beyond display name
- [ ] Playwright covers: XP gain with celebration, level-up, badge unlock, quest progress → complete, streak broken recovery
- [ ] Cross-cutting concerns from the bundle README apply

## Risks

- **Celebration overload** — multiple events can stack. Implement a celebration queue and cap at 1 visible + 2 queued; drop older queued celebrations on new critical ones (level-up wins over quest-progress).
- **Rive licensing** — confirm Rive runtime license is compatible before shipping the 3D badge renderer. Fallback to SVG if not.
- **Under-13 leaderboards** — must never show real names. Confirm display names are enforced on the server side; do not rely on client filtering.
- **Notification permission timing** — browsers penalize sites that request permission on page load. Only request on explicit opt-in click.
- **Optimistic XP rollback** — covered in STU-W-03; verify rollback animation feels natural, not like data loss.
