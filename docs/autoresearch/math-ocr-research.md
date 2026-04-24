# Mathematical OCR & Document Understanding: State-of-the-Art Research

> **Status:** Research findings
> **Date:** 2026-03-27
> **Purpose:** Evaluate tools for extracting structured math (LaTeX) from Bagrut exam PDFs and student-submitted photos, with Hebrew and Arabic RTL text support.
> **Applies to:** Content Authoring Context (CNT-001 corpus ingestion), future student photo-upload feature

---

## Executive Summary

For an Israeli EdTech startup processing Bagrut (matriculation exam) PDFs and student photos containing math in Hebrew and Arabic, no single tool solves every requirement. The recommended approach is a **tiered pipeline**:

1. **Corpus ingestion (Bagrut PDFs to structured data):** Marker/Surya for layout + text extraction, supplemented by Mathpix or Mistral OCR for high-fidelity math-to-LaTeX conversion, with LLM post-processing (Gemini 2.5 Flash or Kimi) for semantic structuring.
2. **Student photo upload (real-time):** Gemini 2.5 Flash vision as primary (cost-effective, multilingual, good math), with Mathpix as fallback for complex handwritten equations.
3. **Validation layer:** SymPy for mathematical correctness verification of extracted LaTeX.

**Key finding:** LLM-based vision approaches (Gemini 2.5 Flash, GPT-4o) have reached parity with or surpassed dedicated OCR tools for mixed-content documents (RTL text + math), especially for printed content. For handwritten math, dedicated tools (Mathpix) still hold an edge but the gap is narrowing rapidly.

---

## 1. Mathpix

### Overview
The market leader in math-specific OCR. Founded 2014, used by major EdTech platforms (Chegg, Coursera) and publishers. Converts images and PDFs of math into LaTeX, MathML, AsciiMath, and Mathpix Markdown.

### Capabilities
- **Printed math:** Industry-leading accuracy, reliably converts complex multi-line equations, matrices, and integral expressions to LaTeX.
- **Handwritten math:** Strong performance on reasonably legible handwriting; accuracy degrades on messy or overlapping strokes, but still the best commercial option.
- **Document structure:** Extracts tables, text, diagrams alongside math. PDF processing preserves page structure.
- **Output formats:** LaTeX, MathML, AsciiMath, Mathpix Markdown (.mmd), DOCX, HTML.

### Hebrew/Arabic Support
- **Confirmed supported.** Mathpix lists both Arabic and Hebrew among 30+ supported languages as of 2025.
- **Caveat:** The math recognition is script-agnostic (math notation is universal LTR), but the surrounding text OCR quality for Hebrew/Arabic has not been independently benchmarked at the same level as English. Diacritics (nikud/harakat) recognition quality is unknown.
- **Bidi handling:** Mathpix's structured output separates math blocks from text blocks, which helps downstream bidi rendering. However, inline math within RTL text may require post-processing to insert correct Unicode bidi isolation markers.

### Accuracy
- No published BLEU/CER scores from Mathpix (proprietary). Independent testing shows:
  - Printed equations: ~95-99% character-level accuracy on clean scans.
  - Handwritten equations: ~85-92% on legible handwriting, dropping to ~70% on poor quality.
  - Tables: High structural accuracy but occasional cell-merging errors.
- Industry standard against which other tools are benchmarked.

### Pricing (as of March 2025)
| Tier | Cost |
|------|------|
| PDF pages (first 1M) | $0.005/page |
| PDF pages (>1M) | $0.0035/page |
| Image/text requests (first 1M) | $0.002/request |
| Image/text requests (next 500K) | $0.0015/request |
| Free tier | 20 requests/month (Snip app) |

**Cost for Cena corpus ingestion:** ~32 Bagrut PDFs x ~20 pages each = ~640 pages = ~$3.20 total. Negligible.
**Cost for student photo upload at scale:** 100K photos/month = $200/month (image API).

### API
- REST API with JSON responses. Well-documented. SDKs for Python, Node.js, Swift, Kotlin.
- Batch endpoint for PDF processing. Real-time endpoint for single images (<1s latency).
- Rate limits: 200 requests/minute on standard plan.

### Verdict for Cena
**Strong candidate for math-to-LaTeX conversion.** Best-in-class math accuracy. Hebrew/Arabic text is supported but should be validated against actual Bagrut PDFs before committing. Cost is very low for corpus ingestion; reasonable for real-time student use.

---

## 2. Nougat (Meta)

### Overview
"Neural Optical Understanding for Academic Documents." Open-source (CC-BY-NC) model from Meta Research (2023). Built on the Donut architecture (Swin Transformer encoder + Transformer decoder). Trained on 8M+ scientific articles from arXiv, PubMed Central, and the Industry Documents Library.

