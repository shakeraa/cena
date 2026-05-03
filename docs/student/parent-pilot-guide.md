# Cena — A Parent's Guide to the Student Pilot

*A plain-language walkthrough of what your child will use and what it does for them.*

---

## 1. What is Cena?

Cena is a personalised learning companion for secondary-school students preparing for matriculation exams (Bagrut, SAT, A-levels, and equivalents). It replaces passive textbook review with a guided daily loop: a short plan, a focused practice session, instant feedback, and a tutor that's always available to explain the hard parts.

It runs on three surfaces that share the same account:
- **Web** — for homework and longer study blocks on a laptop or tablet
- **Mobile app** (iOS and Android) — for short sessions on the go
- **Voice and notifications** — gentle reminders, never spammy

Everything a student does on one device shows up on the others. Parents get visibility without having to hover.

---

## 2. The Daily Loop

A typical day on Cena looks like this:

1. **Open the app** → the home screen shows today's plan, streak, XP, and one recommended session.
2. **Start the session** → 10–25 minutes of adaptive questions picked for what your child needs *right now*.
3. **Get stuck?** → tap the AI tutor. It explains the concept, not just the answer.
4. **Finish the session** → earn XP, check mastery on a per-concept bar, optionally share a win on the class feed.
5. **Review reminder** → later in the day, a gentle notification if there's something due for spaced repetition.

The loop is designed to fit into 15-minute pockets. No child is asked to sit down for an hour.

---

## 3. Feature-by-Feature

### 3.1 Home Dashboard
**What it is:** A single screen summarising the day.
**What's on it:** Today's plan, completed minutes vs. daily goal, current streak, recent achievements, and one "Start here" button.
**Why it matters:** No decision paralysis. A student opens the app and knows exactly what to do next.

### 3.2 Onboarding Wizard
**What it is:** A 3-step setup when a child first signs in.
**Steps:** Pick your role (student / parent / teacher) → pick your language (English, Hebrew, Arabic) → set your daily study goal in minutes.
**Why it matters:** The rest of the app adapts to those choices from the first session.

### 3.3 Learning Sessions
**What it is:** The core practice loop. A start button, a question, an answer box, a result, and then the next question.
**How it adapts:** Questions are picked based on your child's mastery of each concept. Easy concepts get spaced out; weak concepts get revisited. Difficulty is matched to current ability so the student is neither bored nor crushed.
**What it captures:** Answer, time taken, number of attempts, whether a hint was used, and even whether focus was slipping — so the system can adjust next time.
**Offline support:** Questions pre-download so a session survives a wobbly connection.

### 3.4 AI Tutor
**What it is:** A chat window with an AI that is specifically trained to teach, not just answer.
**How it works:** The student asks a question about a concept. The tutor responds with an explanation tailored to the student's current mastery level, streamed word-by-word so it feels like a real tutor writing on a whiteboard.
**Safety:** Rate-limited (10 messages per minute) so it can't be spammed. Every conversation is logged for the student (and for the school, if enrolled) to review. The tutor will not do the student's homework for them — it explains, then asks the student to try.

### 3.5 Progress & Mastery
**What it is:** Three views of how your child is doing over time.
- **Time view** — how many minutes per day, broken down by subject.
- **Mastery view** — how strongly each concept is held (with a decay curve, so "mastered two weeks ago and never reviewed" drops over time, just like real memory).
- **Sessions view** — every session listed with its accuracy, duration, and what was covered.
**Why it matters:** This is the page you, the parent, will spend the most time on. It's honest. A red bar means a weak concept. A green bar means it's holding. The numbers aren't inflated.

### 3.6 Knowledge Graph
**What it is:** A visual map of the curriculum. Concepts are dots; prerequisites are arrows.
**What a student sees:** Their own map, colour-coded by mastery. They can click any concept to see a description, try practice questions, or ask the tutor.
**Why it matters:** It turns a scary syllabus into a navigable terrain. Your child sees what they've already conquered and what's next.

### 3.7 Gamification (XP, Streaks, Badges, Leaderboards)
**What it is:** Lightweight game mechanics that reward consistent effort.
- **XP** is earned per correct answer, with bonuses for streaks and boss battles.
- **Streaks** are tracked per day; missing a day resets. A grace day is granted per week to avoid burnout.
- **Badges** are awarded for meaningful milestones (first mastery, 7-day streak, first boss defeated).
- **Leaderboards** can be scoped to the whole platform, the student's class, or just their friends — set by the school and by the student's privacy settings.
**Why we use it:** Teenagers are motivated by short-term wins. These mechanics keep the daily loop alive without turning the app into a casino.
**Your control:** A parent can disable competitive features entirely in settings. The leaderboard is opt-in per scope.

