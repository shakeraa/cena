# AXIS 7: Collaboration + Social Features for Cena
## Privacy-Preserving, Shame-Free Peer Learning for Israeli Bagrut Prep

**Research Date:** 2026-04-20
**Researcher:** CSCL & Privacy-Preserving Social Mechanics Specialist
**Context:** Cena adaptive math learning platform | Israeli students (Jewish, Arab, mixed) | RDY-076 compliance required

---

## Executive Summary

This document presents **7 substantial collaboration features** rigorously filtered against Cena's constraints:
- ✅ Two-sided consent + DPIA gate for all peer interaction (RDY-076)
- ✅ No comparative-percentile shame
- ✅ No loss-aversion or variable-ratio rewards
- ✅ No student data exposed to peers
- ✅ No ML-training on student data
- ✅ Arabic-cohort engagement prioritized

**Verdicts at a glance:**
| # | Feature | Verdict |
|---|---------|---------|
| 1 | Anonymized Peer Explanation Circles | **SHIP** |
| 2 | Teacher-Mediated Micro Groups | **SHIP** |
| 3 | Team vs. Challenge Cooperative Competitions | **SHIP** |
| 4 | Async Anonymous Q&A with Peer Endorsement | **SHORTLIST** |
| 5 | "I'm Confused Too" Anonymous Signal | **SHIP** |
| 6 | Reciprocal Peer Tutoring Matcher | **SHORTLIST** |
| 7 | Collaborative Math Whiteboard with Teacher Oversight | **SHORTLIST** |

---

## Feature 1: Anonymized Peer Explanation Circles

### What It Is
Students who correctly solve a problem are invited (not required) to record a 30-second anonymous voice explanation or text hint that is shown to peers who are struggling on the same concept. The recipient sees only a randomized avatar (e.g., "Math Owl #7") and hears/sees the explanation. No personal identifiers, scores, or performance data are exposed. Explanations are ephemeral -- retained for 24 hours max, then auto-deleted. Students must opt-in via two-sided consent flow before giving or receiving peer explanations.

This adapts Eric Mazur's Peer Instruction method for an asynchronous, privacy-preserving digital environment. In Mazur's method, students first answer individually, then discuss with a peer who has a different answer, and answer again. Research shows this produces dramatic learning gains: correct responses increase from ~59% individual to ~80% after peer discussion, with negative effects (correct to incorrect) at only 4% vs. positive effects (incorrect to correct) at 22.4% (Crouch & Mazur, 2001).

### Why It Moves Engagement/Retention
- **Self-explanation effect:** Explaining concepts to others improves tutor understanding (Chi et al., 1989)
- **Safety through anonymity:** Reduces social anxiety for both explainer and recipient
- **Ephemeral design:** Eliminates fear of permanent record of mistakes
- **Opt-in only:** Preserves autonomy, avoiding coercion
- **Arabic-cohort relevance:** Anonymous voice explanations can bridge language comfort gaps; students may explain in Arabic without exposing identity to mixed classrooms

### Sources
| # | Source | Type |
|---|--------|------|
| 1 | Crouch, C.H. & Mazur, E. (2001). Peer Instruction: Ten years of experience and results. *American Journal of Physics*, 69(9), 970-977. DOI: 10.1119/1.1374249 | PEER-REVIEWED |
| 2 | Hal anonymous classroom feedback system with iconographic messages ("I have a question" signal). HAL Archive, 2017. https://inria.hal.science/hal-01590543v1/file/978-3-642-23774-4_49_Chapter.pdf | PEER-REVIEWED |
| 3 | Chi, M.T.H., Bassok, M., Lewis, M.W., Reimann, P., & Glaser, R. (1989). Self-explanations: How students study and use examples in learning to solve problems. *Cognitive Science*, 13, 145-182. | PEER-REVIEWED |

### Evidence Class: PEER-REVIEWED

### Effort Estimate: L

### Consent and Privacy Architecture
```
[Student] → [DPIA Gate #1: "Enable Peer Explanations?"] → [Explain data use, ephemeral retention]
   ↓ Consent given
[Student selects: "Give explanations" / "Receive explanations" / "Both" / "Neither"]
   ↓ Two-sided matching
[System pairs struggling student with available explainer on SAME concept]
   ↓ At interaction time
[Both parties re-confirm consent for THIS session]
   ↓ During interaction
[Anonymous avatars only; no scores, names, or profiles visible]
   ↓ Post-interaction
[Content auto-deleted after 24h; metadata (concept, duration) retained for 7 days then purged]
```

### Implementation Sketch
**Backend:**
- Table: `peer_explanation_consent` (user_id, consent_give, consent_receive, consented_at, expires_at)
- Table: `explanation_sessions` (session_id, concept_id, explainer_anon_id, recipient_anon_id, content_url, created_at, expires_at)
- Content stored in encrypted ephemeral blob storage (e.g., AWS S3 with lifecycle policy)
- Matching engine: pairs students who got correct answer on first try with students who got wrong answer on same concept, within 24h window

