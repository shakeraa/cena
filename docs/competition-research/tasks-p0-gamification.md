# P0 Gamification Tasks

> **Source:** extracted-features.md (C2, C3, C5, C6, C7, C8, C16)
> **Sprint Goal:** Close the Duolingo gamification gap. These 5 systems work together as an engagement economy.
> **Total Estimate:** 16-24 weeks across all P0 gamification
> **Expected Impact:** +60% engagement, +20% retention, +20% premium conversion

---

## GAM-001: 10+ Tier League System
**ROI: 9.0 | Size: M (4-6 weeks) | Depends on: GAM-005 (gems for rewards)**

### Description
Weekly competitive leagues with promotion/demotion. 30 users per league, ranked by XP earned that week. Top performers advance, bottom performers drop.

### Acceptance Criteria
- [ ] 10 named tiers (e.g., Bronze, Silver, Gold, Platinum, Diamond, Master, Grandmaster, Legend, Champion, Elite)
- [ ] Weekly reset cycle (Sunday 8PM local time)
- [ ] 30 users per league (matchmaking by XP range)
- [ ] Top 10 advance to next tier
- [ ] Bottom 5 demote to previous tier
- [ ] Middle 15 remain in current tier
- [ ] League position visible on home screen
- [ ] Promotion/demotion animation on cycle end
- [ ] Gem rewards based on final position (1st: 50 gems, 2nd: 30, 3rd: 20, top 10: 10)
- [ ] Anti-cheating: cap XP earning rate, flag anomalies
- [ ] Empty league handling (fill with bots until user base grows)

### Technical Notes
- Backend: leaderboard service with weekly cron job for reset
- Matchmaking algorithm: group users within similar XP bands
- Cache league standings (update every 5 min, not real-time)
- Store league history for analytics

### Subtasks
1. Design league tier names, icons, colors, and badges
2. Build leaderboard backend service (rankings, matchmaking, reset)
3. Build league standings UI (home screen widget + full view)
4. Build promotion/demotion animation screens
5. Implement gem rewards distribution on cycle end
6. Add anti-cheating XP rate limiter
7. Bot backfill for underpopulated leagues
8. Integration tests for weekly cycle edge cases
9. Analytics events: league_joined, league_promoted, league_demoted, league_position

---

## GAM-002: Streak Freeze & Recovery
**ROI: 8.8 | Size: S (2-4 weeks) | Depends on: GAM-005 (gems to purchase)**

### Description
Multiple safety nets for streak maintenance. Users can purchase streak freezes before missing a day, and have a 3-day recovery window after breaking a streak.

### Acceptance Criteria
- [ ] **Streak Freeze**: purchasable for 200 gems (max 2 stockpiled)
- [ ] Freeze auto-activates on missed day (no user action needed that day)
- [ ] Freeze must be purchased BEFORE the missed day
- [ ] Visual indicator showing active freeze count on streak display
- [ ] **Streak Recovery**: 3-day window after broken streak
- [ ] Recovery requires completing a "recovery session" (1.5x normal session length)
- [ ] Recovery session has special UI (urgent theme, countdown timer)
- [ ] Push notification on streak break: "Your streak is in danger! 3 days to recover"
- [ ] Escalating notifications: Day 1 (gentle), Day 2 (urgent), Day 3 (final warning)
- [ ] Analytics: streak_freeze_purchased, streak_freeze_used, streak_recovered, streak_lost

