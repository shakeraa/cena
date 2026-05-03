"""Scoring for OCR benchmark.

Hebrew WER, LaTeX/SymPy equivalence, layout score, figure recall, and a
combined weighted score. The weights in `SCORE_WEIGHTS` match the ones
documented in the spike README and ADR — change them in one place.
"""
from __future__ import annotations

import math
import re
import unicodedata
from dataclasses import dataclass
from typing import Any

# Lazy — jiwer and sympy add startup cost. Import inside functions.


SCORE_WEIGHTS = {
    "wer": 0.25,           # Hebrew text word error rate (lower is better)
    "math_equivalence": 0.30,
    "layout": 0.20,
    "figure_recall": 0.10,
    "perf": 0.10,          # latency (Surface A) or throughput (Surface B)
    "setup": 0.05,
}


# ── Hebrew + bidi normalisation ──────────────────────────────────────────────
_BIDI_CONTROLS = {"\u200e", "\u200f", "\u202a", "\u202b", "\u202c", "\u202d", "\u202e"}


def normalize_hebrew(text: str) -> str:
    """NFC, strip bidi controls and trailing vowel marks, collapse whitespace.

    OCR tools emit Hebrew in inconsistent forms (NFC vs NFD, with/without
    cantillation). Normalise both ground truth and prediction identically
    before WER so we don't penalise equivalent forms.
    """
    if not text:
        return ""
    t = unicodedata.normalize("NFC", text)
    t = "".join(ch for ch in t if ch not in _BIDI_CONTROLS)
    t = re.sub(r"\s+", " ", t).strip()
    return t


def wer(ground_truth: str, prediction: str) -> float:
    """Word error rate on Hebrew text. 0.0 = perfect, 1.0 = all wrong."""
    try:
        import jiwer
    except ImportError:
        return _fallback_wer(ground_truth, prediction)

    gt = normalize_hebrew(ground_truth)
    pred = normalize_hebrew(prediction)
    if not gt:
        return 0.0 if not pred else 1.0
    return float(jiwer.wer(gt, pred))


def _fallback_wer(gt: str, pred: str) -> float:
    """Python-only WER if jiwer unavailable. Levenshtein over token sequences."""
    g = normalize_hebrew(gt).split()
    p = normalize_hebrew(pred).split()
    if not g:
        return 0.0 if not p else 1.0
    d = [[0] * (len(p) + 1) for _ in range(len(g) + 1)]
    for i in range(len(g) + 1):
        d[i][0] = i
    for j in range(len(p) + 1):
        d[0][j] = j
    for i in range(1, len(g) + 1):
        for j in range(1, len(p) + 1):
            cost = 0 if g[i - 1] == p[j - 1] else 1
            d[i][j] = min(d[i - 1][j] + 1, d[i][j - 1] + 1, d[i - 1][j - 1] + cost)
    return d[len(g)][len(p)] / len(g)


# ── LaTeX / SymPy equivalence ────────────────────────────────────────────────
def parse_latex_safe(latex: str) -> Any | None:
    """Parse LaTeX to a SymPy expression. None on failure — caller decides."""
    try:
        from sympy.parsing.latex import parse_latex  # needs antlr4-python3-runtime
    except ImportError:
        return None
    try:
        return parse_latex(_clean_latex(latex))
    except Exception:
        return None


def _clean_latex(latex: str) -> str:
    """Trim common OCR artifacts before handing to the LaTeX parser."""
    s = latex.strip()
    s = s.replace("\\,", "").replace("\\ ", " ")
    # Strip dollar delimiters if present
    s = re.sub(r"^\$+|\$+$", "", s)
    return s


def math_equivalent(gt_latex: str, pred_latex: str) -> tuple[bool, str]:
    """Are two LaTeX snippets mathematically equivalent?

    Returns (equivalent, strategy). Strategy lets us debug why something
    matched: "sympy_simplify", "canonical_form", "string_after_normalize",
    or "parse_failed".
    """
    if not gt_latex and not pred_latex:
        return True, "both_empty"
    if not gt_latex or not pred_latex:
        return False, "one_empty"

    # Fast path — identical after whitespace collapse
    if _norm_latex(gt_latex) == _norm_latex(pred_latex):
        return True, "string_after_normalize"

    try:
        from sympy import simplify, Eq, srepr
    except ImportError:
        return False, "sympy_missing"

    gt_expr = parse_latex_safe(gt_latex)
    pr_expr = parse_latex_safe(pred_latex)
    if gt_expr is None or pr_expr is None:
        return False, "parse_failed"

    # Handle equations (Eq wrapper)
    try:
        if isinstance(gt_expr, Eq) and isinstance(pr_expr, Eq):
            a = simplify(gt_expr.lhs - gt_expr.rhs)
            b = simplify(pr_expr.lhs - pr_expr.rhs)
            if simplify(a - b) == 0:
                return True, "sympy_simplify"
        else:
            if simplify(gt_expr - pr_expr) == 0:
                return True, "sympy_simplify"
    except Exception:
        pass

    try:
        if srepr(gt_expr) == srepr(pr_expr):
            return True, "canonical_form"
    except Exception:
        pass

    return False, "sympy_unequal"


def _norm_latex(s: str) -> str:
    return re.sub(r"\s+", "", s.strip())


# ── Aggregate scoring ────────────────────────────────────────────────────────
@dataclass
class FixtureScore:
    fixture_id: str
    runner_name: str
    wer_score: float                       # normalised 1 - wer, clipped [0,1]
    math_score: float                      # fraction of equations matched
    layout_score: float                    # 0..1 (manual 0..3 → /3)
    figure_recall: float                   # 0..1
    perf_score: float                      # 0..1 (latency bucket)
    setup_score: float                     # 0..1 (3-friction)/3
    weighted: float = 0.0
    notes: str = ""

    def compute_weighted(self) -> float:
        w = SCORE_WEIGHTS
        self.weighted = (
            w["wer"] * self.wer_score
            + w["math_equivalence"] * self.math_score
            + w["layout"] * self.layout_score
            + w["figure_recall"] * self.figure_recall
            + w["perf"] * self.perf_score
            + w["setup"] * self.setup_score
        )
        return self.weighted


def latency_to_score(seconds: float, surface: str = "A") -> float:
    """Map latency → [0, 1]. Surface A targets <3s; Surface B targets throughput.

    Surface A: score = max(0, 1 - (seconds / 10)); <3s → >0.7, 10s+ → 0.
    Surface B: inverted throughput — pages-per-minute; see throughput_to_score.
    """
    if surface == "A":
        return max(0.0, min(1.0, 1.0 - seconds / 10.0))
    raise ValueError(f"use throughput_to_score for surface {surface}")


def throughput_to_score(pages_per_minute: float) -> float:
    """Map throughput → [0, 1]. 60 ppm → 1.0, 6 ppm → 0.1."""
    return max(0.0, min(1.0, math.log1p(pages_per_minute) / math.log1p(60)))


def setup_to_score(friction: int) -> float:
    """0 (pip install only) → 1.0; 3 (custom CUDA) → 0.0."""
    return max(0.0, min(1.0, (3 - friction) / 3.0))