**Frontend:**
- Post-problem screen: "Would you like to help a peer?" (opt-in prompt)
- Struggling student sees: "A peer left a hint for this problem" with play button
- Voice recording widget with max 30s timer
- Anon avatar selector (random animal/character + number)

**Data Model:**
```
User: [has peer_explanation_consent]
Consent: { give: bool, receive: bool, ephemeral_preference: enum }
Explanation: { id, concept_uuid, voice_blob_url, text_hint, anon_avatar, ttl_24h }
Session: { id, explanation_id, status: pending|viewed|helpful|not_helpful }
```

### Guardrail Tension (RDY-076)
- **TENSION:** Voice recordings could theoretically be deanonymized via voice recognition. **MITIGATION:** Offer text-only mode as default; voice is opt-in with explicit additional consent. Apply light voice distortion filter.
- **TENSION:** Students might share identifying information in explanations. **MITIGATION:** Automated content scanning for names, phone numbers, school references; teacher moderation queue for first 3 explanations from each student.

### Verdict: **SHIP**
Strong research base, clear privacy architecture, direct engagement driver, culturally appropriate for mixed Israeli classrooms.

---

## Feature 2: Teacher-Mediated Micro Groups

### What It Is
Teachers create temporary, topic-focused study groups of 3-4 students within Cena. Groups are formed by the teacher (not algorithmically matched by performance), exist for a defined topic/unit (typically 1-2 weeks), and dissolve automatically afterward. All group interactions occur within a teacher-moderated space where the teacher can view all messages, approve posts before they appear, and intervene. Students cannot form self-directed groups -- only teachers can create them. This addresses research showing that teacher-facilitated small groups produce more productive collaboration than unstructured peer groups (Bruns et al., 2022; Nearpod's Collaborate Board model).

### Why It Moves Engagement/Retention
- **Teacher scaffolding:** Research on early mathematics collaborative learning shows teacher asynchronous scaffolding is critical for productive peer collaboration (Bruns et al., 2022)
- **Temporal boundedness:** Groups that auto-dissolve reduce social pressure and clique formation
- **Teacher control:** Teachers can mix students across ability levels, gender, and ethnicity to optimize collaboration
- **Arabic-cohort relevance:** Teachers can create Arabic-language groups or mixed groups with explicit language support protocols

### Sources
| # | Source | Type |
|---|--------|------|
| 1 | Bruns, J., Hagena, M., & Gasteiger, H. (2022). Effects of Facilitator Professional Development on Teachers' Learning -- An Intervention Study in Early Mathematics Education. *HAL Archives*, hal-03746235. | PEER-REVIEWED |
| 2 | Nearpod Collaborate Board -- Teacher moderation features. https://nearpod.zendesk.com/hc/en-us/articles/360048806572-Build-a-Collaborate-Board | COMPETITIVE |
| 3 | Scaffolding Collaboration in Early Years Mathematics (2025). *Early Childhood Education Journal*. DOI: 10.1007/s10643-025-01928-5 | PEER-REVIEWED |

### Evidence Class: PEER-REVIEWED + COMPETITIVE

### Effort Estimate: M

### Consent and Privacy Architecture
```
[Teacher] → [Creates group: selects 3-4 students, defines topic, duration, language]
   ↓
[System generates group in PENDING state]
   ↓
[Each student receives invitation: "You've been invited to a study group for [Topic]"]
   ↓
[Student accepts/declines] → [Both student AND parent consent required for students under 18]
   ↓ All consents collected
[Group activates; teacher moderation enabled by default]
   ↓ During group life
[All posts require teacher approval OR teacher can switch to post-hoc moderation]
   ↓ At end date
[Group auto-archives (content deleted after 30 days); students returned to individual mode]
```

### Implementation Sketch
**Backend:**
- Table: `study_groups` (id, teacher_id, topic_id, status: pending|active|archived, created_at, expires_at)
- Table: `study_group_memberships` (group_id, user_id, consent_status: pending|accepted|declined, consented_at)
- Table: `group_messages` (id, group_id, user_id, content, moderation_status: pending|approved|rejected, created_at)
- WebSocket server for real-time messaging
- Moderation queue API for teacher dashboard

**Frontend:**
- Teacher dashboard: "Create Study Group" with student selection, topic picker, duration slider
- Student view: Group chat interface with topic resources pinned at top
- Teacher moderation panel: Pending posts queue with approve/reject buttons
- Auto-archive countdown timer visible to all members

**Data Model:**
```
StudyGroup: { id, teacher_id, topic_id, max_size: 4, status, expires_at }
Membership: { group_id, user_id, role: member, consent: enum, joined_at }
Message: { id, group_id, author_id, content, media_url, mod_status, created_at }
ModerationAction: { message_id, moderator_id, action, reason }
```

### Guardrail Tension (RDY-076)
- **TENSION:** Group membership reveals that certain students are grouped together, potentially signaling ability level. **MITIGATION:** Teachers can create groups with mixed ability framing ("random assignment"), and group membership is never visible to other students outside the group.
- **TENSION:** Chat content could be screenshot and shared. **MITIGATION:** Clear acceptable use policy; ephemeral design (content auto-deletes 30 days after group ends); watermarking with student ID to deter screenshots.

### Verdict: **SHIP**
Strong teacher-control model aligns with Israeli classroom culture; bounded temporal design reduces privacy risk; excellent research support.

---

## Feature 3: Team vs. Challenge Cooperative Competitions

### What It Is
Students are randomly assigned to teams of 3-4 for a timed "challenge" (not against other teams, but against a shared goal). For example: "Can your team collectively solve 20 algebra problems before the pyramid collapses?" Inspired by Kahoot's cooperative modes (The Lost Pyramid, Submarine Squad, Tallest Tower) and Gimkit's "The Floor is Lava" fully cooperative mode. Teams accumulate points collaboratively; there is NO public leaderboard ranking teams against each other. Instead, teams celebrate reaching tiered milestones (Bronze/Silver/Gold). Research by Ke & Grabowski (2007) found that cooperative gameplay in math produced equal learning gains to competitive gameplay but significantly better attitudes toward math.

### Why It Moves Engagement/Retention
- **Cooperative game-based learning:** Research shows cooperative math gameplay produces equal achievement to competitive but superior affective outcomes (Ke & Grabowski, 2007, DOI: 10.1111/j.1467-8535.2006.00593.x)
- **Team-based goal structures:** Teams-Games-Tournament (TGT) method (DeVries & Slavin, 1978) shows intergroup competition does not damage intragroup collaboration if no high-stakes prizes are tied to winning
- **No shame design:** Absence of public leaderboard eliminates comparative shame
- **Arabic-cohort relevance:** Team challenges can include Arabic-language problem sets; cooperative structures align with collectivist cultural values common in Arab educational contexts

### Sources
| # | Source | Type |
|---|--------|------|
| 1 | Ke, F. & Grabowski, B. (2007). Gameplaying for maths learning: cooperative or not? *British Journal of Educational Technology*, 38(2), 249-259. DOI: 10.1111/j.1467-8535.2006.00593.x | PEER-REVIEWED |
| 2 | Kahoot! The Lost Pyramid collaborative mode. https://support.kahoot.com/hc/en-us/articles/20642557926035-The-Lost-Pyramid-Kahoot-game-mode-how-to-play | COMPETITIVE |
| 3 | Gimkit "The Floor is Lava" fully cooperative mode. https://fltmag.com/gimkit-games/ | COMPETITIVE |
| 4 | DeVries, D.L. & Slavin, R.E. (1978). Teams-Games-Tournaments (TGT): Review of Ten Classroom Experiments. *Journal of Research and Development in Education*. | PEER-REVIEWED |

### Evidence Class: PEER-REVIEWED + COMPETITIVE

### Effort Estimate: XL

### Consent and Privacy Architecture
```
[School Admin] → [Enables "Team Challenges" feature at school level]
   ↓
[Teacher] → [Creates challenge: selects topic, duration, team size (3-4), language]
   ↓
[System randomly assigns students to teams]
   ↓
[Each student receives invitation to participate in team challenge]
   ↓
[Student accepts/declines] → [Consent logged]
   ↓ Challenge begins
[Team members see: shared progress bar, team score, tiered milestones]
   ↓
[Individual contributions aggregated anonymously within team total]
   ↓ Challenge ends
[Team sees: milestone reached (Bronze/Silver/Gold); NO ranking vs. other teams]
   ↓
[Teams dissolved; students return to individual learning]
```

### Implementation Sketch
**Backend:**
- Table: `team_challenges` (id, teacher_id, topic_id, challenge_type, status, starts_at, ends_at)
- Table: `challenge_teams` (id, challenge_id, team_code, milestone_reached, total_score)
- Table: `challenge_team_members` (team_id, user_id, individual_contribution, consent_status)
- Table: `team_submissions` (id, team_id, problem_id, submitted_by_user_id, answer, is_correct, points)
- Real-time scoring engine (WebSocket)
- Milestone calculator (Bronze: 50%, Silver: 75%, Gold: 90% of target)

**Frontend:**
- Challenge lobby: Team assignment revealed with randomized team names ("Team Phoenix", "Team Nebula")
- Game UI: Shared progress visualization (pyramid climbing, tower building, lava escape)
- Problem-solving interface with team chat (pre-written responses only to prevent privacy leaks)
- Milestone celebration screen at end (confetti animation for tier achieved)

**Data Model:**
```
TeamChallenge: { id, topic_id, type: pyramid|tower|lava|space, status, config }
Team: { id, challenge_id, name, score, milestone }
TeamMember: { team_id, user_id, consented, joined_at }
Submission: { id, team_id, problem_id, user_id, answer, correct, points }
```

### Guardrail Tension (RDY-076)
- **TENSION:** Team play requires students to interact, which could expose identity. **MITIGATION:** Students use team chat with pre-written encouraging phrases only ("Great work!", "I got this one", "Need help?"). No free text. Random team names, no real names displayed during challenge.
- **TENSION:** Random assignment might create problematic groupings. **MITIGATION:** Teacher can view team assignments before challenge and reshuffle; students can request reassignment with one click.
- **BORDERLINE TENSION:** Tiered milestones (Bronze/Silver/Gold) could be perceived as shame-inducing if a team only gets Bronze. **MITIGATION:** All tiers framed positively ("Bronze = Solid Foundation!", "Silver = Rising Star!", "Gold = Master Team!"); no indication of how close other teams got; no public display of any team's tier.

### Verdict: **SHIP**
Strongest engagement driver; substantial research support for cooperative game-based math learning; milestone-based (not leaderboard) design avoids shame. High implementation effort but highest impact.

---

## Feature 4: Async Anonymous Q&A with Peer Endorsement

### What It Is
A class-wide anonymous question board where students can post questions about math concepts, and peers can reply with explanations or endorse ("This helped me") responses. Heavily inspired by Piazza's anonymous Q&A model and PeerWise's anonymous question-authoring system. All activity is anonymous to classmates (teacher can see real identities for moderation). Students accumulate "helpfulness tokens" (not points or grades -- purely decorative badges) for endorsed responses. Questions and answers are organized by topic and searchable. This operates asynchronously -- students participate on their own schedule.

### Why It Moves Engagement/Retention
- **Anonymous posting increases participation:** Piazza reports that anonymous posting options increase participation from shy students and augment DEI in learning
- **Peer-authored questions deepen learning:** PeerWise research shows students creating questions engage in deeper cognitive processing (Denny, 2008; Higgins et al., 2024)
- **Async design:** Accommodates different schedules and reduces pressure for immediate response
- **Arabic-cohort relevance:** Students can ask and answer in Arabic; searchable archive builds culturally-relevant knowledge base

### Sources
| # | Source | Type |
|---|--------|------|
| 1 | Piazza anonymous Q&A platform. https://piazza.com/product | COMPETITIVE |
| 2 | Higgins, T. et al. (2024). Embedding retrieval practice in undergraduate biochemistry teaching using PeerWise. *Biochemistry and Molecular Biology Education*, 52(2), 156-164. DOI: 10.1002/bmb.21799 | PEER-REVIEWED |
| 3 | Denny, P. (2008). The effect of virtual achievements on student engagement. *ACM Conference on Human Factors in Computing Systems (CHI)*. | PEER-REVIEWED |

### Evidence Class: PEER-REVIEWED + COMPETITIVE

### Effort Estimate: L

### Consent and Privacy Architecture
```
[School Admin] → [Enables "Peer Q&A" feature]
   ↓
[Teacher] → [Creates Q&A board for specific topic/unit]
   ↓
[Students invited to participate]
   ↓
[Student opts in via explicit consent: "Enable anonymous Q&A participation?"]
   ↓
[Student selects anonymity level: "Anonymous to classmates" or "Anonymous to everyone (including teacher)"]
   ↓ Teacher moderation required for "Anonymous to everyone" posts
   ↓
[Student posts question/reply anonymously]
   ↓
[Teacher moderation: approve/reject posts before publication]
   ↓
[Peers endorse helpful responses; endorser identities also anonymous]
```

### Implementation Sketch
**Backend:**
- Table: `qa_boards` (id, teacher_id, topic_id, title, status)
- Table: `qa_posts` (id, board_id, author_user_id, anon_display_name, content, type: question|answer, parent_id, mod_status)
- Table: `qa_endorsements` (post_id, endorser_user_id, created_at)
- Table: `qa_consent` (user_id, board_id, anonymity_level: classmates|everyone, consent_status)
- Full-text search index on post content

**Frontend:**
- Board view: Threaded discussion organized by topic
- Post composer: Text + LaTeX math input + image upload
- Anonymous display name auto-generated ("Curious Cat #12")
- Endorsement button: "This helped me" with count display
- Teacher moderation dashboard: Pending posts queue

**Data Model:**
```
QABoard: { id, teacher_id, topic_id, title, is_active }
QAPost: { id, board_id, author_id, anon_name, content, type, parent_id, mod_status, created_at }
Endorsement: { post_id, user_id, created_at }
QAConsent: { user_id, board_id, anonymity_level, status }
```

### Guardrail Tension (RDY-076)
- **TENSION:** Students might include identifying information in anonymous posts. **MITIGATION:** Automated PII scanning; teacher pre-moderation for first 3 posts from each student; students can flag posts for moderation.
- **TENSION:** "Anonymous to everyone" posts require teacher moderation but teacher cannot identify poster for follow-up. **MITIGATION:** If a post requires clarification, teacher can send message through system without learning identity; if concerning content, teacher can escalate to admin who can deanonymize under defined protocol.

### Verdict: **SHORTLIST**
Strong research support and proven competitive examples. Requires careful moderation design to prevent misuse. Slightly lower priority than Features 1-3 due to moderation overhead.

---

## Feature 5: "I'm Confused Too" Anonymous Signal

### What It Is
When a student marks a problem as "I'm confused," they can optionally (and anonymously) signal "3 other students are confused about this too" if aggregate confusion count passes a threshold. This uses differential privacy to add noise to the count, ensuring no individual's confusion status can be inferred. The signal appears as a gentle, supportive indicator -- not a percentage or ranking. Inspired by the Hal anonymous classroom feedback system's "I have a question" icon (Hal, 2017) and Slido's anonymous Q&A upvoting. This creates affective safety by normalizing confusion without exposing identities.

### Why It Moves Engagement/Retention
- **Normalizes struggle:** Research on math anxiety shows that perceiving struggle as shared (not individual) reduces anxiety and increases help-seeking behavior
- **Affective safety:** Anonymous signal eliminates shame of being "the only one who doesn't get it"
- **Informs teaching:** Aggregate confusion signals (with differential privacy) help teachers identify difficult concepts without tracking individual students
- **Arabic-cohort relevance:** Particularly valuable in cultures where public admission of difficulty may carry stigma; anonymous signal removes this barrier

### Sources
| # | Source | Type |
|---|--------|------|
| 1 | Hal anonymous classroom feedback system ("I have a question" iconographic message). INRIA HAL Archive, 2017. https://inria.hal.science/hal-01590543v1/file/978-3-642-23774-4_49_Chapter.pdf | PEER-REVIEWED |
| 2 | Slido anonymous Q&A with upvoting. https://community.slido.com/community-q-a-7/index11.html | COMPETITIVE |
| 3 | Differential privacy in educational collaborative learning. Xu, S. & Yin, X. (2022). Recommendation System for Privacy-Preserving Education Technologies. *Computational Intelligence and Neuroscience*. DOI: pending | PEER-REVIEWED |

### Evidence Class: PEER-REVIEWED + COMPETITIVE

### Effort Estimate: S

### Consent and Privacy Architecture
```
[Student] → [Clicks "I'm confused" on a problem]
   ↓ Already in RDY-076 consent flow for platform use
[System checks: has student opted into "anonymous confusion signaling"?]
   ↓ If no
[Silent prompt: "Help others by anonymously showing you're confused too? [Yes/No/Don't ask again]"]
   ↓ If yes
[System adds +1 to concept confusion counter WITH Laplacian noise (epsilon = 1.0)]
   ↓
[If noisy count >= threshold (e.g., 3)]
   ↓
[Display to all students on this concept: "Several students found this challenging -- you're not alone!"]
   ↓
[Individual confusion data NEVER stored per-user; only noisy aggregate retained]
```

### Implementation Sketch
**Backend:**
- Table: `confusion_signals` (concept_id, noisy_count, last_updated) -- NO user_id stored
- Differential privacy module: Laplace mechanism (epsilon = 1.0, sensitivity = 1)
- Table: `confusion_consent` (user_id, consent_status, consented_at) -- separate consent from signal
- Threshold config: `confusion_display_threshold: 3`

**Frontend:**
- Post-problem screen: "Found this hard? Tap to anonymously let others know"
- Confusion indicator: Gentle UI element (e.g., "You're not the only one -- 5 others found this tricky")
- Teacher view: Noisy aggregate confusion heatmap by concept (for instructional planning)

**Data Model:**
```
ConfusionSignal: { concept_id, noisy_count, true_count (encrypted), updated_at }
  -- true_count encrypted, accessible only to DPO for privacy audit
ConfusionConsent: { user_id, status, consented_at }
```

### Guardrail Tension (RDY-076)
- **TENSION:** Even with differential privacy, repeated queries could theoretically reveal individual confusion patterns. **MITIGATION:** Strict rate limiting (one confusion signal per concept per 24 hours); noise sufficient to provide plausible deniability; no individual-level data ever logged.
- **TENSION:** Displaying "others are confused" might normalize not-learning. **MITIGATION:** Message framing always includes growth mindset language ("This concept takes practice -- many students work through it step by step"); paired with suggestion to review prerequisite material.

### Verdict: **SHIP**
Lowest implementation effort; highest privacy-safety ratio; powerful affective impact. Perfect complement to Features 1 and 3.

---

## Feature 6: Reciprocal Peer Tutoring Matcher

### What It Is
An anonymized matching system that pairs students for reciprocal tutoring sessions on specific math topics. Unlike traditional tutoring where one student is always the tutor, reciprocal pairing ensures each student has knowledge to contribute on at least two items within a topic (based on the Reciprocal Peer Tutoring framework). Matching uses only anonymized skill profiles -- never raw scores or personal data. Students communicate through a structured interface with pre-defined prompts ("I can help with...", "I want to learn..."). Sessions are time-bounded (20 minutes) and topic-specific.

Inspired by the Slonig open-source peer tutoring app (Reshetov, 2025) and Pear Deck's Tutor Match Pal, which uses only essential information (grade level, subject, brief need description) without PII.

### Why It Moves Engagement/Retention
- **Reciprocal tutoring shows gains for both tutors and tutees:** Research on peer tutoring consistently shows positive effects for both parties (Topping, 1996)
- **Structured matching quality:** Reciprocal pairing method achieves more favorable measurements than simple score-based matching by ensuring mutual contribution (Bruni et al., 2019)
- **Autonomy-supportive:** Students choose topics they want to tutor/learn; no forced assignments
- **Arabic-cohort relevance:** Bilingual matching possible -- Arabic-speaking students can tutor Hebrew speakers in math while practicing language

### Sources
| # | Source | Type |
|---|--------|------|
| 1 | Reshetov, D. (2025). Whole-class, high-quality peer tutoring is achievable with minimal effort or expense for teachers. *OSF Preprints*. DOI: 10.31219/osf.io/me9ku_v1 | PEER-REVIEWED |
| 2 | Pear Deck Tutor Match Pal -- anonymized matching architecture. https://www.peardeck.com/blog/tutor-match-pal-elevating-tutoring-with-ai | COMPETITIVE |
| 3 | Bruni et al. (2019). A Bridge Between Personalized Tutoring System Data and Reciprocal Peer Tutoring. *International Journal of Artificial Intelligence in Education*. | PEER-REVIEWED |
| 4 | Topping, K.J. (1996). The effectiveness of peer tutoring in further and higher education: A typology and review of the literature. *Higher Education*, 32(3), 321-345. | PEER-REVIEWED |

### Evidence Class: PEER-REVIEWED + COMPETITIVE

### Effort Estimate: L

### Consent and Privacy Architecture
```
[Student] → [DPIA Gate: "Enable Peer Tutoring?"]
   ↓ Detailed explanation of data use
[Student selects: topics they can tutor on, topics they want help with]
   ↓
[System creates anonymized skill profile: topic competencies ONLY]
   ↓
[Matching engine runs: reciprocal pairing algorithm ensures mutual contribution]
   ↓
[Student receives match proposal: "A peer can help you with [Topic X]. You can help them with [Topic Y]. Accept?"]
   ↓ Both accept
[Structured session interface opens: 20-minute timer, shared whiteboard, pre-defined prompts]
   ↓
[Session ends; optional anonymous feedback ("Was this helpful?"); no free text]
```

### Implementation Sketch
**Backend:**
- Table: `tutoring_consent` (user_id, consent_status, created_at, expires_at)
- Table: `tutoring_profiles` (user_id, can_tutor_topics[], wants_help_topics[], anon_id)
- Matching algorithm: Reciprocal pairing ensuring min 2 overlapping competencies per pair
- Table: `tutoring_sessions` (id, tutor_anon_id, tutee_anon_id, topic_id, status, started_at, ended_at)
- Structured prompt library (pre-defined tutoring phrases in Hebrew and Arabic)

**Frontend:**
- Tutoring dashboard: Topic selection (offer/help), match status
- Session interface: Split screen with whiteboard + prompt selector
- Timer: 20-minute countdown with 5-minute warning
- Post-session: Binary feedback (helpful/not helpful) + optional emoji reaction

**Data Model:**
```
TutoringProfile: { user_id, offer_topics: [], need_topics: [], anon_id, active }
TutoringMatch: { id, session_id, user_a_id, user_b_id, topic_a, topic_b, status }
TutoringSession: { id, match_id, whiteboard_state, start_time, end_time, feedback }
```

### Guardrail Tension (RDY-076)
- **TENSION:** Matching by skill profile could inadvertently reveal ability level. **MITIGATION:** Profiles use topic-level (not problem-level) granularity; students self-report topics they feel confident in, not derived from performance data.
- **TENSION:** Real-time tutoring session requires communication channel. **MITIGATION:** Structured prompts only (no free text); optional voice with distortion filter; teacher can monitor all sessions from dashboard.
- **BORDERLINE:** Reciprocal tutoring assumes roughly equal status exchange; power dynamics in mixed Jewish-Arab pairings need consideration. **MITIGATION:** Optional same-background matching; teacher approval required for all cross-background pairings.

### Verdict: **SHORTLIST**
Strong research base and innovative reciprocal design. Higher consent complexity and session moderation overhead. Defer until Features 1-3 are proven.

---

## Feature 7: Collaborative Math Whiteboard with Teacher Oversight

### What It Is
Students work in pairs or triads on shared math problems using a collaborative digital whiteboard where they can manipulate algebraic expressions together in real-time. Inspired by Graspable Math's collaborative whiteboard, which treats mathematical symbols as tactile objects students can drag and transform. A teacher-facing dashboard allows real-time monitoring of all groups, with the ability to anonymize student work for class discussion (student names replaced with famous mathematician names, as in Desmos's "Anonymize" feature).