### Technical Notes
- Streak freeze is a consumable inventory item
- Recovery window: 72 hours from midnight of missed day (user's timezone)
- Quality-gated recovery: session must meet CENA's quality threshold (not just login)

### Subtasks
1. Add streak freeze inventory model (purchase, stockpile, auto-use)
2. Build streak freeze purchase UI in gem shop
3. Implement auto-activation logic on missed day detection
4. Build streak recovery flow (special session type)
5. Recovery countdown UI and push notifications (3-tier escalation)
6. Update streak display to show freeze count and recovery state
7. Unit tests for timezone edge cases, midnight rollover, freeze + recovery interaction

---

## GAM-003: Hearts / Lives System
**ROI: 8.5 | Size: M (4-6 weeks) | Depends on: GAM-005 (gems for refill)**

### Description
Loss aversion mechanic. Users start each session with 5 hearts. Wrong answers cost 1 heart. At 0 hearts, the session ends. Hearts regenerate over time or can be refilled with gems. Premium users get unlimited hearts.

### Acceptance Criteria
- [ ] Free users start with 5 hearts per session
- [ ] Lose 1 heart per incorrect answer
- [ ] At 0 hearts, session ends with "Out of hearts" screen
- [ ] Heart regeneration: 1 heart every 4 hours (automatic)
- [ ] Heart refill: 350 gems for full refill (available mid-session)
- [ ] "Practice to earn hearts": complete a review session to earn 1 heart back
- [ ] Premium users: unlimited hearts (key conversion driver)
- [ ] Heart counter visible during sessions (top bar, animated on loss)
- [ ] Heart loss animation: heart cracks, shake, red flash
- [ ] Heart gain animation: heart fills, pulse, green glow
- [ ] "Out of hearts" screen shows: wait timer, gem refill option, practice option, premium upsell
- [ ] Configurable: hearts can be disabled in "deep study mode" (no interruption)

### Technical Notes
- Hearts are session-scoped but regeneration is account-scoped
- Timer-based regen: store last_heart_regen timestamp, calculate on app open
- Premium check: bypass heart deduction entirely
- Deep study mode: hearts system paused (wellbeing feature takes priority)

### Subtasks
1. Heart state model (current hearts, max hearts, last regen time, premium override)
2. Heart deduction logic in question answer flow
3. Heart regeneration timer service
4. Heart counter UI component (session top bar)
5. Heart loss/gain animations
6. "Out of hearts" screen (4 options: wait, gems, practice, premium)
7. "Practice to earn hearts" session type
8. Heart refill gem purchase flow
9. Premium unlimited hearts bypass
10. Deep study mode hearts pause
11. Unit + widget tests for heart edge cases (regen timing, session boundary)

---

## GAM-004: Friend Streaks
**ROI: 7.3 | Size: S (2-4 weeks) | Depends on: E3 (friend system)**

### Description
Maintain a shared streak with up to 5 friends. Both users must complete their daily goal for the friend streak to increment. Social accountability mechanism.

### Acceptance Criteria
- [ ] Pair with up to 5 friends for concurrent friend streaks
- [ ] Both users must complete daily quality-gated session
- [ ] Friend streak counter separate from personal streak
- [ ] Friend streak display on profile and friend list
- [ ] Notification when friend completes their session ("X just studied! Your turn")
- [ ] Notification when friend streak is at risk ("Your streak with X ends in 4 hours!")
- [ ] Celebration animation when both complete (dual celebration)
- [ ] Friend streak milestones: 7, 30, 100 days (special badge)
- [ ] Broken friend streak notification with empathy ("You and X had a 23-day streak")
- [ ] Friend streak leaderboard (longest active friend streaks)

### Subtasks
1. Friend streak data model (pair, counter, last_completed_by)
2. Daily completion check for both parties
3. Friend streak UI on profile and friend list
4. Push notification triggers (partner completed, at risk, broken)
5. Dual celebration animation
6. Milestone badges (7, 30, 100 day)
7. Friend streak leaderboard

---

## GAM-005: Virtual Currency (Gems)
**ROI: 8.3 | Size: M (4-6 weeks) | No dependencies (foundation for GAM-001, 002, 003)**

### Description
In-app virtual currency with multiple earning and spending paths. Creates a self-sustaining engagement economy that drives daily usage and premium conversion.

### Acceptance Criteria

**Earning Gems:**
- [ ] Complete lesson: 10-15 gems (scaled by difficulty)
- [ ] Complete boss battle: 25-50 gems
- [ ] Complete quest: varies (30-100 gems)
- [ ] Daily streak maintained: 5 gems (increases with streak: 10 at 7d, 20 at 30d, 50 at 100d)
- [ ] League position rewards (weekly): 1st: 50, 2nd: 30, 3rd: 20, top 10: 10
- [ ] Perfect session (no wrong answers): 20 bonus gems
- [ ] Friend invite accepted: 100 gems
- [ ] Daily login bonus: 5 gems (separate from streak)
- [ ] Watch ad (free tier): 15 gems per ad (max 3/day)

**Spending Gems:**
- [ ] Streak freeze: 200 gems
- [ ] Heart refill: 350 gems
- [ ] Double XP boost (1 hour): 150 gems
- [ ] Timer boost (extra time on timed challenges): 100 gems
- [ ] Avatar cosmetics: 50-500 gems (see GAM-006)
- [ ] League repair (prevent demotion): 1000 gems

**In-App Purchase (optional, P1):**
- [ ] Gem packs: 100 gems ($0.99), 500 ($3.99), 1200 ($7.99), 3000 ($17.99)

**Infrastructure:**
- [ ] Gem balance visible on home screen (always)
- [ ] Gem shop / store page
- [ ] Transaction history (earned/spent log)
- [ ] Gem earning animation (floating gems → balance)
- [ ] Gem spending confirmation dialog
- [ ] Anti-inflation: balance analytics, earning rate monitoring
- [ ] Gem balance synced across devices

### Technical Notes
- Gems are a server-side ledger (not client-side) to prevent manipulation
- Every gem transaction is an immutable ledger entry (amount, source, timestamp)
- Rate limiting on earning (prevent farming exploits)
- Gem balance shown as integer (no decimals)

### Subtasks
1. Gem ledger service (earn, spend, balance, history, rate limiting)
2. Earning rules engine (lesson complete, streak, league, etc.)
3. Spending rules engine (freeze, hearts, boosts, cosmetics)
4. Gem balance UI component (home screen, session screen)
5. Gem shop / store page
6. Transaction history page
7. Gem earning animation (floating gems)
8. Gem spending confirmation dialog
9. Anti-exploit rate limiter
10. Server-side sync and conflict resolution
11. Analytics: gems_earned, gems_spent, gem_balance_distribution, earning_source_breakdown
12. Unit tests for ledger integrity, concurrent transactions, rate limiting
