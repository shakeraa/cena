"""Tesseract 5 runner — classic OCR with Hebrew trained data.

Strengths: mature, fast, free, offline. Hebrew via `heb.traineddata`.
Weaknesses: no layout detection, math is treated as text and mangled.

We use tesseract here as the text-extraction leg of the cascade (Layer 2a) —
not as a standalone winner. The benchmark still runs it end-to-end to get a
baseline.
"""
from __future__ import annotations

import shutil
import subprocess
import time
from pathlib import Path

from .base import (
    BoundingBox, Language, OcrContextHints, RecognitionResult,
    Runner, RunnerUnavailable, TextBlock,
)


class TesseractRunner:
    name = "tesseract_5"
    supports_math = False
    supports_layout = False
    supports_hebrew = True
    requires_gpu = False
    setup_friction = 0
    heavy = False

    def setup(self, cache_dir: Path) -> None:
        if shutil.which("tesseract") is None:
            raise RunnerUnavailable("tesseract binary not on PATH (brew install tesseract tesseract-lang)")

        # Verify Hebrew trained data is installed
        try:
            out = subprocess.check_output(["tesseract", "--list-langs"], stderr=subprocess.STDOUT)
        except Exception as e:
            raise RunnerUnavailable(f"tesseract --list-langs failed: {e}")
        langs = out.decode("utf-8", errors="ignore").splitlines()
        if "heb" not in langs:
            raise RunnerUnavailable(
                "heb.traineddata missing — brew install tesseract-lang "
                "or download from github.com/tesseract-ocr/tessdata"
            )

        try:
            import pytesseract  # noqa: F401
        except ImportError:
            raise RunnerUnavailable("pytesseract not installed")

    def recognize(self, input_path: Path, hints: OcrContextHints | None = None) -> RecognitionResult:
        import pytesseract
        from PIL import Image
        from pdf2image import convert_from_path

        t0 = time.perf_counter()
        lang_code = self._lang_code(hints)

        if input_path.suffix.lower() == ".pdf":
            pages = convert_from_path(str(input_path), dpi=300)
        else:
            pages = [Image.open(input_path)]

        blocks: list[TextBlock] = []
        confidences: list[float] = []
        for page_idx, img in enumerate(pages, start=1):
            data = pytesseract.image_to_data(
                img, lang=lang_code, output_type=pytesseract.Output.DICT,
            )
            n = len(data["text"])
            for i in range(n):
                text = (data["text"][i] or "").strip()
                if not text:
                    continue
                try:
                    conf = float(data["conf"][i]) / 100.0
                except (TypeError, ValueError):
                    conf = 0.0
                if conf <= 0:
                    continue
                bbox = BoundingBox(
                    x=data["left"][i], y=data["top"][i],
                    w=data["width"][i], h=data["height"][i],
                    page=page_idx,
                )
                blocks.append(TextBlock(
                    text=text, bbox=bbox,
                    language=Language.HE if lang_code.startswith("heb") else Language.EN,
                    confidence=conf,
                    is_rtl=lang_code.startswith("heb"),
                ))
                confidences.append(conf)

        latency = time.perf_counter() - t0
        return RecognitionResult(
            runner_name=self.name,
            input_path=input_path,
            text_blocks=blocks,
            math_blocks=[],
            figures=[],
            overall_confidence=(sum(confidences) / len(confidences)) if confidences else 0.0,
            latency_seconds=latency,
        )

    def _lang_code(self, hints: OcrContextHints | None) -> str:
        if hints and hints.language == Language.EN:
            return "eng"
        if hints and hints.language == Language.HE:
            return "heb+eng"
        # default: Hebrew + English (Bagrut is primarily Hebrew with English math)
        return "heb+eng"


RUNNER = TesseractRunner
