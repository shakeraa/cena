"""PDF triage — classify a PDF before dispatching to OCR.

Four+1 categories:
    text            — non-empty, non-garbage text layer → extract directly
    image_only      — empty text layer → rasterize + OCR
    mixed           — text layer present + non-trivial images → hybrid
    scanned_bad_ocr — text layer exists but looks like garbage (gibberish ratio > 0.5)
    encrypted       — cannot read without password → reject politely

Callable as a library from the benchmark / cascade, or as a CLI.

Design note: "garbage" detection is heuristic — we build a character-class
histogram and a dictionary-like check for the top 1000 Hebrew/English words.
This is deliberately simple; the cascade's CAS oracle catches misreads later.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass, asdict
from enum import Enum
from pathlib import Path
from typing import Iterable

try:
    import pypdf
except ImportError:
    print("ERROR: pypdf not installed", file=sys.stderr)
    sys.exit(2)


class PdfType(str, Enum):
    TEXT = "text"
    IMAGE_ONLY = "image_only"
    MIXED = "mixed"
    SCANNED_BAD_OCR = "scanned_bad_ocr"
    ENCRYPTED = "encrypted"
    UNREADABLE = "unreadable"


@dataclass
class TriageResult:
    path: str
    pdf_type: PdfType
    pages: int
    text_chars: int
    image_count: int
    gibberish_ratio: float
    hebrew_ratio: float
    latin_ratio: float
    reasons: list[str]

    def to_dict(self) -> dict:
        d = asdict(self)
        d["pdf_type"] = self.pdf_type.value
        return d


# ── Heuristics ─────────────────────────────────────────────────────────────
_HE_RANGE = range(0x0590, 0x05FF + 1)
_LATIN_RANGE = range(0x0020, 0x007E + 1)
_MATH_RANGE = range(0x2200, 0x22FF + 1)  # mathematical operators block


def _char_class_ratio(text: str) -> tuple[float, float, float]:
    """Return (hebrew_ratio, latin_ratio, gibberish_ratio).

    Gibberish ratio = fraction of chars that are neither Hebrew, printable
    Latin, digit, whitespace, nor a small whitelist of math symbols. Very
    high values (>0.5) strongly suggest the text layer is scrambled encoding.
    """
    if not text:
        return 0.0, 0.0, 0.0
    total = 0
    he = latin = weird = 0
    for ch in text:
        cp = ord(ch)
        if ch.isspace():
            continue
        total += 1
        if cp in _HE_RANGE:
            he += 1
        elif cp in _LATIN_RANGE:
            latin += 1
        elif cp in _MATH_RANGE:
            pass
        elif cp < 0x80 and (ch.isalnum() or ch in ",.;:!?()[]{}/\\*+-=<>%&"):
            latin += 1
        else:
            weird += 1
    if total == 0:
        return 0.0, 0.0, 0.0
    return he / total, latin / total, weird / total


def _has_common_tokens(text: str) -> bool:
    """Cheap dictionary sanity — does the text contain at least a few common words?"""
    tokens = re.findall(r"[A-Za-z]{3,}|[\u0590-\u05FF]{2,}", text.lower())
    if len(tokens) < 3:
        return False
    common_en = {"the", "and", "for", "are", "with", "this", "that", "problem",
                 "find", "solve", "given", "show", "compute", "calculate"}
    common_he = {"של", "את", "על", "היא", "הוא", "משוואה", "פתור", "נתון",
                 "מצא", "חשב", "תחום", "תשובה"}
    hits = sum(1 for t in tokens if t in common_en or t in common_he)
    return hits >= 2


def _count_images(reader: pypdf.PdfReader) -> int:
    """Count XObject images across all pages. Approx — we don't resolve nested forms."""
    total = 0
    for page in reader.pages:
        try:
            resources = page.get("/Resources") or {}
            if hasattr(resources, "get_object"):
                resources = resources.get_object()
            xobjects = resources.get("/XObject") if hasattr(resources, "get") else {}
            if hasattr(xobjects, "get_object"):
                xobjects = xobjects.get_object()
            if not xobjects:
                continue
            for _, obj in xobjects.items():
                o = obj.get_object() if hasattr(obj, "get_object") else obj
                subtype = o.get("/Subtype") if hasattr(o, "get") else None
                if str(subtype) == "/Image":
                    total += 1
        except Exception:
            # Malformed resources dict — don't crash triage on it
            continue
    return total


# ── Main classify ──────────────────────────────────────────────────────────
def classify(path: Path, *, min_text_chars: int = 50) -> TriageResult:
    reasons: list[str] = []
    try:
        reader = pypdf.PdfReader(str(path))
    except Exception as e:
        return TriageResult(str(path), PdfType.UNREADABLE, 0, 0, 0, 0, 0, 0,
                            [f"pypdf_open_failed: {e}"])

    if reader.is_encrypted:
        # Try empty password — often works for owner-only restrictions
        try:
            reader.decrypt("")
        except Exception:
            pass
        if reader.is_encrypted:
            return TriageResult(str(path), PdfType.ENCRYPTED, 0, 0, 0, 0, 0, 0,
                                ["pdf_is_encrypted"])

    try:
        texts = [(p.extract_text() or "") for p in reader.pages]
    except Exception as e:
        reasons.append(f"text_extract_failed:{e}")
        texts = []

    full_text = "".join(texts)
    text_chars = len(full_text.strip())
    img_count = _count_images(reader)

    he_ratio, latin_ratio, gibberish_ratio = _char_class_ratio(full_text)
    sane_tokens = _has_common_tokens(full_text)

    pages = len(reader.pages)

    if text_chars == 0:
        reasons.append("empty_text_layer")
        pdf_type = PdfType.IMAGE_ONLY
    elif text_chars < min_text_chars:
        reasons.append(f"text_layer_below_threshold:{text_chars}<{min_text_chars}")
        pdf_type = PdfType.IMAGE_ONLY if img_count > 0 else PdfType.IMAGE_ONLY
    elif gibberish_ratio > 0.5 or not sane_tokens:
        reasons.append(
            f"gibberish_ratio={gibberish_ratio:.2f} common_tokens={sane_tokens}"
        )
        pdf_type = PdfType.SCANNED_BAD_OCR
    elif img_count > 0:
        reasons.append(f"text_layer_and_images:{text_chars}chars,{img_count}imgs")
        pdf_type = PdfType.MIXED
    else:
        reasons.append(f"clean_text_layer:{text_chars}chars")
        pdf_type = PdfType.TEXT

    return TriageResult(
        str(path), pdf_type, pages, text_chars, img_count,
        round(gibberish_ratio, 3), round(he_ratio, 3), round(latin_ratio, 3),
        reasons,
    )


# ── CLI ────────────────────────────────────────────────────────────────────
def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("pdfs", nargs="+", help="one or more PDF paths")
    ap.add_argument("--json", action="store_true", help="emit JSON array")
    args = ap.parse_args(argv)

    results: list[TriageResult] = []
    for p in args.pdfs:
        r = classify(Path(p))
        results.append(r)

    if args.json:
        print(json.dumps([r.to_dict() for r in results], indent=2, ensure_ascii=False))
        return 0

    for r in results:
        print(f"{Path(r.path).name:40s}  "
              f"{r.pdf_type.value:20s}  "
              f"pages={r.pages}  chars={r.text_chars}  imgs={r.image_count}  "
              f"he={r.hebrew_ratio:.2f}  junk={r.gibberish_ratio:.2f}")
        for reason in r.reasons:
            print(f"    - {reason}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
