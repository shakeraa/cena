"""OCR runner plugins.

Each runner wraps one candidate tool behind a shared `Runner` protocol so the
benchmark harness can iterate uniformly. Runners fail-soft: if a required
model / binary isn't available, `setup()` raises `RunnerUnavailable` and the
harness records that row as `skipped: <reason>` rather than crashing the full
matrix.
"""
from __future__ import annotations

from .base import (
    Runner,
    RunnerUnavailable,
    OcrContextHints,
    RecognitionResult,
    TextBlock,
    MathBlock,
    FigureRef,
    Language,
    SourceType,
    Track,
)

# Registry — benchmark.py iterates this.
# Order reflects expected strength on Hebrew+math; the actual score is
# decided by the fixture benchmark, not this order.
RUNNER_MODULES = [
    "runners.surya_runner",
    "runners.marker_runner",
    "runners.mineru_runner",
    "runners.olmocr_runner",
    "runners.tesseract_runner",
    "runners.pix2tex_runner",
    "runners.paddleocr_runner",
    "runners.nougat_runner",
    "runners.doctr_runner",
]

__all__ = [
    "Runner",
    "RunnerUnavailable",
    "OcrContextHints",
    "RecognitionResult",
    "TextBlock",
    "MathBlock",
    "FigureRef",
    "Language",
    "SourceType",
    "Track",
    "RUNNER_MODULES",
]