### Capabilities
- **Primary strength:** Converting entire academic PDF pages to Mathpix Markdown (.mmd), preserving LaTeX equations, section structure, tables, and references.
- **Math accuracy:** Outperforms baseline approaches on arXiv-style documents. Excellent on printed LaTeX-typeset math.
- **Document types:** Optimized for academic/scientific papers. Works well on structured documents with clear layouts.

### Hebrew/Arabic Support
- **Not supported natively.** Trained exclusively on English-language scientific documents. The vocabulary and decoder are English-only.
- **Arabic-Nougat exists:** A community fine-tune (MohamedRashad/arabic-nougat) trained on Arabic book pages. Three model sizes available on HuggingFace. However:
  - Max context length of 2,048 tokens (may truncate long pages).
  - May generate repeated/incorrect text.
  - Not tested on mixed Arabic+math documents.
  - No Hebrew equivalent exists.
- **Hebrew-Nougat:** Does not exist. Would require fine-tuning on a Hebrew scientific document corpus, which is scarce.

### Accuracy
- On arXiv test set: BLEU ~0.88, outperforming other approaches across multiple metrics.
- On non-arXiv documents: Significantly lower accuracy. Marker claims to be more accurate than Nougat outside arXiv.
- Hallucination risk: Known to generate plausible-looking but incorrect LaTeX when the input is ambiguous or low-resolution.
- No benchmarks on exam-style documents (Bagrut format).

### Pricing
- **Free and open-source** (CC-BY-NC license -- note: non-commercial restriction).
- Requires GPU for inference (A100 recommended). ~2-5 seconds per page on A100.
- Self-hosted cost: ~$1-2/hr on cloud GPU, or ~$0.001-0.003/page.

### API
- No hosted API. Must self-host. Available via HuggingFace and PyPI (`nougat-ocr`).

### Verdict for Cena
**Not recommended as primary tool.** English-only, non-commercial license, optimized for academic papers not exam documents. The Arabic-Nougat variant is interesting but immature. The hallucination risk is unacceptable for educational content where math must be correct.

---

## 3. GOT-OCR 2.0 (General OCR Theory)

### Overview
A unified end-to-end OCR model (580M parameters) from UCAS (2024). Aims to handle all "optical signals" -- plain text, math formulas, molecular formulas, tables, charts, sheet music, geometric shapes -- in a single model.

### Capabilities
- **Architecture:** High-compression encoder (1024x1024 input -> 256 tokens) + long-context decoder (up to 8K output tokens). Uses Qwen-0.5B as decoder backbone.
- **Math:** Trained on math formulas using LaTeX/Mathpix Markdown format. Can output formatted results in Markdown or LaTeX.
- **Interactive OCR:** Supports region-level recognition guided by coordinates or color highlighting.
- **Multi-format:** Handles scene text, document text, handwriting, tables, and formulas.

### Hebrew/Arabic Support
- **Limited.** The Qwen-0.5B decoder has multilingual priors, but training data is primarily English and Chinese.
- **Handwriting training:** Chinese (CASIA-HWDB2), English (IAM), Norwegian (NorHand-v3). No Arabic or Hebrew handwriting data.
- **Text recognition:** May handle printed Hebrew/Arabic through the multilingual decoder priors, but this is untested and likely unreliable for diacritical marks.

### Accuracy
- Published results focus on English/Chinese benchmarks. Strong on scene text and printed formulas.
- No published benchmarks on RTL languages or mixed bidi content.
- Math formula accuracy: competitive with specialized tools on clean printed input, but less robust on handwritten or noisy input.

### Pricing
- **Free and open-source** (Apache 2.0 license).
- 580M parameters -- can run on consumer GPUs. ~1-3 seconds per image on RTX 3090.

### API
- No hosted API. Self-host via HuggingFace. GitHub: `Ucas-HaoranWei/GOT-OCR2.0`.

### Verdict for Cena
**Interesting but not production-ready for this use case.** The unified approach is promising, but lack of Hebrew/Arabic training data and RTL handling makes it unsuitable. Worth monitoring for future versions.

---

## 4. Google Cloud Document AI

### Overview
Google's enterprise document processing platform. Combines layout analysis, OCR, entity extraction, and specialized processors (invoices, receipts, identity documents, etc.).

### Capabilities
- **OCR engine:** 200+ languages, best-in-class for general text OCR across scripts.
- **Math OCR (premium feature):** Can detect and extract mathematical formulas as LaTeX. Enabled via `ProcessOptions.ocrConfig.premiumFeatures.enableMathOcr`.
- **Layout analysis:** Excellent document structure detection -- columns, tables, headers, lists.
- **Handwriting:** Supports handwriting recognition in 50 languages.

