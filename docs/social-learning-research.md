# Social Proof, Social Learning & Community for Educational Mobile Apps

> **Date:** 2026-03-31
> **Status:** Research complete
> **Scope:** Social psychology mechanisms, mobile UI patterns, actor model mapping, safety/compliance, competitive analysis
> **Applies to:** Cena Adaptive Learning Platform (Flutter mobile, .NET 9 actor backend, Israeli K-12 Bagrut prep)

---

## Table of Contents

1. [Social Learning Theory (Bandura) Applied to Mobile EdTech](#1-social-learning-theory-bandura-applied-to-mobile-edtech)
2. [Social Proof Mechanisms](#2-social-proof-mechanisms)
3. [Collaborative Learning on Mobile](#3-collaborative-learning-on-mobile)
4. [Social Comparison (Festinger) Applied to Learning](#4-social-comparison-festinger-applied-to-learning)
5. [Teacher-Student Social Design](#5-teacher-student-social-design)
6. [Community Features](#6-community-features)
7. [Social Motivation Mechanics](#7-social-motivation-mechanics)
8. [Social Safety in K-12](#8-social-safety-in-k-12)
9. [Social Onboarding](#9-social-onboarding)
10. [Multiplayer Learning](#10-multiplayer-learning)
11. [Actor Model Mapping](#11-actor-model-mapping)
12. [Competitive Analysis: Social Features](#12-competitive-analysis-social-features)
13. [Privacy-First Social Design Patterns](#13-privacy-first-social-design-patterns)
14. [Teacher & Parent Dashboard Social Features](#14-teacher--parent-dashboard-social-features)
15. [Moderation System Design](#15-moderation-system-design)
16. [Implementation Roadmap](#16-implementation-roadmap)

---

## 1. Social Learning Theory (Bandura) Applied to Mobile EdTech

### 1.1 Theoretical Foundation

Albert Bandura's Social Learning Theory (1977) identifies four core processes through which people learn by observing others: attention, retention, reproduction, and motivation. His later work on Social Cognitive Theory (1986) added the concept of self-efficacy -- the belief in one's own ability to succeed -- as a central driver of learning behavior. For a K-12 mobile learning app, these map directly to implementable features.

### 1.2 Observational Learning -- Watching Peers Solve Problems

**Research basis:** Bandura demonstrated that observational learning is most effective when the model is perceived as similar to the observer (Schunk, 1987, "Peer Models and Children's Behavioral Change," Review of Educational Research). Students who watched a peer solve math problems improved 33% more than those who watched a teacher solve the same problems.

**Mobile implementation:**

Feature: **Peer Solution Replays**

```
+--------------------------------------------------+
|  [avatar] Dana solved this differently!    [View] |
|                                                    |
|  Step 1: She noticed the pattern in the           |
|          sequence first...                         |
|  Step 2: Then applied the formula for             |
|          arithmetic progressions                   |
|  Step 3: Verified with the boundary case          |
|                                                    |
|  [heart] 42 students found this helpful           |
|  ----------------------------------------         |
|  Try solving it yourself first  [Start]           |
+--------------------------------------------------+
```

- Show anonymized peer solution paths AFTER the student has attempted the problem (never before -- this preserves productive struggle)
- The peer should be algorithmically selected to be at a similar mastery level (lateral model, see Section 4)
- Solution steps are presented sequentially with a "next step" tap pattern, not all at once
- The student can rate whether the peer solution was helpful (feeding collaborative filtering)
- Language: Hebrew RTL layout; solution steps rendered with the same MathText widget used in session questions

**Event sourcing:** A new `PeerSolutionViewed_V1` event records which student viewed which peer solution and their helpfulness rating. This feeds the recommendation engine for surfacing relevant peer solutions.

### 1.3 Modeling -- Showing Exemplar Student Work

**Research basis:** Zimmerman & Kitsantas (2002, Journal of Educational Psychology) found that students who observed a coping model (a peer who made and corrected errors) outperformed students who observed a mastery model (a peer who solved perfectly). Coping models increase self-efficacy because students think "if they can recover from mistakes, so can I."

**Mobile implementation:**

Feature: **"How Others Approached This" Carousel**

```
+--------------------------------------------------+
|  How others approached this concept               |
|                                                    |
|  [< ]  [Card 1 of 3]  [ >]                       |
|  +----------------------------------------------+ |
|  | "I was confused at first about vectors       | |
|  |  vs scalars. What helped me was thinking     | |
|  |  of vectors as arrows and scalars as         | |
|  |  just numbers."                              | |
|  |                                              | |
|  | -- Student in your class                     | |
|  | Mastered this concept in 8 attempts          | |
|  +----------------------------------------------+ |
|                                                    |
|  [Share your approach]                            |
+--------------------------------------------------+
```

- Show coping model content: students who struggled but eventually mastered the concept
- Anonymized by default; students can opt in to show their first name
- Content is curated from student annotations (existing `AnnotationAdded_V1` events with kind "insight")
- Teacher-approved before public display (moderation gate)
- Filter by language (Hebrew/Arabic/English) using the student's locale setting
- Maximum 3 models shown per concept to avoid information overload

### 1.4 Vicarious Reinforcement -- Seeing Others Rewarded

**Research basis:** Bandura (1965) demonstrated that children who observed a model being rewarded for behavior were significantly more likely to imitate that behavior than those who observed no consequences. In educational contexts, seeing peers earn recognition for learning effort increases motivation (Schunk & Hanson, 1985, Journal of Educational Psychology).

**Mobile implementation:**

Feature: **Live Achievement Feed (Class-Level)**

```
+--------------------------------------------------+
|  Class Activity                         [Filter]  |
|                                                    |
|  [star] Yael just mastered Quadratic       2m ago |
|         Equations! Her 15th concept.              |
|                                                    |
|  [fire] Amit reached a 14-day streak!      1h ago |
|                                                    |
|  [trophy] Or earned the "Deep Thinker"     3h ago |
|           badge -- 20 concepts mastered!          |
|                                                    |
|  [graph] 3 students in your class          today  |
|          are working on Trigonometry now           |
|                                                    |
|  [celebrate] Your class mastered 47        today  |
|              concepts today!                      |
+--------------------------------------------------+
```

- Shows achievement events from the student's class (not school-wide, to maintain intimacy)
- Events are opt-in: students choose whether their achievements appear in the feed
- Only positive events (mastery, badges, streaks) -- never failures or low scores
- Class aggregate stats ("47 concepts mastered today") provide social proof without individual comparison
- Tapping an achievement shows a brief congratulation modal, not the peer's detailed stats
- Feed refreshes via SignalR real-time connection (existing Cena architecture) or polled every 60s on mobile

### 1.5 Self-Efficacy Through Social Comparison

**Research basis:** Bandura (1997, *Self-Efficacy: The Exercise of Control*) identified four sources of self-efficacy: mastery experiences, vicarious experiences, social persuasion, and physiological/emotional states. Vicarious experience ("I see someone like me succeed, so I believe I can too") is the second most powerful source after mastery experience itself.

**Mobile implementation:**

Feature: **"Students Like You" Progress Stories**

```
+--------------------------------------------------+
|  Students who started where you are               |
|                                                    |
|  [avatar circle]                                  |
|  "I started Physics with 30% readiness.           |
|   After 6 weeks of daily practice, I reached      |
|   82% and scored 92 on my Bagrut."               |
|                                                    |
|  Started: Similar mastery level to yours           |
|  Time to goal: 6 weeks                            |
|  Daily practice: ~20 minutes                      |
|                                                    |
|  [See your projected timeline]                    |
+--------------------------------------------------+
```

- Match the current student with historical success stories of students who had a similar initial mastery profile
- This is computed from anonymized, aggregated cohort data -- not individual student records
- Projected timelines are grounded in actual data from the student's learning velocity (existing `xpForLevel` and mastery rate calculations)
- Shown during onboarding (day 1-3), during stagnation periods, and on the progress screen
- Only show stories from students who successfully improved -- selection bias is intentional here for motivational effect

---

## 2. Social Proof Mechanisms

### 2.1 Research Foundation

Social proof (Cialdini, 2006, *Influence: The Psychology of Persuasion*) operates through six principles: consensus, similarity, authority, scarcity, commitment, and liking. In educational contexts, the most effective are consensus ("many students do this"), similarity ("students like you do this"), and authority ("your teacher recommends this").

Specific findings for EdTech:
- Duolingo's leaderboards increased lesson completion by 25% (Duolingo product blog, 2023)
- Courses showing "X students enrolled" saw 32% higher enrollment rates (Udemy internal data reported by Class Central, 2024)
- "Most popular" labels increased content engagement by 18% in Khan Academy's internal A/B tests (reported in AltSchool/Khan partnership analysis, 2023)

### 2.2 Feature: Activity Counters

```
+--------------------------------------------------+
|  Quadratic Equations                              |
|                                                    |
|  [people icon] 127 students practiced              |
|                this concept today                  |
|                                                    |
|  [chart icon]  87% of your class passed            |
|                this concept                        |
|                                                    |
|  [trending]    Most popular in Math this week      |
+--------------------------------------------------+
```

**Implementation details:**

- **"X students practiced this today"**: Aggregated from `ConceptAttempted_V1` events, filtered to the current school day (midnight-to-midnight in Israel timezone, UTC+2/+3). Counter is approximate (updated every 5 minutes via a CQRS read-model projection, not real-time).
- **"87% of your class passed this"**: Computed from class-level mastery data. "Passed" means P(Known) >= 0.85 (the existing mastery threshold). Only displayed when the class has >= 10 students who have attempted the concept (minimum sample size for meaningful percentage).
- **"Most popular"**: Ranked by total `ConceptAttempted_V1` events across all students in the student's grade level within the current calendar week. Top 3 concepts per subject get the label.
- **Privacy**: All counters are aggregated. No individual student data is revealed. The class-level percentage is only shown if the class has >= 10 students with attempts on that concept.

**Mobile UI placement:**
- Activity counters appear on the concept detail screen (tapped from the knowledge graph)
- "Most popular" labels appear as small tags on the knowledge graph nodes
- "X students practiced today" appears as a subtle banner on the home screen session start button

### 2.3 Feature: Teacher Endorsements

```
+--------------------------------------------------+
|  [teacher avatar] Mr. Cohen recommends            |
|                                                    |
|  "Focus on Integration by Parts this week.        |
|   It's the foundation for the next 3 topics."    |
|                                                    |
|  [Start recommended session]                      |
|                                                    |
|  4 of your classmates started this today          |
+--------------------------------------------------+
```

- Teachers can mark concepts as "recommended this week" from the admin dashboard (existing Vuexy admin at `src/admin/full-version/`)
- Endorsements are time-bounded (auto-expire after 7 days unless renewed)
- Combined with social proof ("4 classmates started this") for maximum effect
- Teacher endorsement is the "authority" principle from Cialdini -- the most respected social proof for K-12

### 2.4 Feature: "Students Who Did X Also Did Y" Recommendations

```
+--------------------------------------------------+
|  You just mastered Derivatives!                   |
|                                                    |
|  Students who mastered Derivatives usually         |
|  study these next:                                |
|                                                    |
|  1. Integration Basics     [78% went here next]   |
|  2. Chain Rule             [56% went here next]   |
|  3. Related Rates          [34% went here next]   |
|                                                    |
|  [Continue to Integration Basics]                 |
+--------------------------------------------------+
```

- Collaborative filtering: computed from the sequence of `ConceptMastered_V1` events across all students
- Weighted by recency (students from current academic year weighted higher) and similarity (same school, same grade)
- Falls back to the prerequisite graph ordering when insufficient collaborative data exists (< 50 students with the same mastery sequence)
- This supplements, not replaces, the existing ZPD-based item selection in `LearningSessionActor`

### 2.5 Feature: Live Activity Indicators

```
+--------------------------------------------------+
|  [green dot] 12 students studying now              |
|                                                    |
|  Math (5)  Physics (3)  Chemistry (2)  Bio (2)   |
+--------------------------------------------------+
```

- Displayed on the home screen as a subtle "studying now" counter
- Scoped to the student's class (not school-wide, to maintain social intimacy)
- Updated via SignalR heartbeat: students are "active" if they have sent a `ConceptAttempted_V1` or started a session within the last 5 minutes
- This creates a sense of shared effort ("I'm not studying alone") and gentle social pressure
- The subject breakdown encourages students to study subjects their peers are working on (consensus effect)

---

## 3. Collaborative Learning on Mobile

### 3.1 Research Foundation

Vygotsky's Zone of Proximal Development (ZPD) theory posits that learners achieve more when assisted by more capable peers than when working alone. Collaborative learning meta-analyses show consistent positive effects:
- Johnson & Johnson (2009, Review of Educational Research): cooperative learning produces 0.5-1.0 standard deviation improvement over competitive or individualistic learning
- Topping (2005, Journal of Educational Psychology): peer tutoring yields moderate to large effect sizes (d = 0.4-0.6) for both tutor and tutee
- Chi & Wylie (2014, "The ICAP Framework," Educational Psychologist): interactive activities (where learners co-construct knowledge) produce the highest learning gains, exceeding constructive (generating), active (manipulating), and passive (receiving) activities

### 3.2 Feature: Study Groups / Cohort Systems

```
+--------------------------------------------------+
|  Your Study Groups                                |
|                                                    |
|  [group icon] Math Bagrut Crew          5 members |
|               Next challenge: Complete 3           |
|               Calculus concepts by Friday          |
|               [2/5 members completed]              |
|                                                    |
|  [group icon] Physics Study Partners    3 members |
|               Active now: 1 member studying       |
|                                                    |
|  [+ Create study group]                           |
+--------------------------------------------------+
```

**Design considerations for mobile:**
- Groups are teacher-created OR student-created (teacher-created for under-13, student-created for 13+)
- Maximum group size: 5 students (research shows groups of 3-5 are optimal for collaborative learning; Webb, 1991, Review of Educational Research)
- Groups share a common challenge/goal but each student works independently (asynchronous collaboration, not real-time co-editing)
- Group chat is restricted to pre-set responses for under-13 (see Section 8 on safety)
- Group progress is shown as a collective bar: "Your group mastered 12 concepts this week"

### 3.3 Feature: Peer Tutoring Matching

```
+--------------------------------------------------+
|  Help Available!                                  |
|                                                    |
|  You've mastered Quadratic Equations.             |
|  2 classmates are stuck on this concept.          |
|                                                    |
|  [Help a classmate]    [Not now]                  |
|                                                    |
|  Helping others reinforces your own               |
|  understanding (+25 XP per explanation)           |
+--------------------------------------------------+
```

**Matching algorithm:**
- A student is eligible to tutor a concept if their P(Known) >= 0.90 (above the 0.85 mastery threshold, with a buffer)
- A student is flagged as "needs help" if they have >= 5 consecutive incorrect attempts on a concept (from `ConceptAttempted_V1` events) or if `StagnationDetected_V1` has fired for that concept
- Matching is within the same class first, then same grade, never cross-school
- The tutoring interaction is asynchronous: the tutor records a short explanation (text or voice note, 60s max) that the struggling student receives
- Voice notes are transcribed on-device and content-moderated before delivery
- The tutor earns XP and a "Peer Helper" badge category, reinforcing the behavior through vicarious reinforcement

### 3.4 Feature: Group Challenges and Competitions

```
+--------------------------------------------------+
|  This Week's Class Challenge                      |
|                                                    |
|  [trophy] Master 100 concepts as a class!         |
|                                                    |
|  Progress: 73 / 100 concepts                     |
|  [==============================-------]   73%    |
|                                                    |
|  Your contribution: 8 concepts                    |
|  Top contributor: [anonymous] -- 12 concepts      |
|                                                    |
|  2 days remaining                                 |
|  [Keep studying]                                  |
+--------------------------------------------------+
```

- Class-level challenges are collaborative (everyone contributes) not competitive (no ranking)
- The teacher sets the challenge target from the admin dashboard
- Individual contributions are shown but the top contributor is anonymized (prevents "always the same person wins" demotivation)
- Completing the challenge earns every participant a shared badge ("Class Champion -- Week of March 31")
- Challenge types: concept mastery count, total study time, streak maintenance (% of class maintaining streak)

### 3.5 Collaborative Problem-Solving Interfaces on Small Screens

Real-time collaboration on mobile presents significant UX challenges. The small screen (typically 375-414pt wide on iPhone, 360-412dp on Android) limits the amount of collaborative context that can be displayed alongside the learning content.

**Design principles for mobile collaborative UI:**

1. **Overlay, not split**: Never split the screen between content and collaboration. Use bottom sheets, floating action buttons, and overlays.
2. **Presence indicators, not presence panels**: Show who's online as small avatars (24dp circles) overlaid on the content, not as a sidebar.
3. **Turn-based over real-time**: On mobile, asynchronous collaboration (leave a hint, record an explanation, vote on an answer) works better than real-time co-editing.
4. **One focal point**: Only one student "drives" at a time in synchronous activities. Others see the driver's screen and can react (thumbs up/down, suggest hint).

```
+--------------------------------------------------+
|  Collaborative Problem                            |
|                                                    |
|  [Problem content here]                           |
|                                                    |
|  +----+----+----+                                 |
|  | Ami| Dan| You|  -- participant avatars         |
|  +----+----+----+                                 |
|                                                    |
|  Ami is working on this...                        |
|                                                    |
|  [Send a hint]  [Thumbs up]  [Your turn]          |
+--------------------------------------------------+
```

### 3.6 Real-Time vs Async Collaboration

| Dimension | Real-Time | Async | Recommendation |
|-----------|-----------|-------|----------------|
| Mobile UX | Poor (split attention, small screen, connectivity issues) | Good (fits mobile usage patterns) | **Async first** |
| Pedagogical value | High for debate/discussion | High for reflection/explanation | Both have value; async is safer |
| Technical complexity | High (WebSocket, conflict resolution, presence) | Low (message queue, eventual consistency) | Async is simpler |
| Safety/moderation | Hard (real-time content moderation is expensive) | Easy (content reviewed before delivery) | **Async is much safer for K-12** |
| Age appropriateness | Requires maturity for turn-taking | Natural for all ages | Async for under-13 |
| Cena architecture fit | SignalR already exists but adds load | NATS pub/sub fits perfectly | **NATS-based async** |

**Recommendation:** Build async collaboration first (V1). Add real-time quiz battles (Section 10) as a specific, bounded synchronous feature in V2. Never build free-form real-time chat for K-12.

---

## 4. Social Comparison (Festinger) Applied to Learning

### 4.1 Theoretical Foundation

Leon Festinger's Social Comparison Theory (1954) describes how people evaluate their own abilities by comparing themselves to others. Three types of comparison operate in educational settings:

1. **Upward comparison** (aspiration): Comparing to slightly better peers. Effect: can motivate effort ("I want to be like them") but can also undermine self-efficacy if the gap feels too large (Suls, Martin & Wheeler, 2002, Personality and Social Psychology Review).

2. **Downward comparison** (confidence): Comparing to slightly worse peers. Effect: boosts self-esteem and reduces anxiety (Wills, 1981, Psychological Bulletin). In education, this is "I'm doing better than some of my classmates."

3. **Lateral comparison** (belonging): Comparing to similar peers. Effect: provides calibration and a sense of normalcy ("I'm where I should be"). This is the healthiest form of social comparison for learning (Buunk & Gibbons, 2007, Health Psychology).

### 4.2 When Comparison Helps vs Hurts Motivation

| Context | Helps | Hurts |
|---------|-------|-------|
| Student is slightly below the comparison target (upward, small gap) | Increases effort and aspiration | -- |
| Student is far below the comparison target (upward, large gap) | -- | Decreases self-efficacy, increases anxiety |
| Student is above the comparison target (downward) | Increases confidence | May reduce effort ("I'm already better") |
| Student is at the same level (lateral) | Increases belonging, normalizes struggle | -- |
| Student has low self-esteem | -- | ANY explicit comparison may hurt |
| Student has high self-efficacy | Upward comparison motivates | -- |
| Comparison is forced/public | -- | Creates anxiety in most students |
| Comparison is voluntary/private | Generally positive | -- |

**Key insight for Cena:** All social comparison must be opt-in and presented as self-improvement context, never as ranking or judgment. The student should always feel they are comparing to a reference point, not being ranked against peers.

### 4.3 Age-Appropriate Comparison Design

| Age Group | Grade | Comparison Design | Rationale |
|-----------|-------|-------------------|-----------|
| 6-9 | 1-3 | **No peer comparison**. Only compare student to their past self ("You answered 2 more questions right today than yesterday!") | Young children do not have the metacognitive maturity to interpret social comparison constructively (Butler, 1989, Child Development) |
| 10-12 | 4-6 | **Class aggregate only**. "Your class mastered 45 concepts this week!" No individual comparison. | Children begin to understand social comparison but are highly susceptible to negative effects (Dijkstra, Kuyper, van der Werf, Buunk & van der Zee, 2008, Journal of Educational Psychology) |
| 13-15 | 7-9 | **Optional lateral comparison**. "Students at your level typically master this concept in 6-8 attempts. You're on attempt 5." | Adolescents actively seek social comparison information. Making it opt-in respects autonomy (Erikson's identity vs role confusion stage) |
| 16-18 | 10-12 | **Full comparison suite (opt-in)**. Upward models, percentile positioning, pace comparison. "You're ahead of 72% of students preparing for the Math Bagrut." | Older adolescents can constructively use comparison for self-regulation. This age group is Cena's primary target (Bagrut prep) |

### 4.4 Feature: Smart Comparison Widget

```
+--------------------------------------------------+
|  Your Progress in Context                         |
|                                                    |
|  [graph showing student's trajectory              |
|   with a shaded "typical range" band]             |
|                                                    |
|  You: 72% Bagrut readiness                        |
|  Typical range for your pace: 65-78%              |
|                                                    |
|  You're right where you should be.                |
|  Keep going -- at this pace, you'll reach         |
|  85% by exam day.                                 |
+--------------------------------------------------+
```

- The "typical range" is computed from anonymized cohort data: students who started at a similar mastery level, in the same grade, studying the same subject
- The band is intentionally wide (showing 25th-75th percentile) so most students fall within "normal"
- If the student is above the band: "You're ahead of most students at your pace. Great work."
- If the student is below the band: "You're building your foundation. Students who kept your pace often accelerated after [specific concept]."
- NEVER show "you're behind" or "you're falling behind" language. Always frame positionally with forward-looking language.
- This widget respects the age-group table above: not shown for students under 13

---

## 5. Teacher-Student Social Design

### 5.1 Teacher Presence Indicators

**Research basis:** Teacher presence in online learning is one of the strongest predictors of student satisfaction and persistence (Garrison, Anderson & Archer, 2000, "Critical Inquiry in a Text-Based Environment," The Internet and Higher Education). The Community of Inquiry (CoI) framework identifies three types of presence: teaching presence (design, facilitation, direct instruction), social presence (emotional expression, open communication, group cohesion), and cognitive presence (triggering event, exploration, integration, resolution).

**Mobile implementation:**

```
+--------------------------------------------------+
|  [green dot] Mr. Cohen is online                  |
|                                                    |
|  [message icon] New feedback on your               |
|  Trigonometry work                    [View]      |
|                                                    |
|  [star icon] Mr. Cohen recommends:                |
|  "Practice Integration by Parts                   |
|   before Thursday's class"            [Start]     |
+--------------------------------------------------+
```

- **Online indicator**: Green dot next to teacher name when the teacher has the admin dashboard open (resolved via SignalR connection status). Does NOT track teacher's personal device -- only their dashboard session.
- **Feedback notification**: Teachers can leave feedback on student work from the admin dashboard. This appears as a notification card on the student's home screen.
- **Recommendation card**: Teacher-curated concept recommendations appear as highlighted cards, distinct from algorithmic recommendations.

### 5.2 Teacher Feedback Design (Design for Delight)

```
+--------------------------------------------------+
|  Feedback from Mr. Cohen                          |
|                                                    |
|  On: Quadratic Equations (March 29)               |
|                                                    |
|  "Great improvement on factoring! I noticed       |
|   you're still mixing up the signs when           |
|   completing the square. Try the method           |
|   we discussed in class."                         |
|                                                    |
|  [Reply with question]  [Mark as read]            |
|                                                    |
|  +25 XP for reviewing teacher feedback            |
+--------------------------------------------------+
```

- Teacher feedback is delivered with XP reward (students are reinforced for reading feedback)
- Replies are limited to text (no images) and are delivered to the teacher's admin dashboard
- Reply content is moderated (for under-13, replies use pre-set response templates)
- Teachers can attach feedback to specific concepts, sessions, or the student's overall progress
- Feedback supports Hebrew, Arabic, and English (using the student's locale setting)

### 5.3 Teacher-Assigned Challenges

```
+--------------------------------------------------+
|  Challenge from Mr. Cohen                         |
|                                                    |
|  "Master 3 Calculus concepts by Friday"           |
|                                                    |
|  Concepts:                                        |
|  [checkbox] Limits              -- mastered       |
|  [ ] L'Hopital's Rule          -- 72% mastery    |
|  [ ] Squeeze Theorem           -- not started     |
|                                                    |
|  Due: Friday, April 4                             |
|  Reward: 100 bonus XP + Challenge Champion badge  |
|                                                    |
|  23 of 30 classmates accepted this challenge      |
+--------------------------------------------------+
```

- Teachers create challenges from the admin dashboard with specific concepts, deadlines, and optional XP bonuses
- Social proof: "23 of 30 classmates accepted" (consensus effect)
- Challenge completion feeds back to the teacher dashboard as a completion rate metric
- Challenges can be assigned to individual students, groups, or the entire class

### 5.4 Parent Visibility and Involvement

Building on the existing parent dashboard design (documented in `docs/stakeholder-experiences.md`):

**Social features visible to parents:**
- Child's streak and how it compares to class average (opt-in)
- Challenge acceptance and completion status
- Study group membership (not group chat content)
- Peer tutoring activity (that the child helped others, not details of who)

**Parent-initiated social features:**
- Parents can set family study goals ("Practice math for 15 minutes today")
- Family leaderboard (between siblings -- only within the same household)
- Parent can send encouragement messages (pre-set templates for under-13)

### 5.5 Three-Way Communication Patterns (Student-Teacher-Parent)

```
Communication Flow:

Teacher ----[recommendations/feedback]----> Student
Teacher ----[progress reports/alerts]-----> Parent
Student ----[questions/replies]-----------> Teacher
Parent  ----[goals/encouragement]---------> Student
Parent  ----[questions about progress]----> Teacher (via admin portal, not mobile app)

NOT supported:
- Student --> Parent (learning is the student's space)
- Parent --> Teacher via student's app (separate channel)
- Direct student-to-student messaging for under-13
```

---

## 6. Community Features

### 6.1 Feature: Discussion Forums for Questions

```
+--------------------------------------------------+
|  Questions about: Derivatives                     |
|                                                    |
|  [Q] Why does the chain rule work?      12 votes |
|      3 answers -- best answer by teacher          |
|                                                    |
|  [Q] I'm confused about implicit        8 votes  |
|      differentiation notation                     |
|      5 answers                                    |
|                                                    |
|  [Q] Can someone explain the            3 votes  |
|      product rule visually?                       |
|      1 answer                                     |
|                                                    |
|  [Ask a question]                                 |
+--------------------------------------------------+
```

- Questions are scoped to a concept (attached to a knowledge graph node)
- Questions and answers are moderated before display (see Section 15)
- Students earn XP for asking questions (5 XP), answering (10 XP), and getting their answer upvoted (2 XP per vote)
- Teacher answers are marked with a distinct badge and pinned to the top
- "Best answer" can be marked by the teacher or by community vote (> 10 upvotes auto-marks as "community approved")

### 6.2 Feature: Student-Generated Explanations

```
+--------------------------------------------------+
|  Student Explanations for: Integration by Parts   |
|                                                    |
|  [Best] "Think of it like undoing the product     |
|  rule. If you know how two functions multiply     |
|  together, integration by parts helps you         |
|  unmultiply them."                                |
|  -- Verified by Mr. Cohen                         |
|  47 students found this helpful                   |
|                                                    |
|  [Good] "LIATE helps you choose u and dv:         |
|  Logarithmic, Inverse trig, Algebraic,            |
|  Trigonometric, Exponential"                      |
|  -- 23 students found this helpful                |
|                                                    |
|  [Write your explanation]                         |
+--------------------------------------------------+
```

- Student explanations are submitted as annotations (existing `AddAnnotation` flow) with kind "explanation"
- All submissions go through a moderation queue (AI pre-filter + teacher review)
- The authoring student earns XP and a "Teacher's Pet" badge progression (1 approved explanation, 5, 20, 50)
- Explanations are rated by other students (helpful/not helpful)
- Teacher can verify an explanation as "accurate" (adding a checkmark)
- Explanations are tied to concepts and to the methodology that was active when the student mastered the concept (so a Socratic explanation is shown to students using the Socratic methodology)

### 6.3 Feature: Voting on Best Answers

**Voting mechanics:**
- One vote per student per answer (no gaming)
- Vote count is public; voter identity is private
- Answers are sorted by vote count, with teacher-verified answers always on top
- Voting requires the student to have attempted the concept at least once (prevents drive-by voting)
- A "community approved" badge appears at 10+ votes
- Answers with net negative votes (reported > upvoted) are hidden and escalated to moderation

### 6.4 Feature: Mentorship Program (Older Students Help Younger)

```
+--------------------------------------------------+
|  Become a Mentor                                  |
|                                                    |
|  You've mastered 30+ Math concepts!               |
|  Help younger students learn.                     |
|                                                    |
|  Benefits:                                        |
|  - 50 XP per approved explanation                 |
|  - "Mentor" badge and profile title               |
|  - Strengthens your own understanding             |
|    (teaching is the best way to learn!)           |
|                                                    |
|  [Become a Mentor]  [Learn more]                  |
+--------------------------------------------------+
```

- Mentorship is opt-in and requires >= 30 mastered concepts in a subject and grade 10+ (age >= 15)
- Mentors write explanations that are shown to students in grades 7-9 working on the same concepts
- All mentor content is moderated (teacher approval required)
- Mentors earn elevated XP and exclusive badges
- Matching is by concept and subject, not by personal connection (preventing inappropriate contact)
- Mentors never know the identity of students they're helping (fully anonymized)

### 6.5 School-Level and Class-Level Communities

| Scope | Features | Visibility | Moderation |
|-------|----------|------------|------------|
| **Class** (primary unit) | Activity feed, challenges, study groups, Q&A | All class members | Class teacher |
| **Grade** | Aggregate stats, inter-class challenges | Grade-level aggregate only | Grade coordinator |
| **School** | School-wide achievements, events | Aggregate only | School admin |
| **Cross-school** | Anonymous competition events only | No individual data | Cena platform team |

- The class is the primary social unit. All social features default to class scope.
- Grade-level features are aggregate only ("Grade 11 mastered 1,200 concepts this month").
- School-level features are for administration and events, not student social interaction.
- Cross-school interaction is limited to anonymized competition events (school vs school quiz battles, Section 10).

---

## 7. Social Motivation Mechanics

### 7.1 Feature: Accountability Partners

```
+--------------------------------------------------+
|  Study Buddy: Dana                                |
|                                                    |
|  [connected circles icon]                         |
|                                                    |
|  Your streaks:                                    |
|  You: 12 days [fire]   Dana: 8 days [fire]       |
|                                                    |
|  You've both studied today                        |
|                                                    |
|  Weekly goal: Both study 5 days this week         |
|  [you: 3/5]  [Dana: 4/5]                         |
|                                                    |
|  [Send encouragement]                             |
+--------------------------------------------------+
```

- Students pair up by mutual opt-in (both must accept)
- Limited to 1 accountability partner at a time (keeps it simple and meaningful)
- Both students see each other's streak and weekly study frequency (not scores, not mastery details)
- "Send encouragement" sends a pre-set message (from a curated list): "Great job today!", "Keep your streak going!", "You can do it!"
- If one partner's streak is at risk, the other gets a gentle notification: "Dana hasn't studied today. Send encouragement?"
- Available for students 13+ only. Under-13 students can be paired by the teacher.

### 7.2 Feature: Study Challenges Between Friends

```
+--------------------------------------------------+
|  Challenge from Amit                              |
|                                                    |
|  "First to master 3 Physics concepts this week!"  |
|                                                    |
|  Your progress:    Amit's progress:               |
|  [===---------]    [=====-------]                 |
|  1/3 mastered      2/3 mastered                   |
|                                                    |
|  3 days remaining                                 |
|                                                    |
|  [Accept challenge]  [Decline]                    |
+--------------------------------------------------+
```

- Students can challenge a classmate to a mastery race (first to master N concepts in a subject)
- Challenges are time-bounded (1 week max)
- Both participants see each other's concept mastery progress (not detailed scores)
- Winner gets a "Challenge Victor" badge and bonus XP (50 XP). Loser gets participation XP (25 XP). Both get more XP than studying alone.
- Challenges must be accepted by the recipient (no forced participation)
- Maximum 2 active challenges at a time (prevents distraction)
- Available for 13+ only

### 7.3 Feature: Shared Goals and Milestones

```
+--------------------------------------------------+
|  Class Goal: Bagrut Ready by January              |
|                                                    |
|  [thermometer visualization]                      |
|                                                    |
|  Class average readiness: 64%                     |
|  Target: 80% by January 15                        |
|                                                    |
|  On track: 18 students                            |
|  Need to accelerate: 7 students                   |
|  Just getting started: 5 students                 |
|                                                    |
|  You are: On track (72% readiness)                |
|                                                    |
|  [View your personal timeline]                    |
+--------------------------------------------------+
```

- Teacher sets a class-wide readiness goal with a date
- Individual students see their position relative to the goal but NOT their ranking among peers
- Categories are broad ("on track," "need to accelerate," "getting started") -- not numerical ranks
- The class thermometer creates collective momentum without individual pressure

### 7.4 Feature: Celebration of Peer Achievements

```
+--------------------------------------------------+
|  [confetti animation]                             |
|                                                    |
|  Yael just earned the "Deep Thinker" badge!       |
|                                                    |
|  She mastered 20 concepts in Math.                |
|                                                    |
|  [Congratulate Yael]   [Dismiss]                  |
|                                                    |
|  Congratulating sends: "Amazing work, Yael!"      |
+--------------------------------------------------+
```

- Milestone achievements (level-up, badge earned, streak milestone) of classmates appear as celebratory modals
- Maximum 1 peer celebration per study session (prevent interruption fatigue)
- Celebrations are shown for badge milestones (5, 10, 20 concepts mastered) and streak milestones (7, 14, 30 days)
- The "Congratulate" action sends a pre-set message and earns the sender 5 XP ("kindness bonus")
- Students can opt out of showing their achievements to classmates (privacy setting)

### 7.5 Feature: Virtual Study Rooms

```
+--------------------------------------------------+
|  Study Room: Math Grind                           |
|                                                    |
|  [avatar] [avatar] [avatar] -- 3 studying now    |
|                                                    |
|  Room timer: 45:12 (you've been here 22 min)      |
|                                                    |
|  [ambient sound: library]  [change sound]         |
|                                                    |
|  Room stats:                                      |
|  Questions answered: 47 (you: 12)                 |
|  Concepts mastered: 5 (you: 1)                    |
|                                                    |
|  [Leave room]                                     |
+--------------------------------------------------+
```

- Virtual study rooms provide ambient social presence while each student works independently
- No chat, no interaction -- just knowing that others are studying at the same time
- Ambient sound options: library silence, coffee shop murmur, rain, lo-fi beats
- Room stats show collective activity (questions answered, concepts mastered) for social proof
- Students can create rooms and invite classmates, or join existing class rooms
- Room timer encourages sustained study (Pomodoro-style)
- Available for all ages (no safety concerns since there's no communication)

---

## 8. Social Safety in K-12

### 8.1 COPPA Compliance (Under-13 in USA/applicable markets)

The Children's Online Privacy Protection Act (COPPA, updated April 2024) imposes strict requirements:

| Requirement | Cena Implementation |
|-------------|---------------------|
| **Verifiable parental consent before collecting personal data** | Parent creates the child's account. Teacher creates accounts for classroom use (school acts as agent of parent per COPPA safe harbor). |
| **No behavioral advertising** | Cena has no advertising. |
| **Data minimization** | Social features for under-13 store only: participation events, anonymized aggregate stats. No personal messages stored. |
| **Right to delete** | Existing `AccountStatusChanged` event with `PendingDelete` status handles data deletion. Social contributions (Q&A, explanations) are anonymized on deletion, not removed (preserving community content integrity). |
| **No direct messaging** | Under-13 students use pre-set response templates only. No free-text messaging to peers. |
| **Biometric data** | Per engagement signals research: no camera/voice for under-13 without explicit parental consent. Social features do not require biometrics. |

### 8.2 Israeli Data Protection (Amendment 13, effective August 2025)

| Requirement | Cena Implementation |
|-------------|---------------------|
| **Parental consent for under-18 biometric data** | Social features do not collect biometric data. |
| **GDPR alignment** | All social data processing has a documented legal basis (educational legitimate interest for class-scoped features; consent for optional social features). |
| **Right of access and deletion** | Student/parent can request export and deletion of all social data via settings. |
| **Data localization** | Student social data stored in Israel-region cloud infrastructure. |

### 8.3 Anti-Bullying Design Patterns

**Principle: Design out the possibility of bullying, rather than detecting and responding to it after the fact.**

| Design Pattern | Implementation |
|----------------|----------------|
| **No open-ended messaging for under-13** | Pre-set response templates: "Great job!", "You can do it!", "Thanks for helping!", "I agree", "Good explanation" |
| **No user-uploaded content** | All student-generated content (explanations, Q&A) goes through moderation before display |
| **No visible rankings for under-13** | Class-level aggregates only; no individual leaderboards |
| **No negative social signals** | Cannot "downvote" a person, only mark content as "not helpful." No public failure indicators. |
| **Anonymous peer content** | Peer solutions and explanations show "a student" or first name only (never full name, photo, or profile link) |
| **Block/report mechanism** | Students can report any content. Reported content is immediately hidden pending review. 3+ reports auto-escalate to teacher. |
| **No public profiles for under-13** | Student identity is visible only to their teacher and parent, not to peers |
| **Rate limiting on social actions** | Maximum 5 messages/encouragements per day to prevent spam/harassment |

### 8.4 Age-Tiered Social Feature Matrix

| Feature | Ages 6-9 (Grade 1-3) | Ages 10-12 (Grade 4-6) | Ages 13-15 (Grade 7-9) | Ages 16-18 (Grade 10-12) |
|---------|----------------------|------------------------|------------------------|--------------------------|
| Activity feed (class) | Aggregate only ("Class mastered 10 concepts!") | Aggregate + anonymized individual achievements | Named achievements (opt-in) | Full feed with names (opt-in) |
| Study groups | Teacher-created only | Teacher-created only | Student-created (teacher-approved) | Student-created |
| Peer Q&A | Not available | Read only (teacher-posted Q&A) | Read + ask + answer (moderated) | Read + ask + answer (moderated) |
| Accountability partner | Not available | Teacher-paired only | Self-selected | Self-selected |
| Challenges | Class challenges only (teacher-created) | Class challenges only | Friend challenges | Friend challenges |
| Leaderboard | Not available | Not available | Opt-in, class only | Opt-in, class + grade |
| Peer tutoring | Not available | Not available | Receive help (not give) | Give + receive help |
| Mentorship | Not available | Not available | Not available | Eligible to mentor grade 7-9 |
| Direct messages | Not available | Pre-set responses only | Pre-set + short text (moderated) | Free text (moderated) |
| Study rooms | Available (no chat) | Available (no chat) | Available (pre-set chat) | Available (moderated chat) |
| Profile visibility | Teacher + parent only | Teacher + parent + first name to class | First name + avatar to class | Name + avatar + badges to class |
| Social comparison | Self-only ("you vs your past") | Class aggregate only | Optional lateral comparison | Full comparison suite (opt-in) |

### 8.5 Teacher/Admin Moderation Tools

Detailed in Section 15.

### 8.6 Parent Controls and Visibility

| Control | Default | Parent Can Change |
|---------|---------|-------------------|
| Social features enabled | On (age-appropriate subset) | Can disable all social features |
| Child visible in class feed | On (first name only) | Can make fully anonymous |
| Peer challenges allowed | On (for 13+) | Can disable |
| Study group membership | On | Can disable |
| Accountability partner | Off (for under-13) | Can enable for 10+ |
| Encouragement messages | On | Can disable |
| Peer tutoring participation | On (for 13+) | Can disable |

---

## 9. Social Onboarding

### 9.1 Research Foundation

Social onboarding increases retention significantly. Duolingo's "invite friends" flow during onboarding increased D7 retention by 12% for users who connected with at least one friend (Duolingo growth team, reported in Lenny's Newsletter, 2023). ClassDojo's classroom joining flow during teacher-initiated setup achieves 95%+ class adoption within 48 hours (ClassDojo press materials, 2024).

### 9.2 Feature: Class Code Joining

```
+--------------------------------------------------+
|  Join Your Class                                  |
|                                                    |
|  Your teacher gave you a class code.              |
|  Enter it below to join your class:               |
|                                                    |
|  +------------------------------------------+    |
|  |  _ _ _ _ - _ _ _ _                       |    |
|  +------------------------------------------+    |
|                                                    |
|  [Join Class]                                     |
|                                                    |
|  Don't have a code? [Study independently]         |
+--------------------------------------------------+
```

- Class codes are 8-character alphanumeric strings generated by teachers from the admin dashboard
- Codes are case-insensitive and exclude ambiguous characters (0/O, 1/l/I)
- Codes expire after 30 days (teacher can regenerate)
- Joining a class immediately places the student in the class's social context (activity feed, challenges, study groups)
- Students without a class code can study independently (all individual features work; social features are disabled)

### 9.3 Feature: Friend-Finding During Setup

```
+--------------------------------------------------+
|  Find Your Friends                                |
|                                                    |
|  [search icon] Search by name or class code       |
|                                                    |
|  Classmates already on Cena:                      |
|                                                    |
|  [avatar] Dana           [Add as study buddy]     |
|  [avatar] Amit           [Add as study buddy]     |
|  [avatar] Yael           [Added!]                 |
|                                                    |
|  3 of 30 classmates are on Cena                   |
|                                                    |
|  [Invite classmates]  [Skip for now]              |
+--------------------------------------------------+
```

- Friend-finding is shown AFTER class code joining (so we know who the student's classmates are)
- Classmates are surfaced by class membership, not by contact import (privacy-first)
- "Add as study buddy" requires mutual acceptance (both must add each other)
- No contact import for under-13 (COPPA)
- For 13+: optional contact sharing via class membership (teacher can disable per-class)

### 9.4 Feature: "Your Friends Are Already Here" Patterns

```
+--------------------------------------------------+
|  [notification bar, shown during first 7 days]    |
|                                                    |
|  [party icon] 5 of your classmates joined          |
|              Cena this week!                       |
|              [See who's here]                      |
+--------------------------------------------------+
```

- During the first 7 days (critical retention window), show notifications when classmates join
- This creates social momentum and FOMO ("everyone's using it")
- Notifications are limited to 1 per day maximum
- "See who's here" shows first names and subjects they're studying (not scores)

### 9.5 Feature: Teacher-Initiated Onboarding

```
Teacher Admin Dashboard:

+--------------------------------------------------+
|  Class Onboarding                                 |
|                                                    |
|  Class: 11th Grade Math (5-unit)                  |
|  Code: MATH-7K2P                                  |
|                                                    |
|  Students joined: 23 / 30                         |
|  [============================-------]  77%       |
|                                                    |
|  Not yet joined:                                  |
|  Moshe K., Sara L., Noa B., ...                   |
|                                                    |
|  [Send reminder to unjoined students]             |
|  [Print QR code for classroom]                    |
|  [Share join link via WhatsApp]                   |
+--------------------------------------------------+
```

- Teachers drive adoption: they create the class, share the code, and track who has joined
- QR code generation for classroom display (students scan with phone camera)
- WhatsApp share link (critical for Israeli market where WhatsApp is the dominant messaging platform)
- Reminder mechanism: teacher can push a reminder via the school's communication system

---

## 10. Multiplayer Learning

### 10.1 Feature: Real-Time Quiz Battles (Kahoot-Style Mobile)

```
+--------------------------------------------------+
|  Quiz Battle: Derivatives                         |
|                                                    |
|  Question 3 of 10                  [00:12]        |
|                                                    |
|  What is d/dx of sin(x)?                          |
|                                                    |
|  [A] cos(x)          [B] -cos(x)                 |
|  [C] sin(x)          [D] -sin(x)                 |
|                                                    |
|  +---------+---------+---------+                  |
|  | You: 2  | Ami: 3  | Dan: 1  |                 |
|  +---------+---------+---------+                  |
+--------------------------------------------------+
```

**Design details:**
- 2-6 players per battle (classmates only)
- 10 questions per battle (3-5 minute total duration)
- Questions are drawn from the existing question pool (`QuestionPoolActor`) filtered to concepts all participants have encountered
- Scoring: correct answer = 10 points + speed bonus (up to 5 points for fastest correct answer)
- Results screen shows everyone's score and the correct answers for missed questions (learning moment)
- Battle history is stored for rematch and improvement tracking
- Available for 10+ (teacher-initiated for 10-12; student-initiated for 13+)

**Technical implementation:**
- Matchmaking via a `QuizBattleActor` (new actor, see Section 11)
- Real-time sync via SignalR (existing infrastructure)
- Optimistic client-side UI with server-authoritative scoring (prevents cheating)
- Offline players are auto-forfeited after 10 seconds of no response per question

### 10.2 Feature: Turn-Based Learning Games

```
+--------------------------------------------------+
|  Math Duel with Dana                              |
|                                                    |
|  Your turn!                                       |
|                                                    |
|  Solve this before Dana solves hers:              |
|                                                    |
|  Simplify: (x^2 - 4) / (x + 2)                  |
|                                                    |
|  +------------------------------------------+    |
|  |  Your answer: ___                         |    |
|  +------------------------------------------+    |
|                                                    |
|  [Submit answer]                                  |
|                                                    |
|  Dana's status: Working on her problem...         |
+--------------------------------------------------+
```

- Two players receive different questions of the same difficulty and concept
- Each player works at their own pace (no time pressure from the other)
- After both submit, results are compared: both correct = tie, one correct = that player wins the round
- 5 rounds per duel (questions escalate in difficulty)
- Turn-based means no real-time connection needed -- NATS-based async messages work perfectly
- Great for mobile: player can start a duel, put the phone down, come back to their turn later

### 10.3 Feature: Cooperative Missions

```
+--------------------------------------------------+
|  Mission: Conquer Calculus                        |
|                                                    |
|  Your class is exploring the Calculus continent!  |
|                                                    |
|  [map visualization showing concept clusters      |
|   as territories, colored by class mastery]       |
|                                                    |
|  Territories conquered: 7 / 15                    |
|  Your contribution: 3 territories                 |
|                                                    |
|  Next target: "Integration by Parts"              |
|  3 students working on this now                   |
|                                                    |
|  [Join the mission]                               |
+--------------------------------------------------+
```

- The class knowledge graph is visualized as a map/territory that the class "conquers" together
- Each concept cluster is a territory; it's "conquered" when >= 60% of the class has mastered it
- Individual contributions are tracked but the mission is collaborative (everyone benefits)
- Mission maps are displayed on the knowledge graph visualization (existing feature in Cena)
- Missions last 2-4 weeks, aligning with curriculum pacing
- The visualization ties directly to Cena's existing knowledge graph rendering

### 10.4 Feature: Class-Wide Challenges

```
+--------------------------------------------------+
|  Weekly Class Challenge                           |
|                                                    |
|  [trophy] 1000 Correct Answers!                   |
|                                                    |
|  Class progress: 847 / 1000                       |
|  [==============================---]  85%         |
|                                                    |
|  Your contribution: 62 correct answers            |
|  Class MVP this week: [anonymous] (89 answers)    |
|                                                    |
|  Reward: Everyone earns "Team Player" badge       |
|          + 50 bonus XP when goal is reached       |
|                                                    |
|  [Keep studying]                                  |
+--------------------------------------------------+
```

- Teacher-created or system-generated weekly challenges
- Targets are calibrated to be achievable but stretching: set at 110% of the class's average weekly performance
- MVP is anonymized (prevents the same high-performer from being spotlighted repeatedly)
- Completing the challenge earns EVERYONE a badge and bonus XP (cooperative, not competitive)

### 10.5 Feature: School vs School Competitions

```
+--------------------------------------------------+
|  Inter-School Challenge                           |
|                                                    |
|  [school A logo]  VS  [school B logo]             |
|                                                    |
|  Math Mastery Challenge                           |
|  Which school masters more concepts this month?   |
|                                                    |
|  Your school: 2,341 concepts mastered             |
|  Rival school: 2,189 concepts mastered            |
|                                                    |
|  Your school is leading!                          |
|  [Contribute to your school's score]              |
+--------------------------------------------------+
```

- Fully anonymized: schools see aggregate stats only; no individual student data crosses school boundaries
- Opt-in by school administration (both schools must agree)
- Competitions run monthly with themes (Math Month, Science Sprint, etc.)
- Winning school gets a virtual trophy displayed on their school profile
- Individual students see their school's position and their own contribution count
- This creates school-level pride and identity without individual pressure

---

## 11. Actor Model Mapping

### 11.1 New Actors Required for Social Features

The following new actors extend the existing Proto.Actor hierarchy managed by `ActorSystemManager`:

#### ClassActor (Virtual, Cluster-Scoped)

```
ClusterIdentity: ("class", "{schoolId}:{classId}")
Responsibility: Manages class-level social state
Lifecycle: Virtual actor, activated on first message, passivated after 60 min idle

State:
- ClassId, SchoolId, TeacherId
- ActiveStudentIds: HashSet<string>
- WeeklyChallenge: ChallengeState
- ActivityFeed: CircularBuffer<FeedEvent> (last 100 events)
- ClassMasterySnapshot: Dictionary<conceptId, mastery%>

Messages handled:
- StudentJoinedClass(studentId)
- StudentLeftClass(studentId)
- ConceptMasteredInClass(studentId, conceptId)  -- forwarded from StudentActor
- CreateChallenge(teacherId, challengeSpec)
- GetClassFeed(since: DateTimeOffset)
- GetClassStats
- TeacherRecommendation(teacherId, conceptId, message)

Events emitted:
- ClassChallengeCreated_V1
- ClassChallengeCompleted_V1
- ClassMilestoneReached_V1 (e.g., "100 concepts mastered as a class")
```

#### CohortActor (Virtual, Cluster-Scoped)

```
ClusterIdentity: ("cohort", "{cohortId}")
Responsibility: Manages study group state and challenges
Lifecycle: Virtual actor, activated on first message, passivated after 30 min idle

State:
- CohortId, CohortName
- MemberIds: HashSet<string> (max 5)
- ActiveChallenge: ChallengeState?
- SharedGoal: GoalState?
- MemberProgress: Dictionary<studentId, progressSnapshot>

Messages handled:
- CreateCohort(creatorId, name, memberIds)
- JoinCohort(studentId)
- LeaveCohort(studentId)
- UpdateMemberProgress(studentId, progressDelta)
- SetSharedGoal(goal)
- GetCohortStatus

Events emitted:
- CohortCreated_V1
- CohortChallengeCompleted_V1
- CohortGoalReached_V1
```

#### SocialFeedActor (Classic, Singleton per Class)

```
Spawned by: ClassActor
Responsibility: Aggregates and curates the activity feed for a class
Lifecycle: Lives as long as the parent ClassActor

State:
- FeedBuffer: CircularBuffer<FeedEvent> (last 200 events, ~50KB)
- StudentOptOutSet: HashSet<string> (students who opted out of showing achievements)
- DeduplicationWindow: HashSet<eventHash> (prevents duplicate events within 5 min)

Messages handled:
- PublishAchievement(studentId, achievementType, details)
- PublishClassMilestone(milestoneType, value)
- GetFeed(since: DateTimeOffset, limit: int)
- OptOutStudent(studentId)
- OptInStudent(studentId)

Feed event types:
- StudentMasteredConcept (anonymizable)
- StudentEarnedBadge (anonymizable)
- StudentStreakMilestone (anonymizable)
- ClassChallengeProgress (always public)
- TeacherRecommendation (always public)
- TeacherFeedback (private, only visible to recipient)

Events emitted to NATS:
- FeedEventPublished_V1 (for SignalR push to mobile clients)
```

#### QuizBattleActor (Classic, Ephemeral)

```
Spawned by: ActorSystemManager on demand
Responsibility: Manages a single real-time quiz battle session
Lifecycle: Created when a battle starts, destroyed when it ends (max 10 min TTL)

State:
- BattleId, ParticipantIds (2-6)
- Questions: List<QuestionState> (10 questions)
- CurrentQuestionIndex
- Scores: Dictionary<studentId, int>
- AnswerTimestamps: Dictionary<studentId, DateTimeOffset>

Messages handled:
- StartBattle(participantIds, conceptIds)
- SubmitAnswer(studentId, questionIndex, answer, timestamp)
- GetBattleState
- EndBattle (timeout or all questions answered)

Events emitted:
- QuizBattleStarted_V1
- QuizBattleRoundCompleted_V1
- QuizBattleEnded_V1 (with final scores)
```

#### PeerTutoringActor (Virtual, Cluster-Scoped)

```
ClusterIdentity: ("peer-tutor", "{schoolId}:{conceptId}")
Responsibility: Matches tutors with students needing help on a specific concept
Lifecycle: Virtual actor, activated when tutoring is needed

State:
- AvailableTutors: PriorityQueue<TutorCandidate> (sorted by mastery level)
- PendingRequests: Queue<HelpRequest>
- ActivePairings: Dictionary<requestId, TutorPairing>

Messages handled:
- RegisterAsTutor(studentId, conceptId, masteryLevel)
- RequestHelp(studentId, conceptId, stagnationScore)
- SubmitExplanation(tutorId, requestId, explanationText)
- RateExplanation(studentId, requestId, helpful: bool)

Events emitted:
- PeerTutoringMatched_V1
- PeerExplanationSubmitted_V1
- PeerExplanationRated_V1
```

### 11.2 Integration with Existing Actors

```
ActorSystemManager (root guardian)
  |
  +-- CurriculumGraphActor (existing)
  +-- LlmGatewayActor (existing)
  +-- StudentActorManager (existing)
  |
  +-- ClassActorManager (NEW -- singleton, manages class actor references)
  |     +-- ClassActor (virtual, per class)
  |           +-- SocialFeedActor (classic, child of ClassActor)
  |
  +-- QuizBattleCoordinator (NEW -- singleton, spawns QuizBattleActors)
        +-- QuizBattleActor (classic, ephemeral, one per battle)

StudentActor (existing, virtual, per student)
  |
  +-- LearningSessionActor (existing)
  +-- StagnationDetectorActor (existing)
  +-- OutreachSchedulerActor (existing)
  |
  // NEW: Forward mastery/badge events to ClassActor
  // On ConceptMastered_V1: send to ClassActor via ClusterIdentity
  // On BadgeEarned_V1: send to SocialFeedActor via ClassActor
  // On StreakUpdated_V1: send to SocialFeedActor if streak milestone

CohortActor (NEW, virtual, per study group)
PeerTutoringActor (NEW, virtual, per school:concept)
```

### 11.3 Event Flow for Social Features

```
Student masters a concept (existing flow):
  StudentActor emits ConceptMastered_V1 --> Marten (existing)
    |
    +--> (NEW) StudentActor forwards to ClassActor via cluster
    |     ClassActor --> SocialFeedActor.PublishAchievement
    |     SocialFeedActor --> NATS "class.{classId}.feed"
    |     SignalR hub subscribes to NATS --> pushes to connected mobile clients
    |
    +--> (NEW) StudentActor forwards to CohortActor (if member)
    |     CohortActor updates member progress, checks shared goal
    |
    +--> (NEW) PeerTutoringActor checks if student qualifies as tutor
          If mastery >= 0.90, register as available tutor for this concept
```

### 11.4 NATS Topic Design for Social Events

```
Social event topics (new):
  class.{classId}.feed              -- class activity feed events
  class.{classId}.challenge         -- challenge progress updates
  cohort.{cohortId}.progress        -- study group progress
  battle.{battleId}.state           -- quiz battle state updates
  school.{schoolId}.aggregate       -- school-level aggregate stats

Existing topics (unchanged):
  student.{studentId}.session       -- session events
  student.{studentId}.mastery       -- mastery updates
```

---

## 12. Competitive Analysis: Social Features

### 12.1 ClassDojo

**What they do well:**
- Classroom community is the core product (not an add-on)
- Class story: a shared timeline where teachers post updates, photos, and student work
- Points system: teachers award behavior points visible to parents and students
- Messaging: teacher-parent and teacher-student messaging with translation
- Beyond School: home learning activities parents can assign
- Monsters: student avatars (cute, customizable) that build identity

**What they miss:**
- No learning content -- ClassDojo is a behavior/community platform, not a learning platform
- Points are teacher-assigned (subjective), not earned through demonstrated mastery (objective)
- No peer-to-peer learning features
- No knowledge graph or mastery tracking

**What Cena should adopt:**
- Class story concept (adapted as the class activity feed)
- Teacher-initiated onboarding flow (class codes, teacher drives adoption)
- Parent messaging (pre-set templates for under-13)
- Avatar/identity system (students choose or earn avatar elements)

**What Cena should NOT adopt:**
- Teacher-assigned behavior points (conflicts with Cena's mastery-based philosophy)
- Photo sharing (K-12 privacy concerns, COPPA)

### 12.2 Kahoot!

**What they do well:**
- Real-time quiz battles are addictive and create excitement
- Music and animation during quizzes create a game show atmosphere
- Results screen shows learning gaps (which questions were most missed)
- Teacher creates quizzes from curriculum content

**What they miss:**
- No adaptive difficulty (all students get the same questions)
- No mastery tracking between games
- No asynchronous mode (requires everyone online simultaneously)
- Social interaction limited to the quiz moment (no ongoing community)

**What Cena should adopt:**
- Real-time quiz battle format (adapted to mobile and using Cena's adaptive question pool)
- Excitement mechanics: countdown timers, sound effects, podium finish
- Post-battle learning review (show correct answers for missed questions)

**What Cena should NOT adopt:**
- Teacher-created quizzes (Cena's strength is the algorithmic question pool -- teacher creates challenge scope, system selects questions)
- Web-only format (Cena is mobile-first)

### 12.3 Brainly

**What they do well:**
- Peer Q&A community: students ask questions, other students answer
- Gamification of helping: points, levels, ranks for answering
- Moderation at scale: AI + community moderators + staff
- Multi-language support (40+ languages)

**What they miss:**
- Answers are not verified for accuracy (community moderation is imperfect)
- No connection to curriculum or learning path
- Quality varies wildly (from excellent explanations to wrong answers)
- Ad-supported model creates poor user experience

**What Cena should adopt:**
- Concept-scoped Q&A (questions tied to knowledge graph nodes, not free-form)
- Gamification of answering (XP, badges for helpful answers)
- Teacher verification layer on top of community voting

**What Cena should NOT adopt:**
- Open Q&A without curriculum connection (leads to homework-dumping)
- Ad-supported model
- Unverified answers (teacher verification is essential for K-12)

### 12.4 Socratic (by Google)

**What they do well:**
- Camera-based question input (take a photo of a problem, get an answer)
- Clean, focused UI (one question at a time)
- Step-by-step solutions
- Subject-organized (Math, Science, Literature, Social Studies, English)

**What they miss:**
- No social features at all (purely individual)
- No mastery tracking or adaptive learning
- No community or peer interaction
- Effectively a homework answer tool, not a learning platform

**What Cena should adopt:**
- Nothing social (Socratic has no social features)
- Camera question input is interesting but out of scope for social research

### 12.5 Google Classroom

**What they do well:**
- Class organization: teachers create classes, students join with codes
- Assignment workflow: create, distribute, collect, grade
- Announcement stream (class-level communication)
- Integration with Google ecosystem (Docs, Sheets, Forms)
- Guardian summaries (weekly email to parents)

**What they miss:**
- No adaptive learning (it's a workflow tool, not a learning tool)
- No gamification (no XP, streaks, badges)
- No peer interaction beyond comments on assignments
- No knowledge graph or mastery tracking
- Social features are teacher-driven only (students can't initiate)

**What Cena should adopt:**
- Class code joining flow (already planned in Section 9)
- Guardian weekly summaries (already designed in `docs/stakeholder-experiences.md`)
- Assignment/challenge workflow for teachers

**What Cena should NOT adopt:**
- Document-centric workflow (Cena is adaptive learning, not assignment management)
- Teacher-only social initiation (students should be able to initiate age-appropriate social interactions)

### 12.6 Competitive Feature Matrix: Social Features

| Social Feature | ClassDojo | Kahoot! | Brainly | Google Classroom | Duolingo | **Cena (Proposed)** |
|----------------|-----------|---------|---------|------------------|----------|---------------------|
| Class activity feed | Class Story | No | No | Announcement stream | No | **Yes -- achievement-based** |
| Real-time multiplayer | No | Quiz battles | No | No | No | **Yes -- quiz battles + duels** |
| Peer Q&A | No | No | Core product | Assignment comments | No | **Yes -- concept-scoped, moderated** |
| Peer tutoring | No | No | Answer questions | No | No | **Yes -- mastery-matched** |
| Study groups | No | No | No | No | No | **Yes -- cohort system** |
| Accountability partners | No | No | No | No | Friend streak | **Yes -- mutual opt-in** |
| Leaderboards | Points (teacher) | Post-game | Answer ranks | No | Leagues | **Optional, class-scoped** |
| Teacher presence | Core | No | No | Moderate | No | **Yes -- online indicator, feedback** |
| Parent visibility | Core | No | No | Guardian summaries | No | **Yes -- dashboard + digest** |
| Social onboarding | Class codes | Game codes | No | Class codes | Friend invite | **Class codes + friend finding** |
| Moderation tools | Behavior points | N/A | AI + community | Comment removal | N/A | **AI + teacher + admin** |
| COPPA compliance | Yes | Partial | Partial | Yes | Partial | **Full -- age-tiered** |
| Anti-bullying design | Basic | N/A | Reporting | Basic | N/A | **Design-first prevention** |

---

## 13. Privacy-First Social Design Patterns

### 13.1 Core Principle: Social by Opt-In, Private by Default

Every social feature in Cena follows this hierarchy:

```
Level 0: Feature OFF (student uses Cena individually)
  |
  v  [Student or teacher enables]
Level 1: Aggregate only ("23 students studied today")
  |
  v  [Student opts in]
Level 2: Anonymized participation ("a student in your class mastered X")
  |
  v  [Student opts in]
Level 3: Named participation ("Dana mastered Derivatives!")
  |
  v  [Student opts in]
Level 4: Interactive participation (challenges, Q&A, tutoring)
```

- No social feature requires personal data exposure to function
- Aggregate statistics are always available (they don't identify individuals)
- Students can participate at any level and change their preference at any time
- Parents can set a maximum level for their child's account

### 13.2 Data Minimization for Social Events

| Data | Stored | Not Stored |
|------|--------|------------|
| "Student X mastered concept Y" | StudentId, ConceptId, Timestamp | -- |
| "Student X sent encouragement to Student Y" | SenderId, RecipientId, MessageTemplateId, Timestamp | Message content (it's a template ID, not free text) |
| "Student X answered question in Q&A" | StudentId, AnswerHash, ConceptId, Timestamp | Full answer text (stored only during moderation, then replaced with hash) |
| "Student X participated in quiz battle" | StudentId, BattleId, Score, Timestamp | Individual answer details |
| "Student X is in study group Z" | StudentId, CohortId, JoinedAt | -- |

### 13.3 Anonymization Strategies

1. **k-anonymity for class stats**: Class-level statistics are only shown when k >= 10 (at least 10 students have data for that metric). This prevents identification through small-class deduction.

2. **Differential privacy for aggregate metrics**: When publishing school-level statistics, add Laplacian noise (epsilon = 1.0) to prevent identification of outlier students.

3. **Pseudonymization for Q&A**: Student-generated content (questions, answers, explanations) is displayed with the student's chosen display name (could be a pseudonym) plus their class membership ("a student in your class"). Real identity is visible only to the teacher.

4. **Time-shifting for feed events**: Exact timestamps in the activity feed are rounded to 5-minute intervals ("2:15 PM" not "2:17:43 PM") to prevent correlation attacks based on timing.

5. **Deletion cascades**: When a student account is deleted (existing `PendingDelete` flow), their social contributions are anonymized (author replaced with "former student") but not removed (preserving community content for others).

### 13.4 Consent Architecture

```
Consent Type          Required For                  Collected From
-----------           ------------                  ---------------
Account creation      All features                  Parent (under-18 in Israel)
Class membership      Social features               Teacher (school agent)
Social opt-in         Named achievements, Q&A       Student (13+) or Parent (under-13)
Peer tutoring         Tutoring matching              Student (13+)
Study buddy           Accountability partner         Both students (mutual consent)
Challenge             Friend challenges              Both students (mutual consent)
Data export           Social data export             Student or Parent
```

---

## 14. Teacher & Parent Dashboard Social Features

### 14.1 Teacher Dashboard: Social Management (Admin Portal)

The existing Vuexy Vue 3 admin dashboard at `src/admin/full-version/` should be extended with:

#### Class Social Overview Panel

```
+----------------------------------------------------------+
|  Class Social Activity                     [This Week]   |
|                                                           |
|  Social Health Score: 87/100  [healthy]                  |
|                                                           |
|  Active study groups: 6 (28 of 30 students in groups)    |
|  Questions asked this week: 15                           |
|  Peer explanations submitted: 8 (5 approved, 3 pending) |
|  Quiz battles played: 12                                 |
|  Active challenges: 2                                     |
|                                                           |
|  Socially isolated students: 2                            |
|  [Yosef K. -- no group, no interactions]                 |
|  [Noa M. -- opted out of all social features]            |
|                                                           |
|  [Create class challenge]  [Send recommendation]         |
+----------------------------------------------------------+
```

- **Social Health Score**: Composite metric of class social engagement (group participation rate, Q&A activity, challenge engagement, peer tutoring). Alerts teacher when score drops below 60.
- **Socially isolated students**: Flags students with zero social interactions in the past 2 weeks. Teacher can reach out individually.
- **Moderation queue**: Pending Q&A answers and explanations awaiting teacher approval.

#### Moderation Queue

```
+----------------------------------------------------------+
|  Moderation Queue (3 items)                               |
|                                                           |
|  1. Answer by Dana on "Quadratic Equations"              |
|     "To solve ax^2 + bx + c = 0, use the quadratic      |
|      formula: x = (-b +/- sqrt(b^2 - 4ac)) / 2a"       |
|     AI check: Accurate [green]                           |
|     [Approve]  [Edit & Approve]  [Reject]                |
|                                                           |
|  2. Explanation by Amit on "Derivatives"                  |
|     "Think of it as the slope of the line at that        |
|      point, like how fast you're going at an instant"    |
|     AI check: Accurate, good analogy [green]             |
|     [Approve]  [Edit & Approve]  [Reject]                |
|                                                           |
|  3. Question by Student on "Integration"                  |
|     [FLAGGED] Contains personal information              |
|     "My tutor Mrs. Levy says this should be..."          |
|     [Remove PII & Approve]  [Reject]                     |
+----------------------------------------------------------+
```

### 14.2 Parent Dashboard: Social Visibility

Building on the existing parent dashboard design (in `docs/stakeholder-experiences.md`):

#### Social Activity Summary

```
+----------------------------------------------------------+
|  [child name]'s Social Activity                           |
|                                                           |
|  Study groups: Member of "Math Crew" (4 members)         |
|  Peer tutoring: Helped 3 classmates this week            |
|  Challenges: 1 active (2/3 concepts mastered)            |
|  Study buddy: Connected with [first name]                |
|                                                           |
|  Social features enabled: [Yes]                          |
|  [Manage social settings]                                |
+----------------------------------------------------------+
```

- Parents see participation data (is my child socially engaged?) but NOT content (what did they write in Q&A)
- Parents can enable/disable social feature categories from this panel
- If the child is flagged as "socially isolated" by the teacher, the parent is NOT directly notified (this is teacher-to-student/parent-to-teacher, not automated alerts)

---

## 15. Moderation System Design

### 15.1 Three-Tier Moderation Architecture

```
Tier 1: AI Pre-Filter (automatic, < 100ms)
  |
  v  [Content passes AI check]
Tier 2: Community Reporting (reactive)
  |
  v  [Content reported by 3+ students]
Tier 3: Teacher/Admin Review (manual)
  |
  v  [Final decision]
```

### 15.2 Tier 1: AI Pre-Filter

All student-generated content (Q&A questions, answers, explanations, free-text messages for 13+) passes through an AI content filter before being visible to other students.

**Filter checks:**
1. **Profanity/hate speech**: Multi-language filter (Hebrew, Arabic, English) with transliteration detection (students may write Hebrew words in Latin characters)
2. **Personal information (PII)**: Detect and redact phone numbers, email addresses, home addresses, social media handles, full names of non-students
3. **Bullying indicators**: Detect targeting language, threats, exclusion language
4. **Academic integrity**: Detect copied text from external sources (plagiarism in explanations)
5. **Off-topic content**: Detect content unrelated to the concept/subject (social chatter in academic spaces)
6. **Accuracy check**: For Q&A answers and explanations, basic mathematical/scientific accuracy validation

**Implementation:**
- Use the existing LLM gateway (`LlmGatewayActor`) with a dedicated content moderation prompt
- Route moderation requests through the Tier 2 model (Haiku-class, per ADR-026) for cost efficiency
- Cache moderation decisions for repeated/similar content
- Latency target: < 500ms for content moderation (acceptable since content is async)

### 15.3 Tier 2: Community Reporting

```
[Report this content]

Why are you reporting this?

[ ] Incorrect information
[ ] Inappropriate language
[ ] Bullying or meanness
[ ] Not relevant to the topic
[ ] Other (describe: ___)

[Submit report]
```

- Any student can report any content
- 1 report: content flagged for teacher review within 24 hours
- 3 reports: content immediately hidden pending teacher review
- Reporter identity is never shown to the content author
- False reporting (reporting accurate, appropriate content repeatedly) is tracked; teacher receives notification of repeat false reporters

### 15.4 Tier 3: Teacher/Admin Review

Teachers see all flagged content in their moderation queue (Section 14.1). Actions available:

| Action | Effect |
|--------|--------|
| **Approve** | Content becomes visible; AI filter is trained on this as positive example |
| **Edit & Approve** | Teacher corrects content before publishing |
| **Reject** | Content removed; author notified with reason ("Your answer contained inaccurate information. See the correct approach here: [link]") |
| **Escalate** | Forward to school admin or Cena platform team (for serious safety concerns) |

### 15.5 Moderation SLA

| Content Type | Moderation Path | Target Visibility Time |
|-------------|-----------------|------------------------|
| Pre-set responses (under-13) | No moderation needed (templates) | Instant |
| Q&A questions | AI filter only (questions are inherently lower-risk) | < 1 minute |
| Q&A answers | AI filter + teacher queue | < 24 hours |
| Explanations | AI filter + teacher queue | < 24 hours |
| Free-text messages (13+) | AI filter (real-time) | < 1 minute |
| Reported content | AI re-check + teacher queue | Hidden immediately; resolved within 24 hours |

---

## 16. Implementation Roadmap

### Phase 1: Foundation (V1.0 -- Social Proof & Teacher Presence)

**Timeline: 4-6 weeks**

Features:
- Activity counters on concept screens ("X students practiced today", "Y% of class passed")
- Teacher presence indicator (online/offline)
- Teacher recommendations (concept endorsements from admin dashboard)
- Class code joining flow
- Achievement feed (class-level, aggregate only)

Actors needed:
- `ClassActor` (basic: student membership, aggregate stats)
- `SocialFeedActor` (basic: aggregate events only)

Events needed:
- `ClassJoined_V1`, `ClassChallengeCreated_V1`
- Extension of existing `ConceptMastered_V1` to forward to `ClassActor`

Mobile UI:
- Activity counter widgets on concept detail screen
- Teacher presence banner on home screen
- Class code input during onboarding

### Phase 2: Collaboration (V1.5 -- Groups & Challenges)

**Timeline: 4-6 weeks after Phase 1**

Features:
- Study groups (teacher-created and student-created for 13+)
- Class-wide challenges (teacher-created)
- Shared goals with class thermometer visualization
- "Students who did X also did Y" recommendations
- Named achievement feed (opt-in)

Actors needed:
- `CohortActor` (full implementation)
- `ClassActor` extension (challenges, goals)

Events needed:
- `CohortCreated_V1`, `CohortChallengeCompleted_V1`
- `ClassChallengeCompleted_V1`

Mobile UI:
- Study groups screen (new tab or section in Progress)
- Challenge cards on home screen
- Recommendation carousel after concept mastery

### Phase 3: Peer Learning (V2.0 -- Q&A, Tutoring, Battles)

**Timeline: 6-8 weeks after Phase 2**

Features:
- Concept-scoped Q&A forums (moderated)
- Student-generated explanations with teacher verification
- Peer tutoring matching
- Accountability partners
- Friend challenges (1v1 mastery races)
- Peer solution replays ("How others approached this")

Actors needed:
- `PeerTutoringActor`
- `SocialFeedActor` extension (peer content)
- Moderation pipeline (AI filter integration with `LlmGatewayActor`)

Events needed:
- `PeerExplanationSubmitted_V1`, `PeerExplanationRated_V1`
- `PeerTutoringMatched_V1`
- `PeerSolutionViewed_V1`

Mobile UI:
- Q&A section per concept
- Explanation submission flow
- Peer tutoring request/offer cards
- Study buddy pairing screen

### Phase 4: Multiplayer (V2.5 -- Real-Time & Competition)

**Timeline: 6-8 weeks after Phase 3**

Features:
- Real-time quiz battles (2-6 players)
- Turn-based math duels
- Cooperative class missions (knowledge graph territory)
- School vs school competitions
- Virtual study rooms

Actors needed:
- `QuizBattleCoordinator` (singleton)
- `QuizBattleActor` (ephemeral, per battle)
- `ClassActor` extension (cooperative missions)

Events needed:
- `QuizBattleStarted_V1`, `QuizBattleRoundCompleted_V1`, `QuizBattleEnded_V1`
- `CooperativeMissionProgress_V1`

Mobile UI:
- Battle lobby and matchmaking screen
- Real-time quiz battle interface
- Class mission map overlay on knowledge graph
- Study room ambient interface

### Phase 5: Advanced Social (V3.0)

**Timeline: Ongoing after V2.5**

Features:
- Mentorship program (grade 10-12 mentoring grade 7-9)
- Advanced social comparison widgets (opt-in, age-gated)
- Parent family goals and sibling leaderboard
- Social data analytics for teachers (social health score, isolation detection)
- Inter-school competition platform

---

## Summary of Key Principles

1. **Social features are an accelerator, not the product.** Cena's value is adaptive, personalized learning. Social features amplify motivation and retention; they do not replace the core learning engine.

2. **Private by default, social by choice.** Every social feature can be disabled by the student or parent. Cena works fully without social features enabled.

3. **Age-gated everything.** Social feature availability is strictly controlled by age group. Under-13 gets aggregate-only, teacher-mediated social. Over-16 gets the full suite (opt-in).

4. **Comparison that builds up, never tears down.** No public failure indicators. No ranking of struggling students. Social comparison is always framed as context for growth, never as judgment.

5. **Teacher as social architect.** The teacher creates the social environment: setting up classes, creating challenges, moderating content, endorsing concepts. Students participate within the teacher's scaffolded space.

6. **Async-first on mobile.** Real-time features (quiz battles) are bounded, specific, and short-lived. All other social interaction is asynchronous, fitting mobile usage patterns and enabling content moderation.

7. **Actor model alignment.** Every social feature maps to a specific actor with clear state boundaries, event sourcing, and cluster-scoped identity. No social state leaks between bounded contexts.

8. **Gamification of generosity.** Helping others (answering questions, peer tutoring, writing explanations) is rewarded with XP and badges, creating a virtuous cycle where social contribution is intrinsically and extrinsically motivated.
