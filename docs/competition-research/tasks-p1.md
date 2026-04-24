# P1 Tasks — Next Quarter

> **Source:** extracted-features.md
> **Sprint Goal:** Expand content delivery, social features, retention, and family monetization
> **Total Estimate:** 30-44 weeks (parallel workstreams)

---

## LRN-002: Video Explanations
**ROI: 8.0 | Size: L (8-10 weeks) | Feature: A6**

### Description
Add video explanations for complex STEM concepts. 60% of learners prefer video for hard topics. Hybrid approach: license existing content + AI-generate short explainers.

### Acceptance Criteria
- [ ] Video player with speed control (0.5x, 1x, 1.25x, 1.5x, 2x)
- [ ] Subtitles/captions (English, Arabic, Hebrew)
- [ ] Video linked to knowledge graph nodes (auto-suggest relevant videos)
- [ ] "Watch explanation" button on difficult concepts
- [ ] Offline download for premium users
- [ ] AI-generated short explainers (60-90s, text-to-video via Synthesia or similar)
- [ ] Video completions tracked in analytics and SRS
- [ ] Premium gate: free users get 2 videos/day, premium unlimited

### Subtasks
1. Video player component (speed, captions, progress tracking)
2. Video content CMS (upload, tag to knowledge graph node, metadata)
3. AI video generation pipeline (concept text -> short explainer)
4. Video suggestion algorithm (triggered by struggle/low mastery)
5. Offline download manager for video content
6. Free tier video limit (2/day) + premium bypass
7. Analytics: video_started, video_completed, video_to_practice_conversion

---

## LRN-003: AI Content Generation (Magic Notes)
**ROI: 7.8 | Size: M (6-8 weeks) | Feature: A8**

### Description
Upload lecture notes, PDFs, or photos and auto-generate flashcards, practice quizzes, and summaries. Quizlet's top premium driver.

### Acceptance Criteria
- [ ] File upload: PDF, image (camera/gallery), text paste
- [ ] OCR for handwritten/printed notes
- [ ] AI generates: flashcards (Q&A pairs), practice quiz (MCQ + free response), summary
- [ ] User can edit/approve/reject generated items before saving
- [ ] Generated content integrates with FSRS (added to review queue)
- [ ] Generated content mapped to knowledge graph (auto-categorize)
- [ ] Premium feature: free users get 3 generations/week, premium unlimited
- [ ] Share generated study sets with classmates

### Subtasks
1. File upload handling (PDF parsing, image OCR, text extraction)
2. AI generation prompts (flashcard, quiz, summary templates)
3. Generated content review/edit UI
4. FSRS integration for generated flashcards
5. Knowledge graph auto-categorization
6. Share generated sets (link or class feed)
7. Free tier limit (3/week) + premium bypass

---

## SOC-001: Parent Dashboard
**ROI: 7.5 | Size: M (4-6 weeks) | Feature: E5**

### Description
Separate parent view to track child's learning progress, set limits, and manage subscription. Enables family plan monetization.

### Acceptance Criteria
- [ ] Parent account linked to child account(s) — up to 5 children
- [ ] Weekly AI-generated progress report (email + in-app)
- [ ] Dashboard shows per child: streak status, topics mastered, weak areas, time spent
- [ ] Goal progress visualization (target vs actual)
- [ ] Set daily time limits per child
- [ ] Set bedtime/quiet hours per child
- [ ] Notification when child completes daily goal
- [ ] Subscription management (add/remove children)
- [ ] Export progress report as PDF
- [ ] Available on web (not just mobile) for parent convenience

### Subtasks
1. Parent-child account linking flow
2. Parent dashboard UI (per-child views)
3. Weekly AI progress report generator (email + in-app)
4. Parental controls: time limits, quiet hours per child
5. Progress PDF export
6. Web-based parent dashboard
7. Analytics: parent_linked, parent_report_viewed, parent_limit_set

---