### Hebrew/Arabic Support
- **Hebrew:** Supported for OCR (both printed and handwritten). Google's Hebrew OCR is generally strong due to massive training data from Google Books, Search, etc.
- **Arabic:** Supported for OCR. Arabic handwriting recognition is available. Google has invested heavily in Arabic NLP.
- **Bidi handling:** Google's OCR correctly identifies reading order in mixed-direction documents.
- **Diacritics:** Hebrew nikud and Arabic harakat recognition exists but accuracy varies -- faint diacritics on scanned documents are a known challenge across all OCR systems.

### Accuracy
- General text OCR: Industry-leading (typically 97-99% on clean documents).
- Math LaTeX extraction: Good on printed formulas; not as specialized as Mathpix for complex multi-line equations. Accuracy metrics not publicly published for the math feature specifically.
- Overall document understanding: 83.42% in comparative benchmarks (vs. Mistral OCR's 94.89%, per Mistral's claims -- likely measured on different test sets).

### Pricing
| Feature | Cost |
|---------|------|
| Document OCR | ~$1.50/1,000 pages (first 5M pages/month) |
| Enterprise Document OCR | ~$4/1,000 pages |
| Math OCR add-on | Premium feature pricing (contact sales) |
| Free tier | 1,000 pages/month |

**Cost for Cena corpus ingestion:** ~640 pages = ~$0.96 (standard OCR). Math OCR add-on cost unclear.
**Cost for student photos at scale:** 100K/month = ~$150-400/month depending on tier.

### API
- REST and gRPC APIs. Client libraries for Python, Java, Node.js, Go, C#, Ruby.
- Batch and online processing. Well-integrated with GCP ecosystem.

### Verdict for Cena
**Strong for general Hebrew/Arabic OCR, but math LaTeX extraction is a premium add-on with unclear accuracy on exam-style content.** Best used as a complementary tool for text extraction alongside a dedicated math OCR solution. The GCP ecosystem integration is a plus if Cena uses GCP.

---

## 5. Marker (by Vik Paruchuri / Datalab)

### Overview
Open-source PDF-to-Markdown converter. Uses a pipeline approach: layout detection (Surya) -> OCR (Surya or Tesseract) -> math detection -> math OCR (Texify, now integrated into Surya) -> Markdown assembly.

### Capabilities
- **PDF to Markdown:** Converts PDFs to clean Markdown preserving headings, lists, tables, and code blocks.
- **Math handling:** Detects math regions and converts them to LaTeX. Uses Texify (now deprecated, merged into Surya) for math-to-LaTeX conversion. "Will not convert 100% of equations to LaTeX because it has to detect then convert" -- admits imperfect math coverage.
- **Speed:** 4x faster than Nougat. Designed for batch processing of large PDF collections.
- **Layout detection:** Powered by Surya, which handles complex multi-column layouts well.

### Hebrew/Arabic Support
- **Surya supports 90+ languages** including Hebrew and Arabic for text recognition.
- **Layout analysis:** Language-agnostic (visual-only), so works for RTL documents.
- **Known issues with Arabic:** A January 2025 GitHub issue reports difficulty getting good OCR results on Arabic text. Fine-tuning may be needed for Arabic-specific document layouts.
- **Hebrew:** Included in the 90+ language list but no specific quality benchmarks available.
- **Math in RTL documents:** The math detection is visual (not language-dependent), but the reading order and inline math extraction from RTL paragraphs may have edge cases.

### Accuracy
- **Marker claims:** More accurate than Nougat outside of arXiv-style documents.
- **Math accuracy:** Dependent on the underlying Texify/Surya math model. On standard benchmarks: competitive but below Mathpix.
- **No published Hebrew/Arabic accuracy benchmarks.**

### Pricing
- **Free and open-source** (GPL-3.0 license for Marker; Surya is AGPL for non-commercial, commercial license available from Datalab).
- Hosted API available via Datalab platform (pricing not publicly listed).
- Self-hosted: runs on CPU (slower) or GPU (fast). ~1-2 seconds/page on GPU.

### API
- Python library (`marker-pdf` on PyPI). CLI tool.
- Datalab platform API for hosted processing.

### Verdict for Cena
**Good candidate for the PDF-to-Markdown stage of corpus ingestion.** The Surya foundation provides decent Hebrew/Arabic OCR, and the pipeline approach means math extraction can be supplemented or replaced with a better tool (e.g., Mathpix for equations only). The AGPL license for Surya requires attention -- commercial use needs a Datalab license.

---

## 6. Docling / Granite-Docling (IBM)

### Overview
IBM's open-source document understanding toolkit. The latest model, **Granite-Docling-258M** (released September 2025), is a compact vision-language model for document-to-text conversion. Apache 2.0 licensed.