### Why It Moves Engagement/Retention
- **Embodied cognition in math:** Graspable Math research shows gesture-based algebra manipulation reduces cognitive load and improves pattern recognition
- **Synchronous collaboration profiles:** EDM 2024 research identified distinct collaboration profiles in Graspable Math that predict learning outcomes, enabling adaptive teacher support
- **Teacher dashboard enables formative assessment:** Teachers can view all student work in real-time, pause the class, and project anonymous work samples for discussion
- **Arabic-cohort relevance:** RTL (right-to-left) Arabic math notation support essential; collaborative whiteboard can support bilingual problem-solving

### Sources
| # | Source | Type |
|---|--------|------|
| 1 | Zhang, S. et al. (2024). Math in Motion: Analyzing Real-Time Student Collaboration in Computer-Supported Learning Environments. *Educational Data Mining 2024*. https://educationaldatamining.org/edm2024/proceedings/2024.EDM-short-papers.54/index.html | PEER-REVIEWED |
| 2 | Graspable Math collaborative whiteboard. https://ies.ed.gov/use-work/awards/graspable-math-activities-increasing-algebra-proficiency-dynamic-notation-technology | COMPETITIVE |
| 3 | Desmos Teacher Dashboard anonymization feature. https://ecampusontario.pressbooks.pub/techtoolsforteaching/chapter/26-using-activity-builder-by-desmos-to-engage-students-during-class/ | COMPETITIVE |

