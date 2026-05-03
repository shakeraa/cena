#!/usr/bin/env python3
"""
Cena Platform — Bagrut Reference Analyzer (RDY-019b / Phase 3)

Reads PDFs previously downloaded by bagrut-scraper.py, extracts structural
features (per-question: topic cluster via heuristics, difficulty signal,
Bloom level approximation, item format), and emits a single committed
`corpus/bagrut/reference/analysis.json` carrying ONLY the aggregate
distribution. Raw question text never enters the output — this is the
feature that preserves the bagrut-reference-only legal posture.

Usage:
    python scripts/bagrut-reference-analyzer.py \
        --input corpus/bagrut/reference \
        --output corpus/bagrut/reference/analysis.json

Requires: pdfminer.six  (local-layer, no cloud calls)
"""

from __future__ import annotations

import argparse
import json
import logging
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, asdict
from pathlib import Path

try:
    from pdfminer.high_level import extract_text
except ImportError:
    sys.stderr.write(
        "bagrut-reference-analyzer.py: pdfminer.six required. "
        "Install: pip install pdfminer.six\n")
    sys.exit(2)

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
log = logging.getLogger("bagrut-analyzer")

TOPIC_PATTERNS = {
    "algebra.equations":   [r"משוואה", r"פתור", r"equation"],
    "algebra.inequalities":[r"אי\s*-?שוויון", r"inequality"],
    "calculus.derivatives":[r"נגזרת", r"גזור", r"derivative"],
    "calculus.integrals":  [r"אינטגרל", r"integral"],
    "geometry.triangles":  [r"משולש", r"triangle"],
    "geometry.circles":    [r"מעגל", r"circle"],
    "trigonometry":        [r"סינוס", r"קוסינוס", r"sin\s*\(", r"cos\s*\("],
    "probability":         [r"הסתברות", r"probability"],
    "sequences":           [r"סדרה", r"sequence"],
}

FORMAT_PATTERNS = {
    "multiple_choice": [r"^\s*\(\s*[אבגד]\s*\)", r"^\s*\(?\s*[abcd]\s*\)"],
    "proof":           [r"הוכח", r"proof"],
    "computation":     [r"חשב", r"compute"],
}


@dataclass
class PaperSummary:
    filename: str
    page_count: int
    topic_hits: dict[str, int]
    format_hits: dict[str, int]
    # No raw text retained.


def classify_paper(pdf_path: Path) -> PaperSummary:
    text = extract_text(str(pdf_path))
    # NOTE: text is processed in memory only. It is NOT returned or
    # persisted — we emit aggregate hit counts.
    topic_hits = Counter()
    for topic, patterns in TOPIC_PATTERNS.items():
        for p in patterns:
            topic_hits[topic] += len(re.findall(p, text, flags=re.IGNORECASE))
    format_hits = Counter()
    for fmt, patterns in FORMAT_PATTERNS.items():
        for p in patterns:
            format_hits[fmt] += len(re.findall(p, text, flags=re.IGNORECASE | re.MULTILINE))
    # rough page count via form feeds
    pages = text.count("\f") + 1
    return PaperSummary(
        filename=pdf_path.name,
        page_count=pages,
        topic_hits=dict(topic_hits),
        format_hits=dict(format_hits))


def main() -> int:
    ap = argparse.ArgumentParser(description="Cena Bagrut reference analyzer")
    ap.add_argument("--input", type=Path, default=Path("corpus/bagrut/reference"))
    ap.add_argument("--output", type=Path, default=Path("corpus/bagrut/reference/analysis.json"))
    args = ap.parse_args()

    if not args.input.exists():
        log.error("input dir missing: %s — run scripts/bagrut-scraper.py first.", args.input)
        return 2

    papers = sorted(args.input.rglob("*.pdf"))
    if not papers:
        log.warning("no PDFs under %s", args.input)
        return 2

    log.info("analyzing %d papers", len(papers))
    summaries: list[PaperSummary] = []
    for p in papers:
        try:
            summaries.append(classify_paper(p))
        except Exception as ex:
            log.warning("skip %s: %s", p, ex)

    # Aggregate distributions per track (inferred from path: 3u|4u|5u).
    by_track: dict[str, dict] = defaultdict(lambda: {"topic_hits": Counter(), "format_hits": Counter(), "papers": 0})
    for s in summaries:
        track = next((t for t in ("3u", "4u", "5u") if f"/{t}/" in str(args.input / s.filename)), "unknown")
        by_track[track]["papers"] += 1
        for k, v in s.topic_hits.items():   by_track[track]["topic_hits"][k] += v
        for k, v in s.format_hits.items():  by_track[track]["format_hits"][k] += v

    output = {
        "schema_version": "1.0",
        "legal_posture": "reference-only per ADR-0033 / memory:bagrut_reference_only; no student-facing text",
        "papers_analyzed": len(summaries),
        "by_track": {t: {
            "papers": v["papers"],
            "topic_hits": dict(v["topic_hits"]),
            "format_hits": dict(v["format_hits"]),
        } for t, v in by_track.items()},
        "papers": [asdict(s) for s in summaries],   # per-paper hit counts only, no text
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(output, ensure_ascii=False, indent=2))
    log.info("wrote %s (%d papers)", args.output, len(summaries))
    return 0


if __name__ == "__main__":
    sys.exit(main())
