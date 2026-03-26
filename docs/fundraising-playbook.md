# Israeli Pre-Seed Fundraising Playbook — Concrete Steps

> **Date:** 2026-03-26
> **Status:** Research complete
> **Context:** Solo architect, no money to spend, needs to get investors to notice the project

---

## Immediate Action Items (Zero Cost, Do This Week)

### 1. Apply to Microsoft for Startups (Today)
- **URL:** https://www.microsoft.com/en-us/startups
- **What you get:** Up to $150K in Azure credits, OpenAI API credits, GitHub Enterprise, LinkedIn Sales Navigator
- **Cost:** Free. Non-equity. Easy acceptance for early-stage startups.
- **Why first:** Free LLM API credits let you build the demo without spending money.

### 2. Apply to AWS Activate
- **URL:** https://aws.amazon.com/activate/
- **What you get:** Up to $10K AWS credits (founder tier, no VC needed). Up to $100K with accelerator/VC affiliation.
- **Cost:** Free. Non-equity.

### 3. Apply to IIA Tnufa Grant
- **URL:** https://innovationisrael.org.il/en (search "Tnufa")
- **What you get:** Up to NIS 200,000 (~$55K) grant. Repaid through royalties (3-5% of revenue) only if product generates income.
- **Application:** Rolling submissions, no fixed deadlines. Submit online.
- **Timeline:** 6-8 weeks for committee review.
- **This is real money with no equity dilution.**

### 4. Contact MindCET
- **URL:** https://www.mindcet.org
- **What:** Israel's EdTech innovation center, part of CET.
- **Why:** Direct connection to Israeli Ministry of Education, schools for piloting, and EdTech Israel ecosystem.
- **Action:** Email/call them. Ask about current accelerator cohort and EdTech community access.
- **This is your bridge to the CET partnership opportunity.**

### 5. Create Start-Up Nation Central Profile
- **URL:** https://finder.startupnationcentral.org
- **What:** Free database that makes you findable by investors searching for Israeli startups.
- **Action:** Register the company and fill in the profile.

---

## Build the Demo (Weeks 1-4, Zero Cost)

**Non-negotiable.** A first-time solo founder in Israel cannot raise on a deck alone. You need a working demo.

### Minimum viable demo for an Israeli angel:
- A working web prototype (React + free tier hosting) showing the core learning experience
- 3-5 minutes of "wow" interaction — the AI doing something that clearly adds educational value
- The knowledge graph visualization growing in real-time as the student answers questions
- **Can be:** A Streamlit app, a React prototype on Vercel, or a React Native Expo demo
- **Built with:** Microsoft for Startups credits (OpenAI/Azure) + free tier hosting

### What the demo must show:
1. Student answers 5 questions → knowledge graph lights up
2. AI adapts methodology mid-session (visible switch from Socratic to worked example)
3. The graph visualization — this is the "wow" moment that no competitor has
4. Hebrew interface (even if rough)

---

## Get 50-100 Test Users (Weeks 4-8, Zero Cost)

### How to find test users for free:
- **Teacher Facebook/WhatsApp groups:** Israeli teachers have active groups. Post asking for beta testers.
- **Telegram Bagrut study groups:** Students organize study groups. Offer free access.
- **Your education advisor's network:** Licensed Bagrut teachers know students who need help.
- **Parent WhatsApp groups:** Israeli parents share education tools aggressively.

### What you need from test users:
- 50+ who complete the onboarding diagnostic + at least 3 sessions
- Session duration, return rate, completion rate data
- Qualitative feedback: "Would you pay 89 NIS/month for this?"
- 5-10 video testimonials from students saying "this helped me understand X"

---

## Target Investors (Ranked by Probability)

### Tier 1: Highest probability for EdTech AI at pre-seed

**Remagine Ventures — Eze Vidra**
- Former Google Campus TLV head. Explicitly interested in EdTech.
- Check size: $100K-$500K
- **Approach:** LinkedIn message with 2-sentence pitch + demo link
- Twitter/X: @eshkol
- **This is your #1 target.**

**MindCET network angels**
- EdTech-specific angels connected through MindCET
- Check size: $25K-$100K each
- **Approach:** Through MindCET program participation

**iAngels syndicate**
- URL: https://www.iangels.com
- They aggregate angel money, review deals, syndicate to members
- Check size: $200K-$1M (aggregated)
- **Approach:** Submit via website

### Tier 2: AI-focused, open to EdTech

