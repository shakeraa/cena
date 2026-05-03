"""Build dev-fixtures/*.json from cascade prototype output + scrubbing rules.

Idempotent — re-running rewrites every file without side-effects outside
`dev-fixtures/`. Safe to run in CI.

Inputs:
    - results/cascade_runs/*.json      live cascade runs on real Bagrut pages
                                       (git-ignored, structural-only scrubbed on write)
    - results/real_corpus_triage.json  151-PDF triage output
    - fixtures/ground_truth/*.json     synthetic student-photo ground truth

Outputs:
    - dev-fixtures/manifest.json
    - dev-fixtures/cascade-results/*.json
    - dev-fixtures/bagrut-analysis/analysis.json
    - dev-fixtures/context-hints/examples.json
    - dev-fixtures/pipeline-states/pipeline-items.json
    - dev-fixtures/triage-verdicts/samples.json
"""
from __future__ import annotations

import hashlib
import json
import random
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).parent
DEV = ROOT / "dev-fixtures"
CASCADE_RUNS = ROOT / "results" / "cascade_runs"
# Prefer the full 4-category triage if present; fall back to the bagrut-only snapshot
TRIAGE_JSON = ROOT / "results" / "full_corpus_triage.json"
if not TRIAGE_JSON.exists():
    TRIAGE_JSON = ROOT / "results" / "real_corpus_triage.json"
GT_DIR = ROOT / "fixtures" / "ground_truth"

# Stable-ish pseudo-UUIDs for pipeline samples so re-runs produce the same ids
_R = random.Random(19)
def _uuid() -> str:
    return "%08x-%04x-4%03x-%04x-%012x" % (
        _R.getrandbits(32),
        _R.getrandbits(16),
        _R.getrandbits(12),
        _R.getrandbits(16),
        _R.getrandbits(48),
    )


def _iso(offset_minutes: int = 0) -> str:
    base = datetime(2026, 4, 16, 12, 0, 0, tzinfo=timezone.utc)
    from datetime import timedelta
    return (base + timedelta(minutes=offset_minutes)).isoformat().replace("+00:00", "Z")


def write_json(path: Path, payload) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(payload, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )


# ── Scrubber ────────────────────────────────────────────────────────────────
def scrub_text_block(block: dict) -> dict:
    """Replace raw text with length + hash. Keep structure usable for dev."""
    raw = block.get("text", "") or ""
    return {
        "text": None,
        "text_length": len(raw),
        "text_hash": hashlib.sha256(raw.encode("utf-8")).hexdigest()[:12],
        "bbox": block.get("bbox"),
        "language": block.get("language"),
        "confidence": block.get("confidence"),
        "is_rtl": block.get("is_rtl"),
        "_redacted": "bagrut-reference-only",
    }


def scrub_math_block(block: dict) -> dict:
    return {
        "latex": None,
        "latex_length": len(block.get("latex", "") or ""),
        "latex_hash": hashlib.sha256((block.get("latex") or "").encode("utf-8")).hexdigest()[:12],
        "bbox": block.get("bbox"),
        "confidence": block.get("confidence"),
        "sympy_parsed": block.get("sympy_parsed"),
        "canonical_form": None,
        "_redacted": "bagrut-reference-only",
    }


