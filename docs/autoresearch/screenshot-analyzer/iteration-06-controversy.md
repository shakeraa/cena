# Iteration 6 Controversy: The Privacy-Utility Tradeoff in Educational AI

**Date**: 2026-04-12
**Series**: Screenshot Analyzer Security Research (6C -- Pedagogical Controversy)
**Companion to**: iteration-06-privacy-preserving-images.md
**Pipeline**: Student photo -> ephemeral processing -> LaTeX -> CAS validation -> image discarded

---

## The Controversy

> "Ephemeral image processing sounds noble, but it means Cena can never learn
> from student mistakes. Every misread equation, every failed extraction --
> thrown away. The system stays equally bad forever. Privacy absolutism produces
> worse education for the students you're claiming to protect."

This is the strongest objection to Cena's privacy-first architecture. It
deserves a full, honest examination.

---

## 1. The Steel-Manned Objection

The argument is not frivolous. It runs as follows:

Machine learning systems improve by studying their failures. When Gemini 2.5
Flash misreads a handwritten integral sign as a summation, the correct
response is to log the image, the incorrect extraction, and the ground truth,
then use that triplet to fine-tune the model or refine the prompt. Cena's
ephemeral processing model discards the image immediately after extraction.
The triplet can never be assembled. The error is forgotten. When the next
student uploads a similar integral, Gemini makes the same mistake.

Competitors who retain student images -- even temporarily in a training
pipeline -- will accumulate an error corpus. Their extraction improves. Cena's
does not. Over six months, the gap compounds. The privacy-strict system
delivers measurably worse math help to the same population of Bagrut students
it claims to protect.

This is not a hypothetical. The entire modern recommendation economy
demonstrates the pattern:

- **Netflix** reported in 2016 that its recommendation algorithm, trained on
  billions of viewing events, saves the company $1 billion per year in
  customer retention. Without viewing history, the algorithm cannot
  learn [1].
- **Spotify** employs hundreds of independent ML models trained on listening
  behavior to power Discover Weekly. The system improves as user interaction
  data accumulates. Remove the data, and the system degrades to popularity-based
  rankings indistinguishable from a static playlist [2].
- **Duolingo** uses student error data to determine optimal spaced-repetition
  intervals. Their 2020 research demonstrated that data-driven scheduling
  outperformed fixed intervals by 12--16% on long-term retention
  metrics [3].

The pattern is consistent: more data produces better personalization, and
better personalization produces better outcomes. A system that discards its
training signal is a system that cannot improve.

---

## 2. Evidence FOR Data Retention (The Utilitarian Case)

### 2.1 The Economics of Privacy: It Is Not Free

Acquisti, Taylor, and Wagman (2016) published a landmark survey in the
*Journal of Economic Literature* examining the economics of privacy across
dozens of theoretical and empirical studies. Their central finding:
**privacy protection has dual effects -- it can both enhance and detract from
individual and societal welfare** [4]. Privacy is not an unalloyed good. There
are real costs:

- Information asymmetry increases when firms cannot observe user behavior,
  potentially leading to adverse selection (worse products for the average user).
- Innovation that depends on behavioral data is foreclosed entirely.
- Consumers themselves are in a position of "imperfect or asymmetric
  information regarding when their data is collected, for what purposes, and
  with what consequences" -- meaning their stated preferences for privacy may
  not reflect informed choices [4].

Acquisti et al. do not argue against privacy protection. They argue that
treating privacy as an absolute -- as a lexicographic preference that
dominates all other values -- is economically incoherent. The correct framing
is tradeoff analysis, not dogma.

### 2.2 Personalized Learning Gains Are Real and Large

The RAND Corporation's 2015 study of 62 schools implementing personalized
learning practices found that students in data-driven personalized programs
made greater progress over two school years than comparison groups, with the
lowest-performing students making the most substantial relative gains. Schools
with the greatest achievement gains reported strong use of data to drive
student grouping, provision of data to students themselves, and learning
spaces designed for personalized strategies [5].

