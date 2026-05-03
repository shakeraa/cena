---
author: kimi-research-swarm (coordinator + 8 sub-agents ALPHA-THETA)
date: 2026-04-22
subject: Q3 writing-pad / handwriting-recognition swarm research
for-tasks: EPIC-PRR-H + sub-tasks TBD
overall-verdict: YELLOW 5.5/10 — ship with pivots
---

# Cena Writing-Pad HWR — Swarm Research Report

_Date: 2026-04-22_
_Coordinator: Kimi agent swarm_
_Agents: ALPHA–THETA_

---

## Executive Summary

The handwriting pad feature for Cena is **technically feasible but commercially constrained**. MyScript iink.js remains the strongest specialized math-HWR candidate for a Vue 3 PWA, yet its pricing is opaque and its RTL support for Hebrew/Arabic math notation is unverified in current public documentation. Vision-LLM fallbacks (Claude Sonnet 4/GPT-4o-class) offer superior general handwriting accuracy but blow the $3.30/student/month ceiling at any meaningful volume. Chemistry OCSR is **not production-ready** for high-school Lewis structures on tablet canvases—DECIMER hand-drawn exact-match accuracy sits at ~73%, while MolScribe collapses to ~8% on hand-drawn inputs. For math, the recommended stack is MyScript primary + vision-LLM fallback for edge cases, but the fallback must be rate-limited to stay within budget. Stroke capture should use Perfect Freehand + raw Canvas PointerEvents, not a heavy framework. The device floor is risky: MyScript's WASM resource alone is ~18 MB, which strains 2 GB RAM Android devices when combined with a Vue 3 PWA runtime. Compliance is the sharpest edge—Israeli PPL Amendment 13 (effective August 14, 2025) classifies biometric identifiers as "highly sensitive information," and handwriting strokes plausibly fall under this category, yet no vendor offers an Israel data-residency guarantee or a zero-retention DPA tailored for K-12. Input-validation for vision-LLM pipelines is non-optional: multimodal prompt-injection via handwritten text is a demonstrated attack vector with success rates up to 82% on some models.

**Top-3 recommendations:**
1. **Ship math pad secondary only** (typed-primary) and defer chemistry OCSR entirely — DECIMER/MolScribe exact-match rates are too low for exam-prep stakes.
2. **Negotiate MyScript enterprise pricing** with explicit Hebrew/Arabic math QA and a zero-retention DPA; cap vision-LLM fallback at <5% of calls.
3. **Treat handwriting strokes as biometric data under PPL Amendment 13** and GDPR Art. 9, requiring a DPIA, DPO consultation, and session-only retention with cryptographic erasure.

**Decision gate: YELLOW (5.5/10)** — ship with noted pivots.

---

## 1. Math HWR Vendor Matrix (ALPHA)

MyScript iink.js leads on specialized math recognition with a neural-network engine and a compact ~18 MB multilingual WASM resource, but current public pricing and 2025 CROHME benchmarks are unavailable. Mathpix offers transparent API pricing at $0.002/image but lacks a dedicated math-HWR DPA for K-12. Vision LLMs now dominate general handwriting accuracy (~1.2–1.4% CER on IAM) but are cost-prohibitive at scale. Google Cloud Vision and Azure Document Intelligence are not math-specific and perform poorly on messy handwriting (~45–50% accuracy). Open-source TrOCR/DTrOCR are viable for printed text but lack math-expression topology parsing.

