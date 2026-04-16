"""docTR runner (Mindee).

Multilingual text OCR — pure text focus, no math. Similar profile to Surya
minus layout detection. Included so we can pick the best text-only OCR for
the cascade's Layer 2a fallback when Tesseract's confidence is low.
"""
from __future__ import annotations

import time
from pathlib import Path

from .base import (
    BoundingBox, Language, OcrContextHints, RecognitionResult,
    Runner, RunnerUnavailable, TextBlock,
)


class DocTRRunner:
    name = "doctr"
    supports_math = False
    supports_layout = False
    supports_hebrew = False    # nominally multilingual; exact Hebrew coverage varies by model
    requires_gpu = False
    setup_friction = 1
    heavy = True

    def __init__(self) -> None:
        self._predictor = None

    def setup(self, cache_dir: Path) -> None:
        try:
            from doctr.models import ocr_predictor
        except ImportError as e:
            raise RunnerUnavailable(f"python-doctr not installed: {e}")
        try:
            self._predictor = ocr_predictor(pretrained=True)
        except Exception as e:
            raise RunnerUnavailable(f"doctr predictor load failed: {e}")

    def recognize(self, input_path: Path, hints: OcrContextHints | None = None) -> RecognitionResult:
        t0 = time.perf_counter()
        try:
            from doctr.io import DocumentFile
            if input_path.suffix.lower() == ".pdf":
                doc = DocumentFile.from_pdf(str(input_path))
            else:
                doc = DocumentFile.from_images(str(input_path))
            result = self._predictor(doc)
        except Exception as e:
            return RecognitionResult(
                runner_name=self.name, input_path=input_path,
                errors=[f"doctr_failed: {e}"],
                latency_seconds=time.perf_counter() - t0,
            )

        text_blocks: list[TextBlock] = []
        confidences: list[float] = []

        for page_idx, page in enumerate(result.pages, start=1):
            for block in page.blocks:
                for line in block.lines:
                    words = [w.value for w in line.words]
                    text = " ".join(words)
                    if not text.strip():
                        continue
                    conf = sum(w.confidence for w in line.words) / max(1, len(line.words))
                    (x0, y0), (x1, y1) = line.geometry
                    has_he = any(0x0590 <= ord(c) <= 0x05FF for c in text)
                    text_blocks.append(TextBlock(
                        text=text,
                        bbox=BoundingBox(
                            x=x0, y=y0, w=x1 - x0, h=y1 - y0, page=page_idx,
                        ),
                        language=Language.HE if has_he else Language.EN,
                        confidence=float(conf),
                        is_rtl=has_he,
                    ))
                    confidences.append(float(conf))

        return RecognitionResult(
            runner_name=self.name,
            input_path=input_path,
            text_blocks=text_blocks,
            math_blocks=[],
            figures=[],
            overall_confidence=(sum(confidences) / len(confidences)) if confidences else 0.0,
            latency_seconds=time.perf_counter() - t0,
        )


RUNNER = DocTRRunner