A 2024 scoping review in *Heliyon* examined 69 studies on personalized
adaptive learning in higher education and found that 59% reported improved
academic performance and 36% reported increased student engagement. The
authors concluded that "educational data mining helps improve learning by
analyzing lots of data" and that learner profiles, personal learning paths,
and competency-based progression all depend on persistent data about student
behavior [6].

A controlled experiment using AI-powered personalization reported a 25%
improvement in grades, test scores, and engagement (p = 0.00045), with the
effect driven by the AI system's ability to model individual student
weaknesses over time [7].

These are not small effects. A 25% improvement in test scores could mean the
difference between passing and failing the Bagrut mathematics exam. If
ephemeral processing forecloses this kind of improvement, students are
paying a real price for privacy.

### 2.3 Students Themselves Want Their Data Used

The privacy paradox is well documented in education. Tsai and
Whitelock-Wainwright (2020), studying learning analytics adoption, found
that **students expect elaborate adaptive dashboards and personalized
feedback**, yet are conservative about data sharing when asked in the
abstract. When presented with concrete benefits, willingness increases
sharply [8].

A study on student perceptions of learning analytics found that 52% of
students identified the value of contributing data to improve education, 38%
saw supporting research as a benefit, and 35% cited positive impact on
future students [9]. Students are not uniformly hostile to data use. Many
actively want their data to contribute to system improvement -- they just
want transparency and control.

However, Rizvi et al. (2021) documented significant disparities in consent
propensity: students from underrepresented demographics were less likely to
opt in, raising concerns that opt-in data programs may systematically
underrepresent the students who most need system improvement [10].

---

## 3. Evidence AGAINST Data Retention (The Rights-Based Case)

### 3.1 Regulatory Constraints Are Non-Negotiable for Minors

Cena serves students aged 14--18. Three regulatory frameworks apply
simultaneously, and none of them offer a "we need the data for ML
improvement" exemption:

**COPPA (2025 Amended Rule, compliance deadline April 22, 2026)**: The
amended COPPA Rule explicitly states that companies "cannot retain children's
personal information indefinitely, even if companies claim that such
retention is necessary to improve their algorithms" [11]. Biometric data
(including facial images) is now explicitly covered. Operators must maintain
a written data retention policy specifying deletion timelines. The FTC has
made the regulatory intent unmistakable: algorithmic improvement is not a
lawful basis for retaining children's data.

**GDPR Article 5(1)(e)**: Data must be "kept in a form which permits
identification of data subjects for no longer than is necessary for the
purposes for which the personal data are processed." The CNIL has clarified
that "the simple purpose of measuring performance over time is not, a priori,
sufficient to justify the retention of all data for long periods" [12]. For
continuous-learning systems, the CNIL requires that dual-use of data (for
primary function and system improvement) receive separate legal analysis.

**Israel PPL Amendment 13 (effective August 2025)**: Imposes data
minimization requirements and classifies biometric data as "information of
special sensitivity" with enhanced enforcement -- fines reaching millions of
shekels [13].

The regulatory message is convergent: you may not retain children's images to
train your algorithms. Full stop.

### 3.2 Aggregate Analytics Do Not Require Individual Image Retention

The steel-manned objection assumes a false binary: either retain images or
learn nothing. This is incorrect. Cena CAN log extensive operational metadata
without storing a single pixel:

| Metadata Field | Privacy Impact | Improvement Signal |
|---|---|---|
| Extraction success/failure rate | None (aggregate counter) | Identifies systematic failure patterns |
| Error type classification (e.g., "misread integral") | None (categorical label) | Directs prompt engineering effort |
| Latency per extraction | None (numeric value) | Identifies performance regressions |
| LaTeX validation pass/fail | None (boolean) | Measures extraction quality |
| CAS agreement rate (MathNet vs SymPy) | None (aggregate) | Detects extraction drift |
| Student-reported "wrong answer" feedback | Minimal (linked to session, not image) | Ground truth signal |
| Image characteristics (resolution, orientation, lighting score) | None (derived numeric features) | Identifies input quality issues |