| Vendor | Pricing | Bundle | Math Accuracy | Handwriting Accuracy | RTL | Retention | Residency |
|---|---|---|---|---|---|---|---|
| MyScript iink.js | [UNVERIFIED enterprise only] | ~18 MB WASM multilingual | CROHME 2019: 3rd place (CTC BLSTM); SDK 3.0 "unprecedented accuracy" | Specialized for stylus | Unverified for HE/AR math | Not stated | Not stated |
| Mathpix Convert API | $0.002/image (0–1M), $0.0015/image (1M+); strokes $0.01/session | Cloud | STEM-specialized OCR | 92–96% clean text | Multilingual claim; no HE math validation | 30d default; 24h opt-out | US |
| Google Cloud Vision | $1.50/1K pages (up to 5M) | Cloud | No math-specific mode | ~84% clean, ~70% messy | 80+ languages | Google standard | EU/US/Asia |
| Azure Document Intelligence | Metered per page | Cloud/container v4.0 | No math mode | ~95% printed, ~45% cursive | Multilingual | Azure standard | EU/US/Asia, FedRAMP High |
| Apple PencilKit/Scribble | Free (iPad only) | Native iOS | Math Notes iPadOS 17+ | High on iPad | Full system-level | On-device | On-device |
| Claude Sonnet 4 / Opus 4 | $3/$15 per MTok | Cloud API | No dedicated math | ~1.31% CER (IAM) | General multilingual | Anthropic policies; no K-12 | US |
| GPT-5 / GPT-4o | GPT-5.4: $2.50/$15 per MTok | Cloud API | No dedicated math | ~1.22% CER | General | OpenAI policies; no K-12 zero-retention | US |
| Gemini 3 Pro / Flash | Gemini 3.1 Pro: $2/$12 per MTok | Cloud API | No dedicated math | ~1.44% CER | Strong multilingual | Google policies; no K-12 zero-retention | EU/US/Asia |
| TrOCR (open-source) | $0 self-hosted | Self-hosted | Not for math topology | 2.89% CER IAM; 2.38% DTrOCR | English-centric tokenizer | Self-determined | Self-hosted |
| DECIMER (open-source) | $0 | Self-hosted/TPU | OCSR: 73.25% hand-drawn | Chem only | General | Self-determined | Self-hosted |

**Ranked shortlist:**
1. MyScript iink.js — only vendor with dedicated online math recognition + WASM client-side. [MEDIUM confidence: unverified pricing/RTL.]
2. Mathpix Convert API — best cloud alternative. [MEDIUM confidence.]
3. Vision-LLM fallback (Claude/GPT) — highest raw accuracy, low-volume edge cases only. [HIGH on accuracy, HIGH on cost risk.]

**Validations:** C2 partial-confirm (MyScript correct primary; Sonnet tier name corrected; pure-Sonnet rejection confirmed). C5 challenged (Mathpix $0.16/student/mo fits; vision-LLM consumes budget in 2-3 calls).

---

## 2. Chem HWR (BETA)

OCSR for hand-drawn Lewis structures is **not viable for exam-prep accuracy in 2026**. DECIMER ~73% exact match on clean hand-drawn structures. MolScribe (93% F1 on publication images) collapses to ~8% on hand-drawn. Reaction arrows, stoichiometric working, and Hebrew state symbols are unsupported by all evaluated tools.

| Tool | Exact Match (Hand-Drawn) | Valid SMILES | License |
|---|---|---|---|
| DECIMER | 73.25% | 99.72% | Open/academic |
| MolScribe | 7.65% | 95.66% | Academic/research |
| OSRA | 0.57% | 54.66% | Open |
| RxnScribe | N/A (reactions only) | N/A | Academic |
| Mathpix chem | [UNVERIFIED] | [UNVERIFIED] | Commercial |

**Validation:** C4 CHALLENGED. Lewis pad not accurate enough for exam stakes. SMILES keyboard not student-viable without curriculum redesign. **Chem = typed-primary only; defer Lewis pad entirely.**

---

## 3. Stroke-Capture Stack (GAMMA)

Optimal: **raw HTML5 Canvas + PointerEvents + Perfect Freehand**. Perfect Freehand ~33 KB gzip, zero-dependency, pressure-aware. Konva ~60 KB gzip adds scene-graph overhead unneeded for a scratch pad. Excalidraw / tldraw React-only, too heavy.

Device matrix:
- iPad 9th gen + Safari PWA: PASS (PencilKit pressure).
- 2 GB RAM Android + Chrome: YELLOW (memory pressure; 18 MB WASM + Vue 3 PWA may trigger tab kill).
- Desktop mouse/trackpad: PASS (no pressure).
- Touch-only phone: YELLOW (poor for complex math).

**Validation:** C1 CONFIRMED (pad secondary/scratch-only justified).

---

## 4. Industry Integration Patterns (DELTA)