### Capabilities
- **Document conversion:** PDF to HTML, Markdown, LaTeX, or DocTags format. Preserves layouts, tables, equations, code blocks.
- **Equation recognition:** Enhanced in Granite-Docling-258M with F1 score of 0.968 (vs. 0.947 in predecessor). Handles both inline and floating math.
- **Compact model:** Only 258M parameters -- runs efficiently on modest hardware.
- **Architecture:** Single unified model replacing multiple specialized models (layout detector + OCR + table recognizer + math recognizer).
- **Training data:** Includes SynthFormulaNet (synthetic math expressions paired with ground-truth LaTeX).

### Hebrew/Arabic Support
- **Not explicitly documented.** The model is primarily trained on English-language documents.
- **Multilingual:** The base model (PaliGemma2) has some multilingual capability, but Hebrew/Arabic document processing is not a claimed feature.
- **RTL handling:** Unknown. IBM's documentation does not mention bidirectional text support.

### Accuracy
- **Equation recognition F1:** 0.968 (on their evaluation set).
- **Overall document understanding:** Competitive with models several times its size.
- **Limitation:** No published benchmarks on non-English documents or exam-style content.

### Pricing
- **Free and open-source** (Apache 2.0). No commercial restriction.
- Self-hosted. Runs efficiently due to small model size.

### API
- Python library (`docling` on PyPI). CLI tool. HuggingFace model hub.
- Larger models (up to 900M parameters) planned for future release.

### Verdict for Cena
**Promising for general document structure extraction, but Hebrew/Arabic support is uncertain.** The Apache 2.0 license is attractive. Worth testing on Bagrut PDFs to see if the multilingual backbone handles Hebrew/Arabic text. The equation recognition is strong. A 900M model in the future may close remaining gaps.

---

## 7. Mistral OCR

### Overview
Mistral AI's document understanding API, launched March 2025 with a major update (OCR 3) in December 2025. Claims best-in-class accuracy at dramatically lower pricing than established players.