Under GDPR, aggregate data resulting from statistical processing is
considered non-personal data and falls outside the regulation's scope [14].
Cena can build a comprehensive quality dashboard from metadata alone.

### 3.3 Differential Privacy Enables Learning Without Exposure

Differential privacy (DP) offers a mathematically rigorous framework for
learning from data while bounding the information leaked about any individual
record. The core mechanism adds calibrated noise to query results or gradient
updates, ensuring that no single data point materially affects the output.

NIST's 2023 guidance on deploying ML with differential privacy identifies
three insertion points: input-level (data perturbation), training-level
(DP-SGD), and inference-level (output perturbation) [15]. For Cena's use
case, input-level DP is most relevant: even if images were temporarily
retained for batch analysis, DP guarantees would prevent individual images
from being reconstructable from the learned model.

However, DP is not free. Recent research (February 2025) on fine-tuning LLMs
with differential privacy found significant accuracy penalties: DP-SGD
imposed approximately 70% accuracy decline on SCIQ benchmarks and required
1.33x the computational cost of standard training. The more efficient
alternative, LoRA (Low-Rank Adaptation), achieved comparable privacy
protection with only 5% accuracy decline and 0.65x computational cost [16].

Apple's deployment of local differential privacy at scale (2017)
demonstrated that aggregate learning from millions of devices is feasible
without accessing individual records, though the privacy-utility tradeoff
requires careful epsilon calibration [17].

### 3.4 Federated Learning Keeps Data Where It Belongs

Federated learning (FL) trains models on-device, transmitting only gradient
updates (not raw data) to a central server. Recent work in educational
settings demonstrates its viability:

- **EADP-FedAvg** (2025): An entropy-adaptive differential privacy federated
  learning method that enhances student performance prediction accuracy while
  ensuring data privacy. The method adapts noise injection based on local data
  entropy, avoiding the uniform noise penalty of standard DP-SGD [18].
- **Privacy-Preserving Federated Recommender** (2025): A federated
  recommender system for student performance prediction that provides
  "highly effective content recommendations without centralizing sensitive
  student data" [19].
- **Federated Automated Scoring** (2025): A federated learning framework for
  automated scoring of educational assessments that "eliminates the need to
  share sensitive data across institutions" [20].

For Cena specifically, a federated approach would mean: the student's device
runs a lightweight error-detection model locally, reports only aggregate
gradient updates (not images), and the central model improves without any
individual image leaving the device. This is architecturally complex but
technically feasible.

---

## 4. Cena's Position: Log Metadata, Not Content

Cena resolves the tradeoff by distinguishing between **content** (the image
itself, which contains PII and is subject to COPPA/GDPR-K/PPL) and
**metadata** (operational signals that describe how the system performed,
which contain no PII and are not regulated).

### 4.1 What Cena Logs (Metadata)

```
{
  "session_id": "uuid-v4",           // rotated per session, not linked to student identity
  "timestamp": "2026-04-12T14:30:00Z",
  "extraction_success": true,
  "error_type": null,                 // or "misread_symbol", "low_contrast", "rotation_failure"
  "latex_validation": "pass",
  "cas_agreement": true,              // MathNet and SymPy agree
  "latency_ms": 1847,
  "image_resolution": "1920x1080",
  "image_orientation": "landscape",
  "lighting_score": 0.73,            // derived from histogram analysis before discard
  "gemini_confidence": 0.91,
  "student_feedback": null            // or "incorrect_extraction" if student reports
}
```

