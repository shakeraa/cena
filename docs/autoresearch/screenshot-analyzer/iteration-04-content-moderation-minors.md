# Iteration 4: Content Moderation for Image Uploads in Educational Platforms Serving Minors

**Series:** Screenshot Question Analyzer — Defense-in-Depth Research
**Date:** 2026-04-12
**Security Domain:** Content safety, child protection, PII prevention, legal compliance
**Cumulative Security Score:** 72/100 (prior iterations: 58/100)

---

## Executive Summary

Cena's Path B pipeline accepts photos from students aged 14-18 containing math and physics questions. These photos pass through Gemini 2.5 Flash for OCR/extraction, then into the CAS validation chain (MathNet, SymPy, Wolfram). Before any vision model processes a student's photo, the platform must guarantee that the image contains nothing harmful, illegal, or privacy-violating. This iteration defines the four-tier moderation architecture, maps all legal obligations (COPPA, GDPR-K, Israeli PPL Amendment 13, CSAM reporting), compares commercial and open-source moderation APIs at Cena's projected 100K images/month scale, and provides implementation code in both Python (AI service sidecar) and C# (.NET backend).

**Key finding:** At 100K images/month, Google Cloud Vision SafeSearch is the most cost-effective commercial option at ~$150/month. Combined with client-side pre-filtering (free), a custom educational-content classifier (inference cost ~$20/month), and post-extraction validation (already in pipeline), the total moderation cost is under $200/month — less than 1x the existing Gemini vision cost.

---

## 1. Legal Requirements

### 1.1 COPPA (United States) — Children's Online Privacy Protection Act

**Applicability to Cena:** Cena serves students aged 14-18 in Israel, but if any US-based students use the platform (or if the platform is marketed to US audiences), COPPA applies.

#### Under-13 Requirements
- **Verifiable parental consent** required before collecting any personal information, including photos
- Photos, videos, and image-derived data are explicitly defined as personal information under the 2025 COPPA amendments (effective June 23, 2025; compliance deadline April 22, 2026)
- Biometric data (face geometry), geolocation, and persistent identifiers embedded in photos all trigger COPPA obligations
- Data must be deleted once no longer necessary for the purpose collected
- Third-party sharing (sending images to Google Cloud Vision, AWS, etc.) requires separate parental consent unless the third party is "integral to the service"

#### Ages 13-17
- The 2025 COPPA amendments introduced new protections for teens (13-17) under COPPA 2.0 / the Kids Online Safety Act framework
- Platforms must provide "clear and prominent" privacy disclosures
- Targeted advertising based on personal information is restricted
- Data minimization principles apply — collect only what is needed

#### Enforcement
- Fines up to **$51,744 per child, per violation** (2025 rate)
- FTC has actively pursued EdTech companies: Edmodo ($6M fine, 2023), Epic Games/Fortnite ($520M, 2022)

**Cena mitigation:** Cena's students are 14-18, placing them in the teen tier. Photos are processed session-scoped (no long-term retention), and the existing telemetry policy already enforces session-scoped data. Key action: ensure photos are deleted after extraction, never stored in raw form beyond the processing window.