## SOC-002: Teacher Dashboard & Classroom Tools
**ROI: 7.5 | Size: L (6-8 weeks) | Feature: E4**

### Description
Teacher creates classes, assigns content, tracks student progress. Free for K-12 educators (Brilliant model — massive acquisition channel).

### Acceptance Criteria
- [ ] Teacher account type (separate from student)
- [ ] Create class with join code
- [ ] Assign specific topics/sessions to class
- [ ] Class progress dashboard (aggregated + per-student)
- [ ] Identify struggling students (below mastery threshold)
- [ ] Assignment due dates with reminders
- [ ] Export class progress report (CSV, PDF)
- [ ] FREE for verified K-12 teachers (email domain verification)
- [ ] Students in teacher's class get Premium features while enrolled
- [ ] Google Classroom integration (import roster) — stretch goal

### Subtasks
1. Teacher account registration and verification flow
2. Class creation + join code system
3. Assignment creation (select topics, set due date)
4. Class dashboard UI (aggregated + per-student mastery view)
5. Struggling student alerts
6. Progress export (CSV, PDF)
7. Free teacher verification (school email domain list)
8. Student Premium access while in teacher's class
9. Google Classroom roster import (stretch)

---

## SOC-003: Friend System & Social Graph
**ROI: 6.5 | Size: M (4-6 weeks) | Feature: E3**

### Description
Follow friends, see their activity, nudge them to study, compete on leaderboards. Foundation for friend streaks (GAM-004) and social features.

### Acceptance Criteria
- [ ] Search users by username or invite via link/QR code
- [ ] Follow/unfollow (mutual follow = "friends")
- [ ] Friends list with streak status, league position, last active
- [ ] Nudge button: send push notification ("X wants you to study!")
- [ ] Friend activity feed (sessions completed, badges earned, streaks)
- [ ] Friends leaderboard (weekly XP)
- [ ] Privacy controls: profile visibility (public/friends-only/private)
- [ ] Block/report user
- [ ] Max 50 friends (prevent spam)

### Subtasks
1. User search + invite system (username, link, QR)
2. Follow/friend relationship model
3. Friends list UI (status, streak, league)
4. Nudge push notification
5. Friend activity feed
6. Friends leaderboard
7. Privacy settings (visibility, block, report)

---

## RET-001: Home Screen Widgets (iOS/Android)
**ROI: 7.0 | Size: S (2-4 weeks) | Feature: I4**

### Description
Home screen widgets showing streak status, daily goal progress, and next review due. Daily visibility drives habit formation.

### Acceptance Criteria
- [ ] iOS widget (WidgetKit): small (streak counter), medium (streak + daily progress)
- [ ] Android widget (Glance/RemoteViews): same sizes
- [ ] Shows: current streak count, daily sessions done/remaining, next review time
- [ ] Tap to open app (deep link to next session)
- [ ] Updates every 30 minutes (background refresh)
- [ ] Dark mode support
- [ ] Lock screen widget (iOS 16+)

### Subtasks
1. iOS WidgetKit implementation (small + medium)
2. Android widget implementation (small + medium)
3. Widget data provider (streak, sessions, next review)
4. Deep link from widget tap to session
5. Background refresh scheduling
6. Dark mode variants

---

## RET-002: Win-Back Campaigns
**ROI: 6.8 | Size: M (4-6 weeks) | Feature: I3**

### Description
Escalating re-engagement for lapsed users. Duolingo's approach: gentle -> urgent -> humor -> viral. 45% of win-back recipients re-engage.

### Acceptance Criteria

**Push Notification Escalation:**
- [ ] Day 1-3: "Your streak is at 0. Start a new one today!"
- [ ] Day 4-7: "You've missed 5 days. Your knowledge is fading..."
- [ ] Day 8-14: "We miss you! Your [topic] mastery dropped from 85% to 72%"
- [ ] Day 15-30: "Come back and get 100 bonus gems + streak restart"