This telemetry feeds dashboards that show extraction quality trends,
systematic failure patterns (e.g., "25% of landscape-orientation images with
lighting_score < 0.4 fail extraction"), and performance regressions. Prompt
engineers use these patterns to improve the Gemini extraction prompt without
ever seeing a student's image.

### 4.2 What Cena Does Not Log (Content)

- The original image (discarded after extraction)
- The student's face, body, or environment
- EXIF metadata (GPS, device model, timestamps -- stripped before processing)
- Any pixel data or image embeddings
- Any linkage between session_id and student identity in the telemetry store

### 4.3 Aggregate Pattern Analysis

Cena aggregates metadata into weekly reports:

- Top 10 error types by frequency
- Extraction success rate by image characteristic (resolution, lighting,
  orientation)
- CAS disagreement patterns (indicating extraction drift)
- Latency percentiles (p50, p95, p99)

These aggregates are sufficient to direct prompt engineering, identify
systematic failures, and measure improvement over time -- without any
individual image retention.

### 4.4 A/B Testing with Synthetic Data

When a prompt change is proposed, Cena validates it against a synthetic
test corpus rather than stored student images:

1. **Synthetic generation**: Use generative models to create math problem
   images with controlled characteristics (handwriting styles, lighting
   conditions, symbol distributions matching Bagrut curricula).
2. **Ground truth**: Each synthetic image has a known-correct LaTeX
   transcription.
3. **A/B protocol**: Run the current prompt and proposed prompt against the
   synthetic corpus. Compare extraction accuracy, error type distribution,
   and latency.
4. **Deploy**: If the proposed prompt outperforms on the synthetic corpus,
   deploy and monitor real-world metadata for regression.

Research supports this approach. Estimates suggest that over 60% of data used
for AI applications in 2024 was synthetic, and studies have shown that
synthetic data augmentation can reduce the required real-world training data
by up to 75% for vision tasks while maintaining performance [21]. The Phi
series of language models, trained predominantly on synthetic data,
demonstrated that "the most significant breakthroughs in synthetic text data
[emerge] in the reasoning and problem-solving domains" [22] -- precisely
Cena's domain.

---

## 5. Design Mitigations

Cena does not simply accept the privacy-utility gap as inevitable. Five
concrete mitigations narrow the gap without compromising the ephemeral
processing model.

### 5.1 Opt-In "Contribute Your Data" Program (18+ Users Only)

Students aged 18 and above (post-Bagrut university students, teachers using
the platform for practice) can opt into a data contribution program:

- **Granular consent**: Modular consent allows participants to contribute
  images for specific purposes (prompt improvement, error analysis) but not
  others (third-party research, marketing). Research shows that modular
  consent structures produce more meaningful participation than blanket
  agreements [23].
- **Age-gated**: Minors (under 18) cannot participate regardless of parental
  consent, eliminating COPPA/GDPR-K complexity entirely.
- **Revocable**: Contributors can withdraw consent and request deletion of
  their contributed images at any time (GDPR Art. 17).
- **Transparent**: Contributors see exactly which images they have
  contributed and how each was used.
- **Bias-aware**: Given Rizvi et al.'s finding that consent propensity
  varies by demographic [10], the program monitors contributor demographics
  and actively recruits underrepresented groups to prevent systematic bias
  in the improvement corpus.

### 5.2 Synthetic Data Augmentation Pipeline

A dedicated pipeline generates training data that mirrors Bagrut-specific
characteristics:

- **Handwriting style variation**: Generate images using handwriting
  synthesis models trained on publicly available handwriting datasets (IAM
  Handwriting Database, CROHME competition data), not student data.
- **Bagrut curriculum alignment**: Generate problems drawn from published
  Bagrut exam archives (public domain), rendered with varied handwriting
  styles, paper textures, and lighting conditions.
- **Error injection**: Deliberately introduce common failure modes
  (low contrast, rotation, partial occlusion, mixed Hebrew/Arabic/English
  text) identified by metadata analysis.
- **Continuous expansion**: As metadata identifies new failure patterns,
  synthetic data is generated to target those specific patterns.

Open-source math datasets already exist at scale: OpenMathInstruct-1
provides 1.8 million problem-solution pairs [24], and MathOdyssey offers
387 expert-generated problems spanning high school through Olympiad
levels [25]. These can seed the synthetic pipeline without any student
data.

### 5.3 Teacher-Contributed Datasets

Cena can establish a teacher contribution program:

- Teachers upload photos of practice problems (not student work) they
  encounter in their own materials.
- Teachers provide ground-truth LaTeX for each image.
- Contributions are licensed under Creative Commons for the Cena
  improvement corpus.
- Teachers are compensated with premium platform features.

This produces a curated, consent-rich, PII-free corpus of real-world math
images -- exactly the kind of data that improves extraction quality. The
MathDial project (2023) demonstrated a similar approach, pairing human
teachers with LLMs to generate pedagogically rich training data [26].

### 5.4 Aggregate Error Dashboards

A real-time dashboard accessible to Cena's engineering team displays:

- **Error heatmap**: Which symbol types fail most often (integrals, sigma
  notation, matrix brackets, Hebrew mathematical notation)
- **Temporal trends**: Is extraction quality improving or degrading over time?
- **Environmental factors**: Does extraction quality vary by time of day
  (lighting proxy), device type, or image resolution?
- **Geographic patterns**: Do students in different regions experience
  different failure rates (potentially indicating curriculum or handwriting
  style variation)?

All data is aggregate. No individual session can be identified. The
dashboard enables data-driven prompt engineering without data-driven privacy
violations.

### 5.5 Prompt Versioning with Rollback

Every Gemini extraction prompt is versioned. When a new prompt is deployed:

1. Metadata from the first 1,000 extractions is compared against the
   previous prompt's baseline.
2. If extraction success rate drops by more than 2 percentage points, the
   system automatically rolls back.
3. All prompt versions and their aggregate performance metrics are retained
   indefinitely (no PII involved).

This creates a feedback loop: metadata identifies problems, synthetic data
tests fixes, deployment monitors for regression, and automatic rollback
prevents degradation. The loop is complete without any student image
retention.

---

## 6. Open Questions

### 6.1 Is Metadata Truly Sufficient?

The honest answer is: probably, but not certainly. There may be failure modes
that metadata alone cannot diagnose -- for example, a systematic
misrecognition of a specific Hebrew mathematical notation that only manifests
in handwritten form. Synthetic data may not capture this if the handwriting
synthesis model has not been exposed to Hebrew mathematical conventions.
Monitoring for this gap is essential.

### 6.2 How Good Can Synthetic Data Get?

Current synthetic data research is promising but not settled. MIT researchers
caution that synthetic data "will expand rather than replace real data" [22],
and models trained exclusively on synthetic data risk "model collapse" --
degenerative feedback loops where synthetic artifacts are amplified across
generations. Cena's approach of using synthetic data for A/B testing (not
model training) mitigates this risk, but the question of whether synthetic
math images will ever fully substitute for real student handwriting remains
open.