# ── Cascade result builders ─────────────────────────────────────────────────
def build_student_photo_fixture(problem_id: str, hint_track: str, tag: str) -> dict:
    """Synthetic student photo result — full text/math retained (non-redacted)."""
    latex_samples = {
        "algebra_01": ["3x + 5 = 14"],
        "calculus_01": ["f(x) = x^3 - 2x^2 + 5x - 7", "f'(x)"],
    }
    text_samples = {
        "algebra_01": "פתור את המשוואה: 3x + 5 = 14",
        "calculus_01": "חשב את הנגזרת: f(x) = x^3 - 2x^2 + 5x - 7, f'(x) = ?",
    }
    return {
        "schema_version": "1.0",
        "runner": "cascade_prototype",
        "source": f"synthetic://student_photo/{problem_id}",
        "hints": {
            "subject": "math",
            "language": "he",
            "track": hint_track,
            "source_type": "student_photo",
            "taxonomy_node": None,
            "expected_figures": False,
        },
        "pdf_triage": None,
        "text_blocks": [
            {
                "text": text_samples.get(problem_id, ""),
                "bbox": {"x": 62, "y": 180, "w": 520, "h": 38, "page": 1},
                "language": "he",
                "confidence": 0.91,
                "is_rtl": True,
            },
        ],
        "math_blocks": [
            {
                "latex": eq,
                "bbox": {"x": 180, "y": 220 + i * 40, "w": 200, "h": 30, "page": 1},
                "confidence": 0.87,
                "sympy_parsed": True,
                "canonical_form": "auto",
            }
            for i, eq in enumerate(latex_samples.get(problem_id, []))
        ],
        "figures": [],
        "overall_confidence": 0.89,
        "fallbacks_fired": [],
        "cas_validated_math": len(latex_samples.get(problem_id, [])),
        "cas_failed_math": 0,
        "human_review_required": False,
        "reasons_for_review": [],
        "layer_timings_seconds": {
            "layer_0_preprocess": 0.42,
            "layer_1_layout": 0.78,
            "layer_2a_text": 1.15,
            "layer_2b_math": 0.32,
            "layer_3_reassemble": 0.01,
            "layer_4_gate": 0.00,
            "layer_5_cas": 0.01,
        },
        "total_latency_seconds": 2.69,
        "captured_at": _iso(),
        "_dev_fixture": {
            "scenario": tag,
            "source_kind": "synthesized",
            "notes": "Synthetic student photo — safe to ship verbatim.",
        },
    }


