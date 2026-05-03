"""Marker runner (VikParuchuri).

Strengths: full-PDF → Markdown with math awareness, builds on Surya internally,
multilingual. Produces the closest single-tool output to what the cascade
wants — text + LaTeX math + layout in one call.

Weaknesses: heavy (downloads Surya + formula + table models); Hebrew quality
is inherited from Surya so not independent.
"""
from __future__ import annotations

import re
import time
from pathlib import Path

from .base import (
    BoundingBox, Language, MathBlock, OcrContextHints, RecognitionResult,
    Runner, RunnerUnavailable, TextBlock,
)


class MarkerRunner:
    name = "marker"
    supports_math = True
    supports_layout = True
    supports_hebrew = True   # inherited from Surya
    requires_gpu = False
    setup_friction = 2       # multiple models, more install friction
    heavy = True

    def __init__(self) -> None:
        self._models = None

    def setup(self, cache_dir: Path) -> None:
        try:
            from marker.models import create_model_dict
        except ImportError as e:
            raise RunnerUnavailable(f"marker-pdf not installed: {e}")

        try:
            self._models = create_model_dict()
        except Exception as e:
            raise RunnerUnavailable(f"marker model load failed: {e}")

    def recognize(self, input_path: Path, hints: OcrContextHints | None = None) -> RecognitionResult:
        t0 = time.perf_counter()
        try:
            from marker.converters.pdf import PdfConverter
            converter = PdfConverter(artifact_dict=self._models)
        except Exception as e:
            return RecognitionResult(
                runner_name=self.name, input_path=input_path,
                errors=[f"converter_init_failed: {e}"],
                latency_seconds=time.perf_counter() - t0,
            )

        # Marker reads PDFs directly; images need to be wrapped in a PDF first
        pdf_path = input_path
        if input_path.suffix.lower() in {".png", ".jpg", ".jpeg"}:
            pdf_path = self._image_to_pdf(input_path)

        try:
            rendered = converter(str(pdf_path))
        except Exception as e:
            return RecognitionResult(
                runner_name=self.name, input_path=input_path,
                errors=[f"convert_failed: {e}"],
                latency_seconds=time.perf_counter() - t0,
            )

        md = getattr(rendered, "markdown", "") or str(rendered)
        text_blocks, math_blocks = self._parse_markdown(md)

        latency = time.perf_counter() - t0
        return RecognitionResult(
            runner_name=self.name,
            input_path=input_path,
            text_blocks=text_blocks,
            math_blocks=math_blocks,
            figures=[],
            overall_confidence=0.85 if text_blocks else 0.0,  # Marker doesn't expose per-region conf
            latency_seconds=latency,
            raw_output={"markdown": md[:10000]},
        )

    @staticmethod
    def _image_to_pdf(img_path: Path) -> Path:
        from PIL import Image
        out = img_path.with_suffix(".tmp.pdf")
        Image.open(img_path).convert("RGB").save(out, format="PDF")
        return out

    @staticmethod
    def _parse_markdown(md: str) -> tuple[list[TextBlock], list[MathBlock]]:
        """Crude parser: `$$…$$` and `$…$` are math, everything else is text."""
        text_blocks: list[TextBlock] = []
        math_blocks: list[MathBlock] = []

        # Pull block math first
        block_math = re.findall(r"\$\$(.+?)\$\$", md, flags=re.DOTALL)
        for m in block_math:
            math_blocks.append(MathBlock(latex=m.strip(), bbox=None, confidence=0.85))

        # Strip block math before inline math pass
        md_no_block = re.sub(r"\$\$.+?\$\$", "", md, flags=re.DOTALL)
        inline = re.findall(r"(?<!\$)\$(?!\$)([^$\n]+?)\$(?!\$)", md_no_block)
        for m in inline:
            math_blocks.append(MathBlock(latex=m.strip(), bbox=None, confidence=0.85))

        # Remaining text
        clean = re.sub(r"\$\$.+?\$\$", " ", md, flags=re.DOTALL)
        clean = re.sub(r"\$[^$\n]+?\$", " ", clean)
        for line in clean.splitlines():
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            has_he = any(0x0590 <= ord(c) <= 0x05FF for c in line)
            text_blocks.append(TextBlock(
                text=line, bbox=None,
                language=Language.HE if has_he else Language.EN,
                confidence=0.85, is_rtl=has_he,
            ))
        return text_blocks, math_blocks


RUNNER = MarkerRunner