**Email Campaign:**
- [ ] Day 7: "Your progress report" (show mastery decline)
- [ ] Day 14: "Special offer: 50% off first month Premium"
- [ ] Day 30: "We saved your progress. Pick up where you left off"

**In-App Return:**
- [ ] "Welcome back" screen with progress summary
- [ ] "Quick review" session (5-min, low-difficulty confidence builder)
- [ ] Streak restart celebration (fresh start messaging)
- [ ] Bonus gems for returning (100 gems first day back)
- [ ] Personalized "what you missed" summary

### Subtasks
1. Lapsed user detection service (days since last session)
2. Push notification escalation schedule (4 tiers)
3. Email campaign templates (3 tiers)
4. "Welcome back" screen with progress delta
5. Quick review session type (5-min confidence builder)
6. Bonus gem grant on return
7. A/B test notification copy
8. Analytics: win_back_sent, win_back_opened, win_back_converted, days_to_return

---

## UX-001: AI Tutor Personality Enhancement
**ROI: 7.0 | Size: S (2-3 weeks) | Feature: F6**

### Description
Make CENA's AI tutor warm, encouraging, and named. Synthesis proved emotional connection drives engagement. Celebrate effort over correctness.

### Acceptance Criteria
- [ ] AI tutor uses student's name in responses
- [ ] Growth mindset messaging: "Great attempt! Let's look at this differently"
- [ ] Celebrates effort: "You worked hard on that. Here's what I noticed..."
- [ ] Mistake reframing: "Mistakes help us learn. Let's try again"
- [ ] Adjustable tone: encouraging (default), neutral, challenging
- [ ] Personality consistent across sessions (remembers previous interactions)
- [ ] Tutor avatar/icon on chat messages (consistent visual identity)

### Subtasks
1. System prompt engineering (personality, tone, growth mindset templates)
2. Name personalization in AI responses
3. Tone selector in settings (encouraging/neutral/challenging)
4. Consistent personality across conversation sessions
5. Tutor avatar design and implementation
6. A/B test warm vs neutral tone on engagement metrics

---

## UX-002: Dyslexia & Neurodiverse Accessibility
**ROI: 5.5-6.0 | Size: S (2-3 weeks) | Features: H6, J9, N5**

### Description
OpenDyslexic font toggle, increased spacing, voice speed adjustment. Synthesis targets neurodiverse learners explicitly. Quick win for market expansion.

### Acceptance Criteria
- [ ] OpenDyslexic font toggle in settings
- [ ] Increased letter/line spacing option
- [ ] Voice speed adjustment (0.5x - 2.0x) for AI tutor speech
- [ ] Reduced motion option (disable celebration animations)
- [ ] High contrast mode
- [ ] Settings grouped under "Accessibility" section
- [ ] Persist across sessions

### Subtasks
1. OpenDyslexic font bundle + toggle
2. Spacing adjustment (letter-spacing, line-height)
3. Voice speed control for TTS
4. Reduced motion toggle
5. High contrast theme
6. Accessibility settings page

---

## UX-003: Review Heatmap (GitHub-style)
**ROI: 6.0 | Size: S (2-3 weeks) | Feature: B5**

### Description
GitHub-style contribution heatmap showing study activity per day. Anki's #1 most popular add-on. Powerful visual motivator.

### Acceptance Criteria
- [ ] Calendar grid showing study activity intensity (color gradient)
- [ ] Green shades: none (gray), light (1 session), medium (2-3), dark (4+)
- [ ] Visible on profile screen
- [ ] Tap day to see: sessions completed, topics studied, XP earned
- [ ] Shows current year by default, scroll to previous years
- [ ] Streak counter integrated with heatmap
- [ ] Share heatmap as image (social sharing)

### Subtasks
1. Study activity data aggregation (sessions per day)
2. Heatmap calendar widget (color-coded grid)
3. Day detail popup (sessions, topics, XP)
4. Profile page integration
5. Share as image functionality