### 6.3 Will the 18+ Opt-In Program Produce Enough Data?

University students and teachers using a Bagrut preparation platform may be
a small population. If the opt-in corpus is too small or too demographically
skewed, its improvement signal may not generalize to the broader student
population. Monitoring corpus size and diversity is a prerequisite for
relying on this channel.

### 6.4 Can Federated Learning Work On-Device for Image Analysis?

Federated learning for image classification on mobile devices is technically
feasible (Google demonstrated it for keyboard prediction in 2017), but
running a vision model fine-tuning loop on a student's phone introduces
battery drain, latency, and device compatibility challenges. For Cena's
Flutter mobile app, this is a medium-term research investment, not a
near-term deployment option.

### 6.5 What If a Competitor With Fewer Scruples Wins?

The deepest version of the objection: if a competing Bagrut prep platform
retains student images, achieves 95% extraction accuracy while Cena plateaus
at 85%, and students migrate to the competitor -- then Cena's privacy
principles have produced a world where more students use a less
privacy-respecting system. The principled stance has paradoxically worsened
aggregate student privacy.

This is a real risk. The mitigation is to ensure Cena's non-image-dependent
improvement mechanisms (metadata analysis, synthetic data, teacher
contributions, prompt engineering) are aggressive enough to keep extraction
quality competitive. Privacy principles that produce an unusable product
protect no one, because no one uses the product.