### Capabilities
- **Document processing:** Handles PDFs, images, scanned documents with complex layouts.
- **Math equations:** 94.29% accuracy on math equations (per Mistral's benchmarks).
- **Handwriting:** Supported, with improvements in the OCR 3 (May 2025) update.
- **Tables:** Strong table extraction capabilities.
- **Speed:** Up to 2,000 pages/minute on a single node.

### Hebrew/Arabic Support
- **Claims "thousands of scripts, fonts, and languages across all continents."** This implies Hebrew and Arabic support, but specific accuracy on these languages is not published.
- **Multilingual text accuracy:** 95.55% across tested languages (languages not specified).
- **RTL handling:** Not explicitly documented.

### Accuracy
- **Overall:** 94.89% (vs. Google 83.42%, Azure 89.52% -- per Mistral's own benchmarks).
- **Math equations:** 94.29%.
- **Multilingual:** 95.55%.
- **Caveat:** These are Mistral's self-reported numbers. Independent verification may differ.

### Pricing (as of December 2025)
| Tier | Cost |
|------|------|
| Standard API | $2/1,000 pages |
| Batch processing | $1/1,000 pages |

**Dramatically cheaper than alternatives:**
- vs. AWS Textract ($65/1,000 for forms): **97% cheaper**
- vs. Google Document AI ($30-45/1,000): **93% cheaper**
- vs. Azure Form Recognizer ($1.50/1,000 basic): **Comparable or slightly more**

**Cost for Cena corpus ingestion:** ~640 pages = ~$1.28.
**Cost for student photos at scale:** 100K/month = ~$200/month.

### API
- REST API. Available directly and via Azure AI Foundry.
- Model identifier: `mistral-ocr-2503` (March), `mistral-ocr-2505` (May update).

### Verdict for Cena
**Strong contender, especially for cost-sensitive batch processing.** The accuracy claims are impressive but should be validated on Hebrew/Arabic Bagrut content. The pricing is extremely competitive. The math accuracy of 94.29% is good but not Mathpix-level for complex equations. Worth testing head-to-head with Mathpix on actual Bagrut PDFs.

---

## 8. LLM-Based Vision Approaches

### GPT-4o / GPT-5 Vision

**Capabilities:**
- Accepts images and PDFs directly. Can extract text, understand math, describe diagrams.
- Can output structured LaTeX, JSON, or any requested format.
- Understands Hebrew and Arabic natively (strong multilingual training).
- Can grade handwritten math solutions (studied in academic research).

**Math OCR accuracy:**
- On printed equations: Very high (~95%+) when properly prompted.
- On handwritten equations: Research shows scores are off by ~7.66% on average when grading handwritten college-level math. Reading accuracy varies significantly by handwriting quality.
- **Key limitation:** Not deterministic. Same image can produce slightly different LaTeX on repeated calls. Needs validation layer.

**Hebrew/Arabic handling:**
- GPT-4o has strong Hebrew and Arabic language understanding.
- Can correctly read RTL text mixed with LTR math notation.
- Understands Bagrut-style question phrasing ("...חשב את", "...احسب").

**Pricing (March 2026):**
| Model | Input (per 1M tokens) | Output (per 1M tokens) |
|-------|----------------------|------------------------|
| GPT-4o | $2.50 | $10.00 |
| GPT-4o-mini | $0.15 | $0.60 |

Image tokens: A typical exam page image uses ~1,000-2,000 tokens. So ~$0.0025-0.005 per page with GPT-4o, or ~$0.00015-0.0003 with GPT-4o-mini.

### Gemini 2.5 Flash / Gemini 2.5 Pro

**Capabilities:**
- Native multimodal understanding of images and PDFs.
- Strong math reasoning (72.0% on AIME 2025, 79.7% on MMMU).
- 1M token context window -- can process entire exam papers in one call.
- Understands Hebrew and Arabic.

**Math OCR accuracy:**
- Excellent on printed math when prompted for LaTeX extraction.
- Limited published benchmarks on OCR-specific tasks, but the math reasoning capability suggests strong formula understanding.
- Can handle mixed RTL text + LTR math due to multilingual training.

**Hebrew/Arabic handling:**
- Gemini models explicitly support Hebrew and Arabic.
- Google's Arabic/Hebrew NLP research is among the strongest in the industry.

**Pricing (March 2026):**
| Model | Input (per 1M tokens) | Output (per 1M tokens) |
|-------|----------------------|------------------------|
| Gemini 2.5 Flash | $0.15-0.30 | $0.60-2.50 |
| Gemini 2.5 Pro | $1.25 | $10.00 |

**Important:** Gemini 2.0 Flash is deprecated and shuts down June 1, 2026. Use Gemini 2.5 Flash.

**Cost per exam page:** ~$0.00015-0.0006 with Gemini 2.5 Flash. **Extremely cheap.**

### LLM-Based Approach: Pros and Cons

| Advantage | Disadvantage |
|-----------|--------------|
| Native Hebrew/Arabic understanding | Non-deterministic output |
| Understands context (question semantics) | May hallucinate LaTeX for ambiguous symbols |
| Can structure output (JSON, tagged LaTeX) | Slower than dedicated OCR (~2-5s per call) |
| No specialized infrastructure needed | API dependency (no self-hosting) |
| Can handle mixed bidi content naturally | Cost scales with volume |
| Improving rapidly with each model generation | May over-interpret or "fix" errors in source |

### Verdict for Cena
**Gemini 2.5 Flash is the most cost-effective option for both corpus ingestion and student photo processing.** The combination of native Hebrew/Arabic support, strong math understanding, massive context window, and extremely low pricing makes it the leading candidate. Use structured prompting to request LaTeX output, and validate with SymPy. GPT-4o is a strong alternative if OpenAI is preferred.

---

## 9. Open-Source Math OCR Tools

### pix2tex / LaTeX-OCR

- **Architecture:** ViT encoder + ResNet backbone + Transformer decoder.
- **Training:** im2latex-100k dataset.
- **Accuracy:** BLEU 0.88 on im2latex test set (reported). Independent tests suggest lower (~0.67-0.73 BLEU) on diverse real-world inputs.
- **Limitations:** Single-equation only (no full page). English math only. No text extraction. No Hebrew/Arabic.
- **Status:** Active but mature. Not designed for document processing.
- **License:** MIT.
- **Verdict:** Useful as a component for isolated equation images, but not a standalone solution for Bagrut PDFs.

### Texify (VikParuchuri)

- **Architecture:** Custom model for math OCR outputting LaTeX and Markdown.
- **Training:** Diverse web data (more varied than im2latex).
- **Status:** **Deprecated.** Functionality merged into Surya (datalab-to/surya).
- **Accuracy:** Better than pix2tex on diverse inputs. 4x faster than Nougat with better accuracy on non-arXiv content.
- **Verdict:** Use Surya instead (Texify's successor).

### Pix2Text (breezedeus)

- **Architecture:** Combines layout detection, text OCR, and math formula recognition in a pipeline.
- **Math formula recognition (MFR):** New model in V1.0 (2025) claiming state-of-the-art accuracy.
- **Languages:** 80+ languages supported.
- **Output:** Markdown with embedded LaTeX.
- **License:** Apache 2.0 (MFR model), MIT (main library).
- **Verdict:** Interesting alternative to Marker/Surya. Worth benchmarking. Chinese-originated project with strong CJK support; Hebrew/Arabic support unverified.

### im2latex Models (Academic)

- **im2latex-100K dataset:** ~100K LaTeX formula-image pairs from academic papers. The standard benchmark for printed math OCR.
- **Best published results:** BLEU ~0.88 (LaTeX-OCR), but dataset contamination concerns exist.
- **Limitation:** Only single isolated formulas, not full documents. No context, no text, no structure.
- **Verdict:** Benchmark-only. Not production-suitable for Cena's needs.

---

## 10. Benchmarks

### Existing Math OCR Benchmarks

| Benchmark | Type | Size | Description | Best Known Accuracy |
|-----------|------|------|-------------|-------------------|
| **im2latex-100K** | Printed formula | 100K pairs | LaTeX formula images from arXiv papers | BLEU ~0.88 (pix2tex), ~0.67 (im2latex baseline) |
| **CROHME** (Competition on Recognition of Online Handwritten Math Expressions) | Handwritten formula | Varies by year | Annual competition dataset. CROHME 2023 is latest. | Expression recognition rate ~57-68% (varies by system) |
| **MathWriting** (Google, 2024) | Handwritten formula | 230K human + 400K synthetic | 3.9x larger than im2latex-100K. Ink stroke data. | Not widely benchmarked yet |
| **InftyReader dataset** | Scientific document | Small | STEM documents with math formulas | Limited public benchmarks |
| **MMMU** (Massive Multi-discipline Multimodal Understanding) | Multimodal QA | 11.5K | Tests vision+reasoning on college-level content including math | Gemini 2.5 Flash: 79.7%, GPT-4o: ~69% |
| **MathVista** | Visual math reasoning | 6,141 | Tests mathematical reasoning from visual inputs | Gemini 2.5 Pro: ~72%, GPT-4o: ~58% |

### What These Benchmarks Tell Us

1. **Printed formula recognition is largely solved** for English/clean input (BLEU >0.85).
2. **Handwritten formula recognition remains challenging** (57-68% expression-level accuracy on CROHME).
3. **Full document understanding** (layout + text + math + structure) has no single standard benchmark.
4. **No benchmark exists for Hebrew/Arabic math documents.** This is a gap. Cena should create a small internal benchmark (50-100 Bagrut pages, manually annotated) to evaluate tools.
5. **LLM-based approaches** are evaluated on reasoning benchmarks (MMMU, MathVista) rather than OCR benchmarks, making direct comparison difficult.

---

## 11. Hebrew/Arabic-Specific Challenges

### 11.1 Mixed Directionality (The Core Problem)

Bagrut exam pages contain:
```
[Hebrew RTL text] [LTR math: x^2 + 3x - 4 = 0] [Hebrew RTL text]
```

OCR systems must:
1. Detect the reading order correctly (RTL paragraph containing LTR math islands).
2. Preserve the logical order in output (not visual order).
3. Not mirror or reorder math expressions.
4. Handle parentheses correctly (mirroring in RTL context).

**Which tools handle this well:**
- Google Cloud Document AI: Best bidi reading order detection.
- GPT-4o / Gemini 2.5: Understand bidi natively from training.
- Mathpix: Separates math from text, reducing bidi complexity.
- Marker/Surya: Layout detection is visual, but text order may need post-processing for RTL.

### 11.2 Hebrew Nikud (Vowel Points)

Hebrew Bagrut exams generally do NOT use nikud (vowel points) in math questions -- nikud is primarily used in biblical/liturgical text and children's reading materials. High school math uses unpointed Hebrew. This actually simplifies OCR.

**Exception:** Some explanatory text or glossary terms in lower-level (3-unit) exams may include nikud for clarity. These are rare.

**Impact on tool selection:** Low. Nikud is not a significant concern for Bagrut math papers.

### 11.3 Arabic Harakat (Diacritical Marks)

Arabic Bagrut exams use unvoweled (undiacriticized) Modern Standard Arabic for the vast majority of content, consistent with standard Arabic publishing conventions. Harakat appear occasionally for disambiguation.

**Impact on tool selection:** Low to medium. Most OCR tools handle unvoweled Arabic well. The occasional harakat may be missed, but this rarely affects mathematical meaning.

### 11.4 Arabic Ligatures and Contextual Forms

Arabic letters change shape based on position (initial, medial, final, isolated). This is handled correctly by all modern OCR engines trained on Arabic data. It is NOT a differentiating factor among the tools evaluated.

### 11.5 Number Systems

Israeli Arab students use **Western Arabic numerals** (0, 1, 2, 3...), not Eastern Arabic-Indic numerals. All tools handle Western Arabic numerals correctly as they are the same as standard ASCII digits.

### 11.6 Mathematical Terminology

Some Hebrew/Arabic math terms may be misrecognized as regular text rather than mathematical keywords. For example, the Hebrew word "פונקציה" (function) or Arabic "دالة" (function) should be tagged as mathematical context. LLM-based approaches handle this naturally; pure OCR tools do not understand semantics.

---

## 12. Comparative Summary

| Tool | Math to LaTeX | Hebrew OCR | Arabic OCR | Bidi Handling | Handwritten | Price (per 1K pages) | API | License |
|------|:---:|:---:|:---:|:---:|:---:|---:|:---:|---------|
| **Mathpix** | Excellent | Yes | Yes | Good (block separation) | Best | $2-5 | REST | Proprietary |
| **Nougat (Meta)** | Good (arXiv) | No | No (community fork) | No | No | Free (self-host) | No | CC-BY-NC |
| **GOT-OCR 2.0** | Good | Untested | Untested | Unknown | Limited | Free (self-host) | No | Apache 2.0 |
| **Google Document AI** | Good (premium) | Yes | Yes | Best | Good (50 langs) | $1.50-4 | REST/gRPC | Proprietary |
| **Marker/Surya** | Moderate | Yes (90+ langs) | Yes (90+ langs) | Moderate | No | Free (self-host) | Python/CLI | AGPL/Commercial |
| **Docling (IBM)** | Good (F1: 0.968) | Unknown | Unknown | Unknown | No | Free (self-host) | Python/CLI | Apache 2.0 |
| **Mistral OCR** | Good (94.29%) | Likely | Likely | Unknown | Good | $1-2 | REST | Proprietary |
| **Gemini 2.5 Flash** | Very Good | Yes | Yes | Excellent | Good | ~$0.15-0.30 | REST | Proprietary |
| **GPT-4o** | Very Good | Yes | Yes | Excellent | Moderate | ~$2.50 | REST | Proprietary |
| **pix2tex/LaTeX-OCR** | Good (isolated) | No | No | N/A | No | Free (self-host) | Python | MIT |
| **Pix2Text** | Good | 80+ langs | 80+ langs | Unknown | No | Free (self-host) | Python | Apache 2.0/MIT |

---

## 13. Recommended Architecture for Cena

### Phase 1: Corpus Ingestion (Bagrut PDFs -> Structured Data)

This is a one-time batch job per subject (see `docs/syllabus-corpus-strategy.md`). Volume is low (~640 pages for Math 5-unit, ~2,000 pages across all subjects). Cost is not a primary concern; accuracy is.

**Recommended pipeline:**

```
Bagrut PDF
    |
    v
[Marker/Surya] -- Layout detection + text OCR
    |                  (Hebrew/Arabic text extraction)
    |
    v
[Math region detection] -- Visual detection of equation regions
    |
    v
[Mathpix API] -- Convert detected math regions to LaTeX
    |                 (highest accuracy for math-to-LaTeX)
    |
    v
[Gemini 2.5 Flash / Kimi K2.5] -- Semantic structuring
    |                                (question parsing, concept tagging,
    |                                 terminology extraction)
    |
    v
[SymPy validation] -- Verify LaTeX is mathematically valid
    |
    v
corpus_analysis_{subject}.json
```

**Why this pipeline:**
1. Marker/Surya handles the layout and Hebrew/Arabic text well, for free.
2. Mathpix handles math-to-LaTeX with the highest accuracy, at negligible cost for this volume.
3. The LLM handles semantic understanding (which no OCR tool provides) -- knowing that "חשב את" means "calculate" and that the following expression is the problem statement.
4. SymPy catches LaTeX errors before they enter the knowledge graph.

**Alternative (simpler):** Feed entire PDF pages directly to Gemini 2.5 Flash with structured prompting. The 1M context window can handle entire exam papers. Cost: ~$0.10-0.20 per entire exam. Accuracy may be slightly lower for complex equations but the simplicity is attractive.

### Phase 2: Student Photo Upload (Real-Time)

Students photograph their homework or exam papers. Need real-time (<3s) extraction of math and text.

**Recommended approach:**

```
Student photo (camera/gallery)
    |
    v
[Client-side preprocessing] -- Crop, rotate, enhance contrast
    |
    v
[Gemini 2.5 Flash] -- Primary: extract text + math as LaTeX
    |                       (fast, cheap, good Hebrew/Arabic)
    |
    +--[Low confidence?]--> [Mathpix API] -- Fallback for complex equations
    |
    v
[SymPy validation] -- Verify extracted LaTeX
    |
    v
Structured question data -> Actor system
```

**Cost at scale:**
- 100K photos/month via Gemini 2.5 Flash: ~$15-30/month
- 10% fallback to Mathpix: ~$20/month
- Total: ~$35-50/month for 100K photos

### Phase 3: Future Consideration

Monitor these developments:
- **Granite-Docling 900M** (IBM, upcoming): May close the gap on multilingual document understanding with an open-source, commercially-friendly model.
- **GOT-OCR 3.0 or successors:** If they add Hebrew/Arabic training data, could become a strong self-hosted option.
- **Gemini 3.x:** Each generation significantly improves vision capabilities.
- **Custom fine-tuning:** Once Cena has a corpus of 1,000+ annotated Bagrut pages, fine-tuning a small model (Docling, GOT-OCR) on this data could yield a specialized, self-hosted Bagrut OCR that outperforms general tools.

---

## 14. Action Items

1. **Immediate:** Run a head-to-head evaluation on 20 Bagrut PDF pages (10 Hebrew, 10 Arabic) comparing:
   - Gemini 2.5 Flash (direct vision)
   - Mathpix API
   - Marker/Surya pipeline
   - Mistral OCR
   Measure: text accuracy, math LaTeX accuracy, structure preservation, bidi correctness.

2. **Build internal benchmark:** Manually annotate 50-100 Bagrut pages with ground-truth LaTeX and text. No public benchmark exists for this specific domain.

3. **Prototype the Phase 1 pipeline** as described above, starting with Math 5-unit Bagrut PDFs.

4. **License review:** Confirm Surya's commercial license terms with Datalab if using Marker in production.

5. **Monitor:** Track Granite-Docling and GOT-OCR releases for improved multilingual support.

---

## Sources

### Tool Documentation
- [Mathpix Convert API](https://mathpix.com/convert)
- [Mathpix API Pricing](https://mathpix.com/pricing/api)
- [Mathpix Language Support](https://mathpix.com/language-support)
- [Nougat (Meta) GitHub](https://github.com/facebookresearch/nougat)
- [Nougat Paper (arXiv)](https://arxiv.org/pdf/2308.13418)
- [GOT-OCR 2.0 Paper (arXiv)](https://arxiv.org/abs/2409.01704)
- [GOT-OCR 2.0 GitHub](https://github.com/Ucas-HaoranWei/GOT-OCR2.0)
- [Google Cloud Document AI - Enterprise OCR](https://docs.cloud.google.com/document-ai/docs/enterprise-document-ocr)
- [Google Cloud Document AI Pricing](https://cloud.google.com/document-ai/pricing)
- [Marker GitHub (datalab-to)](https://github.com/datalab-to/marker)
- [Surya OCR GitHub](https://github.com/datalab-to/surya)
- [Texify GitHub (deprecated)](https://github.com/VikParuchuri/texify)
- [IBM Granite-Docling-258M (HuggingFace)](https://huggingface.co/ibm-granite/granite-docling-258M)
- [IBM Granite-Docling Announcement](https://www.ibm.com/new/announcements/granite-docling-end-to-end-document-conversion)
- [Mistral OCR](https://mistral.ai/news/mistral-ocr)
- [Mistral AI Pricing](https://mistral.ai/pricing)
- [pix2tex / LaTeX-OCR GitHub](https://github.com/lukas-blecher/LaTeX-OCR)
- [Pix2Text GitHub](https://github.com/breezedeus/Pix2Text)

### Pricing References
- [Gemini API Pricing](https://ai.google.dev/gemini-api/docs/pricing)
- [GPT-4o API Pricing](https://pricepertoken.com/pricing-page/model/openai-gpt-4o)
- [Gemini 2.5 Flash API Pricing](https://pricepertoken.com/pricing-page/model/google-gemini-2.5-flash)
- [Mistral OCR 3 Pricing Analysis](https://byteiota.com/mistral-ocr-3-2-1000-pages-cuts-document-ai-costs-97/)

### Research Papers and Benchmarks
- [Arabic-Nougat: Fine-Tuning Vision Transformers for Arabic OCR](https://arxiv.org/html/2411.17835v1)
- [Arabic-Nougat Models (HuggingFace)](https://huggingface.co/MohamedRashad/arabic-large-nougat)
- [Evaluating GPT-4 at Grading Handwritten Solutions in Math Exams](https://arxiv.org/abs/2411.05231)
- [MathWriting Dataset (Google)](https://arxiv.org/html/2404.10690v2)
- [CROHME Competition](https://www.isical.ac.in/~crohme/)
- [Investigating Models for Transcription of Mathematical Formulas](https://www.mdpi.com/2076-3417/14/3/1140)
- [Survey of OCR in Arabic Language](https://www.mdpi.com/2076-3417/13/7/4584)
- [QARI-OCR: Arabic Text Recognition through MLLM Adaptation](https://arxiv.org/html/2506.02295v1)
- [Advancements and Challenges in Arabic OCR (Survey)](https://arxiv.org/html/2312.11812v1)
- [Hebrew OCR with Nikud (BGU)](https://www.cs.bgu.ac.il/~elhadad/hocr/)

### Hebrew/Arabic Specific
- [Jochre 3 and the Yiddish OCR Corpus](https://arxiv.org/html/2501.08442v1)
- [OCR for Arabic & Cyrillic Scripts: Multilingual Tactics](https://medium.com/@API4AI/ocr-for-arabic-cyrillic-scripts-multilingual-tactics-92edc1002d34)
- [Bagrut Exam Solving with AI (GitHub)](https://github.com/motib/bagrut)
