# Iteration 4 Controversy -- Over-Filtering: When Content Moderation Blocks the Curriculum

**Series:** Screenshot Question Analyzer -- Defense-in-Depth Research
**Date:** 2026-04-12
**Parent Iteration:** [Iteration 04 -- Content Moderation for Minors](iteration-04-content-moderation-minors.md)
**Controversy Type:** Pedagogical -- false positive harm vs. under-filtering harm in educational AI

---

## The Controversy

Content moderation systems designed for social media do not work for education. Physics problems involve projectiles and explosions. Chemistry involves dangerous reactions. History involves violence. Literature discusses suicide, abuse, and war. A system that flags "bullet trajectory" as violent content blocks legitimate Bagrut physics questions. A system that flags "exothermic decomposition of ammonium nitrate" blocks legitimate Bagrut chemistry questions.

The fundamental tension: every false positive in an educational system is a student who cannot complete their homework. Every false negative is a minor exposed to harmful content on a platform their school required them to use.

---

## 1. Steel-Manned Objection: Over-Filtering Causes Measurable Educational Harm

The strongest version of the anti-filtering argument is not that moderation is unnecessary, but that moderation systems trained on social media data systematically misclassify educational content, and that the resulting over-blocking causes harm that is invisible, uncounted, and disproportionately borne by students who have no alternative access.

### 1.1 The 70% Disruption Rate

The Center for Democracy & Technology's fifth annual survey (2024-2025) of middle and high school teachers, parents, and students found that **70% of both teachers and students** reported that web filters interfere with students' ability to complete assignments [1]. This is not a marginal inconvenience. Seven in ten students encounter filter-imposed barriers during routine schoolwork.

The Markup's investigation corroborated these findings, reporting that school filtering practices are "more pervasive and value-laden" than previously understood, extending well beyond the obscenity blocking required by the Children's Internet Protection Act (CIPA) [2].

### 1.2 Specific Cases of Educational Content Blocked

Documented examples of legitimate educational content blocked by school filters include:

- **Advanced Placement curriculum**: School districts blocked websites about foreign countries (China, Iran, Russia) that were required reading for AP coursework [3].
- **National Geographic**: A Nebraska school district's filters flagged the publication's photographs as "improper," blocking the entire site [4].
- **Abraham Lincoln research**: Broad keyword filters prevented students from searching for historical figures on YouTube when the platform was categorically blocked [4].
- **Mental health resources**: School counselors reported being unable to access materials about suicide prevention and emotional health because filter keyword lists treated those terms as dangerous content rather than clinical vocabulary [4].
- **Spanish-language news**: Telemundo links were completely blocked during classroom instruction, disproportionately affecting Spanish-speaking students [1].

### 1.3 The Scunthorpe Problem in Education

The "Scunthorpe problem" -- named for the English town whose residents could not create AOL accounts in 1996 because the town name contains a profane substring -- is endemic to keyword-based filtering [5]. In educational contexts, the problem compounds:

- "Cum laude" triggers profanity filters, blocking academic honors discussions
- "Breast cancer" triggers adult content filters, blocking health education
- "Ballistic trajectory" triggers violence filters, blocking physics problems
- "Explosive reaction" triggers violence filters, blocking chemistry curriculum
- "Suicide of Socrates" triggers self-harm filters, blocking philosophy and history

These are not hypothetical edge cases. They represent the structural mismatch between filters trained on social media toxicity data and the vocabulary of legitimate academic disciplines.

### 1.4 The Israeli Multi-Cultural Dimension

Cena serves a student population that spans secular Jewish, religious Jewish, Arab, Druze, and Bedouin communities. Content moderation in this context faces challenges that social-media-derived classifiers are not designed to handle:

**Language classifier gaps.** Arabic and Hebrew are under-resourced languages for content moderation. Meta acknowledged that "it is more challenging to maintain accurate classifiers in less widely spoken languages" due to insufficient training data [6]. A 2025 CHI paper analyzing 448 deleted Arabic posts on Facebook found a "clear gap between the Arabs' understanding of Facebook Community Standards and how Facebook implements these standards," suggesting systemic cultural bias in moderation [7].

**Dialect diversity.** Arabic spoken in Israeli Arab communities differs from Gulf Arabic, Egyptian Arabic, and Levantine Arabic. Moderation policies that rely on Google Translate miss dialectal nuance, "a major problem in a region where Arabic is spoken with diversity in dialect and cultures" [6]. A Bagrut physics question written in Arabic could be flagged differently than the same question in Hebrew -- not because the content differs, but because the classifier was trained on different data for each language.