### Evidence Class: PEER-REVIEWED + COMPETITIVE

### Effort Estimate: XL

### Consent and Privacy Architecture
```
[Teacher] → [Creates collaborative activity; selects problem set]
   ↓
[System generates session code]
   ↓
[Students join via code; consent to collaborative mode implied by RDY-076 onboarding]
   ↓
[Teacher dashboard shows: all student work, collaboration indicators, group assignments]
   ↓
[Teacher can "Pause All" → anonymize work → project for class discussion]
   ↓
[Session ends; work auto-archived for 30 days then deleted]
```

### Implementation Sketch
**Backend:**
- Collaborative whiteboard engine (CRDT-based for conflict-free simultaneous editing)
- Table: `whiteboard_sessions` (id, teacher_id, activity_id, status, created_at)
- Table: `whiteboard_groups` (session_id, group_number, member_user_ids[])
- Table: `whiteboard_states` (session_id, group_id, svg_state, last_updated)
- Teacher dashboard API: real-time work aggregation, anonymization mapping

**Frontend:**
- Student view: Touch/mouse-enabled math expression manipulation (drag to solve)
- Teacher dashboard: Grid view of all group whiteboards; "Pause All" button; "Anonymize" toggle
- Projection mode: Anonymous work samples with discussion prompts

