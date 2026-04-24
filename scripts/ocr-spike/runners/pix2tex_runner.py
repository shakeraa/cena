"""pix2tex / LaTeX-OCR runner.

Strengths: purpose-built math-to-LaTeX; small model, fast on CPU.
Weaknesses: no text OCR; must be pointed at a pre-cropped math region.

We use pix2tex as Layer 2b of the cascade. In the benchmark we run it against
the whole image/page to see how it degrades — in production the cascade feeds
it only math-region crops from Surya's layout pass.
"""
from __future__ import annotations

import time
from pathlib import Path

from .base import (
    MathBlock, OcrContextHints, RecognitionResult,
    Runner, RunnerUnavailable,
)


class Pix2TexRunner:
    name = "pix2tex"
    supports_math = True
    supports_layout = False
    supports_hebrew = False
    requires_gpu = False
    setup_friction = 0
    heavy = False

    def __init__(self) -> None:
        self._model = None

    def setup(self, cache_dir: Path) -> None:
        try:
            from pix2tex.cli import LatexOCR
        except ImportError as e:
            raise RunnerUnavailable(f"pix2tex not installed: {e}")
        try:
            self._model = LatexOCR()
        except Exception as e:
            raise RunnerUnavailable(f"pix2tex model load failed: {e}")

    def recognize(self, input_path: Path, hints: OcrContextHints | None = None) -> RecognitionResult:
        from PIL import Image
        from pdf2image import convert_from_path

        t0 = time.perf_counter()

        if input_path.suffix.lower() == ".pdf":
            pages = convert_from_path(str(input_path), dpi=200, first_page=1, last_page=1)
            img = pages[0] if pages else None
        else:
            img = Image.open(input_path).convert("RGB")

        if img is None:
            return RecognitionResult(
                runner_name=self.name, input_path=input_path,
                errors=["no_image_to_process"],
                latency_seconds=time.perf_counter() - t0,
            )

        try:
            latex = self._model(img)
        except Exception as e:
            return RecognitionResult(
                runner_name=self.name, input_path=input_path,
                errors=[f"pix2tex_failed: {e}"],
                latency_seconds=time.perf_counter() - t0,
            )

        math_block = MathBlock(latex=latex or "", bbox=None, confidence=0.75)
        return RecognitionResult(
            runner_name=self.name,
            input_path=input_path,
            text_blocks=[],
            math_blocks=[math_block] if latex else [],
            figures=[],
            overall_confidence=0.75 if latex else 0.0,
            latency_seconds=time.perf_counter() - t0,
        )


RUNNER = Pix2TexRunner