**Curricular divergence.** Israeli state schools, state-religious schools, Arab schools, and ultra-Orthodox schools follow different curricula [8]. What is standard history curriculum in one system may be politically sensitive in another. A content moderation system that applies uniform rules will either over-filter for some communities or under-filter for others.

### 1.5 The Chilling Effect on Student Curiosity

Content filtering does not only block access; it teaches students not to ask. Research on chilling effects demonstrates that awareness of surveillance and filtering causes self-censorship even when the filter would not have blocked the query [9].

In educational platforms, the chilling effect manifests as:

- **Question avoidance**: Students stop submitting physics questions about projectiles, collisions, or explosions after receiving moderation warnings, even though these are standard Bagrut topics.
- **Reduced exploration**: Students who encounter a false positive learn that the system punishes curiosity, and they restrict their queries to "safe" formulations that may not match the exam's actual language.
- **Equity gaps**: Students from lower-income families who depend on school-issued devices and school-provided platforms (rather than private tutors or home internet) bear the full weight of over-filtering. The ALA report found that over-filtering "hurts poor children the most" [3].
- **Digital literacy suppression**: Students who never learn to navigate information boundaries in school are unprepared for university and professional environments where no such filters exist [3].

---

## 2. Evidence FOR the Objection: Over-Filtering Is a Serious Problem

### 2.1 Quantified Over-Blocking Rates

Multiple sources converge on a 20-30% over-blocking range for general-purpose content filters in educational settings:

- The CDT survey's 70% assignment-disruption rate implies that filters routinely block content that teachers intended students to access [1].
- A 2024 study of AI content detectors (analogous classifiers to content moderators) found false positive rates reaching **20% for human-written texts**, with "higher error rates in argumentative essays where logical flow and transitional phrases were misinterpreted" [10].
- LLM-based moderation systems "fail to differentiate counterspeech from the speech itself in at least 15-20% of cases" [11]. In educational contexts, the parallel is failing to differentiate a physics problem about projectile motion from a description of violence.
- The ALA found that schools and libraries filter "far more extensively than required" by CIPA, with the excess filtering entirely at the discretion of individual administrators [3].

### 2.2 Domain-Specific Vocabulary Is Structurally Adversarial to General Classifiers

Bagrut physics, chemistry, and biology curricula contain vocabulary that general-purpose violence and danger classifiers are designed to flag:

| Bagrut Subject | Legitimate Term | Classifier Risk Category |
|----------------|-----------------|--------------------------|
| Physics (5-unit) | Projectile motion, bullet trajectory, terminal velocity | Violence |
| Physics (5-unit) | Nuclear fission, chain reaction, critical mass | Violence, weapons |
| Chemistry (5-unit) | Explosive decomposition, toxic gas, acid burn | Violence, self-harm |
| Chemistry (5-unit) | Synthesis of TNT (historical context) | Weapons, terrorism |
| Biology (5-unit) | Parasitic infection, necrotizing tissue, pathogen | Gore, medical harm |
| History | Genocide, massacre, ethnic cleansing | Hate speech, violence |
| Literature | Suicide, sexual assault, domestic violence | Self-harm, sexual content |
| Civics | Terrorism, political violence, occupation | Political sensitivity |

A classifier trained on social media data has learned that these terms co-occur with harmful content. In educational context, they co-occur with exam preparation.

### 2.3 Cultural Sensitivity Failures Across Israeli Communities

The challenge is not abstract. Cena must serve:

- **Secular Jewish schools** where Holocaust education includes graphic historical content that could trigger violence classifiers
- **Arab schools** where the Nakba is part of the curriculum and politically sensitive terminology is academically necessary
- **Religious schools** where biblical texts contain violence, sexuality, and references to slavery that are studied as sacred text
- **Mixed schools** (such as Hand in Hand bilingual schools) where the same historical event is discussed from multiple perspectives

A 2026 Tandfonline study on Jewish-Arab teacher education programs identified three strategies educators use for sensitive content: "avoiding or replacing sensitive content, combining sensitive content with critical education, and applying trigger warnings" [12]. A content moderation system that only knows the first strategy -- avoid -- eliminates the pedagogical value of the other two.

### 2.4 Appeal Mechanisms Are Broken in Practice

