# Pedagogical Controversy: Should Adversarially-Attackable AI Systems Be Deployed to Children?

**Series**: Screenshot Analyzer -- Defense-in-Depth Research
**Date**: 2026-04-12
**Type**: Controversy / Ethical Analysis
**Audience**: Cena engineering, product, legal, and compliance teams
**Status**: Complete

---

## Abstract

The Cena platform deploys AI systems -- including Gemini 2.5 Flash for vision-based math extraction and Anthropic Claude for Socratic tutoring -- to Israeli Bagrut students aged 14--18. These systems are, by their nature, susceptible to prompt injection, adversarial image attacks, and other forms of manipulation. A serious objection exists: the precautionary principle suggests that no AI system with known, unfixable attack surfaces should be deployed to minors until the technology is proven safe. This article steel-mans that objection with real regulatory guidance, academic evidence, and documented incidents; then presents the counter-arguments; and finally states Cena's specific position and the design mitigations that justify responsible deployment.

---

## 1. The Objection, Steel-Manned

The strongest version of the argument against deploying adversarially-attackable AI to children runs as follows:

**Premise 1: LLMs and vision-language models have known, unfixable vulnerabilities.** Prompt injection is not a bug but a structural feature of how instruction-following language models process input. No vendor -- not OpenAI, not Google, not Anthropic -- has eliminated it. The OWASP Top 10 for LLM Applications (2025 edition) ranks prompt injection as the number-one risk [1]. Research published in *Scientific Reports* in 2026 demonstrated attack success rates of 0.73--0.82 against educational LLMs specifically, with attackers embedding injection payloads inside pedagogically plausible student responses [2]. The Frontier AI Safety report from FAR AI found that even multi-layer defense-in-depth architectures can be sequentially penetrated: their STACK attack achieved 71% success against a Gemma 2 classifier pipeline in limited-feedback conditions, and 33% success in full black-box conditions [3].

**Premise 2: Children are a uniquely vulnerable population.** The UNICEF Guidance on AI and Children (version 3.0, December 2025) identifies children as requiring heightened protection because of their still-developing cognitive abilities, their susceptibility to manipulation, and their limited capacity to understand and consent to data processing [4]. The MIT Media Lab has shown that children as young as seven attribute real feelings and personality to AI agents [5]. A peer-reviewed paper published in *AI and Ethics* (Springer, February 2026) explicitly calls for "an immediate ban on children's LLM chatbot use," arguing that the precautionary principle's four requisite conditions are met: epistemic uncertainty about long-term effects, credible scientific indication of serious harm (including documented cases of suicide linked to LLM interactions, emotional dependency, and metacognitive laziness), irreversibility of developmental damage, and proportionality of a moratorium response [6].