Every major education product that shipped handwriting math uses **camera-based offline OCR**, not live stylus-to-symbol. Photomath scans then allows manual editing. Mathpix Snip at $4.99–$9.99/mo. No evidence Khan Academy, Brilliant, or Google Classroom shipped live handwriting-for-grading. Apple Math Notes uses on-device engine, iPad only. Israeli competitors (גול, מורפיקס) did not surface HWR in public documentation.

**Pattern that worked:** camera + manual correction (Photomath). **Pattern that failed (or is absent):** live stylus-primary in K-12 graded assessment.

**Validation:** C1 CONFIRMED by industry absence.

---

## 5. RTL Posture (EPSILON)

**No vendor explicitly supports Hebrew or Arabic mathematical notation in HWR.** MyScript multilingual is 7 Latin languages. Mathpix/Google/Azure OCR support Hebrew text in general documents; math expressions with Hebrew letters + Arabic digits + Latin variables have no verified benchmark. No academic CROHME-equivalent for RTL math found 2020–2025.

Test cases to run (any vendor QA):
1. Hebrew variable names (א = 5) in algebraic expressions.
2. Eastern Arabic numerals (٣ + ٤ = ٧) mixed with Latin operators.
3. Bagrut-style geometry notation with Hebrew angle labels.

**Validation:** C1 and C4 CHALLENGED — RTL pad-secondary unverified; may require typed-only for HE/AR streams until vendor QA done.

---

## 6. Cost Model (ZETA)

At 80 HWR calls/student/month, only Mathpix and Google Cloud Vision fit under $3.30 ceiling. MyScript pricing unverified.

| Vendor | Per-Call | 80 calls/mo | 180 calls/mo (engaged) |
|---|---|---|---|
| Mathpix Image | $0.002 | $0.16 | $0.36 |
| Mathpix Strokes (per session) | $0.01 | $0.80 | $1.80 |
| Google Vision | ~$0.0015 | $0.12 | $0.27 |
| Claude Sonnet 4 vision | ~$0.03–$0.10 (est.) | $2.40–$8.00 | $5.40–$18.00 |
| GPT-4o vision | ~$0.05–$0.15 (est.) | $4.00–$12.00 | $9.00–$27.00 |
| Gemini 2.5 Flash vision | ~$0.01–$0.03 (est.) | $0.80–$2.40 | $1.80–$5.40 |
| MyScript iink.js | [UNVERIFIED] | — | — |

**Validation:** C5 CONFIRMED for specialized APIs; CHALLENGED for vision-LLM primary. Fallback must be <5% of volume.

---

## 7. Privacy + Compliance (ETA)

Israeli PPL Amendment 13 (effective August 14, 2025) introduces "highly sensitive information" category explicitly including biometric identifiers. Handwriting strokes plausibly fall under this. GDPR Art. 9 similarly classifies biometric data as special category. **No HWR vendor publishes a K-12-specific zero-retention DPA with Israel data residency.**

| Requirement | MyScript | Mathpix | Google | Microsoft | Anthropic | OpenAI |
|---|---|---|---|---|---|---|
| EU data residency | — | — | Yes | Yes | — | — |
| Israel data residency | — | — | — | — | — | — |
| FedRAMP auth | — | — | Document AI: High | Azure: High | — | — |
| Zero-retention DPA | — | 24h opt-out | Not standard | Not standard | Not standard | Not standard |
| Student-data addendum | — | — | GSuite for EDU | EDU | — | — |

**PPL Amendment 13 key facts:** Effective 2025-08-14. "Highly sensitive information" includes biometric identifiers. Fines up to 5% annual turnover. DPO mandatory for large-scale sensitive processing. Enforcement actions increased in 2025.

**Validation:** C3 CHALLENGED. 24h raw-stroke retention may be too long under PPL Amendment 13. **Reduce to session-only (in-memory, never persisted); retain only canonical symbolic output 30d.**

---

## 8. Device Floor + Input Validation (THETA)

MyScript 18 MB WASM loads on iPad 9th gen (3 GB RAM); risky on 2 GB Android with Vue 3 PWA runtime. Desktop mouse/trackpad viable for simple expressions, loses pressure/eraser.

Device matrix:
- iPad 9th gen (3 GB): GREEN.
- 2 GB Android: YELLOW (tab kill risk).
- Desktop mouse/trackpad: GREEN.
- Touch-only phone: YELLOW.