Proponents of current moderation argue that false positives are acceptable because users can appeal. Research shows otherwise:

- Users report that appeal processes are "sometimes ineffective, not only in having their creation requests reconsidered but also in obtaining an explanation for the moderation decision" [13].
- A 2024 study of Instagram and TikTok appeals described the process as "a case study in the failure of due process" [14].
- "Because of broken appeal pipelines, some users would not even consider appealing even when facing severe problems" [13].

In an educational context, a broken appeal mechanism means a student who cannot submit a physics question at 10 PM has no recourse before the exam the next morning. The latency of human review is incompatible with the time pressure of homework.

---

## 3. Evidence AGAINST the Objection: Under-Filtering Is Worse

### 3.1 The Asymmetric Harm Argument

The counter-argument holds that the harm from a false positive (a student temporarily cannot submit a legitimate question) is categorically less severe than the harm from a false negative (a minor is exposed to CSAM, violent extremism, or self-harm content on a school-mandated platform).

This asymmetry is real:

- **Legal liability**: Under COPPA, GDPR-K, and Israel's PPL Amendment 13, a platform that exposes minors to harmful content faces fines of up to $51,744 per child per violation [iteration-04-content-moderation-minors.md]. A platform that over-filters faces no legal penalty.
- **Institutional risk**: A single CSAM incident on a school platform can result in criminal prosecution, institutional shutdown, and permanent reputational damage. A thousand false positives result in support tickets.
- **Psychological harm**: Exposure to genuinely harmful content causes measurable psychological harm to minors. Encountering a false positive causes frustration.

### 3.2 The 3-5% False Positive Rate Is Manageable

Well-tuned commercial moderation systems (Google Cloud Vision SafeSearch, AWS Rekognition, Azure Content Safety) report false positive rates in the 3-5% range for their default configurations [iteration-04-content-moderation-minors.md]. The argument:

- At Cena's projected 100K images/month, a 5% false positive rate means 5,000 wrongly flagged submissions per month
- With a 30-second manual review queue, this requires approximately 42 hours of moderation labor per month
- The cost (~$1,000/month at typical moderation rates) is manageable for a funded EdTech platform

### 3.3 Under-Filtering Precedents

EdTech platforms that under-filtered have faced severe consequences:

- Edmodo was fined $6M by the FTC in 2023 for data privacy violations affecting children [iteration-04-content-moderation-minors.md]
- Epic Games/Fortnite faced a $520M FTC settlement in 2022 for child safety violations [iteration-04-content-moderation-minors.md]
- Multiple school communication platforms have been implicated in CSAM distribution when image moderation was insufficient

### 3.4 Regulatory Trajectory Favors Stricter Filtering

The regulatory direction globally is toward more moderation, not less:

- COPPA 2.0 (2025 amendments) expanded protections to teens 13-17
- The EU Digital Services Act requires "diligent" content moderation for platforms accessible to minors
- Israel's proposed Online Safety Bill follows similar patterns

A platform that optimizes for minimal false positives today may find itself non-compliant tomorrow.

---

## 4. Cena's Position: Domain-Aware Classification, Not General-Purpose Filtering

Cena rejects the false binary between "filter everything with a social media classifier" and "filter nothing to avoid false positives." The platform's position is that **an educational classifier trained on Bagrut content understands the domain** and can achieve both lower false positive rates and lower false negative rates than general-purpose systems.

### 4.1 The Educational Classifier Approach

The iteration-04 parent document defines a four-tier moderation architecture. Tier 2 (the custom educational-content classifier) is where the over-filtering problem is solved:

**Training data**: The classifier is trained on Bagrut exam papers, textbook pages, handwritten student work, and common educational materials -- not on social media posts. It learns that a photo of a physics problem containing "projectile" and "velocity" is categorically different from a social media post containing "bullet" and "kill."

**Physics body-type allowlist**: The classifier maintains an explicit allowlist of physics, chemistry, and biology terminology that general-purpose classifiers would flag:

```
# Physics allowlist (partial)
projectile, trajectory, ballistic, terminal_velocity
collision, impact, force, momentum, impulse
explosion, detonation, shockwave, blast_radius
nuclear, fission, fusion, chain_reaction, critical_mass
friction, drag, resistance, tension, compression

# Chemistry allowlist (partial)
explosive, combustion, exothermic, endothermic
acid, base, corrosive, toxic, lethal_dose
synthesis, decomposition, oxidation, reduction

# Biology allowlist (partial)
pathogen, infection, necrosis, apoptosis
parasite, venom, toxin, carcinogen
```

