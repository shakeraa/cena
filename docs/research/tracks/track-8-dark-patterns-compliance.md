# Track 8 — Dark Patterns + Child-Safety Enforcement: Legal/Regulatory Risk Map for Cena

**Jurisdictions:** EU (GDPR/GDPR-K), UK (ICO Children's Code), US (COPPA, FTC Act §5, FERPA), Israel (PPL Amendment 13), Belgium, Netherlands.
**Audience served:** minors age ~13–18 (Bagrut, AP, SAT math/physics), EN/AR/HE.
**Date:** 2026-04-11.
**Track owner:** research-only.

---

## 1. Executive summary

Regulators across the EU, UK and US have converged on a single message in 2022–2026: **design choices that psychologically coerce minors — streaks, variable-ratio loot, nudged-off privacy, friend-matching on by default, guilt notifications — are now actionable, expensive, and increasingly criminal**. The trajectory between the Epic/Fortnite $520M settlement (Dec 2022), the Amazon Alexa kids $25M settlement (May 2023), the Edmodo ed-tech consent-laundering case (May 2023), the HoYoverse/Genshin loot-box settlement (Jan 2025), the FTC's final COPPA rule (Jan 2025 / effective June 2025 / compliance deadline April 2026), and the ICO's landmark £14.47M Reddit fine (Feb 2026) is unmistakable: **regulators are no longer chasing data-collection; they are chasing engagement design itself**.

For Cena, the good news is that the proposal's "zero dark-patterns" stance (point 7) is not just defensible — **it is now the regulatory floor, not a ceiling**. The bad news is that several otherwise-innocuous-sounding proposal points (world map, boss fights, daily challenge, tutor characters, co-op) sit in grey zones that become actionable the moment they cross specific lines. This track maps those lines.

---

## 2. Verified regulatory sources

1. **FTC v. Epic Games, Inc. (N.D.N.C. 2022)** — $275M COPPA civil penalty (DOJ complaint) + $245M FTC administrative order for dark patterns and unfair billing. Largest COPPA penalty in history. Covered: collecting data from known under-13 Fortnite players without verifiable parental consent; default-on voice/text chat matching children with strangers; deceptive button placement, single-click purchases during loading screens, charging for disputed purchases. ([FTC press release, 19 Dec 2022](https://www.ftc.gov/news-events/news/press-releases/2022/12/fortnite-video-game-maker-epic-games-pay-more-half-billion-dollars-over-ftc-allegations))
2. **United States v. Amazon.com, Inc. (W.D. Wash. 2023)** — $25M COPPA civil penalty for retaining children's Alexa voice recordings indefinitely and undermining parental deletion rights. Amazon barred from using deleted voice/geolocation data to train any model. ([FTC press release, 31 May 2023](https://www.ftc.gov/news-events/news/press-releases/2023/05/ftc-doj-charge-amazon-violating-childrens-privacy-law-keeping-kids-alexa-voice-recordings-forever); [DOJ release](https://www.justice.gov/archives/opa/pr/amazon-agrees-injunctive-relief-and-25-million-civil-penalty-alleged-violations-childrens))
3. **United States v. Edmodo, LLC (N.D. Cal. 2023)** — $6M COPPA penalty (suspended on inability to pay). First FTC action forbidding an ed-tech provider from requiring more personal data than necessary. Crucially, the FTC rejected Edmodo's "schools give consent on behalf of parents" defense because the company (a) didn't provide direct notice to parents and (b) used student data for contextual advertising, which is **outside the school-official exception's "limited educational context"**. Order required deletion of "Affected Work Product" — models/algorithms trained on unlawful data. ([FTC press release, 22 May 2023](https://www.ftc.gov/news-events/news/press-releases/2023/05/ftc-says-ed-tech-provider-edmodo-unlawfully-used-childrens-personal-information-advertising); [case docket](https://www.ftc.gov/legal-library/browse/cases-proceedings/202-3129-edmodo-llc-us-v))
4. **United States v. Cognosphere / HoYoverse (Genshin Impact), Jan 2025** — $20M settlement. Developer barred from selling loot boxes to under-16s without verifiable parental consent, charged with deceiving players about odds and pricing of loot-box prizes. First FTC loot-box case to settle post-Epic and establishes the **under-16 line** for loot-box age gating — notably higher than COPPA's 13. ([FTC press release, 17 Jan 2025](https://www.ftc.gov/news-events/news/press-releases/2025/01/genshin-impact-game-developer-will-be-banned-selling-lootboxes-teens-under-16-without-parental))
5. **FTC COPPA Final Rule (2025)** — 16 CFR Part 312 amendments, published 22 Apr 2025 in the Federal Register, effective 23 Jun 2025, compliance deadline **22 Apr 2026**. Key changes: (a) **separate verifiable parental consent required for any third-party disclosure for targeted advertising** — default-off behavioral ads; (b) biometric and government ID added to "personal information"; (c) explicit **data minimization and retention limits** — operators cannot hold personal information longer than necessary for a specific purpose; (d) Safe Harbor program transparency. ([FTC press release, 16 Jan 2025](https://www.ftc.gov/news-events/news/press-releases/2025/01/ftc-finalizes-changes-childrens-privacy-rule-limiting-companies-ability-monetize-kids-data); [Federal Register, 22 Apr 2025](https://www.federalregister.gov/documents/2025/04/22/2025-05904/childrens-online-privacy-protection-rule))
6. **ICO v. Reddit, Inc. — Monetary Penalty Notice, 24 Feb 2026 — £14,470,000** — largest Children's Code enforcement to date. Two findings: (a) no robust age assurance — self-declaration is bypassable and therefore insufficient as a lawful basis for processing under-13 data; (b) no DPIA for children. The ICO explicitly held that **robust age assurance is a legal requirement, not a best practice**. ([ICO press release, 24 Feb 2026](https://ico.org.uk/about-the-ico/media-centre/news-and-blogs/2026/02/reddit-issued-with-1447m-fine-for-children-s-privacy-failures/))
7. **ICO Age Appropriate Design Code (Children's Code)** — 15 standards, enforced since Sep 2021. Relevant standards for Cena: #1 Best interests of the child; #2 DPIA; #4 Transparency; #6 Policies & community standards; #7 Default settings (high privacy); #8 Data minimisation; #9 Data sharing; #10 Geolocation; #11 Parental controls; **#12 Profiling (off by default)**; **#13 Nudge techniques (do not use to weaken privacy or harm wellbeing)**; #15 Online tools (for children to exercise rights). ([ICO Code Standards](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/childrens-information/childrens-code-guidance-and-resources/age-appropriate-design-a-code-of-practice-for-online-services/code-standards/))
8. **Belgische Kansspelcommissie / Belgian Gaming Commission, "Research Report on Loot Boxes," April 2018** — declared paid loot boxes in Star Wars Battlefront 2, FIFA 18, Overwatch, CS:GO games of chance under the Belgian Gambling Act; operating them without a licence is a criminal offence. EA eventually removed FUT packs from Belgium to avoid prosecution. ([Belgian Gaming Commission, 2018 report summary via Variety](https://variety.com/2018/gaming/news/belgian-gaming-commission-loot-boxes-1202806402/))
9. **Raad van State (Council of State, NL) — Electronic Arts v. Kansspelautoriteit, ruling of 9 Mar 2022** — overturned a €10M fine against EA, holding that FUT loot boxes are not a stand-alone game of chance because they exist within a game of skill (FIFA). Narrows Dutch scope of loot-box regulation; does **not** disturb the Belgian position. ([CMS LawNow analysis, 2022](https://cms-lawnow.com/en/ealerts/2022/03/dutch-court-rules-fifa-loot-boxes-not-a-game-of-chance-revokes-ea-penalty); [Xiao & Declerck, Gaming Law Review 2023](https://www.liebertpub.com/doi/10.1089/glr2.2023.0020))
10. **GDPR Article 8 + Member State variations** — default digital-consent age = 16; member states may lower to 13. Current floor: UK/Spain/Denmark/Poland/Sweden = 13; Austria = 14; France = 15; Germany/NL = 16. Controllers must make "reasonable efforts" to verify parental consent, proportional to risk. ([Article 8 GDPR text](https://gdpr-info.eu/art-8-gdpr/))
11. **Israel Privacy Protection Law, Amendment 13** — Knesset approved 5 Aug 2024, effective 14 Aug 2025. Aligns Israeli PPL closer to GDPR: mandatory Privacy Protection Officers, new "Information of Special Sensitivity" category, stronger enforcement. **No standalone minors regime yet** — minors are protected via the Legal Capacity and Guardianship Law, 5722-1962 (any legal act by an under-18 requires guardian consent), which covers data-processing consent by implication. ([IAPP coverage](https://iapp.org/news/a/israel-marks-a-new-era-in-privacy-law-amendment-13-ushers-in-sweeping-reform); [Library of Congress Global Legal Monitor, Nov 2025](https://www.loc.gov/item/global-legal-monitor/2025-11-17/israel-amendment-to-privacy-protection-law-goes-into-effect/))
12. **FERPA "School Official" exception, 34 CFR §99.31(a)(1)(i)(B)** — an ed-tech vendor can receive student PII without parental consent only if it (1) performs an institutional service, (2) has a legitimate educational interest, (3) is under the school's direct control for that use, and (4) does not redisclose or re-use the data. **Edmodo demonstrates that using student data for contextual advertising breaks (4) instantly.** ([US DoE Vendor FAQ](https://studentprivacy.ed.gov/sites/default/files/resource_document/file/Vendor%20FAQ.pdf))

---

## 3. Top 3 findings (most important risks for Cena)

### Finding 1 — The "zero dark-patterns" stance is now the legal floor, not a marketing stance

The FTC (Epic, Genshin, Amazon, Edmodo) and the ICO (Reddit, Snap AI) have, between 2022 and 2026, moved the line from "don't lie to kids" to **"don't design engagement that treats kids' attention as harvestable"**. The ICO's Children's Code standard #13 bans nudge techniques that weaken privacy or harm wellbeing. The FTC's 2025 COPPA final rule forces targeted ads off by default. Epic was fined for default-on voice chat and deceptive purchase buttons — not for a single line of code that "violated" any bright-line rule, but because the *totality* of the design harmed kids. Cena's proposal point 7 ("reject Duolingo streak, no confetti, no variable-ratio") is therefore not a moral pose, it is a **survivability requirement**. Any drift back toward streaks, guilt notifications, or variable-ratio loot will put Cena into the exact category the FTC is hunting.

### Finding 2 — The "school-official" shield is a trap unless Cena contracts with schools directly

Cena's EN/AR/HE Bagrut/AP/SAT positioning naturally leans toward school partnerships. The Edmodo case is the clearest warning in the record: **calling yourself a school-facing ed-tech does not give you COPPA cover if (a) you don't provide direct notice to parents, or (b) you use student data for anything outside narrow classroom instruction, including contextual advertising, recommender training, or public-facing ranking**. If Cena ever trains models on student performance data for any purpose other than the specific instructional purpose for which the data was collected, the FTC will treat the resulting model as "Affected Work Product" and require its deletion (as it did with Edmodo). This single risk — ML model contamination — can permanently destroy a year of work.

### Finding 3 — Age assurance by self-declaration is officially dead (UK) and highly risky (US/EU)

Before the Reddit fine, "enter your birthday" was the industry norm. The ICO's Feb 2026 Monetary Penalty Notice against Reddit explicitly said self-declaration is **bypassable and therefore an invalid lawful basis for processing under-13 data**. The ICO fined Reddit £14.47M specifically because its age gate was weak — not because Reddit *wanted* under-13s, but because its controls were *easily circumvented*. For Cena (which will have real 13-year-olds in the database by design), the implication is: every under-18 needs an **age-assurance mechanism proportional to the processing risk**. Self-declaration plus a parent-email double opt-in is the practical minimum for 2026. Self-declaration alone is now a known-bad pattern with a £14.47M precedent.

---

## 4. Ranking the 10 proposal points by legal risk

| # | Proposal | Legal risk | Why |
|---|---|---|---|
| 7 | Zero streak/confetti/variable-ratio | **Zero — mandatory** | This is now the regulatory floor. Keep it. |
| 5 | Co-op not PvP | **Low** | Safer than PvP, but "friend matching" must be opt-in and off by default (Epic precedent). |
| 4 | Sandbox physics | **Low** | Pure construction play. Watch for any user-generated content sharing = content-moderation duty. |
| 8 | MathLive + haptics | **Low** | Haptics are not regulated; keep them neutral, not reward-coded. |
| 3 | Named tutor character per language | **Low-medium** | Safe unless the tutor is framed as a "friend" who "misses you" — that's a Duolingo-owl pattern and crosses ICO #13 nudge. |
| 1 | World map (session-as-game-loop) | **Medium** | Fine if the map is navigational. Becomes risky if progress is gated by streak or variable unlock. |
| 10 | Boss fights as Bagrut synthesizers | **Medium** | Fine if "boss" = hard synthesis question. Risky if it uses jeopardy mechanics, loss aversion, or locks content behind failure streaks. |
| 6 | Daily challenge as Wordle | **Medium-high** | Wordle is one puzzle per 24h — benign. But "daily" + streak = automatic Duolingo pattern. **Must not track or display a daily-challenge streak**. Push notifications for missed days = ICO #13 violation. |
| 9 | Rejection of Duolingo streak | **Zero — mandatory** | Same as 7. Keep loudly. |
| 2 | Misconception-as-enemy | **Medium-high** | Pedagogically brilliant; legally loaded. "Defeat the enemy" framing is fine. Collecting per-student misconception profiles and using them to **train a recommender that drives engagement time** is exactly what Edmodo was punished for (repurposing educational data into an engagement model). Must be kept strictly instructional, not behavioral. |

---

## 5. Contradictions — where the proposal is wrong or I am wrong

### a) Cena thinks point 6 (Daily challenge as Wordle) is fine, evidence says it is the highest-risk point

Wordle's genius is that it has **no streak pressure** — you can skip a day and lose nothing. Most "Wordle clones" in ed-tech bolt on a visible streak counter, push reminders, and leaderboard shaming — the exact mix the ICO would flag under standards #7, #11, #13. Unless Cena guarantees (a) no visible streak, (b) no push notifications for missed days, (c) no leaderboard on the daily challenge, this point becomes the riskiest of all 10, not the quirkiest.

### b) Cena thinks point 2 (Misconception-as-enemy) is a pedagogy win, evidence says it is a data-retention trap

The Edmodo decree required deletion of ML models trained on student personal data outside the narrow classroom purpose. A "misconception profile" per student is personal data about a minor's cognitive state — probably sensitive under Israel's Amendment 13 ISS definition. If the profile is retained past the instructional interaction, re-used to train a global recommender, or exposed to other students (even anonymised), Cena is exactly where Edmodo was in 2023.

### c) Cena thinks point 10 (Boss fights) is safe because Bagrut is real, evidence says the framing matters more than the content

Calling a hard question a "boss fight" is fine. Attaching **loss aversion** mechanics (lose progress, lose streak, lose unlocked characters, lose social standing) is the exact pattern the FTC hit Epic for with default-on matchmaking and purchase-friction. Boss fights must have **zero punitive consequence** on failure — just retry.

### d) Legally fine, the proposal might fear it: point 3 (Named tutor character)

Named tutor characters per language are not inherently a dark pattern. Duolingo's owl is only problematic because of what it *says* and *when* it notifies. A named, silent, non-nagging tutor (think: "Dr. Nur," "Morah Tal," "Mr. Adam") is actually a **positive signal** under ICO #4 (transparency) and #6 (age-appropriate communication) — it makes the learning agent comprehensible to a minor. Keep it.

### e) Possibly fine, Cena thinks it is risky: point 4 (Sandbox physics)

Sandbox creation is not regulated unless it involves user-generated content sharing or social features. As long as Cena's sandbox is single-user and does not publish creations to a feed, it is in the safest category in the entire proposal.

---

## 6. Kill-list — mechanics that must NOT ship

These should be removed on legal grounds before any launch in EU/UK/US/IL:

1. **Any visible streak counter tied to daily-return pressure.** (ICO #13, Duolingo precedent, Reddit fine trajectory.)
2. **Push notifications framed as guilt, loss, or social shame.** ("You're about to lose your streak", "[Tutor] is sad you didn't practice", "Your friends are ahead of you".) ICO #13 + FTC Section 5 unfairness.
3. **Variable-ratio reward loops for answer correctness.** (Belgian loot-box precedent + FTC Genshin settlement. Even without money changing hands, variable-ratio reinforcement on minors has been flagged as psychologically manipulative in all three jurisdictions.)
4. **Default-on social/co-op matchmaking.** (Epic $245M precedent.) Co-op must be explicit opt-in per session.
5. **Behavioral advertising of any kind, to anyone under 18.** (COPPA 2025 final rule, ICO #12 profiling, Edmodo decree.) This is non-negotiable.
6. **Self-declaration-only age gates.** (Reddit £14.47M precedent.) Must add a second factor — parent email, school SSO, or a third-party attribute provider.
7. **Retention of per-student misconception profiles past the instructional interaction** or use of them to train global engagement models. (Edmodo "Affected Work Product" precedent.)
8. **Loss-aversion mechanics on failed questions** (lose XP, lose characters, lose map progress). FTC Section 5 unfair/deceptive + ICO #13.
9. **Leaderboards or public ranking of minors by performance.** ICO #6 + Article 8 GDPR profiling; also violates Israel PPL ISS (profile data about minors' academic performance).
10. **In-game currency → real money → question packs** or any mechanic that resembles a paid randomized reward. Belgian Gaming Commission declares this criminal without a gambling licence.

---

## 7. Must-have-list — compliance artifacts Cena must produce before launch

### Regulatory artifacts (mandatory for EU/UK/IL launch)

1. **Data Protection Impact Assessment (DPIA)** covering all processing of minors' data. Required by GDPR Art. 35, Children's Code #2, and now Israel PPL Amendment 13. The ICO explicitly faulted Reddit for not doing one.
2. **Separate Child-Appropriate Privacy Notice** — plain language, multi-language (EN/AR/HE), age-banded (8-10, 10-13, 13-16, 16-18), delivered at the point of collection. ICO #4 + GDPR Art. 12(1).
3. **Verifiable Parental Consent (VPC) flow** for under-13 users in the US (COPPA) and up to 16 in default GDPR jurisdictions. Email-plus-credit-card-0-charge, government ID, or knowledge-based auth are the currently accepted methods.
4. **Age-assurance mechanism** stronger than self-declaration. Minimum: email double opt-in + parent contact verification. Preferred for school installs: SSO via school LMS.
5. **Data Subject Access Request (DSAR) endpoint** — machine-readable export of a minor's data within 30 days (GDPR Art. 15) and deletion (Art. 17). Parent must be able to trigger on child's behalf. Endpoint must be discoverable from the child's profile (Children's Code #15).
6. **Data retention & deletion policy** with explicit per-field retention periods. Required by the FTC 2025 final COPPA rule.
7. **Privacy Protection Officer (PPO) appointment** — now mandatory under Israel PPL Amendment 13 for any operator processing minors' data at scale. Also satisfies GDPR Art. 37 DPO requirement.

### Ed-tech specific artifacts

8. **FERPA School Official Agreement template** — if Cena sells to US schools. Must include: direct-control clause, purpose-limited use clause, no-redisclosure clause, deletion-on-request clause. Without it, Cena cannot rely on the school-official exception.
9. **Explicit contractual prohibition on using student data for model training outside the specific instructional purpose** — mitigates the Edmodo "Affected Work Product" precedent.
10. **Classroom vs. consumer mode split** in the product itself — a school-install flow gated by school SSO, versus a consumer flow gated by parental consent. Mixing these is the Edmodo trap.

### Self-regulatory hygiene

11. **Dark-pattern design review checklist** — run before every UX change. Should map each proposed mechanic to ICO standards #6/#7/#11/#12/#13 and flag failures.
12. **Notification-content review process** — every push notification string must pass a "guilt test": does it create loss aversion, FOMO, or social shame? If yes, block.
13. **Engagement-metric governance** — do not track "daily-active-minor retention" as a north-star KPI. ICO has explicitly said engagement-maximisation against minors is a Children's Code violation regardless of whether it is accompanied by advertising.

---

## 8. Bright-line "do not cross" rules for 2026

For quick reference, the rules that will get Cena into the FTC or ICO enforcement pipeline fastest:

1. **Never show a streak counter that can go to zero.** Show cumulative progress instead.
2. **Never send a notification that personifies loss or sadness.** ("Your tutor misses you" = fine'd.)
3. **Never collect data you don't need for the current question session.** Data minimisation is now the default posture.
4. **Never re-use student interaction data to train anything outside the specific instructional objective** it was collected for.
5. **Never default social features to on.** Voice, chat, matchmaking, friend suggestion — all opt-in.
6. **Never accept self-declaration alone as an age check.** Always add a second factor.
7. **Never show minors behavioral ads, ever, by anyone.** Even "educational recommendations" should not be built on profiling.
8. **Never retain under-18 data longer than the retention policy declares.** Document and honor it.
9. **Never use variable-ratio rewards**, even without money. The Belgian + Dutch literature is moving in the direction of treating variable-ratio reinforcement on minors as harmful regardless of the stake.
10. **Never ship an engagement feature without a child-wellbeing review** documenting why it does not trigger ICO standards #11/#12/#13.

---

## 9. Enforcement trajectory — where regulation is heading in 2025–2026

Observations across the 12 primary sources:

- **The floor is rising fast.** Between Dec 2022 (Epic) and Feb 2026 (Reddit), the standards around "reasonable" age assurance, default-off profiling, and purpose-limited retention have moved from "best practice" to "enforced baseline." Cena is launching in 2026 into a very different regulatory climate than Duolingo had in 2012.
- **The jurisdictions are converging.** The FTC's 2025 COPPA final rule reads like a lite version of the ICO Children's Code. Israel's Amendment 13 reads like a lite version of the GDPR. A single compliant design can satisfy all four at once — but a single non-compliant design fails everywhere.
- **Loot-box law is still mixed.** Belgium criminal, UK PEGI-label-only, Netherlands overturned, US FTC now going after them via COPPA/§5 indirectly (Genshin). For Cena the practical rule is: do not ship anything that resembles a paid randomized reward, period. Even non-paid variable-ratio rewards are in the radar of UK and EU child-safety advocates.
- **Ed-tech is the next target.** Edmodo was first; Google Classroom, Prodigy Math, and several language apps are under active scrutiny per FTC and ICO progress reports. A 2026 ed-tech launch will be audited against the Edmodo bar, not the Amazon-Alexa bar.
- **Age assurance is now a product requirement, not a policy document.** Reddit's £14.47M fine is proof that the ICO will not wait for repeat offences — a first-time failure of age assurance is sufficient.

---

## 10. Confidence delta on proposal point 7 ("zero dark patterns")

**Before this track:** point 7 was a brand posture — ethical but potentially a growth-cost.
**After this track:** point 7 is a **regulatory prerequisite for operating in any of Cena's four target jurisdictions in 2026**. The evidence *raises* the bar: not only should Cena keep point 7, it should **raise it higher** — specifically by:

- Adding the kill-list in §6 as a product-ship gate.
- Adding the bright-line rules in §8 to the design-review checklist.
- Treating point 7 as the marketing story for school procurement: "compliant with FTC COPPA 2025, ICO Children's Code, Israel PPL Amendment 13" is a sellable differentiator against Duolingo and Khan Academy.

Point 7 is no longer optional. It is the only legal way to ship the rest of the proposal.

---

## 11. Open questions for later tracks

- **Does Israel have a pending minors-specific privacy amendment?** The IAPP and LoC sources note that "various bills addressing minors have been considered but none matured." Worth a 6-month re-check before launch.
- **Will the UK Online Safety Act 2023 apply to Cena?** The OSA creates duties on user-to-user services. If Cena's co-op mode enables any student-to-student interaction, OSA duties apply — separate research track.
- **Do French or Spanish data protection authorities have specific ed-tech guidance that differs from ICO?** CNIL has historically been stricter on schools than the ICO; worth checking for French Bagrut-equivalent rollout.
- **What is the specific Hebrew-language compliance burden** for DPIA, privacy notices, and VPC flows in Israel? Amendment 13 does not specify language requirements but general Israeli consumer-protection law requires Hebrew disclosures.

---

**Deliverable status:** research only. No code, no product decisions. Findings above should feed directly into a compliance workstream before any launch of features 1, 2, 6, or 10.
