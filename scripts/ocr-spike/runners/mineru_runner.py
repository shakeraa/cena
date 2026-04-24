"""MinerU runner (OpenDataLab, 2024).

`magic-pdf` combines layout detection, OCR, formula recognition, and table
structure extraction in one pipeline. Strong on Chinese/English + math; Hebrew
support is incidental — will be quantified by the benchmark.

Writes Markdown + per-element JSON to a temp dir which we read back.
"""
from __future__ import annotations

import json
import re
import shutil
import tempfile
import time
from pathlib import Path

from .base import (
    BoundingBox, Language, MathBlock, OcrContextHints, RecognitionResult,
    Runner, RunnerUnavailable, TextBlock,
)


class MinerURunner:
    name = "mineru"
    supports_math = True
    supports_layout = True
    supports_hebrew = False   # nominally multilingual — benchmark will tell
    requires_gpu = False
    setup_friction = 2
    heavy = True

    def __init__(self) -> None:
        self._module = None

    def setup(self, cache_dir: Path) -> None:
        try:
            # magic-pdf 1.x -> 2.x has shifted imports; try both
            try:
                from magic_pdf.pipe.UNIPipe import UNIPipe
                self._module = ("UNIPipe", UNIPipe)
            except ImportError:
                from magic_pdf.data.read_api import read_local_pdfs
                from magic_pdf.config.make_content_config import MakeMode
                self._module = ("read_api", read_local_pdfs)
        except ImportError as e:
            raise RunnerUnavailable(f"magic-pdf (MinerU) not installed: {e}")

    def recognize(self, input_path: Path, hints: OcrContextHints | None = None) -> RecognitionResult:
        t0 = time.perf_counter()
        pdf_path = input_path
        if input_path.suffix.lower() in {".png", ".jpg", ".jpeg"}:
            pdf_path = self._image_to_pdf(input_path)

        with tempfile.TemporaryDirectory(prefix="mineru_") as tmp:
            out_dir = Path(tmp)
            try:
                self._run_pipeline(pdf_path, out_dir)
            except Exception as e:
                return RecognitionResult(
                    runner_name=self.name, input_path=input_path,
                    errors=[f"mineru_failed: {e}"],
                    latency_seconds=time.perf_counter() - t0,
                )
            text_blocks, math_blocks = self._collect_output(out_dir)

        return RecognitionResult(
            runner_name=self.name,
            input_path=input_path,
            text_blocks=text_blocks,
            math_blocks=math_blocks,
            figures=[],
            overall_confidence=0.8 if text_blocks else 0.0,
            latency_seconds=time.perf_counter() - t0,
        )

    @staticmethod
    def _image_to_pdf(img_path: Path) -> Path:
        from PIL import Image
        out = img_path.with_suffix(".tmp.pdf")
        Image.open(img_path).convert("RGB").save(out, format="PDF")
        return out

    def _run_pipeline(self, pdf_path: Path, out_dir: Path) -> None:
        kind, entry = self._module
        if kind == "UNIPipe":
            jso_useful_key = {"_pdf_type": "", "model_list": []}
            pipe = entry(pdf_bytes=pdf_path.read_bytes(), jso_useful_key=jso_useful_key, image_writer=None)
            pipe.pipe_classify()
            pipe.pipe_analyze()
            pipe.pipe_parse()
            md = pipe.pipe_mk_markdown(str(out_dir), drop_mode="none")
            (out_dir / "content.md").write_text(md or "", encoding="utf-8")
        else:
            datasets = entry([str(pdf_path)])
            for ds in datasets:
                md = ds.apply(lambda d: d.get_markdown())
                (out_dir / "content.md").write_text(md or "", encoding="utf-8")

    @staticmethod
    def _collect_output(out_dir: Path) -> tuple[list[TextBlock], list[MathBlock]]:
        md_files = list(out_dir.rglob("*.md"))
        md = "\n".join(p.read_text(encoding="utf-8", errors="ignore") for p in md_files)
        text_blocks: list[TextBlock] = []
        math_blocks: list[MathBlock] = []
        for m in re.findall(r"\$\$(.+?)\$\$", md, flags=re.DOTALL):
            math_blocks.append(MathBlock(latex=m.strip(), bbox=None, confidence=0.8))
        for m in re.findall(r"(?<!\$)\$(?!\$)([^$\n]+?)\$(?!\$)", md):
            math_blocks.append(MathBlock(latex=m.strip(), bbox=None, confidence=0.8))
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
                confidence=0.8, is_rtl=has_he,
            ))
        return text_blocks, math_blocks


RUNNER = MinerURunner