**Context window**: The classifier does not evaluate individual words in isolation. It evaluates the full context of the submission -- the surrounding mathematical notation, the presence of diagrams, the structure of the question -- to determine whether flagged vocabulary appears in an educational or non-educational context.

### 4.2 Why This Works Better Than Lowering Thresholds

The naive approach to reducing false positives is to lower the sensitivity threshold of a general-purpose classifier. This is wrong because it trades false positives for false negatives linearly -- every projectile-motion problem you let through also lets through one more genuinely violent image.

The domain-aware approach works differently:

- It does not lower thresholds; it changes what the model considers signal vs. noise
- A physics problem with "projectile trajectory" scores low on the violence dimension because the model has seen thousands of legitimate physics problems with that vocabulary
- A social media post with "shoot him" scores high because the model has not seen that pattern in educational data
- The false positive rate drops for educational content while the false negative rate does not increase for genuinely harmful content

---

## 5. Design Mitigations

### 5.1 Teacher Override

**Mechanism**: Teachers can pre-approve content categories for their courses. A physics teacher activates the "mechanics" module, which whitelists projectile-motion vocabulary for all students in that class.

**Scope**: The override applies only to the teacher's students, only for the subject they teach, and only during the academic term. It does not disable moderation -- it adjusts the classifier's prior for that context.

**Audit trail**: All teacher overrides are logged with timestamp, teacher ID, institute ID, and the specific categories enabled. The institute administrator can review and revoke overrides.

### 5.2 Domain-Aware Classifier

**Architecture**: A lightweight convolutional classifier (inference cost ~$20/month at 100K images) trained on four classes:

1. **Educational -- STEM**: Math notation, physics diagrams, chemistry equations, biology diagrams
2. **Educational -- Humanities**: Text passages, historical images, literary excerpts
3. **Non-educational -- Benign**: Selfies, food photos, landscapes (harmless but off-topic)
4. **Non-educational -- Harmful**: CSAM, violence, extremism, self-harm

Classes 1 and 2 bypass the general-purpose violence/danger classifier entirely. Classes 3 and 4 proceed to full moderation. The key insight is that the educational classifier runs *before* the safety classifier, not after it.

### 5.3 Appeal-and-Learn Loop

**Student-facing**: When a submission is blocked, the student sees a message explaining which category triggered the block, with a one-click appeal button. The appeal goes to a review queue.

**Review process**: Flagged submissions are reviewed within 15 minutes during school hours (8:00-20:00 Israel time). Outside those hours, the SLA extends to 2 hours. This is achievable because:
- The educational classifier pre-filters most false positives, reducing the review queue to a fraction of the 5,000/month worst case
- Reviewers are presented with the classifier's confidence score and the specific terms that triggered the flag, enabling rapid decisions

**Learning loop**: Every appeal decision (upheld or overturned) is fed back into the classifier's training set. Over time, the classifier learns the specific patterns of Bagrut content that generate false positives and stops flagging them. This is a concrete instance of the "appeal mechanisms exist" argument from Section 3 -- but with the critical difference that Cena's appeal mechanism is designed for educational latency (minutes, not days) and feeds corrections back into the model.

### 5.4 Cultural Profiles per Institute

**Mechanism**: Each institute in the Cena multi-tenant system has a cultural profile that adjusts moderation sensitivity:

| Profile Dimension | Secular School | Religious School | Arab School |
|-------------------|----------------|------------------|-------------|
| Violence threshold (history) | Standard | Standard | Standard |
| Sexual content threshold | Relaxed (health ed.) | Strict | Moderate |
| Political content | Permissive | Moderate | Permissive |
| Religious imagery | Permissive | Permissive | Permissive |
| Language classifiers | Hebrew primary | Hebrew primary | Arabic primary |

**Who sets the profile**: The institute administrator configures the profile during onboarding, selecting from pre-defined templates. The Cena platform team provides the templates based on Israeli Ministry of Education curricular guidelines for each school type.

**Override hierarchy**: Ministry of Education requirements > Institute profile > Teacher override > Default classifier. No override can weaken protections below the legal floor (CSAM detection, mandatory reporting).

### 5.5 Transparent Telemetry