**Multimodal prompt injection is proven:** 8% (GPT-4o at 20px) → 82.5% (FigStep). SCAM benchmark: handwritten Post-it overlays cause 61.18pp accuracy drops on CLIP.

**Defenses (required for vision-LLM fallback):**
1. OCR pre-filter: extract text from canvas, pass through text safety filter BEFORE vision call.
2. Dual-LLM: isolate vision from action-taking; hardened LLM validates outputs.
3. Rate limiting + anomaly detection for repeated injection patterns.
4. Envelope signing: unproven for handwritten inputs.

**Validation:** C2 CHALLENGED on injection grounds — vision-LLM fallback requires OCR-pre-filter + output validation before deploy.

---

## 9. Recommended Architecture

```
Vue 3 PWA (Vuetify + Pinia)
  └─ Canvas Layer (Perfect Freehand)
       └─ PointerEvents → stroke arrays
            ↓
       Router: Math / Chem / FBD
            ↓
       Primary: MyScript iink.js WASM (client, ~18 MB, offline)
            ↓ (low-confidence only)
       Fallback: Vision-LLM (Claude Sonnet 4 or Gemini Flash)
            - Rate-limited: ≤5% of calls
            - OCR pre-filter + output guardrails
            ↓
       .NET backend (Marten + Redis)
            - Store canonical symbolic ONLY
            - Raw strokes: in-memory session-only, never persisted
```

Key rules:
- Raw strokes never written to persistence (Postgres/Redis).
- MyScript first; only route to vision-LLM on low-confidence.
- Hard cap ≤5% of HWR calls per student per month to vision-LLM fallback.
- OCR the canvas image + text-safety filter BEFORE any vision-LLM call.

---

## 10. Consolidated Validations vs §C (Provisional Decisions)

| §C item | Verdict | Evidence |
|---|---|---|
| C1 modality split (pad secondary at launch) | CONFIRMED with caveat | Industry uses camera-OCR not live-stylus; RTL support unverified |
| C2 MyScript + Claude fallback | PARTIAL CHALLENGE | MyScript correct primary; fallback viable only capped ≤5% with OCR pre-filter |
| C3 retention 24h raw / 30d derived | CHALLENGED | PPL A13 → session-only raw stroke |
| C4 per-subject mix (chem typed + Lewis pad + SMILES) | CHALLENGED | Lewis HWR 73% best → defer entirely |
| C5 $3.30 cost ceiling | CONFIRMED (primary) / CHALLENGED (fallback) | Fallback cap ≤5% |

---

## 11. Decision Scorecard

| Dimension | Score /10 | Confidence |
|---|---|---|
| Vendor viability (math) | 7 | MEDIUM |
| Vendor viability (chem) | 2 | HIGH |
| Stroke-capture stack maturity | 8 | HIGH |
| RTL coverage | 3 | MEDIUM |
| Cost fit vs $3.30 | 7 | MEDIUM |
| Compliance posture | 4 | MEDIUM |
| Device-floor pass | 6 | MEDIUM |
| Input validation robustness | 5 | HIGH |
| **OVERALL** | **5.5** | **MEDIUM** |

**YELLOW — ship with pivots: defer chem pad, cap LLM fallback ≤5%, treat strokes as biometric, demand MyScript RTL QA.**

---

## 12. Open Questions Back to Cena Team

1. **MyScript pricing & K-12 DPA** — procurement task; per-seat price for 1,000–10,000 students.
2. **RTL math vendor QA** — can Cena provide Hebrew/Arabic math handwriting samples for vendor evaluation?
3. **PPL Amendment 13 legal opinion** — do strokes qualify as biometric IDs? If yes, DPIA mandatory.
4. **Device floor testing** — benchmark MyScript on lowest-target Android (2 GB RAM).
5. **Chem curriculum scope** — reduce Lewis to template-select vs. freehand?

---

## 13. Methodology

- 8 sub-agents spawned (ALPHA–THETA). None collapsed or appended.
- Each sub-agent produced focused findings + source citations.
- Coordinator synthesized into §1–§12 above.
- §C items addressed by at least one agent; evidence gaps noted explicitly rather than papered over.

---

## 14. Sources

(Full URL index retained in the original swarm output; citations by sub-agent section. Re-request from swarm if specific source needed for audit.)
