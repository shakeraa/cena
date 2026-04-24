# Iteration 8 (Supplement) -- Pedagogical Controversy: The Digital Divide and Photo-Based Input

**Series**: Screenshot Question Analyzer -- Defense-in-Depth Research
**Iteration**: 8 of 10 (Controversy Supplement)
**Date**: 2026-04-12
**Type**: Pedagogical Controversy -- Steel-manned debate with evidence on both sides
**Scope**: Whether photo-based question input creates a two-tier system that excludes under-resourced Israeli students

---

## Table of Contents

1. [The Objection, Steel-Manned](#1-the-objection-steel-manned)
2. [Evidence FOR the Objection (Photo Input Widens the Gap)](#2-evidence-for-the-objection)
3. [Evidence AGAINST the Objection (Photo Input Narrows the Gap)](#3-evidence-against-the-objection)
4. [Cena's Architectural Position](#4-cenas-architectural-position)
5. [Design Mitigations](#5-design-mitigations)
6. [Open Questions](#6-open-questions)
7. [Sources](#7-sources)

---

## 1. The Objection, Steel-Manned

> "Photo-based question input requires a smartphone with a decent camera. In Israel, Arab-sector and Bedouin students have lower smartphone ownership, slower internet, and older devices. Building a feature that requires a good camera creates a two-tier system: wealthy students get AI-powered tutoring, under-resourced students get a text box."

This is not a fringe concern. It maps directly onto one of the most documented inequalities in Israeli education.

### 1.1 The Israeli Digital Divide in Numbers

Israel's overall smartphone penetration reached 91% by 2025, with 10.4 million cellular connections active against a population of 9.45 million -- 110% penetration ([DataReportal, Digital 2025: Israel](https://datareportal.com/reports/digital-2025-israel)). On the surface, this suggests universal access. It does not.

The gap lies beneath the national average. As of 2018, roughly 50% of Arab society in Israel had access to the internet through a computer, compared with approximately 80% of Jewish society ([ACIT, Digital Gaps and Accessibility for Arab Society](https://www.acitaskforce.org/resource/digital-gaps-and-accessibility-for-arab-society-from-connectivity-to-skills-and-services/)). While the absolute numbers have improved since then, the structural gap persists: around 25% of Arab populations in Israel rely on mobile phones as their primary and often sole means of internet access, compared to Jewish populations that overwhelmingly have fixed broadband at home ([Internet Society Pulse, 2025](https://pulse.internetsociety.org/en/blog/2025/04/studies-highlight-variances-in-israels-internet-performance-connectivity/)).

For Bedouin communities in the Negev, the situation is qualitatively different from the rest of the country. The Israeli government allocated NIS 16 million (approximately $4.67 million) to bring wireless internet to nine Bedouin regional councils -- Arara, Al-Kason, Rahat, Neve Midbar, Ksaife, Hura, Lakiya, Tel Sheva, and Segev -- as part of a five-year plan (2017-2021). The pilot program in Arara attracted approximately 3,000 daily users despite requiring daily re-registration, demonstrating both demand and infrastructural scarcity ([Calcalist Tech, 2020](https://www.calcalistech.com/ctech/articles/0,7340,L-3835713,00.html)). Many unrecognized Bedouin villages remain entirely off the grid: no permanent electricity, no fixed-line internet, and no government plans to connect them ([IWGIA, Indigenous World 2025: Bedouin in the Negev-Naqab](https://iwgia.org/en/bedouin_negev_naqab/5654-iw-2025-bedouin.html)).

### 1.2 Infrastructure Is Not Just "Connectivity"

Even where mobile connections exist, quality varies dramatically by geography. A 2025 study by the Israel Internet Association (ISOC-IL) and 7amleh found that:

- In the Negev, 42% of Arab residents rely on cellular-only connectivity, compared to 27% in the Triangle region, 20.2% in the Galilee, and 24% in mixed cities ([Internet Society Pulse, 2025](https://pulse.internetsociety.org/en/blog/2025/04/studies-highlight-variances-in-israels-internet-performance-connectivity/)).
- Areas with low Palestinian populations, such as Nof HaGalil, have extensive fiber-optic infrastructure, while predominantly Palestinian cities like Nazareth lack such infrastructure entirely and were excluded from Israeli Ministry of Communications tenders.
- Despite being separated by a single street, Nazareth has limited 3G access while Nof HaGalil receives 5G services. Some areas in the Negev experience complete signal loss.
- Close to 700,000 Palestinian citizens of Israel (roughly one-third) depend on a single fixed-broadband provider, Bezeq, creating a fragile service monopoly.

### 1.3 The Bagrut Gap

The educational stakes are enormous. Arab students are 30% less likely to be eligible for a Bagrut matriculation certificate than Jewish students -- a consistent finding across multiple years of data ([MDPI Education Sciences, 2022](https://www.mdpi.com/2227-7102/12/8/545)). In 2006-2007, only 35.6% of Arab-sector and 43.7% of Druze-sector students earned the certificate, compared to significantly higher rates in the Jewish sector ([Taub Center, Educational Inequality in Israel](https://taubcenter.org.il/wp-content/uploads/educationinequalityinisraeleng.pdf)). While the gap narrowed from 17 percentage points in 2000 to 12 in 2015, PISA 2022 data shows that Israel has the widest educational gap between Hebrew-speaking and Arabic-speaking students among all OECD countries, with socioeconomic status accounting for 20% of the variance in mathematics performance -- versus 15% across the OECD average ([OECD PISA 2022, Israel Country Note](https://www.oecd.org/en/publications/pisa-2022-results-volume-i-and-ii-country-notes_ed6fbcc5-en/israel_056c6cf0-en.html)).

The average performance difference between advantaged and disadvantaged Israeli students is 124 points in mathematics (OECD average: 93) and 118 points in reading (OECD average: 89). Only 8% of disadvantaged students in Israel score in the top quarter -- below the OECD average of 10%.

**This is the terrain on which Cena operates.** Any feature that correlates with device quality or connectivity speed risks landing on the fault line of Israel's most consequential educational inequality.

---

## 2. Evidence FOR the Objection

*Photo input widens the gap.*

### 2.1 UNESCO: The Digital Divide Is Becoming an AI Divide

UNESCO's position is unambiguous: "The digital divide is rapidly becoming an AI divide. Without electricity, meaningful connectivity and affordable devices, millions will be left behind" ([UNESCO, Bridging the Digital Divide, 2025](https://www.unesco.org/en/right-education/digitalization)). Globally, 2.6 billion people (32% of the world population) still lack internet access, with 1.8 billion of them in rural areas. Only 40% of primary schools and 65% of upper secondary schools worldwide are connected to the internet ([UNESCO GEM Report 2023: Technology in Education](https://gem-report-2023.unesco.org/)).

The UNESCO GEM 2023 report on technology in education specifically warned that "access to technology and connectivity are unequally distributed," creating a widening digital gap among learners. The cost of moving to basic digital learning in low-income contexts would add 50% to existing financing gaps for SDG 4 targets. The message is clear: technology-first features in education carry an inherent risk of exclusion.

### 2.2 Feature Complexity as a Barrier to Entry

Feature creep in educational software has documented equity consequences. Each additional feature raises the floor of technical competence and device capability required to use the product. Research on software usability shows that 56% of consumers report feeling overwhelmed by post-purchase complexity, and each new capability increases the learning curve, deterring adoption among less tech-savvy populations ([ProductPlan, Feature Creep](https://www.productplan.com/glossary/feature-creep/); [ProductSchool, Avoiding Feature Creep](https://productschool.com/blog/product-strategy/avoiding-feature-creep-tips-to-keep-your-product-focused)).

In education specifically, the U.S. Department of Education's 2024 National Educational Technology Plan identifies three distinct divides: the access divide (who has devices), the design divide (whether tools were built for diverse learners), and the use divide (whether students engage actively or passively). Photo input potentially fails on all three: it requires a capable device (access), it was designed around the assumption of a modern smartphone (design), and it enables passive use -- snap and get an answer -- more readily than typed input, which at least forces the student to formulate the question ([US Dept. of Education, 2024 NETP](https://technical.ly/civic-news/digital-divide-national-educational-technology-plan-2024/)).

### 2.3 The "Fancy Feature for Rich Kids" Criticism

This is the bluntest version of the objection: a photo-input feature with AI vision processing is a showcase feature that looks great in demos, appeals to tech-savvy families who already have good devices and fast connections, and does nothing for the student in Rahat typing on a cracked-screen Android phone with 3G. Worse, it signals to under-resourced students that the product was not designed for them -- that they are the afterthought users who get the "fallback" experience.

This criticism has force because Israel's private tutoring market already tracks socioeconomic lines. State-sponsored supplementary education is available mostly to the Arab and low-socioeconomic population, while private extended education is more prevalent in the Jewish sector and predominantly among those in high-socioeconomic schools, making it "a mechanism that can perpetuate social reproduction" ([Springer, Types of Extended Education in View of the Socioeconomic/Ethnic Intersection in Israel, 2024](https://link.springer.com/chapter/10.1007/978-3-658-47630-4_9)). If Cena's best feature requires the best hardware, it replicates this pattern digitally.

### 2.4 Bedouin Community Connectivity Is Not a Solved Problem

Despite the NIS 16 million government investment, the digital infrastructure in Bedouin communities remains fragile. The Bedouin population has been "subject to systematic exclusion, discrimination, and neglect since the establishment of the State of Israel in 1948 in all aspects of life, including infrastructure, education, and health services" ([Tandfonline, The Capabilities Divide: ICT Adoption and Use among Bedouin in Israel, 2024](https://www.tandfonline.com/doi/full/10.1080/19452829.2024.2370417)). Unrecognized villages -- home to a significant portion of the Negev Bedouin population -- have no plans for permanent infrastructure connection. A feature that requires uploading a high-resolution photograph over a mobile connection may be functionally unavailable to these students even if they technically own a phone.

### 2.5 Lessons from Low-Resource Contexts Globally

Research on mobile learning in developing countries confirms that adoption is highest when the barrier to using technology is low and users have reason to trust the application ([PMC, A Mobile Learning Framework for Higher Education in Resource Constrained Environments, 2022](https://pmc.ncbi.nlm.nih.gov/articles/PMC9127289/)). The World Bank's review of EdTech in developing countries found that smartphone-dependent features can reinforce existing inequalities when school facilities and local teaching resources are insufficient ([World Bank, EdTech in Developing Countries](https://openknowledge.worldbank.org/server/api/core/bitstreams/6ac08b1a-d072-4727-8678-d42bbee86a8a/content)). Successful implementations take a "problem-first approach rather than a techno-solutionist approach." Photo input, from this lens, is a solution looking for a problem that typed input already solves.

---

## 3. Evidence AGAINST the Objection

*Photo input narrows the gap when designed correctly.*

### 3.1 Smartphone Penetration Among Israeli Teens Is Near-Universal

Israel's smartphone adoption rate reached 91% by 2025 ([Statista, Israel: Smartphone Penetration 2020-2029](https://www.statista.com/statistics/974326/smartphone-user-penetration-in-israel/)), with 99.9% of mobile connections classified as broadband (3G, 4G, or 5G) ([DataReportal, Digital 2025: Israel](https://datareportal.com/reports/digital-2025-israel)). Among teenagers specifically, penetration rates are even higher than adult averages across all sectors: mobile phones are the one category of digital access where the Arab sector approaches parity with the Jewish sector. The ISOC-IL data itself shows that even in the Negev, where fixed-broadband is scarce, mobile phone ownership is high -- the 42% cellular-only reliance figure means they have phones, they just lack alternatives.

A 2024 study on Bedouin ICT adoption found that "access to technology (PCs, laptops, and smartphones) has been on the rise in recent years" even in under-resourced communities, with smartphone ownership growing fastest among younger demographics ([Tandfonline, The Capabilities Divide, 2024](https://www.tandfonline.com/doi/full/10.1080/19452829.2024.2370417)). The digital divide in the Bedouin sector is primarily about fixed broadband and computer access, not about phone ownership per se.

### 3.2 Photo Input Degrades Gracefully to Typed Input

This is the strongest architectural counterargument. Cena's Path B pipeline (photo to LaTeX to CAS to step-solver) is an enhancement layer on top of Path A (typed question to parser to CAS to step-solver). If the photo upload fails -- due to a bad camera, slow connection, low-quality image, or no camera at all -- the student is not locked out. They type the question. The typed input goes through the same AI-powered CAS pipeline, receives the same step-by-step solution, and accesses the same pedagogical engine.

This is the difference between a gating feature and an additive feature. A gating feature removes capability from users who cannot access it (e.g., if photo input were the only way to enter questions). An additive feature provides an additional pathway that some users will find more convenient without removing anything from those who do not use it. Photo input is additive.

Progressive enhancement -- building a baseline experience that works for everyone and layering richer capabilities on top -- is the established best practice for inclusive web design. The W3C explicitly defines this as starting with "a baseline of usable functionality, then increasing the richness of the user experience step by step by testing for support for enhancements before applying them" ([W3C, Graceful Degradation vs. Progressive Enhancement](https://www.w3.org/wiki/Graceful_degradation_versus_progressive_enhancement)). Cena's architecture follows this pattern.

### 3.3 Typed Input Is Still AI-Powered Tutoring

The objection implies that without photo input, under-resourced students get a lesser product. This is false. The "text box" that the objection dismisses is not a dumb text box. It is a natural-language and LaTeX input field connected to the same Gemini-powered parsing, the same CAS validation chain (MathNet, SymPy, Wolfram), the same step-by-step solver, the same hint system, and the same mastery tracking. The photo is merely one way to get a question into the system. The tutoring quality is identical regardless of input method.

### 3.4 The Real Divide Is Access to Human Tutors

The most expensive resource in Israeli education is not a smartphone camera -- it is a qualified human tutor. Private tutoring costs 80-150 NIS per 45-minute session. Wealthy local authorities, strong parents' associations, and private institutions provide additional funding for support programs, modern technology, extracurricular activities, smaller class sizes, and private tutoring ([Taub Center, Educational Inequality in Israel](https://taubcenter.org.il/wp-content/uploads/educationinequalityinisraeleng.pdf)). Private extended education is disproportionately available in the Jewish sector and high-socioeconomic schools ([Springer, Types of Extended Education, 2024](https://link.springer.com/chapter/10.1007/978-3-658-47630-4_9)).

Cena's core value proposition is replacing the 80-150 NIS/session human tutor with a free or low-cost AI tutor accessible from any device. This is inherently equalizing. The student in Rahat who cannot afford a private tutor can access unlimited, step-by-step, pedagogically scaffolded math instruction on the same cracked-screen Android phone. Photo input is one convenience feature; the platform's entire existence narrows the tutoring divide.

### 3.5 Not Building Features Also Perpetuates Inequality

There is a subtler argument: if photo input genuinely reduces friction for entering complex mathematical expressions (integrals, matrices, piecewise functions) -- which it does -- then refusing to build it in the name of equity means denying an improvement to all students, including the majority of Arab-sector students who do have adequate smartphones. Equity achieved by withholding capability is a Pyrrhic victory. The correct response is to build the feature and separately address the access gap.

---

## 4. Cena's Architectural Position

### 4.1 Additive, Not Gating

Photo input in Cena is architecturally additive. It improves the experience for students who can use it without degrading the experience for students who cannot. The full decision matrix:

| Capability | Photo Path (Path B) | Typed Path (Path A) | Parity? |
|---|---|---|---|
| Question entry | Camera snap | Typed LaTeX or natural language | Path A is full-featured |
| AI parsing | Gemini 2.5 Flash vision | Gemini text parsing | Same model family |
| CAS validation | MathNet -> SymPy -> Wolfram | MathNet -> SymPy -> Wolfram | Identical |
| Step-by-step solver | Same engine | Same engine | Identical |
| Hint system | Same engine | Same engine | Identical |
| Mastery tracking | Same telemetry | Same telemetry | Identical |
| Pedagogical quality | Identical | Identical | Identical |

The only difference is the input method. No tutoring capability is gated behind photo input.

### 4.2 Manual Input Remains Full-Featured

Cena's typed input is not a "fallback." It supports:

- Natural language question entry ("Find the derivative of x squared")
- LaTeX input with live preview (for students comfortable with notation)
- A visual equation builder with buttons for common operations (for students who need guided input)
- Voice-to-text input (planned, for accessibility)

Each of these input methods connects to the same backend pipeline. The photo path adds a fifth input method. It does not replace or diminish the other four.

---

## 5. Design Mitigations

Even though photo input is additive, Cena can and should actively mitigate the access gap through deliberate design choices.

### 5.1 Low-Bandwidth Mode

Detect connection quality on the client side (via the Network Information API or timed probe requests). When the connection is slow:

- Compress uploaded images aggressively before transmission (target 100-200 KB using WebP or progressive JPEG at 60-70% quality; the OCR model does not need photographer-grade resolution)
- Show a progress indicator with estimated time
- Offer a "Type it instead" prompt after 3 seconds of upload stall
- Cache the vision model result client-side so re-submission is instant if the connection drops mid-response

Progressive JPEG is particularly suited here: the first scan shows a blurry preview within the first 10-20% of the file transfer, so the student sees feedback immediately even on a slow connection. Each subsequent scan adds detail. The vision model can often extract sufficient LaTeX from a low-quality progressive scan, meaning the student may get results before the full image even finishes uploading.

### 5.2 Offline Question Entry

For students with intermittent or no connectivity (a real scenario in unrecognized Bedouin villages):

- Allow questions to be entered and queued offline using the typed input path
- When connectivity resumes, batch-submit queued questions
- For photo input: allow the photo to be captured offline and queued for upload when a connection becomes available
- Store the step-solver results locally so previously solved question types are available offline

### 5.3 School Computer Webcam Support

Many Israeli schools have computer labs, even in under-resourced areas, where individual students lack smartphones. Cena should support:

- Webcam capture in the browser-based student web client (not just mobile camera)
- File upload from desktop (student photographs the question with any camera, transfers to the school computer, uploads the file)
- Clipboard paste of screenshots (for questions displayed on screen)

This expands the photo input pathway from "requires a personal smartphone" to "requires any device with any camera, including a shared school computer."

### 5.4 Image Compression for Slow Connections

Technical specification for the upload pipeline:

- Client-side resize to maximum 1600x1200 pixels before upload (sufficient for OCR; most phone cameras produce 4000x3000+)
- Convert to WebP where supported (30-50% smaller than JPEG at equivalent quality), fall back to progressive JPEG
- Target upload size: 100-200 KB (achievable at 65% quality for a typical worksheet photo)
- At 3G speeds (1-2 Mbps typical), a 150 KB image uploads in under 1 second
- Server-side: accept any image format, normalize internally; do not reject low-quality images -- attempt OCR and report confidence

### 5.5 Partner with Bedouin Education Nonprofits for Device Access

Cena should establish partnerships with organizations working on digital access in the Negev:

- **NCF (Negev Coexistence Forum)**: Already runs digital literacy programs for Bedouin women in unrecognized villages
- **Ajeec-Nisped**: Arab-Jewish partnership organization focused on Negev community development
- **Sidreh**: Bedouin women's organization with educational programs
- **Tech for Social Good (T4SG)** programs: Device donation and recycling initiatives

These partnerships would focus on device provision (refurbished smartphones and tablets), connectivity (mobile hotspot lending), and digital literacy training that includes Cena as a use case. Cena's role is to ensure the product works well on the devices these organizations can provide -- typically older Android phones with limited storage and processing power.

### 5.6 Adaptive Quality Detection

The vision model pipeline should be designed to work with low-quality input:

- Accept blurry, poorly lit, and angled photographs (common with older phone cameras)
- When confidence is low, show the extracted expression and ask "Is this what you meant?" with an easy correction interface
- Track device capability metrics anonymously to identify the actual device profile of under-resourced users and optimize for those devices specifically

---

## 6. Open Questions

These are genuine open questions without clear answers. They require policy decisions, not engineering solutions.

### 6.1 Is Cena Responsible for Device Access?

Cena is an educational software product, not a telecommunications provider or hardware charity. Its direct responsibility is to ensure its product works on the widest possible range of devices. But is there a broader responsibility?

Arguments for: Cena positions itself as closing the tutoring gap. If its best features are inaccessible to the students who most need tutoring, it is failing its own mission. A company that profits from educational equity has an obligation to invest in the infrastructure that makes equity possible.

Arguments against: Cena cannot solve Israel's telecommunications infrastructure policy, housing policy in the Negev, or device affordability. Expanding scope to include hardware distribution and internet provision dilutes focus and may not be sustainable for a software company. The responsibility lies with the Israeli government, the Ministry of Communications, and the Ministry of Education.

Practical middle ground: Cena can advocate, partner, and optimize -- but it should not own the device access problem. It should own the "works on any device" problem.

### 6.2 Should Cena Actively Lobby for School Device Programs?

The Israeli Ministry of Education funds various technology programs for schools. Should Cena:

- Advocate for expanded device lending programs in Arab-sector and Bedouin schools?
- Provide data on student device profiles (anonymized) to demonstrate the gap?
- Participate in public policy discussions about digital equity in education?
- Offer preferential pricing (free tier, school licenses) to under-resourced schools?

This moves Cena from "software vendor" to "education advocate" -- a different role with different risks and rewards.

### 6.3 Does Typed Input Need to Be Better Than Photo Input?

An aggressive equity stance would argue that typed input should be the primary, best-supported, and most-invested-in pathway -- and photo input should be a convenience shortcut, not a flagship feature. This means:

- The equation builder should be excellent, not merely functional
- Natural language parsing should handle colloquial Arabic and Hebrew mathematical phrasing
- Auto-complete and suggestion features should make typed input nearly as fast as photo input
- Marketing should lead with "AI tutoring for every student" not "just snap a photo"

### 6.4 What Is the Minimum Viable Device?

Cena should define and publish a minimum supported device specification:

- What is the oldest Android version supported?
- What is the minimum screen size?
- What is the minimum camera resolution for photo input to work?
- What is the minimum connection speed for acceptable latency?

These decisions have equity implications. Supporting Android 8.0 (2017) instead of Android 12 (2021) reaches millions more users in the Arab sector, where device replacement cycles are longer. Testing should be conducted on actual low-end devices, not just emulators.

### 6.5 Should Cena Build an Arabic-First Experience?

Arabic is the primary language of 21% of Israel's population. If Cena's interface, error messages, mathematical terminology, and pedagogical content are designed English-first or Hebrew-first with Arabic as a translation afterthought, the product will feel alien to its most underserved users. An Arabic-first design process for at least the core tutoring flow would signal genuine commitment to equity.

### 6.6 How Should Cena Measure Its Own Equity Impact?

Without measurement, equity commitments are aspirational. Cena should track:

- Usage by geographic region (Negev, Galilee, Triangle, mixed cities, Jewish-sector cities)
- Input method distribution by region (photo vs. typed vs. equation builder)
- Average latency by region and connection type
- Dropout rate (session abandonment) by device age and connection quality
- Bagrut improvement rates by sector (the ultimate outcome metric)

If photo input usage is 80% in Tel Aviv and 5% in Rahat, the feature is de facto exclusive regardless of its technical availability.

---

## 7. Sources

1. DataReportal. "Digital 2025: Israel." January 2025. [https://datareportal.com/reports/digital-2025-israel](https://datareportal.com/reports/digital-2025-israel)

2. ACIT Task Force. "Digital Gaps and Accessibility for Arab Society: From Connectivity to Skills and Services." 2024. [https://www.acitaskforce.org/resource/digital-gaps-and-accessibility-for-arab-society-from-connectivity-to-skills-and-services/](https://www.acitaskforce.org/resource/digital-gaps-and-accessibility-for-arab-society-from-connectivity-to-skills-and-services/)

3. Internet Society Pulse. "Studies Highlight Variances in Israel's Internet Performance, Connectivity." April 2025. [https://pulse.internetsociety.org/en/blog/2025/04/studies-highlight-variances-in-israels-internet-performance-connectivity/](https://pulse.internetsociety.org/en/blog/2025/04/studies-highlight-variances-in-israels-internet-performance-connectivity/)

4. Calcalist Tech. "Israeli Government to Invest Millions in Bringing Bedouins Online." 2020. [https://www.calcalistech.com/ctech/articles/0,7340,L-3835713,00.html](https://www.calcalistech.com/ctech/articles/0,7340,L-3835713,00.html)

5. IWGIA. "The Indigenous World 2025: Bedouin in the Negev-Naqab." 2025. [https://iwgia.org/en/bedouin_negev_naqab/5654-iw-2025-bedouin.html](https://iwgia.org/en/bedouin_negev_naqab/5654-iw-2025-bedouin.html)

6. Ghanem, A. and Khatib, R. "The Capabilities Divide: ICT Adoption and Use among Bedouin in Israel." *Journal of Human Development and Capabilities*, 2024. [https://www.tandfonline.com/doi/full/10.1080/19452829.2024.2370417](https://www.tandfonline.com/doi/full/10.1080/19452829.2024.2370417)

7. OECD. "PISA 2022 Results (Volume I and II) -- Country Notes: Israel." 2023. [https://www.oecd.org/en/publications/pisa-2022-results-volume-i-and-ii-country-notes_ed6fbcc5-en/israel_056c6cf0-en.html](https://www.oecd.org/en/publications/pisa-2022-results-volume-i-and-ii-country-notes_ed6fbcc5-en/israel_056c6cf0-en.html)

8. Taub Center for Social Policy Studies. "Educational Inequality in Israel: From Research to Policy." [https://taubcenter.org.il/wp-content/uploads/educationinequalityinisraeleng.pdf](https://taubcenter.org.il/wp-content/uploads/educationinequalityinisraeleng.pdf)

9. UNESCO. "Bridging the Digital Divide and Ensuring Online Protection." 2025. [https://www.unesco.org/en/right-education/digitalization](https://www.unesco.org/en/right-education/digitalization)

10. UNESCO. "GEM Report 2023: Technology in Education." 2023. [https://gem-report-2023.unesco.org/](https://gem-report-2023.unesco.org/)

11. Springer. "Types of Extended Education in View of the Socioeconomic/Ethnic Intersection in Israel." 2024. [https://link.springer.com/chapter/10.1007/978-3-658-47630-4_9](https://link.springer.com/chapter/10.1007/978-3-658-47630-4_9)

12. MDPI Education Sciences. "The Correlation between Budgets and Matriculation Exams: The Case of Jewish and Arab Schools in Israel." 2022. [https://www.mdpi.com/2227-7102/12/8/545](https://www.mdpi.com/2227-7102/12/8/545)

13. Statista. "Israel: Smartphone Penetration 2020-2029." [https://www.statista.com/statistics/974326/smartphone-user-penetration-in-israel/](https://www.statista.com/statistics/974326/smartphone-user-penetration-in-israel/)

14. U.S. Department of Education. "National Educational Technology Plan 2024." [https://technical.ly/civic-news/digital-divide-national-educational-technology-plan-2024/](https://technical.ly/civic-news/digital-divide-national-educational-technology-plan-2024/)

15. W3C. "Graceful Degradation versus Progressive Enhancement." [https://www.w3.org/wiki/Graceful_degradation_versus_progressive_enhancement](https://www.w3.org/wiki/Graceful_degradation_versus_progressive_enhancement)

16. PMC / Springer. "A Mobile Learning Framework for Higher Education in Resource Constrained Environments." 2022. [https://pmc.ncbi.nlm.nih.gov/articles/PMC9127289/](https://pmc.ncbi.nlm.nih.gov/articles/PMC9127289/)

17. World Bank. "EdTech in Developing Countries: A Review of the Evidence." [https://openknowledge.worldbank.org/server/api/core/bitstreams/6ac08b1a-d072-4727-8678-d42bbee86a8a/content](https://openknowledge.worldbank.org/server/api/core/bitstreams/6ac08b1a-d072-4727-8678-d42bbee86a8a/content)

18. ProductPlan. "Feature Creep." [https://www.productplan.com/glossary/feature-creep/](https://www.productplan.com/glossary/feature-creep/)

19. Digital Promise. "A New Approach to Digital Equity: A Framework for States and Schools." 2024. [https://digitalpromise.org/2024/08/14/a-new-approach-to-digital-equity-a-framework-for-states-and-schools/](https://digitalpromise.org/2024/08/14/a-new-approach-to-digital-equity-a-framework-for-states-and-schools/)

20. Taub Center. "The Education System in Israel 2020-2024." [https://www.taubcenter.org.il/en/research/education-system-2024/](https://www.taubcenter.org.il/en/research/education-system-2024/)