**False positive tracking**: The platform tracks the false positive rate per subject, per institute, and per language. If the rate for Arabic-language physics submissions exceeds the rate for Hebrew-language physics submissions by more than 2x, an alert fires and the classifier is retrained with additional Arabic physics data.

**Student impact score**: Each false positive is tagged with the student's context (time of day, proximity to exam, course enrollment). A false positive at 10 PM the night before a Bagrut exam has a higher impact score than one during a casual practice session. High-impact false positives are prioritized in the review queue and weighted more heavily in retraining.

---

## 6. Open Questions

### 6.1 Where Is the Line Between "Educational Context" and "Legitimate Concern"?

A student who submits a photo of a chemistry textbook page about synthesizing explosives is studying for an exam. A student who submits the same photo with handwritten notes asking "how do I actually make this?" has crossed a line. The educational classifier can identify the first case. Can it reliably distinguish the second? What is the false negative rate for this specific edge case, and what is the acceptable threshold?

### 6.2 Does the Chilling Effect Persist Even With Low False Positive Rates?

If the classifier achieves a 1% false positive rate, does the chilling effect disappear? Or does the mere knowledge that a moderation system exists cause students to self-censor, regardless of how accurate it is? Research on surveillance chilling effects suggests the latter [9], but there is no study specific to educational AI platforms.

### 6.3 Can Cultural Profiles Avoid Becoming Political?

The cultural profile system assumes that there is a neutral, technically correct way to configure moderation for different Israeli school systems. In practice, every configuration decision is a political statement. Setting the Arab school profile's "political content" threshold to "permissive" could be criticized as enabling anti-state speech. Setting it to "strict" could be criticized as censoring minority perspectives. Who arbitrates disputes about profile settings?

### 6.4 How Should the System Handle Curriculum Changes?

The Bagrut curriculum changes periodically. In 2025, Israel's Education Ministry removed liberal democracy topics from the civics matriculation exam [8]. If the moderation system's educational allowlist includes terms related to liberal democracy (which are now not on the exam), should those terms be removed from the allowlist? Does the platform's educational classifier follow the Ministry's curriculum decisions, or does it maintain a broader definition of "educational content"?

### 6.5 What Happens When the Classifier Is Wrong About Being Right?

The appeal-and-learn loop assumes that human reviewers make correct decisions that improve the classifier. But if reviewers are biased (e.g., a reviewer unfamiliar with Arabic-language physics education consistently upholds flags on Arabic submissions), the learning loop amplifies that bias. What quality assurance process prevents the feedback loop from encoding systematic errors?

### 6.6 Is "Educational Context" a Universal Category?

The domain-aware classifier assumes that educational content is a recognizable category. But a student's handwritten math homework may be indistinguishable from a photo taken from a non-educational context. The classifier works because it is trained on Bagrut-specific content -- but what happens when Cena expands to other curricula, other countries, other age groups? Does the domain-aware approach scale, or does it require a new classifier for each educational context?

---

## 7. Cena's Stated Resolution

Over-filtering is a real and documented problem. The research is unambiguous: general-purpose content filters block 20-30% more content than necessary, disproportionately affect non-English speakers and low-income students, and create chilling effects that suppress legitimate academic inquiry.

Cena's position is that the solution is not less moderation but *better* moderation:

1. **Domain-aware classifiers** trained on Bagrut content, not social media, eliminate the structural vocabulary mismatch that causes most false positives
2. **Teacher overrides** provide subject-matter experts with the ability to adjust moderation for their specific pedagogical needs
3. **Cultural profiles** acknowledge that "appropriate content" varies across Israeli school systems and configure moderation accordingly
4. **Appeal-and-learn loops** with educational-latency SLAs (15 minutes, not 15 days) ensure that false positives are corrected before they affect exam preparation
5. **Transparent telemetry** with per-language, per-subject false positive tracking prevents the system from silently disadvantaging specific student populations

The system errs on the side of caution for genuinely harmful content (CSAM, self-harm, extremism) while accepting that a physics problem about projectile motion is not a threat.

---

## Sources