### 6.6 Should Cena Revisit This Decision If Regulations Change?

Privacy regulation for children has only tightened over the past decade.
The 2025 COPPA amendments, GDPR-K, and Israel's Amendment 13 all move in
the same direction. However, if future regulation creates a safe harbor for
privacy-preserving ML training on children's data (e.g., a regulatory
framework for federated learning with formal DP guarantees), Cena should
revisit the ephemeral-only constraint.

### 6.7 How Do We Measure the Privacy-Utility Gap?

Cena should track two metrics over time:

1. **Extraction accuracy on a held-out benchmark** (synthetic + teacher-
   contributed images with ground truth). If this stagnates while competitors
   improve, the gap is real.
2. **Student satisfaction with extraction quality** (in-app feedback). If
   students increasingly report incorrect extractions, the privacy cost is
   being paid in user experience.

If both metrics degrade relative to alternatives, the privacy-utility
tradeoff demands re-examination -- not necessarily abandonment of ephemeral
processing, but investment in the mitigations described above.

---

## 7. Verdict

The objection is legitimate. Ephemeral processing imposes a real cost on
system improvement. The question is whether the cost is acceptable given:

1. **Regulatory reality**: COPPA, GDPR-K, and Israeli PPL Amendment 13
   collectively prohibit retaining children's images for algorithmic
   improvement. This is not a design choice -- it is a legal constraint.
2. **Available alternatives**: Metadata logging, synthetic data, teacher
   contributions, and federated learning collectively provide substantial
   improvement signal without individual image retention.
3. **Trust economics**: Cena operates in an education market where a single
   data breach involving student photos would be existential. The
   reputational cost of retaining images exceeds the accuracy cost of
   discarding them.
4. **Proportionality**: The privacy-utility tradeoff is real, but the
   utility loss from ephemeral processing is bounded (metadata captures
   most of the improvement signal), while the privacy risk from retention
   is unbounded (a breach exposes every retained image).

Cena's position is not privacy absolutism. It is privacy pragmatism:
extract every bit of improvement signal that does not require retaining
PII, invest heavily in PII-free alternatives, and accept a bounded
accuracy penalty rather than an unbounded privacy risk.

The controversy is genuine. The resolution is not "privacy always wins"
but "privacy wins when the alternatives are good enough, and our job is
to make the alternatives good enough."

---

## References

[1] C. A. Gomez-Uribe and N. Hunt, "The Netflix Recommender System: Algorithms, Business Value, and Innovation," *ACM Transactions on Management Information Systems*, vol. 6, no. 4, 2016. https://dl.acm.org/doi/10.1145/2843948

[2] "Inside Spotify's Recommendation System: A Complete Guide (2025 Update)," Music Tomorrow, 2025. https://www.music-tomorrow.com/blog/how-spotify-recommendation-system-works-complete-guide

[3] B. Settles and B. Meeder, "A Trainable Spaced Repetition Model for Language Learning," *Proceedings of ACL*, 2016. https://research.duolingo.com/

[4] A. Acquisti, C. Taylor, and L. Wagman, "The Economics of Privacy," *Journal of Economic Literature*, vol. 54, no. 2, pp. 442--492, 2016. https://www.aeaweb.org/articles?id=10.1257/jel.54.2.442

[5] J. F. Pane, E. D. Steiner, M. D. Baird, and L. S. Hamilton, "Continued Progress: Promising Evidence on Personalized Learning," RAND Corporation, RR-1365-BMGF, 2015. https://www.rand.org/pubs/research_reports/RR1365.html

[6] M. Perez-Sanagustin et al., "Personalized adaptive learning in higher education: A scoping review," *Heliyon*, vol. 10, no. 21, 2024. https://www.sciencedirect.com/science/article/pii/S2405844024156617

[7] "Integrating deep learning techniques for personalized learning pathways in higher education," *PMC*, 2024. https://pmc.ncbi.nlm.nih.gov/articles/PMC11219980/

