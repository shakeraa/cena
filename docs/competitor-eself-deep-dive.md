# eSelf AI — Deep Dive Competitive Intelligence Report

> **Last updated:** 2026-03-26
> **Status:** eSelf was acquired by Kaltura (Nov 2025, $27M). eself.ai now redirects to Kaltura.

---

## 1. Company Overview

| Field | Detail |
|---|---|
| **Legal Name** | eSelf AI (eSelf.ai) |
| **Founded** | 2023 |
| **HQ** | Tel Aviv, Israel |
| **Website** | https://www.eself.ai/ (now redirects to https://corp.kaltura.com/blog/eself-is-now-kaltura/) |
| **Employees** | ~20 (15+ AI specialists in computer vision, NLP, and voice technologies) |
| **Status** | Acquired by Kaltura Inc. (NASDAQ: KLTR) for ~$27M in November 2025 |

### Founders

**Dr. Alan Bekker — CEO & Co-Founder**
- Ph.D. in Conversational AI
- 5 patents, 10+ academic papers
- Named among Europe's top 30 under 30 (2019)
- Previously co-founded Voca.ai (AI voice assistants for call centers; clients included American Express and AT&T)
- Voca acquired by Snap Inc. for ~$100M in 2020
- Served as Head of Conversational AI at Snap; led the team that built the "My AI" chatbot
- Originally studied to become a rabbi before pivoting to engineering, physics, and computer science
- LinkedIn: https://www.linkedin.com/in/alan-bekker/ (inferred)
- Now serving as CTO at Kaltura post-acquisition

**Eylon Shoshan — CTO & Co-Founder**
- M.Sc. in AI & NLP from Technion (Israel Institute of Technology)
- IDF Unit 8200 alumnus (Captain rank)
- Alumni of the Psagot Excellence Program
- Received 2020 Israeli Defense Award
- Co-built Voca.ai with Bekker prior to eSelf
- LinkedIn: https://www.linkedin.com/in/eylon-shoshan/
- Now at Kaltura post-acquisition

### Key Advisor
**Eyal Manor** — Former VP of Engineering at YouTube; former Chief Product & Engineering Officer at Twilio. Strategic angel investor.

---

## 2. Funding & Financial History

### Seed Round (December 11, 2024)
- **Amount:** $4.5 million
- **Lead Investor:** Explorer Investments
- **Participants:** Ridge Ventures, M-Fund Club, Meta AI Accelerator
- **Key Angels:** Eyal Manor (ex-YouTube VP, ex-Twilio CPO)

### Acquisition by Kaltura (November 10, 2025)
- **Total Deal Value:** ~$27 million
- **Deal Structure:**
  - $7.5M cash at closing
  - $12.5M cash over 3 years (contingent on performance milestones)
  - 4,690,025 shares of Kaltura common stock (~3% of outstanding shares), vesting over 3 years with retention holdback provisions for founders/key employees
- **Acquisition closed:** Q4 2025
- **Impact on Kaltura stock:** Jumped 30%+ at market open on announcement
- **Context:** Kaltura was valued at ~$300M at time of deal (down from $1.2B at 2021 IPO); Q3 2025 revenue was $43.9M

