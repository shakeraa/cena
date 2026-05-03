# P0 Regional & Strategic Tasks

> **Source:** extracted-features.md (N1, N6), eSelf deep dive
> **Sprint Goal:** Secure CENA's position in Israeli and MENA markets
> **Expected Impact:** Market entry, institutional credibility, first-mover advantage

---

## REG-001: Bagrut Exam Alignment
**ROI: 8.0 | Size: L (8-12 weeks) | No technical dependencies**

### Description
Align CENA's content and assessment with the Israeli Bagrut (matriculation) exam system. eSelf proved 3.94-point score improvement with just 25 minutes of practice. CENA's learning science stack (FSRS, knowledge graph, Socratic AI) should significantly exceed this.

### Acceptance Criteria

**Content Alignment:**
- [ ] Map Bagrut exam subjects to CENA knowledge graph nodes
- [ ] Priority subjects: Mathematics (3-5 units), English, Physics, Chemistry
- [ ] Each subject's syllabus mapped to prerequisite graph
- [ ] Practice questions aligned with Bagrut format and difficulty
- [ ] Bagrut-specific session mode: "Exam Prep" with timer and scoring rubric
- [ ] Past exam questions integrated (where publicly available)
- [ ] Scoring rubric matches official Bagrut marking scheme

**Study Plan:**
- [ ] "Bagrut Prep" onboarding flow: select subjects, exam date, target score
- [ ] AI generates personalized study plan (days until exam, weak areas)
- [ ] Daily recommended sessions based on plan
- [ ] Progress tracker: predicted score vs target score
- [ ] Weak area drill mode (FSRS prioritizes low-mastery Bagrut topics)

**Language Support:**
- [ ] Bagrut content available in Hebrew (primary) and Arabic
- [ ] English Bagrut content in English
- [ ] UI language independent of content language

### Subtasks
1. Research: obtain Bagrut syllabi for Math, English, Physics, Chemistry
2. Map syllabi to knowledge graph nodes (prerequisite dependencies)
3. Content creation: Bagrut-aligned practice questions (500+ per subject minimum)
4. "Exam Prep" session mode (timed, rubric-scored)
5. Bagrut study plan generator (subjects, exam date, target score -> daily plan)
6. Predicted score model (mastery % -> estimated Bagrut score)
7. Past exam integration (publicly available questions)
8. Hebrew + Arabic localization of Bagrut content
9. Beta test with 50+ Israeli students

---

## REG-002: CET Partnership Exploration
**ROI: 8.5 | Size: N/A (business development) | Depends on: REG-001**

### Description
CET (Center for Educational Technology) is Israel's largest K-12 textbook publisher, serving 3,400 schools and millions of students. eSelf's absorption into Kaltura may create an opening for a new AI education partner.

### Context from eSelf Deep Dive
- eSelf partnered with CET in April 2025 for Israel's first countrywide AI tutoring rollout
- Pilot: 2,031 students, 90.6% completion, 3.94-point avg improvement
- eSelf acquired by Kaltura (Nov 2025, $27M) — education focus may dilute
- CET may need a new AI partner if Kaltura deprioritizes education
- CET's MindCET is an EdTech innovation accelerator

### Action Items
- [ ] Research CET's current AI strategy post-Kaltura acquisition
- [ ] Identify key contacts at CET (CEO: Irit Touitou) and MindCET
- [ ] Prepare CENA capability deck emphasizing advantages over eSelf:
  - Knowledge graph (eSelf lacks)
  - FSRS spaced repetition (eSelf lacks)
  - Gamification system (eSelf lacks)
  - Multiple pedagogical methods (eSelf: single avatar mode)
  - Offline capability (eSelf: requires streaming)
- [ ] Explore MindCET accelerator application
- [ ] Investigate Israel Innovation Authority AI Education Sandbox (NIS 10M funding)
- [ ] Prepare pilot proposal: 100 students, 2 Bagrut subjects, 8-week duration
- [ ] Target: exceed eSelf's 3.94-point improvement benchmark

---

## REG-003: Israel Innovation Authority Sandbox Application
**ROI: 7.0 | Size: N/A (grant application) | No dependencies**

### Description
The Israel Innovation Authority launched an AI Education Sandbox with NIS 10M (~$3M) investment. Companies get access to real-world school pilots, regulatory support, and financial assistance.

### Requirements (from research)
- Must meet standards for: privacy, cybersecurity, data analysis, UX
- Access to real school environments for piloting
- Part of Israel's National AI Program

### Action Items
- [ ] Research current sandbox application timeline and requirements
- [ ] Prepare application materials:
  - Technology overview (FSRS, knowledge graph, AI tutor, gamification)
  - Privacy & security compliance documentation
  - Pilot proposal (target schools, subjects, metrics)
  - Team qualifications
- [ ] Submit application
- [ ] If accepted: plan pilot with sandbox school(s)