**Explorer Investments** (led eSelf's seed)
- They already bet on Israeli AI + education once. They know the space.
- **Approach:** Reference the eSelf investment, position as "deeper learning science"

**Firstime VC**
- Managing Partners: Tal Slobodkin, Jonathan Benartzi
- Check size: $250K-$1.5M
- More open to non-traditional sectors than most Israeli VCs
- URL: https://firstime.vc — has pitch submission form

### Tier 3: Broader Israeli angels

| Angel | Background | Check Size | How to Reach |
|-------|-----------|------------|-------------|
| Eyal Manor | Ex-YouTube VP, invested in eSelf | $50K-$150K | LinkedIn, Google Israel alumni network |
| Gigi Levy-Weiss | Ex-CEO 888, 200+ investments | $50K-$250K | Twitter @gigilevy, NFX |
| Yaron Galai | Co-founder Outbrain | $50K-$200K | LinkedIn |
| Eilon Reshef | Co-founder Gong.io | $50K-$250K | LinkedIn |

---

## The Pitch Deck (What Israeli Investors Expect)

### 10-12 slides, direct, no-BS:

1. **Problem:** Israeli families spend 860M NIS/year on private tutoring. 45% of students use tutors. Average: 3,000 NIS/subject.
2. **Solution:** AI personal mentor that adapts teaching methodology + shows knowledge graph. Demo screenshot.
3. **Live demo:** 60-second embedded video or "try it now" link
4. **Market:** 85K-100K Bagrut students/year. Yschool proved 190K will pay for digital prep.
5. **Why now:** eSelf acquired by Kaltura (education play absorbed — see `docs/competitor-eself-deep-dive.md` for full competitive intelligence). STEM Bagrut AI is untouched. LLM costs dropped 50%/year.
6. **Differentiation:** Feature matrix vs. Khanmigo, Yschool, eSelf. Knowledge graph + methodology switching = genuinely novel (see `docs/competitor-eself-deep-dive.md` Section 5 for gap analysis).
7. **Business model:** 89 NIS/month (premium tutoring replacement — see `docs/product-research.md` Section 7). Unit economics: LTV:CAC = 2.1:1 at launch (below 3:1 benchmark but with clear path to 3:1 via LLM cost decline ~50%/year and multi-subject upsell extending lifetime from 9 to 12+ months). Contribution margin: ~35-37 NIS/month. Payback period: ~4 months. Break-even at ~1,600–2,300 subscribers.
8. **Traction:** [Demo users, session data, testimonials, waitlist — whatever you have]
9. **Team:** Your enterprise architecture background. AI agents as force multiplier. Education advisor on retainer from Month 1 (content review critical path).
10. **Ask:** 1.5M NIS target on a SAFE, $3-4M cap. 18 months runway. (1.2M NIS minimum but tight — only 14 months at 82K/month burn; 1.5M provides buffer for two September acquisition cycles.)
11. **Milestones:** Month 3: MVP launch with ~50% of Math curriculum (expert review bottleneck — full Math by Month 5, Physics by Month 7). Month 13–18: break-even at 2,000+ subscribers (requires surviving two September acquisition cycles — see `docs/product-research.md` structural churn model). Month 18+: international readiness (AP curriculum).

### SAFE Terms (Standard Israel 2025)
- Instrument: Y Combinator SAFE (post-money)
- Valuation cap: $3M-$4M (solo first-time founder with working demo + initial users)
- Discount: 15-20% (or no discount with cap)
- Legal: NIS 10K-30K. Firms: Meitar, GKH, Shibolet, Yigal Arnon, Pearl Cohen

---

## The "No Money" Path — Full Timeline

| Week | Action | Cost |
|------|--------|------|
| 1 | Apply: MS for Startups, AWS Activate, IIA Tnufa | Free |
| 1 | Contact MindCET, create SNC profile | Free |
| 1-4 | Build demo with AI agents + free credits | Free |
| 4-6 | Get 50 test users from teacher/student groups | Free |
| 6-8 | Collect usage data + testimonials | Free |
| 8 | Create pitch deck with real data | Free |
| 8-10 | Pitch Eze Vidra (Remagine), iAngels, MindCET angels | Free |
| 10-12 | Pitch Explorer Investments, Firstime VC | Free |
| 12-16 | Close SAFE round: 1.2-1.5M NIS | Receive money |

**Total cost to reach funded: Zero.** Everything uses free credits, your time, and AI agents.

---

## Networking (Where Investors Are)

### Events (verify 2026 dates):
- **OurCrowd Global Investor Summit** — February, Jerusalem. Largest Israeli investor event. https://summit.ourcrowd.com
- **DLD Tel Aviv** — September. Innovation festival. https://dld-conference.com
- **EdTechIL** — MindCET events, check mindcet.org
- **Calcalist conferences** — Multiple/year, check calcalistech.com

### Physical spaces (Tel Aviv):
- WeWork Sarona — high investor density
- Labs TLV — Rothschild Blvd, startup-dense
- Mindspace — multiple locations

### Online:
- **LinkedIn** — primary platform for Israeli investors (more than Twitter)
- **Facebook:** "Israeli Startups & Entrepreneurs" group (large, active)
- **Start-Up Nation Central Finder** — free investor database