> **Source:** [COPPA Compliance in 2025: A Practical Guide for Tech, EdTech, and Kids' Apps](https://blog.promise.legal/startup-central/coppa-compliance-in-2025-a-practical-guide-for-tech-edtech-and-kids-apps/), [FTC COPPA FAQ](https://www.ftc.gov/business-guidance/resources/complying-coppa-frequently-asked-questions), [The FTC's Updated COPPA Rule](https://www.finnegan.com/en/insights/articles/the-ftcs-updated-coppa-rule-redefining-childrens-digital-privacy-protection.html)

### 1.2 GDPR-K (EU) — Article 8, Children's Data

**Applicability to Cena:** GDPR applies if Cena processes data of EU residents or offers services in the EU. Even without active EU marketing, GDPR extraterritorial reach means any EU student using the platform triggers obligations.

#### Age Thresholds
- GDPR Article 8 sets a default consent age of **16**, but allows member states to lower it to 13
- Actual thresholds by country: Germany/Netherlands: 16, France/UK: 13, Italy: 14, Spain: 14
- Cena's students (14-18) fall within the range — some are below consent age in certain EU jurisdictions

#### Legal Basis for Image Processing
- **Legitimate interest** can justify processing if the platform can demonstrate the processing is necessary and proportionate
- For educational platforms: processing math question photos is directly necessary for the educational service
- However, **content moderation** (scanning for NSFW, faces, PII) requires careful balancing — the Data Protection Impact Assessment (DPIA) must show the moderation is proportionate
- **Consent** is the safest legal basis for under-16s, but requires parental verification

#### Key Requirements
- **Data minimization:** Process images only to the extent needed (extract math content, then delete)
- **Purpose limitation:** Images collected for math question extraction cannot be repurposed for training ML models, analytics, or marketing
- **Right to erasure:** Students/parents can request deletion of all image data
- **Privacy by design:** Content moderation must be built into the pipeline, not bolted on

#### GDPR and Cloud API Processing
- Sending images to Google Cloud Vision (US servers) constitutes a **cross-border data transfer**
- Requires Standard Contractual Clauses (SCCs) or adequacy decision
- Google Cloud offers EU data residency options for Vision API — Cena should use `europe-west1` endpoint

### 1.3 Israeli Privacy Protection Law (PPL) — Amendment 13

**Applicability to Cena:** Directly applicable. Cena operates in Israel, processes Israeli students' data.

#### Amendment 13 (Effective August 14, 2025)

Amendment 13 represents a comprehensive overhaul of Israel's 1981 Privacy Protection Law, aligning it with GDPR standards.

##### Key Provisions Relevant to Cena

| Area | Requirement | Cena Impact |
|------|-------------|-------------|
| **Sensitive data** | Biometric identifiers (face geometry), health data, sexual orientation data classified as "information of special sensitivity" (ISS) | Face detection in student photos = processing ISS. Must have explicit consent or legal basis |
| **Consent** | Explicit, informed consent required for ISS processing | Photo upload consent flow must clearly state what moderation checks occur |
| **Data security** | Organizations must implement "appropriate security measures" proportionate to data sensitivity | Image processing pipeline must encrypt in transit and at rest |
| **DPO requirement** | Required for large-scale processing of sensitive data or systematic monitoring | Cena likely meets threshold — must appoint Privacy Protection Officer (PPO) |
| **Breach notification** | Mandatory notification to PPA and affected individuals | If moderation pipeline is breached (student photos leaked), notification required |
| **Penalties** | Fines reaching millions of shekels, with multipliers for sensitive data | Processing student biometric data (face detection) without compliance = aggravated penalty |
| **Cross-border transfers** | Restricted to countries with adequate protection or with appropriate safeguards | Sending photos to Google Cloud (US) requires safeguards |

##### Children-Specific Provisions

The PPL does not have a dedicated "children's chapter" equivalent to COPPA or GDPR Article 8. However:
- The Israeli **Student Rights Law (1988)** and **Education Ministry Circular (Hozer Mankal)** regulate student data in educational settings
- The PPA has issued guidance that children's data requires "heightened protection" consistent with the GDPR approach
- Biometric data of minors is treated with the highest sensitivity classification
- Educational institutions must maintain a data inventory that includes all student data processing activities

##### Practical Requirements for Cena
1. **Register the database** with the PPA if processing personal data systematically
2. **Appoint a PPO** (Privacy Protection Officer)
3. **Conduct a DPIA** for the photo moderation pipeline (processing biometric-adjacent data of minors)
4. **Obtain informed consent** from parents/guardians for image processing (especially if face detection is used)
5. **Data retention:** Delete photos within the processing window; retain only extracted LaTeX/math content
6. **EU adequacy:** Israel has an EU adequacy decision, so data can flow from EU to Israel without SCCs — but Cena must maintain the security standards that justify this adequacy

> **Source:** [Israel marks a new era in privacy law: Amendment 13](https://iapp.org/news/a/israel-marks-a-new-era-in-privacy-law-amendment-13-ushers-in-sweeping-reform), [Data Protection Laws and Regulations Report 2025-2026 Israel](https://iclg.com/practice-areas/data-protection-laws-and-regulations/israel), [What Israel's Amendment 13 Means for Businesses in 2025](https://bigid.com/blog/what-israel-amendment-13-means-for-businesses-in-2025/)

### 1.4 CSAM Reporting Obligations

**This is non-negotiable. Failure to report known CSAM is a criminal offense in virtually every jurisdiction.**

#### NCMEC CyberTipline (US)

- **Who must report:** Any US-based electronic service provider (ESP) that becomes aware of CSAM on its platform. Under the REPORT Act (signed May 7, 2024), this extends to platforms that enable user-generated uploads.
- **What to report:** Any image that appears to depict a minor in a sexually explicit manner
- **How to report:** File a CyberTipline report at [report.cybertip.org](https://report.cybertip.org) within 24 hours of becoming aware
- **Preservation:** Must preserve the report and associated content for **at least 1 year** (up from 90 days, per REPORT Act)
- **Penalties for failure to report:** $600,000 per initial violation, $850,000 for subsequent violations (REPORT Act 2024 rates)
- **NCMEC hash database:** Platforms can integrate NCMEC's hash-sharing service to proactively detect known CSAM

#### Internet Watch Foundation (IWF) (UK/International)

- Similar hash-sharing service for non-US platforms
- Provides URL and hash lists of confirmed CSAM for proactive blocking
- Relevant if Cena has UK or EU users

#### PhotoDNA / Hash Matching

- **PhotoDNA** (Microsoft, donated to NCMEC) generates perceptual hashes of known CSAM images
- Hashes are **non-reversible** — cannot reconstruct the original image from the hash
- **Fuzzy matching:** Detects images even after resizing, cropping, color adjustment
- **Access:** Free for vetted organizations. Apply through Microsoft's PhotoDNA application page. Educational platforms qualify.
- **Integration:** Cloud API — hash each uploaded image against the NCMEC database before any other processing

#### Israeli Law (Penal Law 5737-1977, Section 214)

- Possession, distribution, or production of CSAM (termed "obscene material depicting a minor") is a criminal offense
- No explicit ESP reporting obligation equivalent to US law, but:
  - The Israel National Cyber Directorate recommends voluntary reporting
  - The Israeli Police Lahav 433 unit handles CSAM cases
  - Failure to act on known CSAM could constitute aiding/abetting under general criminal law

#### Cena's Obligation

Even though Cena is not a social media platform, students upload photos. The probability of CSAM in math homework photos is very low, but the legal obligation exists regardless of probability. **Cena must:**

1. Integrate PhotoDNA hash matching as Tier 0 (before any other processing)
2. If a match is found: block upload, do NOT display to any user, preserve the hash and metadata, file a CyberTipline report within 24 hours
3. Do NOT delete the flagged image until instructed by law enforcement
4. Train/designate a CSAM reporting officer within the organization

> **Source:** [REPORT Act 2024](https://warrantbuilder.com/report_act/), [NCMEC Reporting by Online Platforms](https://www.globalchildexploitationpolicy.org/content/gpp-ncmec/us/en/policy-advocacy/reporting-by-online-platforms.html), [Technology Coalition Update on CSAM Detection](https://technologycoalition.org/resources/update-on-voluntary-detection-of-csam/), [PhotoDNA — Wikipedia](https://en.wikipedia.org/wiki/PhotoDNA)

---

## 2. Content Categories to Detect and Reject

### 2.1 Category Taxonomy

| # | Category | Severity | Action | Rationale |
|---|----------|----------|--------|-----------|
| 1 | **CSAM** | CRITICAL | Block + report to NCMEC + preserve | Legal obligation |
| 2 | **NSFW / Sexual Content** | HIGH | Block + log | Minors on platform; zero tolerance |
| 3 | **Violence / Gore** | HIGH | Block + log | Harmful to minors; no educational value |
| 4 | **Hate Speech / Symbols** | HIGH | Block + log + escalate | Swastikas, white supremacist symbols, etc. |
| 5 | **Self-Harm / Crisis** | HIGH | Block + trigger crisis response | Duty of care to minors |
| 6 | **PII — Faces** | MEDIUM | Reject + explain | Student privacy; photos should show paper, not faces |
| 7 | **PII — Documents** | MEDIUM | Reject + explain | ID cards, report cards with names/numbers |
| 8 | **PII — Text** | MEDIUM | Redact or reject | Names, phone numbers, ID numbers in image text |
| 9 | **Non-Educational** | LOW | Reject + explain | Memes, social media screenshots, selfies |
| 10 | **Spoof / Adversarial** | LOW | Flag + manual review | Attempt to bypass moderation |

### 2.2 Detection Strategy Per Category

#### CSAM (Category 1)
- **Detection:** PhotoDNA hash matching against NCMEC database
- **False positive rate:** Extremely low (<0.001% by design — hashes match only known confirmed CSAM)
- **Action:** Immediate block. No human review of the image itself. Report to NCMEC. Preserve metadata.
- **Never:** Display to moderators. Delete before law enforcement authorization.

#### NSFW / Sexual Content (Category 2)
- **Detection:** Google Cloud Vision SafeSearch (`adult`, `racy` categories)
- **Threshold:** Block if `adult >= LIKELY` or `racy >= VERY_LIKELY`
- **Secondary:** NudeNet classifier for edge cases (exposed body parts detection)
- **False positive risk:** LOW for math photos. Anatomy diagrams from biology could trigger — but Cena is math/physics only.

#### Violence / Gore (Category 3)
- **Detection:** Google Cloud Vision SafeSearch (`violence` category)
- **Threshold:** Block if `violence >= LIKELY`
- **Note:** Physics problems involving weapons (projectile motion of a bullet) are text-based, not image-based. The photo itself should show paper with equations, not violent imagery.

#### Hate Speech / Symbols (Category 4)
- **Detection:** Custom classifier or AWS Rekognition (detects hate symbols)
- **Supplementary:** OCR text extraction + hate speech text classifier
- **Context:** In Israel, Nazi imagery is particularly sensitive (Denial of Holocaust Law, 1986)

#### Self-Harm / Crisis Content (Category 5)
- **Detection:** Google Cloud Vision SafeSearch (`violence` category) + custom classifier for self-harm imagery
- **Critical addition:** If OCR-extracted text contains crisis language ("I want to die", "kill myself", Hebrew/Arabic equivalents), trigger crisis response protocol
- **Crisis response:** Display crisis hotline information (ERAN — 1201 in Israel, Natal — 1-800-363-363), notify designated staff member, do NOT simply reject the upload
- **Reference:** [Samaritans' guidelines for tech industry](https://www.samaritans.org/about-samaritans/research-policy/internet-suicide/guidelines-tech-industry/effective-content-moderation/)

#### PII — Faces (Category 6)
- **Detection:** Google Cloud Vision Face Detection API
- **Threshold:** If any face detected with confidence > 0.7, reject the image
- **Rationale:** Students should photograph their notebook/paper, not themselves. A face in the photo means either a selfie (not educational) or an accidental self-capture
- **User message:** "Your photo appears to contain a face. For your privacy, please photograph only your math/physics question."

#### PII — Documents (Category 7)
- **Detection:** Custom object detection for ID cards, passports, report cards
- **Supplementary:** OCR + regex for structured PII patterns (Israeli ID: 9 digits, phone: 05X-XXXXXXX)
- **Action:** Reject with explanation. Never forward to vision model.

#### PII — Text (Category 8)
- **Detection:** OCR (already performed by Gemini 2.5 Flash) + PII regex patterns
- **Patterns:** Email addresses, phone numbers, Israeli ID numbers (Teudat Zehut), credit card numbers, full names adjacent to identifying context
- **Action:** If PII found in extracted text, redact before storing. If PII is the primary content (not math), reject.

#### Non-Educational Content (Category 9)
- **Detection:** Custom classifier — "Is this a math/physics question?"
- **Implementation:** Fine-tuned image classifier (MobileNet or EfficientNet) trained on:
  - Positive examples: photos of handwritten math equations, printed math problems, physics diagrams
  - Negative examples: memes, selfies, food photos, social media screenshots, blank pages
- **Threshold:** Reject if educational confidence < 0.6
- **False positive risk:** MEDIUM — poorly lit or angled photos of legitimate math problems may be rejected

#### Spoof / Adversarial (Category 10)
- **Detection:** Google Cloud Vision SafeSearch (`spoof` category) + adversarial input detection from Iteration 1
- **Action:** Flag for manual review. Do not auto-reject (could be a legitimate but unusual photo).

---

## 3. Moderation Architecture — Four-Tier Pipeline

### 3.1 Architecture Diagram

```
Student Device                    Cena Backend                         Cloud Services
┌─────────────┐                  ┌──────────────────┐                 ┌────────────────┐
│             │                  │                  │                 │                │
│  TIER 1     │    HTTPS/TLS    │  TIER 2          │    API calls    │  Google Cloud   │
│  Client-    │ ──────────────► │  Cloud Vision    │ ──────────────► │  Vision API     │
│  Side Pre-  │                 │  SafeSearch      │                 │  (SafeSearch,   │
│  Filter     │                 │                  │                 │   Face Detect)  │
│             │                 │  TIER 0          │                 │                │
│ ┌─────────┐ │                 │  PhotoDNA Hash   │ ──────────────► │  PhotoDNA       │
│ │File type│ │                 │  (CSAM check)    │                 │  Cloud Service  │
│ │Size     │ │                 │                  │                 │                │
│ │Dims     │ │                 │  TIER 3          │                 │                │
│ │EXIF     │ │                 │  Custom          │                 │                │
│ │strip    │ │                 │  Classifier      │                 │                │
│ └─────────┘ │                 │  (Educational?)  │                 │                │
│             │                 │                  │                 │                │
│             │                 │  TIER 4          │                 │                │
│             │                 │  Post-Extraction │                 │  Gemini 2.5     │
│             │                 │  Validation      │ ──────────────► │  Flash (OCR)    │
│             │                 │                  │                 │                │
└─────────────┘                 └──────────────────┘                 └────────────────┘

Processing Order:
  Tier 0 (PhotoDNA)  →  Tier 1 (client)  →  Tier 2 (SafeSearch)  →  Tier 3 (classifier)  →  Tier 4 (post-OCR)
  < 200ms               < 50ms              300-800ms                100-300ms                (in existing pipeline)
```

### 3.2 Tier 0: CSAM Hash Matching (Pre-Pipeline)

**Purpose:** Legal obligation. Check every uploaded image against the NCMEC PhotoDNA hash database before any other processing.

**When:** Before the image enters the moderation pipeline. This is a gate — nothing else happens until this check passes.

**Implementation:**
- Call PhotoDNA Cloud Service API with the raw image bytes
- If match found: HTTP 451 (Unavailable For Legal Reasons), log incident to sealed audit log, trigger CSAM reporting workflow
- If no match: proceed to Tier 1

**Latency:** ~100-200ms
**Cost:** Free (PhotoDNA Cloud Service is free for vetted organizations)
**False positive rate:** Near zero (matches only confirmed, human-verified CSAM hashes)

### 3.3 Tier 1: Client-Side Pre-Filter

**Purpose:** Reject obviously invalid uploads before consuming server bandwidth. Strip EXIF metadata to protect student privacy.

**When:** On the student's device (Vue 3 web app or Flutter mobile app) before upload.

**Checks:**

| Check | Criterion | Rejection Message |
|-------|-----------|-------------------|
| File type | Must be JPEG, PNG, WebP, or HEIF | "Please upload a photo (JPEG, PNG, or WebP)" |
| File size | Max 10 MB | "Photo is too large. Please take a clearer, closer photo." |
| Dimensions | Min 200x200, max 8192x8192 | "Photo dimensions are invalid." |
| Aspect ratio | Between 1:4 and 4:1 | "Photo shape is unusual. Please use a standard photo." |
| EXIF stripping | Remove ALL EXIF metadata before upload | (silent — no user message) |
| Magic bytes | Verify file header matches declared type | "File format is not supported." |

**EXIF Stripping Detail:**
- EXIF metadata can contain GPS coordinates (accurate to ~3 meters), device serial numbers, camera make/model, timestamps, and even thumbnail previews of the original uncropped image
- **Up to 80% of smartphone photos retain GPS coordinates** when uploaded without stripping
- Cena must strip EXIF on the client before upload AND re-strip on the server (defense in depth — do not trust the client)
- Server-side: use `ExifTool` (Perl, CLI) or `SixLabors.ImageSharp` (.NET) to strip all metadata
- Mobile (Flutter): use `image` package to re-encode without metadata

> **Source:** [EXIF Data Risks: Strip Image Metadata for Global Privacy](https://mochify.xyz/guides/exif-data-risks-image-compression-2026), [EDUCAUSE: Privacy Implications of EXIF Data](https://er.educause.edu/articles/2021/6/privacy-implications-of-exif-data)

### 3.4 Tier 2: Cloud Vision SafeSearch + Face Detection

**Purpose:** Detect NSFW content, violence, and faces using Google Cloud Vision API.

**When:** Server-side, immediately after receiving the uploaded image and re-stripping EXIF.

**API Calls (batched in single request):**

```json
{
  "requests": [
    {
      "image": { "content": "<base64-encoded-image>" },
      "features": [
        { "type": "SAFE_SEARCH_DETECTION" },
        { "type": "FACE_DETECTION", "maxResults": 5 },
        { "type": "LABEL_DETECTION", "maxResults": 10 }
      ]
    }
  ]
}
```

**Decision Matrix:**

| SafeSearch Category | UNKNOWN | VERY_UNLIKELY | UNLIKELY | POSSIBLE | LIKELY | VERY_LIKELY |
|--------------------|---------|---------------|----------|----------|--------|-------------|
| `adult` | Pass | Pass | Pass | Flag | **Block** | **Block** |
| `violence` | Pass | Pass | Pass | Flag | **Block** | **Block** |
| `racy` | Pass | Pass | Pass | Pass | Flag | **Block** |
| `medical` | Pass | Pass | Pass | Pass | Pass | Flag |
| `spoof` | Pass | Pass | Pass | Pass | Flag | Flag |

**Face Detection Rules:**
- If any face detected with `detectionConfidence > 0.7`: **Reject**
- User message: "Your photo appears to show a person's face. For your privacy, please photograph only your written question."
- If face detected with `0.5 < confidence <= 0.7`: Flag for manual review

**Label Detection (supplementary):**
- Check returned labels for non-educational indicators: "selfie", "meme", "screenshot", "social media", "food", "animal"
- If non-educational label has score > 0.8: flag for Tier 3 custom classifier

**Latency:** 300-800ms (batch request)
**Cost:** See Section 4 for detailed pricing

### 3.5 Tier 3: Custom Educational Content Classifier

**Purpose:** Determine whether the image actually contains a math or physics question, as opposed to non-educational content that happened to pass SafeSearch.

**When:** After Tier 2 passes. Before sending to Gemini 2.5 Flash for OCR.

**Model Options:**

| Option | Accuracy | Latency | Cost/100K | Deployment |
|--------|----------|---------|-----------|------------|
| Fine-tuned MobileNetV3 | ~92% | 20-50ms | ~$5 (self-hosted) | ONNX on .NET backend |
| Fine-tuned EfficientNet-B0 | ~95% | 50-100ms | ~$8 (self-hosted) | ONNX on .NET backend |
| Gemini 2.5 Flash (prompt-based) | ~97% | 200-500ms | ~$20 | Already in pipeline |
| GPT-4o mini (prompt-based) | ~96% | 300-600ms | ~$25 | API call |

**Recommended approach:** Use Gemini 2.5 Flash with a classification prompt as the first step of the existing OCR call. This adds zero extra API cost — the classification is embedded in the OCR prompt:

```
You are analyzing a student's photo. Before extracting any math content:

1. CLASSIFY: Is this a photo of a math or physics question/problem?
   - If YES: proceed to extract the mathematical content as LaTeX.
   - If NO: respond with {"classification": "non_educational", "reason": "<brief description>"} and stop.

2. PII CHECK: Does the image contain any visible personal information?
   - Names, ID numbers, phone numbers, email addresses
   - If YES: respond with {"classification": "contains_pii", "pii_types": [...]} and stop.

3. EXTRACT: [existing extraction prompt...]
```

**False positive analysis:** See Section 5.

### 3.6 Tier 4: Post-Extraction Validation

**Purpose:** After Gemini 2.5 Flash extracts LaTeX from the image, validate that the extracted content is actually mathematical/educational.

**When:** After OCR extraction, before sending to CAS pipeline.

**Checks:**

| Validation | Method | Action on Failure |
|------------|--------|-------------------|
| LaTeX parseable | Attempt KaTeX/MathJax parse | Reject — "Could not read your question. Please retake the photo with better lighting." |
| Contains math symbols | Regex check for `\frac`, `\int`, `\sum`, `+`, `-`, `=`, `\sin`, etc. | Reject — "No mathematical content found in your photo." |
| Not pure text | Ratio of math symbols to plain text > 0.1 | Flag — may be a text screenshot, not a math problem |
| No injected prompts | Iteration 2 sanitization checks | Reject — adversarial input detected |
| No executable LaTeX | Iteration 3 LaTeX sanitization | Sanitize — remove `\input`, `\write`, etc. |

---

## 4. API Comparison — Cost Analysis at 100K Images/Month

### 4.1 Google Cloud Vision API

| Feature | Price per 1,000 | Monthly Cost (100K images) | Notes |
|---------|----------------|---------------------------|-------|
| SafeSearch Detection | $1.50 (free with Label Detection) | **$0** (bundled) | Free when Label Detection is also used |
| Label Detection | $1.50 | $150.00 | Needed for content classification |
| Face Detection | $1.50 | $150.00 | Required for PII check |
| **Combined (SafeSearch + Label + Face)** | — | **$300.00** | Three features on same image = 3 billable units |
| **Optimized (SafeSearch + Label only)** | — | **$150.00** | SafeSearch free with Label; face detect via Gemini |

**Free tier:** First 1,000 units/month per feature are free (saves ~$4.50/month)
**Volume discount:** At 5M+ units: Label $1.00, Face $0.60 — but Cena is well below this tier
**Data residency:** Use `europe-west1` or `me-west1` (Tel Aviv) endpoint for GDPR/PPL compliance

> **Source:** [Google Cloud Vision API Pricing](https://cloud.google.com/vision/pricing)

### 4.2 AWS Rekognition Content Moderation

| Volume Tier | Price per Image | Monthly Cost (100K images) |
|-------------|----------------|---------------------------|
| First 1M images | $0.0010 | **$100.00** |
| 1M - 10M images | $0.0008 | — |
| 10M - 40M images | $0.0006 | — |
| 40M+ images | $0.0004 | — |

**Categories detected:** Nudity, violence, drugs, tobacco, alcohol, gambling, hate symbols, disturbing content
**Face detection (separate API):** Same pricing tiers ($0.0010/image for first 1M)
**Total with face detection:** **$200.00/month**
**Free tier:** 1,000 images/month for 12 months (new accounts)
**Advantage:** Detects hate symbols natively (Google Vision does not)
**Disadvantage:** No SafeSearch-equivalent severity levels; binary confidence scores only

> **Source:** [AWS Rekognition Pricing](https://aws.amazon.com/rekognition/pricing/)

### 4.3 Azure Content Safety

| Feature | Price per 1,000 | Monthly Cost (100K images) |
|---------|----------------|---------------------------|
| Image moderation | $1.50 | **$150.00** |
| Text moderation | $1.00 | — |
| Face detection (Azure Face API) | $1.00 | $100.00 |
| **Combined** | — | **$250.00** |

**Categories:** Hate, sexual, violence, self-harm — each with severity levels 0-6
**Advantage:** Severity levels per category (more granular than AWS); self-harm category (unique to Azure)
**Advantage:** Hybrid deployment option — on-premise containers for strict data residency
**Disadvantage:** No native hate-symbol detection in images (text only)

> **Source:** [Azure Content Safety Pricing](https://azure.microsoft.com/en-us/pricing/details/content-safety/)

### 4.4 Open-Source Alternatives

| Tool | Detection | Accuracy | Hosting Cost (100K/mo) | Limitations |
|------|-----------|----------|----------------------|-------------|
| **NudeNet** | Nudity/body parts | ~90% | ~$15 (CPU), ~$30 (GPU) | Nudity only; no violence, hate, or face detection |
| **NSFW.js** (Yahoo/Gant) | NSFW classification | ~93% | ~$10 (CPU) | Binary NSFW/SFW; no granular categories |
| **OpenNSFW2** | NSFW classification | ~91% | ~$12 (CPU) | 5-category model; dated (2020) |
| **Tesseract + regex** | PII in text | ~85% | ~$5 (CPU) | OCR accuracy varies with handwriting quality |

**Verdict:** Open-source tools are useful as **supplementary** checks but should not be the primary moderation layer for a platform serving minors. Commercial APIs provide:
- Higher accuracy (95%+ vs 90%)
- Legal defensibility ("we used industry-standard tools")
- SLA guarantees and compliance certifications
- Broader category coverage

> **Source:** [NudeNet on PyPI](https://pypi.org/project/nudenet/), [A Comparative Study of Tools for Explicit Content Detection in Images](https://hal.science/hal-04179978/document)

### 4.5 Cost Summary — Recommended Stack

| Component | Provider | Monthly Cost | Notes |
|-----------|----------|-------------|-------|
| CSAM hash matching | PhotoDNA Cloud | **$0** | Free for vetted organizations |
| Client-side pre-filter | Built-in | **$0** | No API calls |
| SafeSearch + Labels | Google Cloud Vision | **$150** | SafeSearch free with Label Detection |
| Face detection | Gemini 2.5 Flash (in OCR prompt) | **$0** | Already paying for OCR; add face-check to prompt |
| Educational classifier | Gemini 2.5 Flash (in OCR prompt) | **$0** | Combined with OCR prompt |
| Post-extraction validation | Built-in (.NET) | **$0** | Regex + KaTeX parse |
| EXIF stripping | Built-in (ImageSharp) | **$0** | No external API |
| **Total** | | **~$150/month** | At 100K images/month |

**Comparison to Gemini OCR cost:** ~$200/month (100K images at Gemini 2.5 Flash pricing). Content moderation adds ~75% to the vision processing cost. **Total pipeline cost: ~$350/month for 100K images.**

---

## 5. False Positive Analysis

### 5.1 Categories of False Positives in Math/Physics Photo Context

| Scenario | Trigger | Category | Estimated FP Rate | Mitigation |
|----------|---------|----------|-------------------|------------|
| Student's hand visible while holding paper | Face detection | PII | 5-10% | Require `face.boundingPoly` to cover >15% of image area; ignore hand/finger detections |
| Geometry problem with nude figure drawing | NSFW / adult | Content | <0.5% | Cena is math/physics only; anatomy figures unlikely |
| Physics problem about projectile/bullet | Violence (text) | Content | 1-2% | Check SafeSearch `violence` on image, not on extracted text |
| Red ink marks on paper | Medical / violence | Content | 0.5-1% | Require `LIKELY` threshold, not `POSSIBLE` |
| Student photographed their phone screen | Spoof | Content | 3-5% | Allow if educational classifier confirms math content |
| Low-quality / blurry photo | Non-educational | Classification | 5-8% | Provide retry guidance; lower classification threshold for blurry images |
| Hebrew/Arabic handwritten text without equations | Non-educational | Classification | 2-4% | Word problems are valid math content; classifier must handle text-heavy problems |
| Multiple pages / textbook photo | Label "book" or "document" | Classification | 1-2% | "book" label is fine for educational context |

### 5.2 Aggregate False Positive Estimate

**Expected rejection rate for legitimate math photos:** 3-5%

This is acceptable for an educational platform. The rejection message should always include:
1. A clear explanation of why the photo was rejected
2. Specific guidance on how to retake the photo
3. An option to contact support if the student believes the rejection is an error
4. A "try again" button that preserves their session context

### 5.3 False Positive Monitoring

```sql
-- Track false positive rate over time
CREATE TABLE moderation_decisions (
    id UUID PRIMARY KEY,
    image_hash VARCHAR(64) NOT NULL,
    student_id UUID NOT NULL,
    tier VARCHAR(10) NOT NULL,       -- 'tier0','tier1','tier2','tier3','tier4'
    category VARCHAR(50) NOT NULL,   -- 'nsfw','violence','face','non_educational'
    decision VARCHAR(20) NOT NULL,   -- 'pass','flag','block'
    confidence DECIMAL(4,3),
    appealed BOOLEAN DEFAULT FALSE,
    appeal_overturned BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    tenant_id UUID NOT NULL
);

-- Weekly false positive report
SELECT
    category,
    COUNT(*) AS total_blocks,
    SUM(CASE WHEN appeal_overturned THEN 1 ELSE 0 END) AS false_positives,
    ROUND(100.0 * SUM(CASE WHEN appeal_overturned THEN 1 ELSE 0 END) / COUNT(*), 2) AS fp_rate_pct
FROM moderation_decisions
WHERE decision = 'block'
  AND created_at > NOW() - INTERVAL '7 days'
GROUP BY category
ORDER BY fp_rate_pct DESC;
```

**Alert threshold:** If any category exceeds 10% false positive rate (appeals overturned), auto-escalate to engineering team for threshold adjustment.

---

## 6. PII Detection Specifics

### 6.1 Face Detection

**Why reject photos with faces:**
- Students are minors (14-18). Their facial images are biometric data under Israeli PPL Amendment 13 and GDPR.
- Processing facial biometric data of minors requires explicit parental consent (which Cena does not collect for this purpose).
- A face in a math homework photo is almost always accidental (selfie mode, reflection, other person in frame).
- Even if the face is not stored, sending it to Google Cloud Vision for processing constitutes "processing biometric data."

**Detection approach:**
```python
# Google Cloud Vision Face Detection response
{
    "faceAnnotations": [
        {
            "boundingPoly": { "vertices": [...] },
            "detectionConfidence": 0.95,
            "landmarkingConfidence": 0.87,
            "joyLikelihood": "UNLIKELY",
            "sorrowLikelihood": "VERY_UNLIKELY",
            "angerLikelihood": "VERY_UNLIKELY",
            "surpriseLikelihood": "VERY_UNLIKELY"
        }
    ]
}
```

**Decision logic:**
```
IF face_count > 0 AND max(detection_confidence) > 0.7:
    IF face_area / image_area > 0.15:
        REJECT("Photo contains a face. Please photograph only your question.")
    ELSE:
        FLAG("Small face detected — possibly a person in background. Manual review.")
ELSE:
    PASS
```

**Alternative (cost-saving):** Instead of a separate Cloud Vision Face Detection call, embed face detection in the Gemini 2.5 Flash prompt:

```
Before extracting math content, check: does this image contain any human faces?
If yes, respond with {"contains_face": true, "face_area_percent": <estimated>} and stop.
```

This is less accurate than Cloud Vision Face Detection but costs $0 extra.

### 6.2 Text PII Detection

**When:** After Gemini 2.5 Flash extracts text/LaTeX from the image.

**Patterns to detect:**

```csharp
public static class PiiPatterns
{
    // Israeli ID (Teudat Zehut) — 9 digits
    public static readonly Regex IsraeliId = new(@"\b\d{9}\b", RegexOptions.Compiled);

    // Israeli phone numbers
    public static readonly Regex IsraeliPhone = new(
        @"(?:\+972|0)[\s-]?(?:5[0-9]|[2-4]|[8-9])[\s-]?\d{3}[\s-]?\d{4}",
        RegexOptions.Compiled);

    // Email addresses
    public static readonly Regex Email = new(
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Credit card numbers (basic Luhn-eligible patterns)
    public static readonly Regex CreditCard = new(
        @"\b(?:\d{4}[\s-]?){3}\d{4}\b",
        RegexOptions.Compiled);

    // Full name patterns (Hebrew) — first + last name adjacent to ID context
    // This is heuristic; combine with NER for better accuracy
    public static readonly Regex HebrewName = new(
        @"שם[:\s]+[\u0590-\u05FF]+\s+[\u0590-\u05FF]+",
        RegexOptions.Compiled);
}
```

**Action:**
- If PII found in extracted text AND it is not part of the math problem (e.g., "Ahmed has 5 apples" is fine; "Ahmed Ben-Yosef, ID 123456789" is PII): redact before storing
- If the image is primarily a document with PII (ID card photo, report card): reject entirely

### 6.3 EXIF / Location Metadata

**Risk:** A student's photo taken at home contains GPS coordinates accurate to ~3 meters, revealing their home address. The photo also contains device serial number, timestamp, and camera model.

**Mitigation — defense in depth:**

| Layer | Method | Library |
|-------|--------|---------|
| Client (Vue 3) | `canvas.toBlob()` re-encode (strips EXIF) | Built-in Canvas API |
| Client (Flutter) | Re-encode via `image` package | `package:image` |
| Server (.NET) | Strip with ImageSharp before any processing | `SixLabors.ImageSharp` |
| Server (Python sidecar) | Strip with Pillow | `Pillow` (PIL) |

**Server-side stripping is mandatory.** Never trust that the client stripped EXIF — a modified client or direct API call could bypass client-side stripping.

```csharp
// C# — SixLabors.ImageSharp EXIF stripping
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;

public static byte[] StripExifAndReencode(byte[] imageBytes)
{
    using var image = Image.Load(imageBytes);

    // Remove all EXIF metadata
    image.Metadata.ExifProfile = null;
    image.Metadata.IccProfile = null;
    image.Metadata.IptcProfile = null;
    image.Metadata.XmpProfile = null;

    // Re-encode as JPEG (strips any remaining metadata)
    using var output = new MemoryStream();
    image.SaveAsJpeg(output, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder
    {
        Quality = 85  // Good quality for OCR; reduces file size
    });
    return output.ToArray();
}
```

```python
# Python — Pillow EXIF stripping
from PIL import Image
import io

def strip_exif_and_reencode(image_bytes: bytes) -> bytes:
    """Strip all EXIF metadata and re-encode as JPEG."""
    img = Image.open(io.BytesIO(image_bytes))

    # Create new image without metadata
    data = list(img.getdata())
    clean_img = Image.new(img.mode, img.size)
    clean_img.putdata(data)

    # Re-encode
    output = io.BytesIO()
    clean_img.save(output, format='JPEG', quality=85)
    return output.getvalue()
```

---

## 7. Implementation Code

### 7.1 Python — Moderation Service (AI Sidecar)

```python
"""
Content moderation service for Cena screenshot analyzer.
Runs as a sidecar to the .NET backend, called via NATS request/reply.

Dependencies:
    pip install google-cloud-vision pillow nudenet
"""

import hashlib
import logging
from dataclasses import dataclass, field
from enum import Enum
from typing import Optional

from google.cloud import vision

logger = logging.getLogger(__name__)


class ModerationVerdict(Enum):
    PASS = "pass"
    FLAG = "flag"
    BLOCK = "block"
    CSAM_MATCH = "csam_match"


class ContentCategory(Enum):
    CSAM = "csam"
    NSFW = "nsfw"
    VIOLENCE = "violence"
    HATE_SYMBOL = "hate_symbol"
    SELF_HARM = "self_harm"
    FACE_PII = "face_pii"
    TEXT_PII = "text_pii"
    NON_EDUCATIONAL = "non_educational"
    SPOOF = "spoof"


@dataclass
class ModerationResult:
    verdict: ModerationVerdict
    category: Optional[ContentCategory] = None
    confidence: float = 1.0
    reason: str = ""
    user_message: str = ""
    details: dict = field(default_factory=dict)


# ---------------------------------------------------------------------------
# Likelihood thresholds (Google Cloud Vision)
# ---------------------------------------------------------------------------
# Google returns: UNKNOWN, VERY_UNLIKELY, UNLIKELY, POSSIBLE, LIKELY, VERY_LIKELY
# We map these to integer scores for comparison.
LIKELIHOOD_SCORE = {
    vision.Likelihood.UNKNOWN: 0,
    vision.Likelihood.VERY_UNLIKELY: 1,
    vision.Likelihood.UNLIKELY: 2,
    vision.Likelihood.POSSIBLE: 3,
    vision.Likelihood.LIKELY: 4,
    vision.Likelihood.VERY_LIKELY: 5,
}

# Block thresholds (score at which we block)
BLOCK_THRESHOLDS = {
    "adult": 4,       # LIKELY
    "violence": 4,    # LIKELY
    "racy": 5,        # VERY_LIKELY
}

# Flag thresholds (score at which we flag for review)
FLAG_THRESHOLDS = {
    "adult": 3,       # POSSIBLE
    "violence": 3,    # POSSIBLE
    "racy": 4,        # LIKELY
    "medical": 5,     # VERY_LIKELY
    "spoof": 4,       # LIKELY
}


class ContentModerationService:
    """Four-tier content moderation for student photo uploads."""

    def __init__(self, vision_client: Optional[vision.ImageAnnotatorClient] = None):
        self._vision = vision_client or vision.ImageAnnotatorClient()

    async def moderate(self, image_bytes: bytes) -> ModerationResult:
        """
        Run all moderation tiers on an image.

        Order:
            Tier 0: PhotoDNA CSAM hash check
            Tier 2: Google Cloud Vision SafeSearch + Face Detection
            Tier 3: Educational content classification (delegated to Gemini prompt)

        Tier 1 (client-side) and Tier 4 (post-extraction) are handled elsewhere.
        """

        # --- Tier 0: CSAM hash check ---
        csam_result = await self._check_csam_hash(image_bytes)
        if csam_result.verdict == ModerationVerdict.CSAM_MATCH:
            return csam_result

        # --- Tier 2: Cloud Vision SafeSearch + Face Detection ---
        vision_result = self._check_cloud_vision(image_bytes)
        if vision_result.verdict in (ModerationVerdict.BLOCK, ModerationVerdict.FLAG):
            return vision_result

        return ModerationResult(
            verdict=ModerationVerdict.PASS,
            reason="Image passed all moderation checks",
        )

    async def _check_csam_hash(self, image_bytes: bytes) -> ModerationResult:
        """
        Tier 0: Check image hash against PhotoDNA / NCMEC database.

        In production, this calls the PhotoDNA Cloud Service API.
        Here we show the interface; actual API key and endpoint are
        configured via environment variables.
        """
        image_hash = hashlib.sha256(image_bytes).hexdigest()
        # TODO: Replace with actual PhotoDNA API call
        # response = await photodna_client.check(image_bytes)
        # if response.is_match:
        #     logger.critical(
        #         "CSAM MATCH DETECTED",
        #         extra={"hash": image_hash, "match_id": response.match_id}
        #     )
        #     await self._trigger_csam_report(image_bytes, image_hash, response)
        #     return ModerationResult(
        #         verdict=ModerationVerdict.CSAM_MATCH,
        #         category=ContentCategory.CSAM,
        #         reason="PhotoDNA match",
        #     )

        return ModerationResult(verdict=ModerationVerdict.PASS)

    def _check_cloud_vision(self, image_bytes: bytes) -> ModerationResult:
        """
        Tier 2: Google Cloud Vision SafeSearch + Face Detection.

        Single API call with multiple features for cost efficiency.
        """
        image = vision.Image(content=image_bytes)

        # Batch request: SafeSearch + Face Detection + Label Detection
        response = self._vision.annotate_image({
            "image": image,
            "features": [
                {"type_": vision.Feature.Type.SAFE_SEARCH_DETECTION},
                {"type_": vision.Feature.Type.FACE_DETECTION, "max_results": 5},
                {"type_": vision.Feature.Type.LABEL_DETECTION, "max_results": 10},
            ],
        })

        # --- Check SafeSearch ---
        safe = response.safe_search_annotation
        if safe:
            for category, threshold in BLOCK_THRESHOLDS.items():
                score = LIKELIHOOD_SCORE.get(getattr(safe, category), 0)
                if score >= threshold:
                    return ModerationResult(
                        verdict=ModerationVerdict.BLOCK,
                        category=ContentCategory(
                            "nsfw" if category in ("adult", "racy") else category
                        ),
                        confidence=score / 5.0,
                        reason=f"SafeSearch {category}: {score}/5",
                        user_message=(
                            "This image has been rejected because it contains "
                            "inappropriate content. Please upload a photo of "
                            "your math or physics question only."
                        ),
                        details={"safesearch": {
                            "adult": LIKELIHOOD_SCORE.get(safe.adult, 0),
                            "violence": LIKELIHOOD_SCORE.get(safe.violence, 0),
                            "racy": LIKELIHOOD_SCORE.get(safe.racy, 0),
                            "medical": LIKELIHOOD_SCORE.get(safe.medical, 0),
                            "spoof": LIKELIHOOD_SCORE.get(safe.spoof, 0),
                        }},
                    )

            # Check flag thresholds
            for category, threshold in FLAG_THRESHOLDS.items():
                score = LIKELIHOOD_SCORE.get(getattr(safe, category), 0)
                if score >= threshold:
                    return ModerationResult(
                        verdict=ModerationVerdict.FLAG,
                        category=ContentCategory.SPOOF
                        if category == "spoof"
                        else ContentCategory.NSFW,
                        confidence=score / 5.0,
                        reason=f"SafeSearch {category} flagged: {score}/5",
                    )

        # --- Check Face Detection ---
        if response.face_annotations:
            max_confidence = max(
                f.detection_confidence for f in response.face_annotations
            )
            if max_confidence > 0.7:
                # Calculate face area as percentage of image
                # (simplified — use bounding poly in production)
                return ModerationResult(
                    verdict=ModerationVerdict.BLOCK,
                    category=ContentCategory.FACE_PII,
                    confidence=max_confidence,
                    reason=f"Face detected with confidence {max_confidence:.2f}",
                    user_message=(
                        "Your photo appears to contain a person's face. "
                        "For your privacy, please photograph only your "
                        "written math or physics question."
                    ),
                    details={"face_count": len(response.face_annotations)},
                )

        return ModerationResult(verdict=ModerationVerdict.PASS)
```

### 7.2 C# — .NET Backend Integration

```csharp
// =============================================================================
// Cena.Api/Services/ImageModerationService.cs
// =============================================================================
// Orchestrates the four-tier moderation pipeline for student photo uploads.
// Called by the screenshot upload endpoint before forwarding to Gemini OCR.
// =============================================================================

using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace Cena.Api.Services;

/// <summary>
/// Moderation verdict for an uploaded image.
/// </summary>
public enum ModerationVerdict
{
    Pass,
    Flag,
    Block,
    CsamMatch
}

/// <summary>
/// Content category that triggered the moderation decision.
/// </summary>
public enum ContentCategory
{
    None,
    Csam,
    Nsfw,
    Violence,
    HateSymbol,
    SelfHarm,
    FacePii,
    TextPii,
    NonEducational,
    Spoof
}

/// <summary>
/// Result of running an image through the moderation pipeline.
/// </summary>
public sealed record ModerationResult(
    ModerationVerdict Verdict,
    ContentCategory Category = ContentCategory.None,
    double Confidence = 1.0,
    string Reason = "",
    string UserMessage = ""
);

/// <summary>
/// Tier 1 client-side validation result (verified server-side).
/// </summary>
public sealed record Tier1ValidationResult(
    bool IsValid,
    string? RejectionReason = null
);

/// <summary>
/// Orchestrates the four-tier content moderation pipeline.
/// </summary>
public sealed class ImageModerationService
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/heif",
        "image/heic"
    };

    private const int MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int MinDimension = 200;
    private const int MaxDimension = 8192;

    private readonly ILogger<ImageModerationService> _logger;

    public ImageModerationService(ILogger<ImageModerationService> logger)
    {
        _logger = logger;
    }

    // -----------------------------------------------------------------
    // Tier 1: Server-side re-validation of client pre-filter checks
    // -----------------------------------------------------------------

    /// <summary>
    /// Re-validates client-side pre-filter checks on the server.
    /// Never trust the client.
    /// </summary>
    public Tier1ValidationResult ValidateTier1(
        byte[] imageBytes,
        string declaredMimeType)
    {
        // Check file size
        if (imageBytes.Length > MaxFileSizeBytes)
            return new(false, "Image exceeds 10 MB size limit.");

        if (imageBytes.Length < 1024) // < 1 KB is suspicious
            return new(false, "Image file is too small to contain a valid photo.");

        // Check MIME type
        if (!AllowedMimeTypes.Contains(declaredMimeType))
            return new(false, "Unsupported image format. Please use JPEG, PNG, or WebP.");

        // Verify magic bytes match declared type
        if (!MagicBytesMatch(imageBytes, declaredMimeType))
            return new(false, "File content does not match declared image format.");

        // Check dimensions
        try
        {
            var info = Image.Identify(imageBytes);
            if (info is null)
                return new(false, "Could not read image dimensions.");

            if (info.Width < MinDimension || info.Height < MinDimension)
                return new(false, "Image is too small. Please take a clearer photo.");

            if (info.Width > MaxDimension || info.Height > MaxDimension)
                return new(false, "Image dimensions are too large.");

            // Check aspect ratio (between 1:4 and 4:1)
            double ratio = (double)info.Width / info.Height;
            if (ratio < 0.25 || ratio > 4.0)
                return new(false, "Unusual image shape. Please use a standard photo.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to identify image dimensions");
            return new(false, "Could not read image. Please try a different photo.");
        }

        return new(true);
    }

    // -----------------------------------------------------------------
    // EXIF stripping
    // -----------------------------------------------------------------

    /// <summary>
    /// Strips ALL metadata (EXIF, IPTC, XMP, ICC) from an image
    /// and re-encodes as JPEG. This is a critical privacy protection
    /// for student photos that may contain GPS coordinates.
    /// </summary>
    public byte[] StripMetadataAndReencode(byte[] imageBytes)
    {
        using var image = Image.Load(imageBytes);

        // Remove all metadata profiles
        image.Metadata.ExifProfile = null;
        image.Metadata.IccProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;

        // Re-encode as JPEG (quality 85 is sufficient for OCR)
        using var output = new MemoryStream();
        image.SaveAsJpeg(output, new JpegEncoder { Quality = 85 });

        _logger.LogDebug(
            "Stripped metadata: {OriginalSize} -> {CleanSize} bytes",
            imageBytes.Length,
            output.Length);

        return output.ToArray();
    }

    // -----------------------------------------------------------------
    // Post-extraction PII detection (Tier 4)
    // -----------------------------------------------------------------

    /// <summary>
    /// Scans extracted text for personally identifiable information.
    /// Called after Gemini 2.5 Flash extracts text from the image.
    /// </summary>
    public PiiScanResult ScanForPii(string extractedText)
    {
        var detections = new List<PiiDetection>();

        // Israeli ID (Teudat Zehut) — 9 digits
        foreach (Match m in Regex.Matches(extractedText, @"\b\d{9}\b"))
        {
            if (IsValidIsraeliId(m.Value))
                detections.Add(new("israeli_id", m.Value, m.Index));
        }

        // Phone numbers (Israeli format)
        foreach (Match m in Regex.Matches(
            extractedText,
            @"(?:\+972|0)[\s\-]?(?:5[0-9]|[2-4]|[8-9])[\s\-]?\d{3}[\s\-]?\d{4}"))
        {
            detections.Add(new("phone", m.Value, m.Index));
        }

        // Email addresses
        foreach (Match m in Regex.Matches(
            extractedText,
            @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
            RegexOptions.IgnoreCase))
        {
            detections.Add(new("email", m.Value, m.Index));
        }

        // Credit card numbers
        foreach (Match m in Regex.Matches(
            extractedText,
            @"\b(?:\d{4}[\s\-]?){3}\d{4}\b"))
        {
            detections.Add(new("credit_card", m.Value, m.Index));
        }

        return new PiiScanResult(
            ContainsPii: detections.Count > 0,
            Detections: detections
        );
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static bool MagicBytesMatch(byte[] data, string mimeType)
    {
        if (data.Length < 4) return false;

        return mimeType.ToLowerInvariant() switch
        {
            "image/jpeg" => data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF,
            "image/png" => data[0] == 0x89 && data[1] == 0x50
                        && data[2] == 0x4E && data[3] == 0x47,
            "image/webp" => data.Length >= 12
                        && data[0] == 0x52 && data[1] == 0x49  // "RI"
                        && data[2] == 0x46 && data[3] == 0x46   // "FF"
                        && data[8] == 0x57 && data[9] == 0x45,  // "WE"
            "image/heif" or "image/heic" => data.Length >= 12
                        && data[4] == 0x66 && data[5] == 0x74   // "ft"
                        && data[6] == 0x79 && data[7] == 0x70,  // "yp"
            _ => false,
        };
    }

    /// <summary>
    /// Validates an Israeli ID number using the Luhn-like check digit algorithm.
    /// </summary>
    private static bool IsValidIsraeliId(string digits)
    {
        if (digits.Length != 9) return false;

        int sum = 0;
        for (int i = 0; i < 9; i++)
        {
            int digit = digits[i] - '0';
            int val = digit * ((i % 2 == 0) ? 1 : 2);
            if (val > 9) val -= 9;
            sum += val;
        }
        return sum % 10 == 0;
    }
}

public sealed record PiiDetection(string Type, string Value, int Position);
public sealed record PiiScanResult(bool ContainsPii, List<PiiDetection> Detections);
```

### 7.3 Vue 3 — Client-Side Pre-Filter (Tier 1)

```typescript
// src/student/composables/useImageModeration.ts
// Client-side Tier 1 pre-filter for student photo uploads

interface PreFilterResult {
  valid: boolean
  error?: string
  cleanBlob?: Blob
}

const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'image/heif', 'image/heic']
const MAX_SIZE_BYTES = 10 * 1024 * 1024  // 10 MB
const MIN_DIMENSION = 200
const MAX_DIMENSION = 8192

/**
 * Strips EXIF metadata by re-encoding via canvas.
 * Canvas.toBlob() produces a clean image without metadata.
 */
async function stripExifViaCanvas(file: File): Promise<Blob> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    const url = URL.createObjectURL(file)

    img.onload = () => {
      URL.revokeObjectURL(url)
      const canvas = document.createElement('canvas')
      canvas.width = img.naturalWidth
      canvas.height = img.naturalHeight
      const ctx = canvas.getContext('2d')
      if (!ctx) {
        reject(new Error('Canvas context unavailable'))
        return
      }
      ctx.drawImage(img, 0, 0)
      canvas.toBlob(
        (blob) => {
          if (blob) resolve(blob)
          else reject(new Error('Canvas toBlob failed'))
        },
        'image/jpeg',
        0.85
      )
    }

    img.onerror = () => {
      URL.revokeObjectURL(url)
      reject(new Error('Failed to load image'))
    }

    img.src = url
  })
}

/**
 * Client-side pre-filter for student photo uploads.
 * Validates file type, size, dimensions, and strips EXIF metadata.
 */
export async function preFilterImage(file: File): Promise<PreFilterResult> {
  // Check MIME type
  if (!ALLOWED_TYPES.includes(file.type)) {
    return { valid: false, error: 'Please upload a photo (JPEG, PNG, or WebP).' }
  }

  // Check file size
  if (file.size > MAX_SIZE_BYTES) {
    return { valid: false, error: 'Photo is too large (max 10 MB). Try a closer photo.' }
  }

  if (file.size < 1024) {
    return { valid: false, error: 'Photo is too small to be valid.' }
  }

  // Check dimensions
  try {
    const dims = await getImageDimensions(file)

    if (dims.width < MIN_DIMENSION || dims.height < MIN_DIMENSION) {
      return { valid: false, error: 'Photo is too small. Please take a clearer photo.' }
    }

    if (dims.width > MAX_DIMENSION || dims.height > MAX_DIMENSION) {
      return { valid: false, error: 'Photo dimensions are too large.' }
    }

    const ratio = dims.width / dims.height
    if (ratio < 0.25 || ratio > 4.0) {
      return { valid: false, error: 'Unusual photo shape. Please use a standard photo.' }
    }
  } catch {
    return { valid: false, error: 'Could not read photo. Please try a different image.' }
  }

  // Strip EXIF metadata via canvas re-encoding
  try {
    const cleanBlob = await stripExifViaCanvas(file)
    return { valid: true, cleanBlob }
  } catch {
    return { valid: false, error: 'Could not process photo. Please try again.' }
  }
}

function getImageDimensions(file: File): Promise<{ width: number; height: number }> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    const url = URL.createObjectURL(file)
    img.onload = () => {
      URL.revokeObjectURL(url)
      resolve({ width: img.naturalWidth, height: img.naturalHeight })
    }
    img.onerror = () => {
      URL.revokeObjectURL(url)
      reject(new Error('Failed to load image'))
    }
    img.src = url
  })
}
```

### 7.4 Flutter — Client-Side Pre-Filter (Mobile)

```dart
// src/mobile/lib/core/services/image_prefilter_service.dart
// Client-side Tier 1 pre-filter for Flutter mobile app

import 'dart:typed_data';
import 'package:image/image.dart' as img;

/// Result of client-side image pre-filtering.
class PreFilterResult {
  const PreFilterResult({
    required this.valid,
    this.error,
    this.cleanBytes,
  });

  final bool valid;
  final String? error;
  final Uint8List? cleanBytes;
}

/// Client-side image pre-filter for student photo uploads.
/// Validates file type, size, dimensions, and strips EXIF metadata.
class ImagePreFilterService {
  static const int maxSizeBytes = 10 * 1024 * 1024; // 10 MB
  static const int minDimension = 200;
  static const int maxDimension = 8192;
  static const Set<String> allowedExtensions = {
    'jpg', 'jpeg', 'png', 'webp', 'heif', 'heic',
  };

  /// Run all pre-filter checks and strip EXIF metadata.
  Future<PreFilterResult> preFilter(Uint8List imageBytes, String fileName) async {
    // Check file size
    if (imageBytes.length > maxSizeBytes) {
      return const PreFilterResult(
        valid: false,
        error: 'Photo is too large (max 10 MB). Try a closer photo.',
      );
    }

    if (imageBytes.length < 1024) {
      return const PreFilterResult(
        valid: false,
        error: 'Photo is too small to be valid.',
      );
    }

    // Check file extension
    final ext = fileName.split('.').last.toLowerCase();
    if (!allowedExtensions.contains(ext)) {
      return const PreFilterResult(
        valid: false,
        error: 'Please upload a photo (JPEG, PNG, or WebP).',
      );
    }

    // Decode and check dimensions
    final decoded = img.decodeImage(imageBytes);
    if (decoded == null) {
      return const PreFilterResult(
        valid: false,
        error: 'Could not read photo. Please try a different image.',
      );
    }

    if (decoded.width < minDimension || decoded.height < minDimension) {
      return const PreFilterResult(
        valid: false,
        error: 'Photo is too small. Please take a clearer photo.',
      );
    }

    if (decoded.width > maxDimension || decoded.height > maxDimension) {
      return const PreFilterResult(
        valid: false,
        error: 'Photo dimensions are too large.',
      );
    }

    final ratio = decoded.width / decoded.height;
    if (ratio < 0.25 || ratio > 4.0) {
      return const PreFilterResult(
        valid: false,
        error: 'Unusual photo shape. Please use a standard photo.',
      );
    }

    // Strip EXIF by re-encoding as JPEG (img.encodeJpg strips metadata)
    final cleanBytes = Uint8List.fromList(
      img.encodeJpg(decoded, quality: 85),
    );

    return PreFilterResult(
      valid: true,
      cleanBytes: cleanBytes,
    );
  }
}
```

---

## 8. Cena-Specific Flow Diagram

```
                            Student uploads photo
                                    │
                    ┌───────────────┼───────────────┐
                    │         TIER 1 (Client)        │
                    │  ┌──────────────────────────┐  │
                    │  │ File type check           │  │
                    │  │ Size check (< 10 MB)      │  │
                    │  │ Dimension check            │  │
                    │  │ Aspect ratio check         │  │
                    │  │ EXIF strip (canvas)        │  │
                    │  │ Magic bytes verify         │  │
                    │  └────────────┬───────────────┘  │
                    └───────────────┼───────────────────┘
                                    │ PASS
                                    ▼
                    ┌───────────────────────────────────┐
                    │  HTTPS upload to /api/student/     │
                    │  screenshot/upload                 │
                    └───────────────┬───────────────────┘
                                    │
                    ┌───────────────┼───────────────┐
                    │       SERVER PRE-PROCESS       │
                    │  ┌──────────────────────────┐  │
                    │  │ Re-validate Tier 1 checks │  │
                    │  │ Re-strip EXIF (ImageSharp)│  │
                    │  │ Generate content hash     │  │
                    │  └────────────┬───────────────┘  │
                    └───────────────┼───────────────────┘
                                    │
                    ┌───────────────┼───────────────┐
                    │     TIER 0 (PhotoDNA CSAM)     │
                    │  ┌──────────────────────────┐  │
                    │  │ Hash against NCMEC DB     │  │
                    │  │ Match → HTTP 451 + report │  │
                    │  └────────────┬───────────────┘  │
                    └───────────────┼───────────────────┘
                                    │ NO MATCH
                                    ▼
                    ┌───────────────────────────────────┐
                    │     TIER 2 (Cloud Vision API)      │
                    │  ┌─────────────────────────────┐  │
                    │  │ SafeSearch Detection         │  │
                    │  │   adult ≥ LIKELY → BLOCK     │  │
                    │  │   violence ≥ LIKELY → BLOCK  │  │
                    │  │   racy ≥ VERY_LIKELY → BLOCK │  │
                    │  │                              │  │
                    │  │ Face Detection               │  │
                    │  │   confidence > 0.7 → REJECT  │  │
                    │  │                              │  │
                    │  │ Label Detection               │  │
                    │  │   non-educational → FLAG     │  │
                    │  └──────────────┬──────────────┘  │
                    └─────────────────┼─────────────────┘
                                      │ PASS
                                      ▼
                    ┌───────────────────────────────────┐
                    │  TIER 3 (Gemini 2.5 Flash OCR)    │
                    │  ┌─────────────────────────────┐  │
                    │  │ Classification prompt:       │  │
                    │  │   "Is this math/physics?"    │  │
                    │  │   NO → REJECT                │  │
                    │  │                              │  │
                    │  │ Face check (in prompt):       │  │
                    │  │   "Any faces visible?"       │  │
                    │  │   YES → REJECT               │  │
                    │  │                              │  │
                    │  │ Extract LaTeX content        │  │
                    │  └──────────────┬──────────────┘  │
                    └─────────────────┼─────────────────┘
                                      │ LaTeX extracted
                                      ▼
                    ┌───────────────────────────────────┐
                    │  TIER 4 (Post-Extraction)          │
                    │  ┌─────────────────────────────┐  │
                    │  │ LaTeX parseable? (KaTeX)     │  │
                    │  │ Contains math symbols?       │  │
                    │  │ PII scan on extracted text   │  │
                    │  │ Prompt injection check       │  │
                    │  │ LaTeX sanitization           │  │
                    │  └──────────────┬──────────────┘  │
                    └─────────────────┼─────────────────┘
                                      │ VALIDATED
                                      ▼
                    ┌───────────────────────────────────┐
                    │  CAS Pipeline                      │
                    │  MathNet → SymPy → Wolfram         │
                    │  (existing flow)                   │
                    └───────────────────────────────────┘

    Delete original image after extraction.
    Retain only: LaTeX, moderation decision log, content hash.
```

---

## 9. Security Score Contribution

### 9.1 Scoring Breakdown

| Control | Points | Justification |
|---------|--------|---------------|
| **CSAM hash matching (Tier 0)** | 15/100 | Legal obligation; blocks known illegal content |
| **EXIF stripping (client + server)** | 10/100 | Prevents location/device leakage for minors |
| **SafeSearch NSFW/violence blocking** | 12/100 | Protects minors from harmful content |
| **Face detection and rejection** | 10/100 | Biometric PII protection; PPL Amendment 13 compliance |
| **Educational content classifier** | 8/100 | Ensures pipeline processes only valid inputs |
| **Text PII detection and redaction** | 8/100 | Catches PII missed by image-level checks |
| **Post-extraction LaTeX validation** | 5/100 | Defense against injection (Iterations 2-3) |
| **Image retention policy (delete after extraction)** | 7/100 | COPPA/GDPR/PPL data minimization compliance |
| **Audit logging of moderation decisions** | 5/100 | Compliance evidence; false positive tracking |
| **Crisis content response protocol** | 6/100 | Duty of care to minors; self-harm detection |

### 9.2 Cumulative Score

| Iteration | Domain | Points Added | Cumulative |
|-----------|--------|-------------|------------|
| 1 | Adversarial image attacks | 18 | 18 |
| 2 | Multimodal prompt injection | 20 | 38 |
| 3 | LaTeX sanitization | 20 | 58 |
| **4** | **Content moderation for minors** | **14** | **72** |

**Note:** The full 86 points from this iteration's controls are not all additive — some overlap with prior iterations (e.g., post-extraction validation was partially scored in Iteration 3). The net new contribution is 14 points, bringing the cumulative score to 72/100.

### 9.3 Remaining Gap (28 points)

Planned coverage in remaining iterations:
- Iteration 5: Rate limiting for image uploads (+6)
- Iteration 6: Privacy-preserving processing, no PII retention (+6)
- Iteration 7: Academic integrity / cheating detection (+4)
- Iteration 8: Accessibility (+4)
- Iteration 9: Error handling / graceful degradation (+4)
- Iteration 10: End-to-end attack simulation (+4)

---

## 10. Operational Procedures

### 10.1 CSAM Incident Response Procedure

```
1. PhotoDNA API returns MATCH
2. IMMEDIATELY:
   a. Return HTTP 451 to client (generic "upload failed" — do NOT reveal reason)
   b. Write to sealed audit log (separate from regular logs, restricted access)
   c. Quarantine the image (do NOT delete; do NOT display to any staff)
   d. Block the student account pending investigation
3. WITHIN 1 HOUR:
   a. Designated CSAM Reporting Officer notified via secure channel
   b. Officer files CyberTipline report at report.cybertip.org
   c. Officer contacts Israeli Police Lahav 433 unit if Israeli student
4. WITHIN 24 HOURS:
   a. CyberTipline report confirmed filed
   b. Preserve all metadata, logs, and the quarantined image
5. DO NOT:
   a. View the quarantined image (legal risk to viewer)
   b. Delete the image until law enforcement authorizes
   c. Notify the student of the specific reason for account block
   d. Discuss the incident outside the designated response team
```

### 10.2 Crisis Content Response (Self-Harm)

```
IF extracted text contains crisis language:
  - Hebrew: "אני רוצה למות", "לשים סוף", "להתאבד"
  - Arabic: "أريد أن أموت", "انتحار"
  - English: "kill myself", "want to die", "end it all"

THEN:
  1. Do NOT reject the upload with a generic error
  2. Display crisis resources prominently:
     - ERAN (Israel): 1201
     - Natal (Israel): 1-800-363-363
     - National Suicide Prevention Lifeline (US): 988
  3. Allow the math question to be processed normally
  4. Send a NATS notification to the school counselor dashboard
  5. Log the event for the institution's safeguarding team
  6. Do NOT contact the student directly about the crisis language
     (trained professionals should handle this)
```

### 10.3 Monitoring and Alerting

| Metric | Threshold | Alert |
|--------|-----------|-------|
| SafeSearch block rate | > 5% of uploads | Investigate — possible coordinated misuse |
| Face detection rate | > 15% of uploads | UX issue — students not understanding "photograph your paper" |
| Non-educational rate | > 20% of uploads | UX issue or misuse pattern |
| False positive rate (appeals) | > 10% in any category | Adjust thresholds |
| CSAM match | Any | Immediate incident response |
| Crisis content detection | Any | Safeguarding notification |
| Moderation API latency | > 2000ms p95 | Performance degradation — check API quotas |
| Moderation API error rate | > 1% | Fallback to degraded mode (allow with flag) |

---

## 11. Recommendations for Cena

### 11.1 Immediate Actions (Before Launch)

1. **Apply for PhotoDNA Cloud Service access** via Microsoft's application page. This takes 2-4 weeks for vetting. Start now.
2. **Enable Google Cloud Vision API** in the GCP project (`cena-platform`). Configure SafeSearch + Label Detection.
3. **Implement EXIF stripping** on both Vue 3 and Flutter clients AND on the .NET backend.
4. **Add face detection** to the Gemini 2.5 Flash OCR prompt (zero additional cost).
5. **Designate a CSAM Reporting Officer** and establish the incident response procedure.
6. **Configure Cloud Vision API to use `me-west1` (Tel Aviv) endpoint** for data residency compliance.

### 11.2 Pre-Production Hardening

7. **Deploy the `ImageModerationService`** (C# code in Section 7.2) in the .NET backend.
8. **Add the Vue 3 `preFilterImage` composable** (Section 7.3) to the student upload flow.
9. **Add the Flutter `ImagePreFilterService`** (Section 7.4) to the mobile upload flow.
10. **Create the `moderation_decisions` table** (Section 5.3) for false positive tracking.
11. **Conduct a DPIA** (Data Protection Impact Assessment) covering the image moderation pipeline, as required by both GDPR and Israeli PPL Amendment 13.
12. **Update the privacy policy** to disclose: (a) images are scanned for safety before processing, (b) no images are stored after math content extraction, (c) EXIF metadata is stripped.

### 11.3 Post-Launch Optimization

13. **Monitor false positive rates** weekly and adjust thresholds.
14. **Train the custom educational classifier** on real student upload data (after anonymization) to improve Tier 3 accuracy beyond the Gemini prompt-based approach.
15. **Evaluate Azure Content Safety** as a secondary provider for self-harm detection (Azure has a dedicated self-harm category that Google Vision lacks).

---

## References

### Legal and Compliance
- [COPPA Compliance in 2025: A Practical Guide for EdTech](https://blog.promise.legal/startup-central/coppa-compliance-in-2025-a-practical-guide-for-tech-edtech-and-kids-apps/)
- [FTC COPPA FAQ](https://www.ftc.gov/business-guidance/resources/complying-coppa-frequently-asked-questions)
- [The FTC's Updated COPPA Rule](https://www.finnegan.com/en/insights/articles/the-ftcs-updated-coppa-rule-redefining-childrens-digital-privacy-protection.html)
- [COPPA 2.0 and GDPR-K: What Kids' Creators Must Know in 2026](https://air.io/en/youtube-hacks/coppa-20-and-gdpr-k-what-kids-creators-must-know-in-2026)
- [Children's Online Privacy Rules: COPPA, GDPR-K, and Age Verification](https://pandectes.io/blog/childrens-online-privacy-rules-around-coppa-gdpr-k-and-age-verification/)
- [Israel Privacy Protection Law Amendment 13](https://iapp.org/news/a/israel-marks-a-new-era-in-privacy-law-amendment-13-ushers-in-sweeping-reform)
- [Data Protection Laws 2025-2026 Israel](https://iclg.com/practice-areas/data-protection-laws-and-regulations/israel)
- [What Israel's Amendment 13 Means for Businesses in 2025](https://bigid.com/blog/what-israel-amendment-13-means-for-businesses-in-2025/)

### CSAM and Reporting
- [REPORT Act 2024](https://warrantbuilder.com/report_act/)
- [NCMEC Reporting by Online Platforms](https://www.globalchildexploitationpolicy.org/content/gpp-ncmec/us/en/policy-advocacy/reporting-by-online-platforms.html)
- [Technology Coalition: Update on Voluntary CSAM Detection](https://technologycoalition.org/resources/update-on-voluntary-detection-of-csam/)
- [PhotoDNA — Wikipedia](https://en.wikipedia.org/wiki/PhotoDNA)
- [Deploying PhotoDNA for CSAM Detection (Scribd)](https://tech.scribd.com/blog/2026/photodna-csam-detection.html)
- [CSAM Detection Tools Guide](https://www.cometchat.com/blog/csam-detection-tools)

### Content Moderation APIs
- [Google Cloud Vision API Pricing](https://cloud.google.com/vision/pricing)
- [Google Cloud Vision SafeSearch Documentation](https://docs.cloud.google.com/vision/docs/detecting-safe-search)
- [AWS Rekognition Content Moderation](https://aws.amazon.com/rekognition/content-moderation/)
- [AWS Rekognition Pricing](https://aws.amazon.com/rekognition/pricing/)
- [Azure Content Safety Pricing](https://azure.microsoft.com/en-us/pricing/details/content-safety/)
- [Best AI Content Moderation APIs Compared 2026](https://wavespeed.ai/blog/posts/best-ai-content-moderation-apis-tools-2026/)
- [12 Best AI Content Moderation APIs: Complete Guide](https://estha.ai/blog/12-best-ai-content-moderation-apis-compared-the-complete-guide/)

### PII and Privacy
- [EXIF Data Risks: Strip Image Metadata for Global Privacy](https://mochify.xyz/guides/exif-data-risks-image-compression-2026)
- [EDUCAUSE: Privacy Implications of EXIF Data](https://er.educause.edu/articles/2021/6/privacy-implications-of-exif-data)
- [Octopii: AI-Powered PII Scanner for Images](https://redhuntlabs.com/blog/octopii-an-opensource-pii-scanner-for-images/)

### Open-Source Tools
- [NudeNet on PyPI](https://pypi.org/project/nudenet/)
- [NudeNet: Ensemble of Neural Nets for Nudity Detection](https://praneethbedapudi.medium.com/nudenet-an-ensemble-of-neural-nets-for-nudity-detection-and-censoring-d9f3da721e3)
- [Comparative Study of Explicit Content Detection Tools](https://hal.science/hal-04179978/document)

### Self-Harm and Crisis Response
- [Samaritans: Effective Content Moderation for Self-Harm](https://www.samaritans.org/about-samaritans/research-policy/internet-suicide/guidelines-tech-industry/effective-content-moderation/)
- [Sightengine: Self-Harm Moderation Guide 2026](https://sightengine.com/self-harm-mental-health-suicide-moderation-guide)

### False Positives and Accuracy
- [Content Moderation by LLM: From Accuracy to Legitimacy (Springer)](https://link.springer.com/article/10.1007/s10462-025-11328-1)
- [Evaluating Google Cloud Vision for Image Moderation (HUMAN Protocol)](https://humanprotocol.org/blog/evaluating-google-cloud-vision-for-image-moderation-how-reliable-is-it)
- [Benchmarking Google Vision, Amazon Rekognition, Microsoft Azure (Sightengine)](https://medium.com/sightengine/benchmarking-google-vision-amazon-rekognition-microsoft-azure-on-image-moderation-73909739b8b4)