**Premise 3: Regulators have classified educational AI as high-risk.** The EU AI Act (entered into force August 1, 2024; full application August 2, 2026) classifies AI systems in education as high-risk under Annex III, Section 3. Specifically, the Act designates as high-risk any AI system "intended to be used to evaluate learning outcomes, including when those outcomes are used to steer the learning process" and any system "intended to be used for the purpose of assessing the appropriate level of education that an individual will receive" [7]. Cena's adaptive difficulty engine, mastery prediction via Bayesian Knowledge Tracing, and Elo-rated question selection fall squarely within these categories. The ICO's Age Appropriate Design Code (Children's Code) requires that profiling of children be switched off by default and that high-privacy settings apply unless a compelling justification exists [8].

**Premise 4: The alternative -- waiting -- is available.** The precautionary principle holds that when an action raises threats of harm to human health or the environment, precautionary measures should be taken even if some cause-and-effect relationships are not fully established scientifically. Applied here: we do not yet have longitudinal studies on the developmental effects of AI tutoring on adolescents. We do not have proof that defense-in-depth is sufficient against motivated attackers targeting children. The responsible action is to wait until the technology matures, the regulatory landscape stabilizes, and the evidence base grows.

**Conclusion (as stated by the objector):** Deploying Cena's AI systems to minors is ethically premature. The platform should operate without LLM-powered features until prompt injection is solved, adversarial robustness is proven, and regulators have issued clear approval for this use case.

---

## 2. Evidence Supporting the Objection

### 2.1 Regulatory and Policy Frameworks

**UNICEF Guidance on AI and Children 3.0 (December 2025).** This third edition, informed by a twelve-country study with children and caregivers, establishes ten requirements for child-centered AI: (1) regulatory compliance with child rights law, (2) safety testing for security and robustness, (3) prevention of AI-enabled crimes against children, (4) responsible handling of AI companions and chatbots, (5) privacy-by-design with children's data agency, (6) non-discrimination, (7) transparency and accountability, (8) children's best interests and well-being, (9) inclusion, and (10) skills preparation [4]. Requirement 2 explicitly calls for testing AI systems for "safety, security and robustness" before deployment to children -- a standard that adversarially-attackable systems arguably fail to meet.

**EU AI Act Annex III, Section 3.** The Act's education provisions classify as high-risk: (a) AI systems determining access or admission to educational institutions, (b) AI systems evaluating learning outcomes when used to steer learning, (c) AI systems assessing appropriate education levels, and (d) AI systems monitoring student behavior during tests [7]. Cena's adaptive engine evaluates learning outcomes (via BKT, Elo, FSRS) and uses those evaluations to steer the learning process (adaptive difficulty, methodology switching, concept unlocking). Under the Act, this triggers mandatory risk management systems, data governance, technical documentation, human oversight, and conformity assessment.

**2025 COPPA Amendments (effective June 23, 2025; compliance deadline April 22, 2026).** The FTC's updated rule expands the definition of personal information to include biometric identifiers, shifts the default from opt-out to opt-in consent for data collection from children, and explicitly requires separate verifiable parental consent before disclosing a child's personal information to train AI technologies [9]. The FTC has actively enforced against EdTech companies: Edmodo received a $6 million fine in 2023, and Epic Games/Fortnite paid $520 million in 2022 [10]. Fines under the 2025 rule can reach $51,744 per child, per violation.

**ICO Age Appropriate Design Code.** The Code's Standard 12 requires that profiling of children be switched off by default [8]. The 5Rights Foundation has argued that the Code can protect children from AI harms but only if "properly enforced," noting that no service has yet been confirmed as fully compliant [11].

### 2.2 Documented Incidents and Research

**Prompt injection in educational contexts.** Cai (2026) published in *Scientific Reports* a systematic study of prompt injection attacks against educational LLMs for higher and vocational education. The method decomposes composite educational prompts into functional segments, constructs role-consistent attack vectors, and embeds stealthy injections inside pedagogically plausible student responses. Attack success rates reached 0.82 on the ASAP dataset, 0.79 on SciEntsBank, 0.76 on EduBench, and 0.73 on MMLU-Edu, outperforming competitive baselines by 0.19--0.33 absolute [2]. The implication: educational prompts expose structural attack surfaces not captured by generic safety evaluations.

**Defense-in-depth is penetrable.** FAR AI researchers developed STACK (STaged AttaCK), demonstrating that multi-layer AI defenses can be bypassed sequentially. The critical finding: when systems reveal which layer rejected a request (via different error codes, timing patterns, or response formats), attackers achieve 71% bypass rates. Even in full black-box conditions, sequential attacks achieved 33% success [3]. The researchers conclude that defense-in-depth is "valuable" but "requires careful implementation to minimize the attack surface" and remains an "early-stage field."

**Children's cognitive vulnerability.** The Federation of American Scientists' agenda on child safety in the AI era notes that seven in ten teens have used generative AI, primarily for homework help, yet most parents remain unaware [12]. The EU has recognized that "children and teenagers are vulnerable to manipulation by technology" [13]. A University of Illinois Urbana-Champaign study (presented at IEEE S&P 2025) found that parents have little understanding of generative AI, how their children use it, and its potential risks, and that AI platforms offer insufficient protection [5].

**Harmful content generation.** Amazon Alexa once advised a child to stick a coin in an electrical socket. Snapchat's AI companion gave inappropriate advice to reporters posing as children [5]. The Internet Watch Foundation reported over 8,000 cases of AI-generated child sexual abuse content in the first half of 2025 alone, a 14% rise from the previous year [13]. While these incidents involve general-purpose AI systems rather than educational platforms, they demonstrate the category of risk.

**The social media precedent.** Leung (2026) in *AI and Ethics* draws explicit parallels between the current unregulated deployment of LLMs to children and the early, unregulated rollout of social media, arguing that the pattern of delayed regulatory response inflicted "measurable harm on children's social, cognitive, physical, and mental health" and that we should not repeat it [6].

### 2.3 The Precautionary Principle Argument

The precautionary principle, as formulated by the Wingspread Conference (1998) and applied in EU environmental law, holds: "When an activity raises threats of harm to human health or the environment, precautionary measures should be taken even if some cause and effect relationships are not fully established scientifically."

Applied to AI in education, the four conditions identified by Leung (2026) are arguably met:

1. **Epistemic uncertainty**: We lack longitudinal studies on the developmental effects of AI tutoring on adolescents.
2. **Credible indication of serious harm**: Documented cases of emotional dependency, metacognitive laziness, and at least one suicide linked to LLM interaction.
3. **Irreversibility**: Developmental impacts during adolescence may be irreversible.
4. **Proportionality**: A moratorium on LLM deployment to children, while costly, is proportionate to the potential harm.

---

## 3. Evidence Against the Objection / Nuancing the Case

### 3.1 Every EdTech Product Has Attack Surfaces

The objection implicitly holds AI-powered systems to a standard of zero vulnerability that no software system meets. Every educational platform with user-generated input -- from Google Classroom to Moodle to any forum with a text field -- has exploitable attack surfaces: XSS, CSRF, SQL injection, privilege escalation, data leakage. The question is not whether vulnerabilities exist but whether the risk-mitigation architecture reduces residual risk to an acceptable level. The fact that OWASP maintains a Top 10 list for web applications does not mean we ban web applications from schools; it means we apply defense-in-depth and continuous monitoring.

### 3.2 The Alternative Is Not Zero AI -- It Is Unmediated AI

Banning AI tutoring from educational platforms does not prevent students from using AI. A 2024 survey found that 70% of teens already use generative AI, primarily for homework help [12]. The realistic alternative to a supervised, guardrailed AI tutoring environment is students interacting directly with ChatGPT, Claude, or Gemini -- systems that lack the educational context, content moderation for minors, mathematical verification, and institutional oversight that a purpose-built platform provides. As the Public Knowledge Foundation has noted, overly restrictive regulations on AI chatbots for minors could "backfire" by driving children toward less safe alternatives [14].

Khan Academy's Khanmigo demonstrates this principle in practice: rather than waiting for perfect AI safety, they deployed with moderation guardrails that stop inappropriate conversations and immediately notify parents and teachers, combined with full conversation visibility for guardians [15]. The result is a system that is demonstrably safer for children than unmediated access to general-purpose AI.

### 3.3 Defense-in-Depth Is Standard Practice, Not Wishful Thinking

The FAR AI research on STACK attacks, while important, also validated the core principle: multi-layer defenses significantly raise the cost and difficulty of attacks. Their key recommendation was not to abandon defense-in-depth but to implement it carefully by eliminating layer-revealing signals, using strict model separation, and testing against sequential attacks [3]. Microsoft's Responsible AI framework employs exactly this pattern: a "layered, defense-in-depth approach to AI safety, using an AI-based safety system that surrounds the model, monitoring inputs and outputs" [16]. The NSA/CISA joint guidance on deploying AI systems securely (April 2024) recommends defense-in-depth as the primary architecture for AI system security [17].

### 3.4 The EU AI Act Permits High-Risk AI -- With Obligations

The EU AI Act classifies educational AI as high-risk, but it does not ban it. The entire framework of high-risk classification exists to define the obligations that make deployment acceptable: risk management systems, data governance, technical documentation, transparency, human oversight, accuracy, robustness, and cybersecurity [7]. The Act's design reflects the regulatory judgment that high-risk AI in education can be deployed responsibly when these obligations are met. A precautionary ban would be stricter than the EU's own considered regulatory position.

### 3.5 UNICEF's Position Is Pro-Deployment With Safeguards

UNICEF's Guidance on AI and Children 3.0 does not advocate for banning AI from children's lives. Its ten requirements define conditions for responsible deployment, not conditions for prohibition. Requirement 8 explicitly states that AI policies, laws, and systems should "prioritize how AI can benefit children" [4]. The guidance recognizes that AI can serve children's developmental needs, educational access, and well-being when properly governed. UNICEF has supported pilot testing of AI systems for children across multiple countries to study responsible implementation [18].

### 3.6 The Precautionary Principle Has Limits

The American Enterprise Institute's analysis of the precautionary principle in AI development identifies a fundamental tension: strict application of the principle can itself cause harm by preventing beneficial deployment [19]. In education, the harm of withholding effective AI tutoring from students who cannot afford private tutors, who attend under-resourced schools, or who learn at non-standard paces is real and measurable. The precautionary principle must weigh the harms of inaction alongside the harms of action. As the World Economic Forum's seven principles for responsible AI in education note, the goal is not zero risk but "proportionate" risk management [20].

### 3.7 The Longitudinal Evidence Gap Is Not Unique to AI

The objection demands longitudinal evidence before deployment, but no educational technology has ever been deployed with complete longitudinal evidence of its effects. Calculators were introduced to classrooms without longitudinal studies. The internet was deployed to schools without proof that it would not harm adolescent development. Smartphones entered children's lives without regulatory pre-approval. In each case, the technology was deployed, monitored, and adjusted -- the same iterative approach that responsible AI deployment demands.

---

## 4. Cena's Specific Position

Cena's position is that responsible deployment of AI to minors is both ethically defensible and educationally necessary, provided that the system architecture treats adversarial safety as a first-class design constraint rather than an afterthought. The following architectural features substantiate this position.

### 4.1 The 10-Layer Defense Architecture

Cena's Screenshot Question Analyzer implements a 10-layer defense-in-depth architecture, documented across iterations 1--10 of this research series [21--30]. The layers are:

| Layer | Defense | Function |
|-------|---------|----------|
| L1 | Client-side checks | File type validation, max size (10 MB), client-side EXIF stripping, CAPTCHA on repeated uploads |
| L2 | Edge rate limiting | Per-IP sliding window (60 req/min), per-user token bucket (10 uploads/min, 30/hour), concurrency limiter (3 in-flight per user), geo-fence |
| L3 | Auth and tenant isolation | Firebase token validation (RS256), tenant-scoped claims, CASL ability checks |
| L4 | Privacy-preserving preprocessing | Server-side EXIF strip, face detection and blur (MediaPipe BlazeFace), PII text detection and redaction, image re-encoding, original image never persisted |
| L5 | Content moderation (pre-vision) | Google Cloud Vision SafeSearch, threshold-based rejection, skin-exposure heuristic for minors |
| L6 | Vision model safety | Constrained system prompt (LaTeX-only output), structured output schema enforcement, confidence threshold (reject below 0.7), adversarial image detector |
| L7 | Prompt injection defense | Input/output boundary markers (canary tokens), output regex validator (LaTeX grammar), instruction-following detector, dual-call verification |
| L8 | LaTeX sanitization | Allowlist-only LaTeX commands, blocklist of dangerous commands, Unicode NFKC normalization, max nesting depth (10), max command count (200) |
| L9 | CAS safety | SymPy sandbox (no exec/eval/imports), 5-second timeout, 256 MB memory limit, parsed LaTeX AST input only, containerized (Podman rootless, no network) |
| L10 | Defense integration and monitoring | Structured audit log, anomaly detection, academic integrity checks, error sanitization (6 generic codes only), graceful degradation |

In the end-to-end attack simulation (iteration 10), 30 attacks across 8 categories achieved a 0% attack success rate against this architecture [30].

### 4.2 The CAS Backstop: "The LLM Explains; the CAS Computes"

Cena's most distinctive architectural decision is the separation of explanation from computation. The LLM (Gemini for extraction, Claude for tutoring) generates natural-language explanations and pedagogical responses. But every mathematical claim is independently verified by the Computer Algebra System (CAS) pipeline: MathNet for numeric evaluation, SymPy for symbolic verification, and Wolfram as a tertiary fallback [21]. An adversary who tricks the LLM into extracting "2+2=5" will be caught by SymPy's symbolic equivalence check. This architectural pattern provides a natural defense layer that most LLM deployments -- including general-purpose chatbots that children already use -- lack entirely.

### 4.3 No Profile Data on Minors Beyond Educational Necessity

Cena's data architecture follows strict data minimization as documented in the DPIA [31]:

- **No long-term image storage**: Original photos are processed in memory only, never written to disk or object storage. Only extracted LaTeX persists, session-scoped and auto-deleted per GDPR TTL.
- **No biometric collection**: Face detection is used only for privacy (blurring faces before processing), not for identification.
- **No behavioral profiling beyond learning**: The platform tracks mastery state (BKT, Elo, FSRS) and session analytics (response time, accuracy) -- data that is pedagogically necessary and directly serves the student's learning. It does not track browsing behavior, social connections, or off-platform activity.
- **No advertising, no data sales**: The children's privacy notice explicitly states: "We do NOT sell your info. We do NOT share it with other kids. We do NOT use it to show you ads" [32].
- **Session-scoped AI conversations**: Tutor conversations are auto-deleted per GDPR retention policy. No conversation corpus is built up over time.

### 4.4 Regulatory Alignment

- **EU AI Act**: Cena's adaptive engine falls within Annex III, Section 3(b) (evaluating learning outcomes to steer learning). The 10-layer defense architecture, DPIA, technical documentation, and human oversight mechanisms are designed to satisfy the Act's high-risk obligations.
- **COPPA 2025**: Students are 14--18 (teen tier). Photos processed session-scoped with no long-term retention. No data used for AI training. Parental consent required at registration.
- **Israeli PPL Amendment 13**: Direct applicability. Enhanced minor protections aligned with the amendment's GDPR-equivalent requirements.
- **ICO Children's Code**: Profiling is limited to pedagogically necessary learning metrics. High-privacy defaults apply. No nudge techniques for data collection.

---

## 5. Design Mitigations

Beyond the 10-layer defense architecture, Cena implements the following design-level mitigations specifically addressing the controversy.

### 5.1 Transparency About AI Limitations

**Disclosed, not hidden.** The student-facing UI must clearly indicate when content is AI-generated versus CAS-verified. The SignalR protocol already distinguishes between `explanation` (AI-generated, marked as such) and mathematical results (CAS-verified). The children's privacy notice is written at Flesch-Kincaid grade 5 [32].

**AI is not the authority.** The Socratic tutoring model never provides direct answers. It asks guiding questions, offers hints in progressive tiers (1--3), and directs students toward their own reasoning. The CAS engine, not the LLM, is the mathematical authority. This architectural separation means that even a successful prompt injection against the tutoring LLM cannot cause the system to present incorrect mathematics as verified.

**Confidence disclosure.** When vision model confidence falls below 0.7, the system returns an explicit uncertainty message: "Verification uncertain -- please check." Students are taught that the AI can be wrong.

### 5.2 Parent and Teacher Visibility

**Full conversation access.** Following Khan Academy's model [15], parents and teachers will have access to all student-AI conversations. The admin dashboard (Vuexy Vue 3 at `src/admin/full-version/`) provides institutional oversight.

**Real-time alerts.** Content moderation failures, safety-flagged interactions, and academic integrity triggers generate immediate alerts to teachers and, for parental consent settings, to parents. This follows UNICEF's Requirement 7 on transparency and accountability [4].

**Audit trail.** Every defense-layer decision is logged in a structured audit document (`ModerationAuditDocument`), providing a forensic record for incident review.

### 5.3 Incident Response

**Six generic error codes.** Client-facing errors are mapped to exactly six codes (`invalid_image`, `processing_failed`, `rate_limited`, `auth_required`, `content_rejected`, `service_unavailable`), preventing attackers from mapping the backend architecture through error enumeration [30].

**Anomaly detection.** Spikes in rejection rates trigger automated alerts. A sustained increase in content moderation rejections from a single tenant, for example, would trigger a review of that school's usage patterns.

**Red-teaming in CI/CD.** The 30 attack scenarios documented in iteration 10 are implemented as automated regression tests. Any code change that degrades the defense architecture's ability to catch known attacks will fail the build.

**Responsible disclosure.** Security vulnerabilities discovered by students, teachers, or external researchers will be handled through a responsible disclosure process. Students who discover prompt injection bypasses should be encouraged to report them, not punished -- this transforms adversarial curiosity into a constructive security culture.

### 5.4 Wellbeing Protections

**Session limits.** The LLM budget caps at 50 interactions per day ("Study Energy"), framed as a health limit, not a monetization gate [33]. The `CognitiveLoadWarning` SignalR event recommends breaks, difficulty reduction, or session termination based on detected fatigue signals.

**No emotional dependency design.** The AI tutor does not have a persistent personality, name, or avatar designed to encourage emotional attachment. Conversations are session-scoped and explicitly framed as tool use, not relationship.

**Bedtime mode and wellbeing service.** The mobile app implements `BedtimeModeService` and `WellbeingService` to discourage late-night use and monitor engagement patterns for signs of unhealthy usage.

---

## 6. Open Questions

The following questions remain genuinely unresolved. They are not rhetorical -- they require ongoing research, stakeholder input, and potentially regulatory guidance before Cena can claim a definitive position.

### 6.1 What Level of Residual Risk Is Acceptable for Minors?

The 10-layer defense achieves 0% attack success rate in design-time simulation, but no security architecture achieves 0% in production against motivated, adaptive adversaries. What residual ASR is acceptable when the users are 14--18-year-olds? The FAR AI research suggests that even careful defense-in-depth leaves 33% residual risk in black-box conditions [3]. Cena's CAS backstop significantly lowers the impact of successful attacks (the LLM can be tricked but cannot cause mathematically incorrect verified output), but the question of acceptable residual risk for minors has no established regulatory threshold.

### 6.2 Should Parental Consent Be Per-Feature or Blanket?

Current design requires parental consent at registration. But should specific AI features -- vision model extraction, LLM tutoring, adaptive difficulty -- require separate, granular consent? The 2025 COPPA amendments suggest that disclosure to third-party AI services requires separate consent [9]. If Cena sends images to Google Cloud Vision and conversations to Anthropic Claude, each third-party disclosure may require its own parental opt-in.

### 6.3 How Do We Measure Developmental Impact?

The precautionary principle objection rests partly on the absence of longitudinal evidence. Cena should contribute to filling this gap. But designing ethical longitudinal studies on adolescent AI interaction raises its own ethical challenges: randomized controlled trials that deny AI tutoring to a control group of struggling students may themselves be ethically problematic.

### 6.4 What Happens When Students Share Successful Attacks?

The adversary profile in iteration 2 identifies viral propagation as a key risk: a student who discovers a prompt injection bypass will share it with classmates via TikTok or WhatsApp [22]. Cena's defense architecture is designed to catch known attack patterns, but novel attacks shared virally could temporarily overwhelm monitoring before defenses adapt. The speed of the incident response cycle -- from first viral bypass to deployed fix -- becomes the critical metric.

### 6.5 Is the CAS Backstop Sufficient for Non-Mathematical Content?

The CAS engine provides strong verification for mathematical claims, but Cena's roadmap includes non-mathematical subjects (history, literature, civics). For these subjects, there is no equivalent formal verification system. When the LLM tutors a student in history, what prevents a successful prompt injection from causing the system to present historically inaccurate information as fact? This question becomes more pressing as Cena expands beyond STEM.

### 6.6 How Should Israeli Law Evolve?

Israel's PPL Amendment 13 (effective August 2025) aligns with GDPR but does not specifically address AI tutoring for minors. As the Israeli Privacy Protection Authority develops guidance on AI and children, Cena should actively engage in the regulatory process rather than waiting for requirements to be imposed.

---

## 7. Conclusion

The objection is serious. The evidence supporting it is real. Children are genuinely vulnerable, adversarial attacks against LLMs are genuinely unsolved, and regulators have genuinely classified educational AI as high-risk. These facts deserve respect, not dismissal.

But the objection, taken to its logical conclusion, would also ban every current educational technology that processes student input through software with known vulnerability classes -- which is to say, all of them. The question is not whether to deploy perfect systems but whether to deploy systems whose risk-mitigation architecture reduces residual risk to a level that is proportionate to the educational benefit and acceptable to the stakeholders (students, parents, teachers, regulators) who bear that risk.

Cena's position is that the 10-layer defense architecture, the CAS backstop, the data minimization posture, the transparency commitments, and the institutional oversight mechanisms collectively meet that standard -- provided they are continuously tested, honestly reported, and iteratively improved. The open questions in Section 6 are the honest acknowledgment that this is an ongoing obligation, not a solved problem.

---

## References

[1] OWASP. "Top 10 for LLM Applications 2025." https://genai.owasp.org/llmrisk/llm01-prompt-injection/

[2] Cai, Y. "Prompt injection attacks on educational large language models for higher and vocational education." *Scientific Reports* (2026). https://www.nature.com/articles/s41598-026-46563-1

[3] FAR AI. "Layered AI Defenses Have Holes: Vulnerabilities and Key Recommendations." https://www.far.ai/news/defense-in-depth

[4] UNICEF Office of Research -- Innocenti. "Guidance on AI and Children 3.0." December 2025. https://www.unicef.org/innocenti/reports/policy-guidance-ai-children

[5] University of Illinois Urbana-Champaign. "Illinois researchers examine teens' use of generative AI, safety concerns." December 2024 (IEEE S&P 2025). https://ischool.illinois.edu/news-events/news/2024/12/illinois-researchers-examine-teens-use-generative-ai-safety-concerns

[6] Leung, J. "The case for an immediate ban on children's LLM chatbot use: applying lessons from two decades of social media harm." *AI and Ethics* (Springer, 2026). https://link.springer.com/article/10.1007/s43681-026-01014-5

[7] European Parliament and Council. "Regulation (EU) 2024/1689 -- Artificial Intelligence Act, Annex III." https://artificialintelligenceact.eu/annex/3/

[8] ICO. "Age appropriate design: a code of practice for online services (Children's Code)." https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/childrens-information/childrens-code-guidance-and-resources/age-appropriate-design-a-code-of-practice-for-online-services/

[9] FTC. "Children's Online Privacy Protection Act -- 2025 Final Rule Amendments." https://www.ftc.gov/business-guidance/privacy-security/childrens-privacy

[10] FTC enforcement actions: Edmodo ($6M, 2023), Epic Games ($520M, 2022). https://blog.promise.legal/startup-central/coppa-compliance-in-2025-a-practical-guide-for-tech-edtech-and-kids-apps/

[11] 5Rights Foundation. "The AADC can protect children from AI -- if properly enforced." https://5rightsfoundation.com/the-age-appropriate-design-code-can-protect-children-from-ai-harms-if-properly-enforced/

[12] Federation of American Scientists. "An Agenda for Ensuring Child Safety in the AI Era." https://fas.org/publication/ensuring-child-safety-ai-era/

[13] European Parliament. "Children and generative AI." EPRS Briefing, 2025. https://www.europarl.europa.eu/RegData/etudes/ATAG/2025/769494/EPRS_ATA(2025)769494_EN.pdf

[14] Public Knowledge. "Kids & Teens Safety Regulations for AI Chatbots Could Backfire." https://publicknowledge.org/kids-teens-safety-regulations-for-ai-chatbots-could-backfire/

[15] Khan Academy. "Framework for Responsible AI in Education" and Khanmigo safety documentation. https://blog.khanacademy.org/khan-academys-framework-for-responsible-ai-in-education/

[16] Microsoft. "Responsible AI Mitigation Layers." https://techcommunity.microsoft.com/blog/azuredevcommunityblog/responsible-ai-mitigation-layers/4281878

[17] NSA/CISA. "Deploying AI Systems Securely." April 2024. https://media.defense.gov/2024/apr/15/2003439257/-1/-1/0/csi-deploying-ai-systems-securely.pdf

[18] UNICEF. "Pilot testing 'Policy Guidance on AI for Children.'" https://www.unicef.org/innocenti/projects/ai-for-children/pilot-testing-policy-guidance-ai-children

[19] American Enterprise Institute. "The Precautionary Principle, Safety Regulation, and AI." https://www.aei.org/research-products/report/the-precautionary-principle-safety-regulation-and-ai-this-time-it-really-is-different/

[20] World Economic Forum. "7 principles on responsible AI use in education." January 2024. https://www.weforum.org/stories/2024/01/ai-guidance-school-responsible-use-in-education/

[21] Iteration 01: Vision Model Safety -- Adversarial Image Attacks and Defenses. `docs/autoresearch/screenshot-analyzer/iteration-01-vision-model-safety.md`

[22] Iteration 02: Multimodal Prompt Injection via OCR/Vision Pipelines. `docs/autoresearch/screenshot-analyzer/iteration-02-prompt-injection-ocr.md`

[23] Iteration 03: LaTeX Sanitization. `docs/autoresearch/screenshot-analyzer/iteration-03-latex-sanitization.md`

[24] Iteration 04: Content Moderation for Minors. `docs/autoresearch/screenshot-analyzer/iteration-04-content-moderation-minors.md`

[25] Iteration 05: Rate Limiting. `docs/autoresearch/screenshot-analyzer/iteration-05-rate-limiting.md`

[26] Iteration 06: Privacy-Preserving Images. `docs/autoresearch/screenshot-analyzer/iteration-06-privacy-preserving-images.md`

[27] Iteration 07: Academic Integrity. `docs/autoresearch/screenshot-analyzer/iteration-07-academic-integrity.md`

[28] Iteration 08: Accessibility and Photo Input. `docs/autoresearch/screenshot-analyzer/iteration-08-accessibility-photo-input.md`

[29] Iteration 09: Error Handling and Degradation. `docs/autoresearch/screenshot-analyzer/iteration-09-error-handling-degradation.md`

[30] Iteration 10: End-to-End Attack Simulation and Defense Verification. `docs/autoresearch/screenshot-analyzer/iteration-10-e2e-attack-simulation.md`

[31] Cena DPIA. `docs/compliance/dpia-2026-04.md`

[32] Cena Children's Privacy Notice. `docs/legal/privacy-policy-children.md`

[33] Cena Ethical Persuasion and Digital Wellbeing Research. `docs/ethical-persuasion-digital-wellbeing-research.md`

[34] OpenAI. "Introducing the Child Safety Blueprint." April 2026. https://openai.com/index/introducing-child-safety-blueprint/

[35] NEA. "Five Principles for the Use of Artificial Intelligence in Education." https://www.nea.org/resource-library/artificial-intelligence-education/v-five-principles-use-artificial-intelligence-education