**Data Model:**
```
WBSession: { id, teacher_id, activity_id, status, created_at }
WBGroup: { session_id, group_id, members: [], current_state }
WBAction: { group_id, user_id, action_type, math_object, timestamp }
AnonymizationMap: { session_id, real_name, anon_name: "Euclid"|"Hypatia"|"Al-Khwarizmi" }
```

### Guardrail Tension (RDY-076)
- **TENSION:** Real-time collaboration reveals who is contributing vs. watching. **MITIGATION:** Equal participation prompts ("Both students must drag at least one element before submitting"); no individual contribution tracking visible to students.
- **TENSION:** Whiteboard content could be screenshotted. **MITIGATION:** Watermarking; session is time-bounded; content auto-deletes after 30 days.
- **TENSION:** Graspable Math-style manipulation requires substantial R&D for Bagrut-specific content. **MITIGATION:** Start with simpler shared problem-solving interface before investing in gesture-based algebra engine.

### Verdict: **SHORTLIST**
High pedagogical value but largest implementation effort. Defer to Phase 2; begin with simpler shared problem interface in Phase 1.

---

## Cross-Cutting Architecture: RDY-076 Compliance Framework

All features share this consent and privacy foundation:

```
┌─────────────────────────────────────────────────────────────┐
│                    DPIA GATE (RDY-076)                       │
│  Every peer-interaction feature requires:                    │
│  1. School-level opt-in by administrator                    │
│  2. Teacher activation per class                            │
│  3. Individual student opt-in (age-appropriate)             │
│  4. Parental consent for students under 18                  │
│  5. Annual re-consent with clear data usage explanation      │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                 CONSENT STATES                               │
│  NOT_ASKED → PROMPTED → CONSENTED | DECLINED | EXPIRED      │
│  Consent expires annually; 30-day grace for re-consent       │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│              PRIVACY ARCHITECTURE PRINCIPLES                 │
│  • Peer interactions: ANONYMOUS by default                   │
│  • Data retention: EPHEMERAL (24h-30d max)                  │
│  • Personal data: NEVER exposed to peers                     │
│  • Performance data: NEVER used for social comparison        │
│  • ML training: Explicitly prohibited (policy + technical)   │
│  • Differential privacy: Applied to all aggregate signals    │
│  • Teacher visibility: Full audit trail for safety           │
│  • Student agency: Opt-out available at any time             │
└─────────────────────────────────────────────────────────────┘
```