### 3.8 Challenges
**What it is:** Optional, higher-stakes practice formats.
- **Daily Challenge** — one new problem every day; students compete on a daily leaderboard.
- **Boss Battles** — 5–10 question themed boss fights, unlocked by reaching mastery thresholds. Beating a boss grants XP, a badge, and gems.
- **Card Chains** — concept chains that unlock progressively (e.g. "Algebra Fundamentals" → "Quadratics" → "Functions").
- **Tournaments** — weekly multi-student brackets (opt-in, inside the student's school).
**Why it matters:** Variety. A month of pure practice is monotonous; challenges break the routine.

### 3.9 Social Learning
**What it is:** A safe, school-scoped social layer.
- **Class feed** — celebrations from classmates (new badges, boss victories, mastery milestones). Read-only from the student; no comments, no likes on the MVP.
- **Peers** — discover classmates' public profiles and send friend requests.
- **Friends list** — see which friends are practising, their streaks, and who's ahead on the leaderboard.
- **Study rooms** — opt-in group study sessions with classmates.
**Safety model:** All social features are scoped to the student's school unless explicitly enabled otherwise. Direct messaging is NOT in the MVP — there is no private chat with peers. Every interaction is observable by the school's teachers if the school enrols.

### 3.10 AI Tutor (repeated here because it matters)
Separate section above — but worth repeating: this is the feature we expect pilot parents to notice first, because it replaces the need to pay for private tutoring on simple concepts.

### 3.11 Notifications
**What it is:** Gentle nudges to the student — never the parent — to come back and practise.
**Channels:**
- **In-app** — a bell icon inside the app
- **Web push** — browser notifications on the student's laptop
- **Email** — a daily digest if the student opts in
- **SMS** — only for urgent things like exam-day reminders
**Timing:** Notifications respect quiet hours (you choose the window) and the student's "best focus time" learned from their own patterns. We don't send reminders at 2am.
**Your control:** Every channel can be toggled in settings. The student can also mute all notifications for a period.

### 3.12 Profile & Settings
**What it is:** A one-stop page for the student to control their own experience.
- Display name, avatar, visibility (class-only / friends-only / public)
- Language (English / Hebrew / Arabic — Hebrew can be hidden outside Israel)
- Daily time goal
- Notification preferences per channel
- Privacy controls (who can see your mastery, your leaderboard rank, your social feed)
- Appearance (light / dark / high-contrast)
**Why it matters:** Teenagers need to feel in control of their own space. These settings are deliberately exposed to the student, not hidden behind a parent dashboard.

---

## 4. For Parents — What You Will Actually See

During the pilot, parents have a lightweight view:

1. **A weekly email summary** — how much your child practised, which subjects, and whether mastery moved.
2. **A read-only view of the progress pages** — the same three views (time / mastery / sessions) the student sees.
3. **A consent page** — what data is collected, how long it's kept, and how to request deletion.

What parents will *not* see during the pilot:
- The student's tutor chat transcripts (teachers can request them for the student; parents cannot access them directly unless the school's policy allows it)
- The student's social feed or friend interactions
- The student's private notes

This is deliberate. Teenagers are more likely to engage honestly with the tutor if they know a parent isn't reading over their shoulder. The school and parents still get the *outcomes* (mastery, time, accuracy) — just not the content of every conversation.

---

## 5. How Cena Is Different From…

**…a textbook.** A textbook is the same for every child. Cena is different for every child. The order of questions, the difficulty, the concept mix — all change daily based on what the child actually needs.

**…Khan Academy / Duolingo.** Those platforms have high-quality content but treat every learner the same. Cena adds: your child's school's curriculum, your child's pace, your child's tutor history, and a safety-scoped social layer inside their actual class.

**…a private tutor.** A private tutor costs £30–£60 an hour, sees your child weekly, and knows them well. Cena costs a fraction, works daily, and has the advantage of remembering every single question your child ever answered. It does NOT replace a great human tutor for exam prep — but it covers the 80% of practice that doesn't need a human in the room.

**…a game.** Cena has game mechanics. It is not a game. If a student only logs in to grind XP without learning, mastery bars stay flat and the streak becomes hollow. The system is honest about that.

---

## 6. Privacy, Safety, Data

- **Authentication** is via Firebase Auth (the same system used by Google and millions of apps). Parents can set up email/password or Google sign-in for their child.
- **Data at rest** lives in a private database the school controls, not shared with other schools.
- **Data in transit** is encrypted end-to-end (TLS).
- **AI tutor conversations** are sent to Anthropic's Claude model with the student's messages; no personally identifying information (full name, address, etc.) is included in the prompt.
- **No ads, no data selling.** Cena is sold to schools, not to advertisers. There is no business model that involves selling student data.
- **Deletion on request.** A parent can request full account deletion at any time; the school's admin processes it.
- **Rate limits and safety guards** on the tutor prevent inappropriate content, jailbreaks, and abuse.

---

## 7. What's Live Today vs. Coming Soon

### Live in the pilot
- All 10 web pages listed above
- Mobile app with 14 feature modules (home, auth, onboarding, knowledge graph, progress, gamification, challenges, diagrams, social, profile, tutor, notifications, and more)
- Real AI tutor with streaming answers
- Real mastery tracking with decay curves (spaced repetition)
- Real daily plan and recommendations
- Real leaderboards, badges, XP, streaks
- Real social feed scoped to the class
- Real notifications (in-app and web push)

### Coming after the pilot
- Email and SMS notification channels (today they log locally; turning on the external senders requires each school's approval)
- Parent dashboard (a dedicated view just for you — the pilot uses the student's progress pages)
- Exam simulation mode (full-length mock Bagrut / SAT papers with time limits)
- Real-time voice tutor (speak instead of type)
- Offline-first mobile sync (practise on the Underground, sync when you get home)

---

## 8. A Word to Pilot Parents

The point of a pilot is to learn what works and what doesn't. Your child is not using a finished product. They are helping us make it better. Some things will break, some features will feel thin, and we'll need your feedback on what to fix first.

What we ask from you:
1. **Let your child drive the daily loop.** Don't do it with them; let them get stuck and use the tutor. That's the whole point.
2. **Check the progress page once a week.** Not daily. You're looking for trends, not day-to-day noise.
3. **Send us feedback.** A two-line email about what worked or didn't, once a week, is more useful than a long form once at the end.
4. **Flag anything that feels wrong** — wrong content, wrong difficulty, wrong tone from the tutor, privacy concerns, anything. These matter more to us than bug reports.

Thank you for piloting Cena. The students in this cohort are shaping what the next version looks like.

— The Cena Team

---

*Last updated: 2026-04-11. Product in active development. Some features listed as "live" may have rough edges; please report them.*