### Business Model (Pre-Acquisition)
- **B2B:** Enterprise clients (Christie's Portugal real estate, AGI Bank Brazil, DL Holdings Hong Kong)
- **B2G/B2B:** CET partnership for national education deployment
- **Planned B2C:** Monthly subscription for AI tutoring ("significantly cheaper than a private lesson" — human tutors cost 150-250 NIS/hour)
- **Revenue:** Not publicly disclosed; likely pre-revenue or early revenue given seed stage

### No Known Grants
No evidence found of Israel Innovation Authority grants, BIRD Foundation, or EU Horizon funding specifically to eSelf. However, CET and the broader ecosystem benefit from the government's NIS 10M AI education sandbox.

---

## 3. Technology

### Core Product: Conversational AI Avatar Platform
eSelf built a **unified engine** that processes speech, comprehension, and visual elements simultaneously, achieving **sub-one-second response times** for natural conversation.

### How It Differs from Competitors
> "Unlike other solutions that simply animate faces for voice responses, our platform is a complete visual comprehension engine." — Dr. Alan Bekker

The avatars can actively engage with visuals in real-time — showcasing property tours, educational content, or presentation slides during conversations. They don't just talk; they see and respond to on-screen content.

### Technology Stack (Inferred from Public Sources)
- **LLMs:** Fine-tuned Meta Llama models (among others); not building LLMs from scratch
- **Speech-to-Video Generation:** Proprietary
- **Low-Latency Speech Recognition:** Proprietary
- **Screen Understanding:** Visual comprehension engine that processes on-screen content
- **Real-time Voice Synthesis:** Proprietary
- **Video Generation:** On-demand image/video creation for lesson explanations
- **Languages:** 30+ supported
- **Avatars:** 80+ pre-built customizable avatars, or create custom "twin"
- **No-code Studio:** Self-service platform for configuring agent personality, knowledge base, and capabilities
- **Integrations:** Salesforce, Calendly, YouTube, and other business systems

### Education-Specific Features
- Customizable avatar appearance and personality (students can choose)
- Generates tailored visual explanations aligned with classroom content
- Real-time question-response during lessons
- Individual student adaptation over time (learns student strengths/weaknesses)
- Off-topic redirection (guides students back to material when conversations drift)
- Multilingual support (students can engage in language of choice)
- Grades performance using official Bagrut rubrics
- Provides instant personalized feedback and improvement strategies

### Patents
Alan Bekker holds **5 patents** (details not publicly enumerated, likely related to conversational AI and speech processing from Voca/Snap era).

### What eSelf Does NOT Have (Based on Available Evidence)
- **No knowledge graph architecture** — No evidence of structured curriculum knowledge graphs
- **No spaced repetition system** — No mention of SRS or memory optimization
- **No gamification** — No XP, streaks, leaderboards, or game mechanics mentioned
- **No methodology switching** — Single conversational avatar approach; no Socratic/direct instruction/worked example switching
- **No mastery-based progression** — No mention of prerequisite mapping or skill trees
- **No offline capability** — Requires real-time video streaming
- **No detailed learning analytics dashboard** — Focus is on avatar conversation, not analytics

---

## 4. The CET Partnership

### Timeline
| Date | Milestone |
|---|---|
| April 22, 2025 | Partnership announced (PR Newswire) |
| May 2025 | Pilot begins with 10,000 students (A/B testing) |
| May–June 2025 | English Oral Bagrut exam preparation module live |
| August 14, 2025 | Results published: 2,031 students, 8,000+ simulated exams |
| November 2025 | eSelf acquired by Kaltura; CET partnership status unclear |

### What the Pilot Covers
- **Initial subject:** English Oral Matriculation Exam (Bagrut) preparation
- **Planned expansion:** Hebrew language instruction, math, then all K-12 subjects
- **Target:** Eventually every K-12 student in Israel

### How It Works
1. Students access the AI tutor via mobile or desktop (no special training needed)
2. An interactive avatar conducts face-to-face virtual tutoring sessions
3. The avatar is integrated with CET's pedagogical content (textbooks, curricula)
4. Students practice, ask questions, and receive instant feedback
5. The system grades using official Bagrut rubrics
6. Sessions are personalized — avatar adapts to individual learning patterns

### Published Results (August 2025, MIT-Validated)
- **Participants:** 2,031 Israeli high school students
- **Completion rate:** 90.6% completed at least two sessions (1,841 students)
- **Total simulated exams:** 8,000+ in one month
- **Average score increase:** 3.94 points (from 78.23 to 82.17)
- **Statistical significance:** p < 0.001, medium effect size
- **Dose-response relationship:**
  - Under 15 minutes practice: +2.0 points
  - 15–30 minutes: +3.4 points
  - Over 30 minutes: +6.1 points
- **Evaluated by:** MIT Media Lab (Dr. Abel Sanchez, Director of MIT's Geospatial Data Center)

### Harvard's Involvement
- **Role:** Academic advisor (not developer or operator)
- **Person:** Victor Pereira, faculty in Teaching and Teaching Leadership program
- **Scope:** Helped shape educational framework and measures impact
- **Quote:** "AI should be integrated into education: not as a replacement for human educators, but as a powerful supplement."

### Cost Model
- **Pilot phase:** Free (first 10,000 students)
- **Post-pilot:** Monthly subscription planned
- **Who pays:** Unclear — CET may subsidize; Ministry of Education involvement not confirmed
- **Pricing target:** "Significantly cheaper than a private lesson"

### CET's Role vs. eSelf's Role
- **CET:** Content (textbooks, curricula, pedagogical framework), student access/distribution, institutional credibility
- **eSelf:** Technology (avatar platform, AI engine, real-time conversation), product development
- **Harvard:** Academic advisory and impact measurement
- **MIT Media Lab:** Independent evaluation of learning outcomes

---

## 5. CET (Center for Educational Technology)

### Organization Profile
| Field | Detail |
|---|---|
| **Type** | Non-profit organization (NGO) |
| **Founded** | 1971 |
| **HQ** | 16 Klausner St., Tel Aviv, Israel |
| **CEO** | Irit Touitou |
| **CFO** | Netta Avrahamov Bitan |
| **Board Chair** | Prof. Ami Moyal |
| **Revenue** | ~$161M USD (~234M NIS, 2018 data) |
| **Mission** | Advance Israeli education through innovative content and technology |

### What CET Does
- **Israel's largest K-12 textbook publisher** — leading position across all subjects and grade levels
- Develops printed and digital content used in **3,400 schools**
- Serves **millions of students** nationwide

### Digital Platforms
- **Ofek (Digital Backpack):** Israel's largest learning environment — 17 million student visits in 2022
- **Ivritna:** Online Hebrew tutoring platform for conversational practice; 3,500 high school students; adopted by Ministry of Education
- **BaGroup:** WhatsApp-based study groups; 44,000 students
- **Virtual High School:** Founded 2013
- **LMS (Learning Management System):** Launched 2010

### Professional Development
- 600 annual online training courses for educators
- 18,000 teachers and teacher trainees participate yearly

### Innovation Hub: MindCET
- Founded 2012 within CET
- EdTech Innovation Center — identifies, accelerates, and invests in EdTech startups
- Organizes Israel EdTech Week
- Recognized as a world leader in EdTech ecosystem development
- Partners with universities, R&D centers, and tech companies

### Relationship with Ministry of Education
- CET content is used in Israeli public schools nationwide
- Ivritna platform officially adopted by the Ministry of Education
- CET operates as an independent NGO but is deeply integrated with the national education system
- Works within the Ministry's curriculum framework

---

## 6. Competitive Analysis: eSelf vs. Cena

### Feature Comparison

| Feature | eSelf AI | Cena (Planned) |
|---|---|---|
| **Core Approach** | Conversational AI avatar (video) | Adaptive learning platform with knowledge graphs |
| **Knowledge Graph** | No evidence | Yes — structured curriculum mapping |
| **Methodology Switching** | Single mode (avatar conversation) | Multiple (Socratic, direct, worked examples, etc.) |
| **Spaced Repetition** | No evidence | Yes — SRS for long-term retention |
| **Gamification** | No evidence | Yes — XP, streaks, leaderboards |
| **Mastery-Based Progression** | No evidence | Yes — prerequisite mapping, skill trees |
| **Avatar/Conversational AI** | Core product — best-in-class | Not primary focus |
| **Visual Content Generation** | Yes — real-time image/video | Not specified |
| **Multilingual** | 30+ languages | Arabic, Hebrew focus |
| **Offline Support** | No (requires real-time video) | Planned (offline-sync protocol) |
| **Learning Analytics** | Basic (score tracking) | Detailed (engagement signals, mastery metrics) |
| **Content Integration** | CET textbooks | Country-agnostic curriculum design |
| **Target Exam** | Bagrut (Israel) | Bagrut (Israel), expandable |
| **Cognitive Load Management** | Not mentioned | Yes — explicit design consideration |
| **Enterprise/B2B** | Yes (real estate, banking, etc.) | Education-focused |

### eSelf's Strengths
1. **First-mover at national scale** — Only company to achieve countrywide AI tutoring deployment
2. **Avatar technology** — Best-in-class conversational video AI with sub-second latency
3. **CET partnership** — Access to Israel's largest K-12 content library and distribution network
4. **Harvard/MIT validation** — Published results with academic credibility
5. **Proven results** — Measurable score improvements in a real pilot with 2,000+ students
6. **Founder pedigree** — Bekker built Snap's My AI; Shoshan is ex-8200
7. **Kaltura backing** — Now part of a public company with enterprise distribution

### eSelf's Weaknesses / Cena's Opportunities
1. **No learning science architecture** — No knowledge graphs, no spaced repetition, no mastery tracking. The avatar is engaging but lacks the pedagogical backbone for deep, lasting learning.
2. **Modest score gains** — 3.94 points average improvement is statistically significant but practically modest. 25 minutes of any focused practice might yield similar results.
3. **Avatar-first, not learning-first** — eSelf is fundamentally a conversational AI company that pivoted to education. Education is one use case among many (sales, support, banking).
4. **Acquired and absorbed** — eSelf is now Kaltura. The education focus may dilute as Kaltura prioritizes enterprise video, marketing, sales, and corporate training.
5. **No offline capability** — Requires stable internet for real-time video streaming. Excludes students with poor connectivity.
6. **Subscription pricing concern** — If post-pilot pricing is subscription-based, it may not reach the students who need it most (those who can't afford private tutors).
7. **Single-mode interaction** — Only avatar conversation. No variety in pedagogical approach.
8. **No gamification or engagement hooks** — Relies entirely on avatar novelty. No streaks, XP, or game mechanics to sustain long-term engagement.
9. **CET dependency** — Content is CET's, not eSelf's. Without CET, the educational product is hollow.
10. **Narrow pilot scope** — Only English Oral Bagrut so far. Has not proven effectiveness across math, sciences, or other subjects.

### User Reviews / Public Feedback
No independent user reviews found. The only published data comes from eSelf/CET's own press releases, validated by MIT Media Lab. No criticism from teachers' unions or education policy experts found in public sources, though the Ynet article raises questions about whether avatars can replicate the personal connection essential to tutoring.

---

## 7. The Kaltura Acquisition — Implications

### What Happened
- Kaltura (NASDAQ: KLTR) signed definitive agreement on November 10, 2025
- Deal closed Q4 2025
- Alan Bekker joined Kaltura as CTO
- eself.ai now redirects to Kaltura

### Strategic Rationale
Kaltura wants to add "human-like capabilities — faces, eyes, mouths, ears" to make its AI agents conversational and expressive. Target sectors: education, media/telecom, e-commerce, financial services, healthcare, pharmaceuticals.

### Impact on CET Partnership
**Unclear.** No public statement found about CET partnership continuation under Kaltura. The education use case is just one of many for Kaltura. Risk: education gets deprioritized in favor of higher-margin enterprise use cases.

### What This Means for Cena
The acquisition creates both opportunity and risk:
- **Opportunity:** eSelf's education focus may dilute under Kaltura, creating a gap in the market
- **Opportunity:** CET may need a new AI education partner if Kaltura deprioritizes education
- **Risk:** Kaltura has enterprise distribution and resources that a startup cannot match
- **Risk:** The eSelf/CET pilot established a benchmark that any competitor must exceed

---

## 8. Israeli AI Education Ecosystem

### Government Initiatives
- **AI Education Sandbox:** Launched by Ministry of Education + Israel Innovation Authority
  - NIS 10M (~$3M) initial investment
  - Part of Israel's National AI Program
  - Companies get access to real-world school pilots, regulatory support, financial assistance
  - Must meet stringent standards for privacy, cybersecurity, data analysis, UX
  - 110,000+ teachers trained on AI tools; hundreds of thousands of students using smart tools daily

### Other Israeli AI Education Players

**MagniLearn**
- Founded by AI researchers from Hebrew University
- Based on 12+ years of NLP research by Prof. Ari Rappaport
- Product: AI-based personalized language learning (English as a foreign language)
- Technology: Linguistic engine using AI/NLP + neuroscience + cognitive principles
- Funding: $2.8M seed (OurCrowd's Labs/02, Reliance Industries India, Israel Innovation Authority)
- Clients: Schools in Israel, Korea, Japan
- Recognition: Selected by Microsoft Israel for AI for Good Acceleration Program
- Status: Active but smaller scale than eSelf

**StudyWise**
- Founded mid-2024 by Ori Nurieli (CEO), Chen Berger (COO), Guy Aronson (CTO)
- Product: AI-driven platform for creating, managing, and evaluating assignments/exams
- Growth: 150,000+ users in under a year (bootstrapped)
- Acquired by Collage AI (Boston, backed by Morningside) in October 2025 for $3M+
- StudyWise team joins Collage AI; becomes their global development center

**TinyTap**
- AI-generated interactive lessons for in-class or at-home learning
- $17.6M in funding
- Broader focus (not AI tutoring specifically)

### Market Context
- Israel has **163 active K-12 EdTech companies** (as of Jan 2026)
- Collectively raised **$83.9M** in funding
- 2025 was peak funding year: $13.8M+ raised

---

## 9. Key Takeaways for Cena Strategy

1. **eSelf proved the market exists** — A national-scale AI tutoring pilot in Israel is real, funded, and showing results. This validates Cena's thesis.

2. **The gap is in learning science** — eSelf's avatar is impressive technology, but lacks the pedagogical depth (knowledge graphs, SRS, mastery tracking, methodology switching) that Cena is building. The 3.94-point improvement is a floor, not a ceiling.

3. **CET is the kingmaker** — Any serious play in Israeli K-12 education needs to work with or around CET. They control the content and distribution.

4. **The Kaltura acquisition creates a window** — eSelf's absorption into a public enterprise video company may create a strategic opening in the education-specific AI tutoring space.

5. **Government sandbox is an opportunity** — The NIS 10M AI education sandbox provides funding, school access, and regulatory support for qualifying companies.

6. **Harvard/MIT validation sets the bar** — Any competitor needs comparable academic validation. Partnerships with credible research institutions are table stakes.

7. **Bagrut is the beachhead** — Both eSelf and the market are starting with Bagrut exam prep. This is the initial proving ground.

8. **Pricing sensitivity is real** — The promise of "free" or "cheaper than a private tutor" resonates. The business model must address affordability.

---

## Sources

- [PR Newswire: eSelf Partners with CET](https://www.prnewswire.com/il/news-releases/eself-partners-with-israels-center-for-educational-technology-to-lead-the-worlds-first-countrywide-rollout-of-ai-tutoring-302434204.html)
- [PR Newswire: eSelf Raises $4.5M Seed](https://www.prnewswire.com/il/news-releases/eself-raises-4-5m-seed-launches-platform-for-face-to-face-ai-conversational-agents-302328792.html)
- [PR Newswire: Study Shows Score Gains](https://www.prnewswire.com/news-releases/study-shows-thousands-of-students-see-test-score-gains-in-just-25-minutes-with-eselfs-ai-avatar-tutors-302530283.html)
- [Calcalist: Israel Rolls Out AI Tutors](https://www.calcalistech.com/ctechnews/article/r1tkoce1gg)
- [Calcalist: Kaltura Acquires eSelf](https://www.calcalistech.com/ctechnews/article/byzvwwllbx)
- [TechCrunch: Kaltura Acquires eSelf ($27M)](https://techcrunch.com/2025/11/10/kaltura-acquires-eself-founded-by-creator-of-snaps-ai-in-27m-deal/)
- [TechCrunch: Founder Who Built Snap's AI](https://techcrunch.com/2024/12/11/founder-who-built-snaps-ai-launches-a-snappy-new-take-on-video-chatbots/)
- [Ynet: AI Avatars from Harvard to Tutor Israeli Kids](https://www.ynetnews.com/business/article/skhub3rygx)
- [Jerusalem Post: Israel First Country AI Tutoring](https://www.jpost.com/israel-news/article-851072)
- [Times of Israel: Israel Rolls Out Pilot](https://www.timesofisrael.com/israel-rolls-out-pilot-for-students-to-learn-with-conversational-avatar-companions/)
- [Kaltura IR: Definitive Agreement to Acquire eSelf](https://investors.kaltura.com/news-releases/news-release-details/kaltura-signs-definitive-agreement-acquire-eselfai-provider-ai/)
- [Calcalist: Collage AI Acquires StudyWise](https://www.calcalistech.com/ctechnews/article/by00lbgbrxg)
- [TechCrunch: MagniLearn Raises $2.8M](https://techcrunch.com/2021/10/19/magnilearn-uses-ai-to-help-students-learn-new-languages-raises-2-8m/)
- [Israel Innovation Authority: AI Education Sandbox](https://innovationisrael.org.il/en/press_release/personalized-education-tech/)
- [CET Official Site](https://cet.ac.il/about-us/?lang=en)
- [MindCET](https://www.mindcet.org/en/)
- [Crunchbase: eSelf AI](https://www.crunchbase.com/organization/eself-ai)
- [ZoomInfo: eSelf AI](https://www.zoominfo.com/c/eself-ai/1325970005)