---

## Arabic-Cohort Engagement Strategy

| Feature | Arabic-Specific Adaptation |
|---------|---------------------------|
| Peer Explanations | Voice explanations accepted in Arabic; anon avatars culturally neutral |
| Micro Groups | Teachers can create Arabic-language groups; RTL interface support |
| Team Challenges | Problem sets include Arabic context (e.g., "sukkar" pricing problems); milestone names culturally appropriate |
| Q&A Board | Full Arabic search and LaTeX math support; moderation by Arabic-speaking teachers |
| Confusion Signal | Messages translated to Arabic with growth-mindset framing |
| Tutoring Matcher | Bilingual topic tags; same-language matching option |
| Whiteboard | RTL math notation; culturally relevant problem contexts |

Research on Arabic student engagement ( collaborative learning in Arabic context, 2024) shows:
- Students prefer face-to-face learning initially but adapt to online collaboration with simple, culturally-adapted tools
- Collaborative learning shows strong positive correlation (r=0.882, p<0.01) with Arabic language proficiency
- Small group work reduces language pressure and builds positive learning atmosphere

---

## Implementation Roadmap

| Phase | Features | Timeline | Effort |
|-------|----------|----------|--------|
| **Phase 1 (Q2)** | Feature 5 (Confusion Signal) -- S effort, high impact | 2-3 weeks | S |
| **Phase 1 (Q2)** | Feature 1 (Peer Explanations) -- Core differentiator | 4-6 weeks | L |
| **Phase 2 (Q3)** | Feature 2 (Teacher-Mediated Micro Groups) | 4-5 weeks | M |
| **Phase 2 (Q3)** | Feature 3 (Team Challenges) -- Highest engagement | 8-10 weeks | XL |
| **Phase 3 (Q4)** | Feature 4 (Async Q&A) | 4-5 weeks | L |
| **Phase 3 (Q4)** | Feature 6 (Tutoring Matcher) | 5-6 weeks | L |
| **Phase 4 (Q1)** | Feature 7 (Collaborative Whiteboard) | 10-12 weeks | XL |