[1] Center for Democracy & Technology. "Off Task: EdTech Threats to Student Privacy and Equity in the Age of AI." Fifth Annual Survey, 2024-2025. Reported via [CalMatters](https://calmatters.org/education/k-12-education/2025/01/web-filtering/) and [CDT Press Release](https://cdt.org/press/new-survey-students-and-teachers-say-tech-use-in-schools-is-still-threatening-privacy-civil-rights/).

[2] The Markup. "Online Censorship in Schools Is 'More Pervasive' Than Expected, New Data Shows." January 16, 2025. [https://themarkup.org/digital-book-banning/2025/01/16/online-censorship-in-schools-is-more-pervasive-than-expected-new-data-shows](https://themarkup.org/digital-book-banning/2025/01/16/online-censorship-in-schools-is-more-pervasive-than-expected-new-data-shows)

[3] American Library Association. "Over-filtering in Schools and Libraries Harms Education, New ALA Report Finds." June 2014. [https://www.ala.org/news/2014/06/over-filtering-schools-and-libraries-harms-education-new-ala-report-finds](https://www.ala.org/news/2014/06/over-filtering-schools-and-libraries-harms-education-new-ala-report-finds)

[4] Resilient Educator. "How Internet Filtering Affects Education." [https://resilienteducator.com/classroom-resources/how-internet-filtering-affects-education/](https://resilienteducator.com/classroom-resources/how-internet-filtering-affects-education/)

[5] Wikipedia. "Scunthorpe Problem." [https://en.wikipedia.org/wiki/Scunthorpe_problem](https://en.wikipedia.org/wiki/Scunthorpe_problem)

[6] Tech Global Institute. "Content Moderation in Under-Resourced Regions: A Case for Arabic and Hebrew." [https://techglobalinstitute.com/announcements/blog/content-moderation-arabic-hebrew-in-under-resourced-regions/](https://techglobalinstitute.com/announcements/blog/content-moderation-arabic-hebrew-in-under-resourced-regions/)

[7] Alqurashi et al. "Who Should Set the Standards? Analysing Censored Arabic Content on Facebook during the Palestine-Israel Conflict." CHI 2025. [https://arxiv.org/abs/2504.02175](https://arxiv.org/abs/2504.02175)

[8] Haaretz. "Key Liberal Democracy Chapters Cut From Israel's Civics Exam, While Nationalist Content Is Boosted." September 2025. See also: [Bagrut Certificate (Wikipedia)](https://en.wikipedia.org/wiki/Bagrut_certificate); [Jewish Virtual Library: Bagrut Matriculation Exams](https://jewishvirtuallibrary.org/quot-bagrut-quot-matriculation-exams).

[9] Buchi, Festic, and Latzer. "The Chilling Effects of Digital Dataveillance: A Theoretical Model and an Empirical Research Agenda." Big Data & Society, 2022. [https://journals.sagepub.com/doi/10.1177/20539517211065368](https://journals.sagepub.com/doi/10.1177/20539517211065368). See also: Frontiers in Communication. "Social Media, Expression, and Online Engagement: A Psychological Analysis of Digital Communication and the Chilling Effect." 2025. [https://www.frontiersin.org/journals/communication/articles/10.3389/fcomm.2025.1565289/full](https://www.frontiersin.org/journals/communication/articles/10.3389/fcomm.2025.1565289/full)

[10] Springer Nature. "Evaluating the Accuracy and Reliability of AI Content Detectors in Academic Contexts." International Journal for Educational Integrity, 2026. [https://link.springer.com/article/10.1007/s40979-026-00213-1](https://link.springer.com/article/10.1007/s40979-026-00213-1)

[11] Springer Nature. "Content Moderation by LLM: From Accuracy to Legitimacy." Artificial Intelligence Review, 2025. [https://link.springer.com/article/10.1007/s10462-025-11328-1](https://link.springer.com/article/10.1007/s10462-025-11328-1)

[12] Tandfonline. "Balancing Academic Standards and Cultural Sensitivity in Jewish-Arab Teacher Education." Intercultural Education, 2026. [https://www.tandfonline.com/doi/full/10.1080/14675986.2026.2657732](https://www.tandfonline.com/doi/full/10.1080/14675986.2026.2657732)

[13] arXiv. "Understanding Content Moderation Policies and User Experiences in Generative AI Products." 2025. [https://arxiv.org/html/2506.14018](https://arxiv.org/html/2506.14018)

[14] Tandfonline. "'Dysfunctional' Appeals and Failures of Algorithmic Justice in Instagram and TikTok Content Moderation." Information, Communication & Society, 2024. [https://www.tandfonline.com/doi/full/10.1080/1369118X.2024.2396621](https://www.tandfonline.com/doi/full/10.1080/1369118X.2024.2396621)