def build_bagrut_text_shortcut_fixture() -> dict:
    """Text-layer shortcut path — no OCR. Structural only."""
    return {
        "schema_version": "1.0",
        "runner": "pypdf_shortcut",
        "source": "bagrut-reference://3u/2024_summer/035381",
        "hints": {
            "subject": "math",
            "language": "he",
            "track": "3u",
            "source_type": "bagrut_reference",
            "taxonomy_node": None,
            "expected_figures": False,
        },
        "pdf_triage": "text",
        "text_blocks": [
            scrub_text_block({
                "text": f"<redacted block #{i}>",
                "bbox": {"x": 0, "y": i * 18, "w": 500, "h": 16, "page": (i // 120) + 1},
                "language": "he",
                "confidence": 0.99,   # text layer is ground truth
                "is_rtl": True,
            })
            for i in range(612)
        ],
        "math_blocks": [],
        "figures": [],
        "overall_confidence": 0.99,
        "fallbacks_fired": [],
        "cas_validated_math": 0,
        "cas_failed_math": 0,
        "human_review_required": False,
        "reasons_for_review": [],
        "layer_timings_seconds": {
            "layer_0_preprocess": 0.08,
            "layer_1_layout": 0.00,
            "layer_2a_text": 0.00,
            "layer_2b_math": 0.00,
            "layer_3_reassemble": 0.00,
            "layer_4_gate": 0.00,
            "layer_5_cas": 0.00,
        },
        "total_latency_seconds": 0.08,
        "captured_at": _iso(),
        "_dev_fixture": {
            "scenario": "bagrut_text_shortcut",
            "source_kind": "bagrut_reference",
            "notes": "90% of Bagrut PDFs take this path. text_blocks are scrubbed (structural-only) per bagrut-reference-only.",
            "scrubbing": "text_blocks[].text → null, preserves length + hash + bbox + confidence",
        },
    }


def build_bagrut_full_cascade_fixture(source_run: Path | None) -> dict:
    """OCR cascade path (the 10% that doesn't have a clean text layer).

    If a live cascade run JSON is available, use its per-page structural
    metadata. Otherwise synthesize a representative shape.
    """
    if source_run and source_run.exists():
        live = json.loads(source_run.read_text())
        live_text = [scrub_text_block(b) for b in live.get("text_blocks", [])]
        live_math = [scrub_math_block(b) for b in live.get("math_blocks", [])]
        overall = live.get("overall_confidence", 0.85)
        timings = live.get("layer_timings", {})
    else:
        live_text = [
            scrub_text_block({
                "text": f"<redacted block #{i}>",
                "bbox": {"x": 0, "y": i * 12, "w": 500, "h": 11, "page": 1},
                "language": "he",
                "confidence": 0.82 + (i % 7) * 0.02,
                "is_rtl": True,
            })
            for i in range(296)
        ]
        live_math = []
        overall = 0.888
        timings = {
            "layer_0_preprocess": 0.59,
            "layer_1_layout": 0.80,
            "layer_2a_text": 2.62,
            "layer_2b_math": 4.13,
            "layer_3_reassemble": 0.00,
            "layer_4_gate": 0.00,
            "layer_5_cas": 0.00,
        }

    return {
        "schema_version": "1.0",
        "runner": "cascade_prototype",
        "source": "bagrut-reference://5u/2024_winter/035581",
        "hints": {
            "subject": "math",
            "language": "he",
            "track": "5u",
            "source_type": "bagrut_reference",
            "taxonomy_node": None,
            "expected_figures": True,
        },
        "pdf_triage": "image_only",
        "text_blocks": live_text,
        "math_blocks": live_math,
        "figures": [],
        "overall_confidence": round(overall, 3),
        "fallbacks_fired": [f"mock:block_{i}" for i in range(38)],
        "cas_validated_math": len(live_math),
        "cas_failed_math": 0,
        "human_review_required": False,
        "reasons_for_review": [],
        "layer_timings_seconds": {k: round(v, 3) for k, v in timings.items()},
        "total_latency_seconds": round(sum(timings.values()), 3),
        "captured_at": _iso(),
        "_dev_fixture": {
            "scenario": "bagrut_full_cascade",
            "source_kind": "bagrut_reference",
            "notes": "OCR path — structural-only. Raw Hebrew text + LaTeX are scrubbed. Use confidence/bbox/timings for shaping admin UI + pipeline review.",
            "scrubbing": "text_blocks[].text → null, math_blocks[].latex → null",
        },
    }


def build_encrypted_fixture() -> dict:
    return {
        "schema_version": "1.0",
        "runner": "cascade_prototype",
        "source": "admin-upload://pdf_05_encrypted.pdf",
        "hints": {"subject": None, "language": None, "track": None, "source_type": "admin_upload", "taxonomy_node": None, "expected_figures": None},
        "pdf_triage": "encrypted",
        "text_blocks": [],
        "math_blocks": [],
        "figures": [],
        "overall_confidence": 0.0,
        "fallbacks_fired": [],
        "cas_validated_math": 0,
        "cas_failed_math": 0,
        "human_review_required": True,
        "reasons_for_review": ["preprocess_failed_or_encrypted"],
        "layer_timings_seconds": {
            "layer_0_preprocess": 0.04,
            "layer_1_layout": 0.00,
            "layer_2a_text": 0.00,
            "layer_2b_math": 0.00,
            "layer_3_reassemble": 0.00,
            "layer_4_gate": 0.00,
            "layer_5_cas": 0.00,
        },
        "total_latency_seconds": 0.04,
        "captured_at": _iso(),
        "_dev_fixture": {
            "scenario": "pdf_encrypted",
            "source_kind": "synthesized",
            "notes": "Early rejection. Surface A returns 422; Surface B flags to admin.",
        },
    }


def build_from_live_run(
    live_path: Path,
    *,
    schema_source: str,
    hints: dict,
    triage: str,
    scenario: str,
    scrub: bool,
) -> dict:
    """Wrap a real cascade_runs/*.json output into a dev-fixture with optional scrubbing."""
    live = json.loads(live_path.read_text())
    text_blocks = live.get("text_blocks", [])
    math_blocks = live.get("math_blocks", [])
    if scrub:
        text_blocks = [scrub_text_block(b) for b in text_blocks]
        math_blocks = [scrub_math_block(b) for b in math_blocks]
    t = live.get("layer_timings", {})
    return {
        "schema_version": "1.0",
        "runner": "cascade_prototype",
        "source": schema_source,
        "hints": hints,
        "pdf_triage": triage,
        "text_blocks": text_blocks,
        "math_blocks": math_blocks,
        "figures": live.get("figures", []),
        "overall_confidence": round(live.get("overall_confidence", 0.0), 3),
        "fallbacks_fired": live.get("fallbacks_fired", []),
        "cas_validated_math": live.get("cas_validated_math", 0),
        "cas_failed_math": live.get("cas_failed_math", 0),
        "human_review_required": live.get("human_review_required", False),
        "reasons_for_review": live.get("reasons_for_review", []),
        "layer_timings_seconds": {k: round(v, 3) for k, v in t.items()},
        "total_latency_seconds": round(sum(t.values()), 3),
        "captured_at": _iso(),
        "_dev_fixture": {
            "scenario": scenario,
            "source_kind": hints.get("source_type") or "unknown",
            "notes": (
                f"Wrapped from live cascade run on {live_path.name}. "
                f"{'Text + math scrubbed (reference-only).' if scrub else 'Text + math retained (safe to ship).'}"
            ),
            "scrubbing": "text/latex redacted; length+hash+bbox+confidence retained" if scrub else "none",
        },
    }


def build_scanned_bad_ocr_fixture() -> dict:
    return {
        "schema_version": "1.0",
        "runner": "cascade_prototype",
        "source": "admin-upload://pdf_04_scanned_bad_ocr_layer.pdf",
        "hints": {"subject": "math", "language": None, "track": None, "source_type": "admin_upload", "taxonomy_node": None, "expected_figures": None},
        "pdf_triage": "scanned_bad_ocr",
        "text_blocks": [
            scrub_text_block({
                "text": f"<unreadable {i}>",
                "bbox": {"x": 40 + i * 30, "y": 120, "w": 28, "h": 14, "page": 1},
                "language": "unknown",
                "confidence": 0.18 + (i % 3) * 0.05,
                "is_rtl": False,
            })
            for i in range(6)
        ],
        "math_blocks": [],
        "figures": [],
        "overall_confidence": 0.21,
        "fallbacks_fired": [],
        "cas_validated_math": 0,
        "cas_failed_math": 0,
        "human_review_required": True,
        "reasons_for_review": ["low_overall_confidence"],
        "layer_timings_seconds": {
            "layer_0_preprocess": 0.46,
            "layer_1_layout": 0.82,
            "layer_2a_text": 2.11,
            "layer_2b_math": 0.00,
            "layer_3_reassemble": 0.00,
            "layer_4_gate": 0.00,
            "layer_5_cas": 0.00,
        },
        "total_latency_seconds": 3.39,
        "captured_at": _iso(),
        "_dev_fixture": {
            "scenario": "pdf_scanned_bad_ocr",
            "source_kind": "synthesized",
            "notes": "Triage catches garbled text layer. Falls to full cascade, confidence stays low, goes to human-review queue.",
        },
    }


# ── Corpus analysis (4 categories — RDY-019b §2 output shape) ──────────────
def build_bagrut_analysis() -> dict:
    if TRIAGE_JSON.exists():
        triage = json.loads(TRIAGE_JSON.read_text())
    else:
        triage = []

    by_type = Counter(r.get("type", "error") for r in triage)
    total_pages = sum(r.get("pages", 0) or 0 for r in triage if r.get("type"))
    total_chars = sum(r.get("chars", 0) or 0 for r in triage if r.get("type") == "text")

    def unit_of(path: str) -> str:
        if "3_units" in path or "0353" in path or "0373" in path:
            return "3u"
        if "4_units" in path or "0354" in path or "0374" in path:
            return "4u"
        if "5_units" in path or "0355" in path or "0375" in path:
            return "5u"
        return "unknown"

    # Per-category breakdown (only populated when triage JSON has `category` field)
    per_category: dict[str, dict] = {}
    for r in triage:
        cat = r.get("category", "unknown")
        if cat not in per_category:
            per_category[cat] = {
                "count": 0,
                "pages": 0,
                "text_chars": 0,
                "by_pdf_type": Counter(),
                "avg_hebrew_ratio": 0.0,
                "avg_latin_ratio": 0.0,
                "avg_gibberish": 0.0,
            }
        b = per_category[cat]
        b["count"] += 1
        b["pages"] += (r.get("pages") or 0)
        if r.get("type") == "text":
            b["text_chars"] += (r.get("chars") or 0)
        b["by_pdf_type"][r.get("type", "error")] += 1
        b["avg_hebrew_ratio"] += (r.get("he_ratio") or 0)
        b["avg_latin_ratio"] += (r.get("latin_ratio") or 0)
        b["avg_gibberish"] += (r.get("gibberish") or 0)

    # Finalise averages + convert Counters to dicts
    for cat, b in per_category.items():
        n = max(1, b["count"])
        b["avg_hebrew_ratio"] = round(b["avg_hebrew_ratio"] / n, 3)
        b["avg_latin_ratio"] = round(b["avg_latin_ratio"] / n, 3)
        b["avg_gibberish"] = round(b["avg_gibberish"] / n, 3)
        b["by_pdf_type"] = dict(b["by_pdf_type"])
        b["text_shortcut_rate"] = round(b["by_pdf_type"].get("text", 0) / n, 3)

    # Bagrut-specific unit breakdown preserved
    bagrut_rows = [r for r in triage if r.get("category") == "bagrut" or not r.get("category")]
    by_unit = Counter(unit_of(r.get("path", "")) for r in bagrut_rows)

    return {
        "schema_version": "1.1",
        "analysis_kind": "corpus_structural_aggregate",
        "generated_at": _iso(),
        "source": "scripts/ocr-spike/results/full_corpus_triage.json",
        "summary": {
            "total_pdfs": len(triage),
            "total_pages": total_pages,
            "text_extractable_chars": total_chars,
            "by_pdf_type": dict(by_type),
            "categories": list(per_category.keys()),
        },
        "per_category": per_category,
        "bagrut_unit_breakdown": dict(by_unit),
        "text_shortcut_rate": round(by_type.get("text", 0) / max(1, len(triage)), 3),
        "requires_ocr_rate": round(
            (by_type.get("mixed", 0) + by_type.get("scanned_bad_ocr", 0)) / max(1, len(triage)),
            3,
        ),
        "recreation_inputs": {
            "note": "Feed this aggregate into ReferenceCalibratedGenerationService (bagrut category only — SAT/psychometric/geva are out-of-scope for Cena recreations). Per-question topic/difficulty/Bloom will be filled in by the structural analyzer (RDY-019b §2); this fixture is the outer shell + cost-model data.",
            "topic_weights": {
                "_pending": "Populated by the structural analyzer. Shape: { topic: { share, difficulty_dist, bloom_dist, format_dist } }",
            },
        },
        "_dev_fixture": {
            "notes": "Aggregate only — no question-level content. Covers bagrut + sat + psychometric + geva_downloads so downstream teams can see the full language/category diversity. Detailed analysis.json produced by the admin ingestion pipeline stays git-ignored under corpus/bagrut/reference/.",
        },
    }


# ── OcrContextHints examples ────────────────────────────────────────────────
def build_context_hints_examples() -> dict:
    return {
        "schema_version": "1.0",
        "examples": [
            {
                "id": "student_photo_math_he_unknown_track",
                "description": "Student uploads a homework photo — infers language + track",
                "hints": {
                    "subject": "math",
                    "language": "he",
                    "track": None,
                    "source_type": "student_photo",
                    "taxonomy_node": None,
                    "expected_figures": None,
                },
            },
            {
                "id": "student_pdf_math_he_4u",
                "description": "Student uploads a PDF worksheet — 4u track known from tutor context",
                "hints": {
                    "subject": "math",
                    "language": "he",
                    "track": "4u",
                    "source_type": "student_pdf",
                    "taxonomy_node": None,
                    "expected_figures": None,
                },
            },
            {
                "id": "bagrut_batch_5u",
                "description": "Batch scrape of Ministry 5u exams — track + source known",
                "hints": {
                    "subject": "math",
                    "language": "he",
                    "track": "5u",
                    "source_type": "bagrut_reference",
                    "taxonomy_node": None,
                    "expected_figures": True,
                },
            },
            {
                "id": "admin_upload_curator_tagged",
                "description": "Curator uploaded file with full metadata from RDY-019e review",
                "hints": {
                    "subject": "math",
                    "language": "he",
                    "track": "5u",
                    "source_type": "admin_upload",
                    "taxonomy_node": "algebra.quadratics.discriminant",
                    "expected_figures": False,
                },
            },
            {
                "id": "cloud_dir_unknown",
                "description": "Cloud-directory drop-zone — metadata will be back-filled by RDY-019e handshake",
                "hints": {
                    "subject": None,
                    "language": None,
                    "track": None,
                    "source_type": "cloud_dir",
                    "taxonomy_node": None,
                    "expected_figures": None,
                },
            },
            {
                "id": "english_proof_problem",
                "description": "English-language proof problem (e.g. olympiad-style)",
                "hints": {
                    "subject": "math",
                    "language": "en",
                    "track": "5u",
                    "source_type": "student_photo",
                    "taxonomy_node": "geometry.proof",
                    "expected_figures": True,
                },
            },
        ],
        "_dev_fixture": {
            "notes": "Use examples[].hints as the payload to IOcrCascadeService.RecognizeAsync. Mirrors the dataclass in runners/base.py.",
        },
    }


# ── PipelineItemDocument state samples ──────────────────────────────────────
def build_pipeline_items() -> dict:
    states = [
        {
            "item_id": _uuid(),
            "state": "pending",
            "tenant_id": "tenant_demo",
            "source_uri": "cloud_dir://s3/bagrut-dev/raw/2024_summer_5u.pdf",
            "source_kind": "cloud_dir",
            "curator_hints": None,
            "cascade_result_ref": None,
            "created_at": _iso(0),
            "updated_at": _iso(0),
        },
        {
            "item_id": _uuid(),
            "state": "triaged",
            "tenant_id": "tenant_demo",
            "source_uri": "cloud_dir://s3/bagrut-dev/raw/2024_summer_5u.pdf",
            "source_kind": "cloud_dir",
            "curator_hints": None,
            "triage_verdict": "text",
            "cascade_result_ref": None,
            "created_at": _iso(0),
            "updated_at": _iso(2),
        },
        {
            "item_id": _uuid(),
            "state": "in_cascade",
            "tenant_id": "tenant_demo",
            "source_uri": "admin_upload://curator_alice/scan_042.pdf",
            "source_kind": "admin_upload",
            "curator_hints": {
                "subject": "math", "language": "he", "track": "5u",
                "source_type": "admin_upload",
                "taxonomy_node": "calculus.derivatives",
                "expected_figures": True,
            },
            "triage_verdict": "image_only",
            "cascade_result_ref": None,
            "layer_progress": "layer_2a",
            "created_at": _iso(0),
            "updated_at": _iso(12),
        },
        {
            "item_id": _uuid(),
            "state": "awaiting_review",
            "tenant_id": "tenant_demo",
            "source_uri": "admin_upload://curator_bob/worksheet_015.pdf",
            "source_kind": "admin_upload",
            "curator_hints": {
                "subject": "math", "language": "he", "track": "4u",
                "source_type": "admin_upload",
                "taxonomy_node": None,
                "expected_figures": False,
            },
            "triage_verdict": "scanned_bad_ocr",
            "cascade_result_ref": "dev-fixtures/cascade-results/pdf-scanned-bad-ocr.json",
            "review_reasons": ["low_overall_confidence"],
            "assigned_reviewer": "curator_admin_01",
            "created_at": _iso(-60),
            "updated_at": _iso(-40),
        },
        {
            "item_id": _uuid(),
            "state": "reviewed_approved",
            "tenant_id": "tenant_demo",
            "source_uri": "bagrut_reference://5u/2024_winter/035581",
            "source_kind": "bagrut_reference",
            "curator_hints": {
                "subject": "math", "language": "he", "track": "5u",
                "source_type": "bagrut_reference",
                "taxonomy_node": None, "expected_figures": True,
            },
            "triage_verdict": "image_only",
            "cascade_result_ref": "dev-fixtures/cascade-results/bagrut-5u-full-cascade.json",
            "review_reasons": [],
            "reviewer_decision": "approved_for_recreation",
            "assigned_reviewer": "curator_admin_01",
            "created_at": _iso(-180),
            "updated_at": _iso(-30),
        },
        {
            "item_id": _uuid(),
            "state": "rejected",
            "tenant_id": "tenant_demo",
            "source_uri": "admin_upload://curator_carol/bad_scan.pdf",
            "source_kind": "admin_upload",
            "curator_hints": None,
            "triage_verdict": "encrypted",
            "cascade_result_ref": "dev-fixtures/cascade-results/pdf-encrypted-422.json",
            "review_reasons": ["preprocess_failed_or_encrypted"],
            "reviewer_decision": "rejected_unreadable",
            "created_at": _iso(-240),
            "updated_at": _iso(-230),
        },
        {
            "item_id": _uuid(),
            "state": "recreated",
            "tenant_id": "tenant_demo",
            "source_uri": "bagrut_reference://5u/2023_summer/035572",
            "source_kind": "bagrut_reference",
            "curator_hints": {
                "subject": "math", "language": "he", "track": "5u",
                "source_type": "bagrut_reference",
                "taxonomy_node": "algebra.polynomials",
                "expected_figures": False,
            },
            "triage_verdict": "text",
            "cascade_result_ref": "dev-fixtures/cascade-results/bagrut-3u-text-shortcut.json",
            "reviewer_decision": "approved_for_recreation",
            "recreations": {
                "generated_item_ids": [_uuid(), _uuid(), _uuid()],
                "provenance": "recreation",
                "reference_calibration": {
                    "year": "2023",
                    "topic_cluster": "algebra.polynomials",
                    "difficulty": 0.65,
                },
                "cas_validated": True,
            },
            "created_at": _iso(-1200),
            "updated_at": _iso(-600),
        },
    ]
    return {
        "schema_version": "1.0",
        "description": "PipelineItemDocument samples — one per major lifecycle state",
        "items": states,
        "_dev_fixture": {
            "notes": "Shape mirrors the Marten document type in Cena.Admin.Api. Use to seed the admin review queue for local dev.",
        },
    }


# ── Triage-verdict samples ──────────────────────────────────────────────────
def build_triage_samples() -> dict:
    """One example per PdfType category, using real chunks of the 151-PDF triage output."""
    if not TRIAGE_JSON.exists():
        return {"schema_version": "1.0", "samples": []}

    triage = json.loads(TRIAGE_JSON.read_text())
    # Pick one example per (category, pdf_type) combo — richer than the old 1-per-type
    picks: list[dict] = []
    seen: set[tuple[str, str]] = set()
    for r in triage:
        t = r.get("type")
        cat = r.get("category", "unknown")
        key = (cat, t)
        if not t or t == "error" or key in seen:
            continue
        picks.append({
            "category": cat,
            "pdf_type": t,
            "pages": r.get("pages"),
            "text_chars": r.get("chars"),
            "image_count": r.get("imgs"),
            "hebrew_ratio": r.get("he_ratio"),
            "latin_ratio": r.get("latin_ratio"),
            "gibberish_ratio": r.get("gibberish"),
            "source_filename_hash": hashlib.sha256((r.get("path", "")).encode()).hexdigest()[:12],
        })
        seen.add(key)

    # Ensure encrypted category exists even if corpus didn't surface one
    if not any(p["pdf_type"] == "encrypted" for p in picks):
        picks.append({
            "category": "synthetic",
            "pdf_type": "encrypted",
            "pages": 0,
            "text_chars": 0,
            "image_count": 0,
            "hebrew_ratio": 0.0,
            "latin_ratio": 0.0,
            "gibberish_ratio": 0.0,
            "source_filename_hash": "synthetic_",
        })

    return {
        "schema_version": "1.1",
        "description": "PDF triage verdict samples — one per (category, PdfType). Hand to pdf-triage unit tests.",
        "categories_covered": sorted({p["category"] for p in picks}),
        "samples": picks,
        "_dev_fixture": {
            "notes": "Sourced from live triage of 182 user-provided PDFs across bagrut/sat/psychometric/geva_downloads. Filenames are SHA-256 prefixed so nothing identifiable leaks.",
        },
    }


# ── Manifest ────────────────────────────────────────────────────────────────
def build_manifest(emitted: list[Path]) -> dict:
    entries = []
    for p in sorted(emitted):
        rel = p.relative_to(DEV)
        size = p.stat().st_size
        digest = hashlib.sha256(p.read_bytes()).hexdigest()[:16]
        entries.append({
            "path": str(rel).replace("\\", "/"),
            "bytes": size,
            "sha256_prefix": digest,
        })
    return {
        "schema_version": "1.0",
        "generator": "scripts/ocr-spike/build_dev_fixtures.py",
        "generated_at": _iso(),
        "files": entries,
        "count": len(entries),
    }


# ── Main ────────────────────────────────────────────────────────────────────
def main() -> int:
    emitted: list[Path] = []

    # Cascade results
    cr = DEV / "cascade-results"
    for problem, track, tag in [
        ("algebra_01", "3u", "student-photo-algebra-3u"),
        ("calculus_01", "5u", "student-photo-calculus-5u"),
    ]:
        out = cr / f"{tag}.json"
        write_json(out, build_student_photo_fixture(problem, track, tag))
        emitted.append(out)

    out = cr / "bagrut-3u-text-shortcut.json"
    write_json(out, build_bagrut_text_shortcut_fixture())
    emitted.append(out)

    # Pick an available live run as source if present
    live_runs = sorted(CASCADE_RUNS.glob("bagrut_5u*.json")) if CASCADE_RUNS.exists() else []
    source_run = live_runs[0] if live_runs else None
    out = cr / "bagrut-5u-full-cascade.json"
    write_json(out, build_bagrut_full_cascade_fixture(source_run))
    emitted.append(out)

    out = cr / "pdf-encrypted-422.json"
    write_json(out, build_encrypted_fixture())
    emitted.append(out)

    out = cr / "pdf-scanned-bad-ocr.json"
    write_json(out, build_scanned_bad_ocr_fixture())
    emitted.append(out)

    # Per-category fixtures from live cascade runs (when available)
    per_category_fixtures = [
        ("sat-english-text.json",
         CASCADE_RUNS / "sat_text_sample.json",
         {
             "scenario": "sat_english_text",
             "triage": "text",
             "hints": {"subject": "math", "language": "en", "track": None,
                       "source_type": "admin_upload", "taxonomy_node": None, "expected_figures": False},
             "source": "admin-upload://sat/psat_10_2023_english_solution.pdf",
             "scrub": False,   # English SAT text — safe to ship verbatim
         }),
        ("psychometric-hebrew-mixed.json",
         CASCADE_RUNS / "psychometric_mixed_sample.json",
         {
             "scenario": "psychometric_hebrew_mixed",
             "triage": "mixed",
             "hints": {"subject": "verbal_quantitative", "language": "he", "track": None,
                       "source_type": "admin_upload", "taxonomy_node": None, "expected_figures": True},
             "source": "admin-upload://psychometric/full_2022_winter_hebrew_sample.pdf",
             "scrub": True,   # NITE content — treat with same reference-only caution
         }),
        ("psychometric-english-text.json",
         CASCADE_RUNS / "psychometric_text_sample.json",
         {
             "scenario": "psychometric_english_text",
             "triage": "text",
             "hints": {"subject": "verbal_quantitative", "language": "en", "track": None,
                       "source_type": "admin_upload", "taxonomy_node": None, "expected_figures": False},
             "source": "admin-upload://psychometric/full_2019_spring_english_sample.pdf",
             "scrub": True,
         }),
        ("geva-hebrew-solutions.json",
         CASCADE_RUNS / "geva_downloads_text_sample.json",
         {
             "scenario": "geva_hebrew_solutions",
             "triage": "text",
             "hints": {"subject": "math", "language": "he", "track": "3u",
                       "source_type": "admin_upload", "taxonomy_node": None, "expected_figures": False},
             "source": "admin-upload://geva/35371_shaalon_35371.pdf",
             "scrub": True,   # Geva is third-party copyrighted — redact text
         }),
    ]
    for filename, live_path, meta in per_category_fixtures:
        if not live_path.exists():
            print(f"  skip {filename}: no live cascade run at {live_path.name}")
            continue
        out = cr / filename
        write_json(out, build_from_live_run(
            live_path,
            schema_source=meta["source"],
            hints=meta["hints"],
            triage=meta["triage"],
            scenario=meta["scenario"],
            scrub=meta["scrub"],
        ))
        emitted.append(out)

    # Analysis
    out = DEV / "bagrut-analysis" / "analysis.json"
    write_json(out, build_bagrut_analysis())
    emitted.append(out)

    # Hints
    out = DEV / "context-hints" / "examples.json"
    write_json(out, build_context_hints_examples())
    emitted.append(out)

    # Pipeline states
    out = DEV / "pipeline-states" / "pipeline-items.json"
    write_json(out, build_pipeline_items())
    emitted.append(out)

    # Triage samples
    out = DEV / "triage-verdicts" / "samples.json"
    write_json(out, build_triage_samples())
    emitted.append(out)

    # Manifest LAST so it includes every emitted file
    manifest = build_manifest(emitted)
    write_json(DEV / "manifest.json", manifest)

    print(f"✓ Emitted {len(emitted) + 1} files under {DEV.relative_to(ROOT.parent.parent)}")
    for p in emitted:
        rel = p.relative_to(DEV)
        print(f"  {p.stat().st_size:>8} bytes  {rel}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