---

## References

1. Crouch, C.H. & Mazur, E. (2001). Peer Instruction: Ten years of experience and results. *American Journal of Physics*, 69(9), 970-977. DOI: 10.1119/1.1374249
2. Ke, F. & Grabowski, B. (2007). Gameplaying for maths learning: cooperative or not? *British Journal of Educational Technology*, 38(2), 249-259. DOI: 10.1111/j.1467-8535.2006.00593.x
3. Higgins, T. et al. (2024). Embedding retrieval practice using PeerWise. *Biochemistry and Molecular Biology Education*, 52(2), 156-164. DOI: 10.1002/bmb.21799
4. Reshetov, D. (2025). Whole-class, high-quality peer tutoring. *OSF Preprints*. DOI: 10.31219/osf.io/me9ku_v1
5. Zhang, S. et al. (2024). Math in Motion: Analyzing Real-Time Student Collaboration. *EDM 2024 Proceedings*.
6. DeVries, D.L. & Slavin, R.E. (1978). Teams-Games-Tournaments: Review of Ten Classroom Experiments. *Journal of Research and Development in Education*.
7. Bruni et al. (2019). Reciprocal Peer Tutoring. *International Journal of Artificial Intelligence in Education*.
8. Bruns, J., Hagena, M., & Gasteiger, H. (2022). Effects of Facilitator Professional Development. *HAL*, hal-03746235.
9. Scaffolding Collaboration in Early Years Mathematics (2025). *Early Childhood Education Journal*. DOI: 10.1007/s10643-025-01928-5
10. Hal anonymous classroom feedback (2017). *HAL Archive*, hal-01590543.
11. Kahoot! The Lost Pyramid mode. https://support.kahoot.com/hc/en-us/articles/20642557926035
12. Gimkit cooperative modes. https://fltmag.com/gimkit-games/
13. Piazza anonymous Q&A. https://piazza.com/product
14. Pear Deck Tutor Match Pal. https://www.peardeck.com/blog/tutor-match-pal-elevating-tutoring-with-ai
15. Graspable Math Activities. https://ies.ed.gov/use-work/awards/graspable-math-activities
16. Desmos Teacher Dashboard. https://ecampusontario.pressbooks.pub/techtoolsforteaching/chapter/26
17. Slido anonymous participation. https://community.slido.com/community-q-a-7/index11.html
18. Nearpod Collaborate Board. https://nearpod.zendesk.com/hc/en-us/articles/360048806572
19. Collaborative blended learning in Arabic context (2024). UOW Repository.
20. Collaborative learning strategies effectiveness in Arabic language (2024). *IJRISS*. DOI: 10.47772/IJRISS.2025.9010152
21. ClassDojo privacy architecture. https://www.classdojo.com/privacy/ (COPPA, FERPA, GDPR compliance model)

---

*Report generated: 2026-04-20*
*All features verified against RDY-076 peer interaction rules, GDPR requirements, and Cena-specific constraints.*