[8] Y.-S. Tsai and A. Whitelock-Wainwright, "The privacy paradox and its implications for learning analytics," *Proceedings of LAK*, 2020. https://dl.acm.org/doi/10.1145/3375462.3375536

[9] Data Quality Campaign, "Student Data Principles," 2024. https://dataqualitycampaign.org/resources/student-data-principles/

[10] S. Rizvi et al., "Disparities in Students' Propensity to Consent to Learning Analytics," *International Journal of AI in Education*, vol. 31, 2021. https://link.springer.com/article/10.1007/s40593-021-00254-2

[11] FTC, "FTC Finalizes Changes to Children's Privacy Rule," January 2025; Fenwick, "What the Amended COPPA Rule Means for Data Retention," 2025. https://www.fenwick.com/insights/publications/what-the-amended-coppa-rule-means-for-data-retention-practices

[12] CNIL, "AI System Development: CNIL's Recommendations to Comply with the GDPR," 2024. https://www.cnil.fr/en/ai-system-development-cnils-recommendations-to-comply-gdpr

[13] Library of Congress, "Israel: Amendment to Privacy Protection Law Goes into Effect," November 2025. https://www.loc.gov/item/global-legal-monitor/2025-11-17/israel-amendment-to-privacy-protection-law-goes-into-effect/

[14] UNECE, "Shedding Light on the Legal Approach to Aggregate Data," 2021. https://unece.org/sites/default/files/2021-12/SDC2021_Day1_Podda_AD.pdf

[15] NIST, "How to Deploy Machine Learning with Differential Privacy," 2023. https://www.nist.gov/blogs/cybersecurity-insights/how-deploy-machine-learning-differential-privacy

[16] S. Dutt et al., "Revisiting Privacy, Utility, and Efficiency Trade-offs when Fine-Tuning Large Language Models," arXiv:2502.13313, February 2025. https://arxiv.org/html/2502.13313v1

[17] Apple Machine Learning Research, "Learning with Privacy at Scale," 2017. https://machinelearning.apple.com/research/learning-with-privacy-at-scale

[18] "Entropy-adaptive differential privacy federated learning for student performance prediction," *Frontiers in AI*, 2025. https://www.frontiersin.org/journals/artificial-intelligence/articles/10.3389/frai.2025.1653437/full

[19] "Privacy-Preserving Personalization in Education: A Federated Recommender System for Student Performance Prediction," arXiv:2509.10516, 2025. https://arxiv.org/html/2509.10516v3

[20] "Privacy-Preserved Automated Scoring using Federated Learning for Educational Research," arXiv:2503.11711, 2025. https://arxiv.org/html/2503.11711

[21] MIT News, "3 Questions: The Pros and Cons of Synthetic Data in AI," 2025; Datacebo, "Synthetic Data in 2024: The Year in Review," 2024. https://news.mit.edu/2025/3-questions-pros-cons-synthetic-data-ai-kalyan-veeramachaneni-0903

[22] "The Prominence of Synthetic Data, and Why It Will Expand Rather Than Replace Real Data," Dataversity, 2025. https://www.dataversity.net/articles/the-prominence-of-synthetic-data-and-why-it-will-expand-rather-than-replace-real-data/

[23] Nielsen Norman Group, "Obtaining Consent for User Research," 2024. https://www.nngroup.com/articles/informed-consent/

[24] "OpenMathInstruct-1: A 1.8 Million Math Instruction Tuning Dataset," NeurIPS 2024. https://arxiv.org/html/2402.10176v1

[25] "MathOdyssey: Benchmarking Mathematical Problem-Solving Skills in Large Language Models," *Nature Scientific Data*, 2025. https://www.nature.com/articles/s41597-025-05283-3

[26] "MathDial: A Dialogue Tutoring Dataset with Rich Pedagogical Properties Grounded in Math Reasoning Problems," arXiv:2305.14536, 2023. https://arxiv.org/abs/2305.14536
