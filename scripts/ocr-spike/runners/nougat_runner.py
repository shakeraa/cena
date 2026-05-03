"""Nougat runner (Meta, 2023).

Paper-to-LaTeX transformer. Strong on academic English + math; Hebrew is
not a training-set language. Included for completeness — Bagrut pages have
structurally-academic math, so Nougat's math LaTeX output may still beat
general OCR even with zero Hebrew text recall.
"""
from __future__ import annotations

import re
import time
from pathlib import Path

from .base import (
    Language, MathBlock, OcrContextHints, RecognitionResult,
    Runner, RunnerUnavailable, TextBlock,
)


class NougatRunner:
    name = "nougat"
    supports_math = True
    supports_layout = False
    supports_hebrew = False
    requires_gpu = False
    setup_friction = 2
    heavy = True

    def __init__(self) -> None:
        self._model = None
        self._processor = None
        self._device = "cpu"

    def setup(self, cache_dir: Path) -> None:
        try:
            import torch
            from transformers import NougatProcessor, VisionEncoderDecoderModel
        except ImportError as e:
            raise RunnerUnavailable(f"transformers/torch missing: {e}")

        self._device = (
            "mps" if torch.backends.mps.is_available()
            else "cuda" if torch.cuda.is_available()
            else "cpu"
        )
        try:
            model_id = "facebook/nougat-base"
            self._processor = NougatProcessor.from_pretrained(model_id, cache_dir=cache_dir)
            mod = VisionEncoderDecoderModel.from_pretrained(
                model_id, cache_dir=cache_dir,
                torch_dtype=torch.float16 if self._device != "cpu" else torch.float32,
            ).to(self._device)
            infer = getattr(mod, "eval")
            self._model = infer()
        except Exception as e:
            raise RunnerUnavailable(f"nougat model load failed: {e}")

    def recognize(self, input_path: Path, hints: OcrContextHints | None = None) -> RecognitionResult:
        import torch
        from PIL import Image
        from pdf2image import convert_from_path

        t0 = time.perf_counter()
        if input_path.suffix.lower() == ".pdf":
            pages = convert_from_path(str(input_path), dpi=200, first_page=1, last_page=3)
        else:
            pages = [Image.open(input_path).convert("RGB")]

        text_blocks: list[TextBlock] = []
        math_blocks: list[MathBlock] = []

        for img in pages:
            try:
                pixel_values = self._processor(img, return_tensors="pt").pixel_values.to(self._device)
                with torch.no_grad():
                    outputs = self._model.generate(
                        pixel_values,
                        min_length=1, max_new_tokens=1024,
                        bad_words_ids=[[self._processor.tokenizer.unk_token_id]],
                    )
                sequence = self._processor.batch_decode(outputs, skip_special_tokens=True)[0]
                sequence = self._processor.post_process_generation(sequence, fix_markdown=False)
            except Exception as e:
                return RecognitionResult(
                    runner_name=self.name, input_path=input_path,
                    errors=[f"nougat_generate_failed: {e}"],
                    latency_seconds=time.perf_counter() - t0,
                )

            for m in re.findall(r"\$\$(.+?)\$\$", sequence, flags=re.DOTALL):
                math_blocks.append(MathBlock(latex=m.strip(), bbox=None, confidence=0.75))
            for m in re.findall(r"(?<!\$)\$(?!\$)([^$\n]+?)\$(?!\$)", sequence):
                math_blocks.append(MathBlock(latex=m.strip(), bbox=None, confidence=0.75))
            clean = re.sub(r"\$\$.+?\$\$", " ", sequence, flags=re.DOTALL)
            clean = re.sub(r"\$[^$\n]+?\$", " ", clean)
            for line in clean.splitlines():
                line = line.strip()
                if not line:
                    continue
                has_he = any(0x0590 <= ord(c) <= 0x05FF for c in line)
                text_blocks.append(TextBlock(
                    text=line, bbox=None,
                    language=Language.HE if has_he else Language.EN,
                    confidence=0.75, is_rtl=has_he,
                ))

        return RecognitionResult(
            runner_name=self.name,
            input_path=input_path,
            text_blocks=text_blocks,
            math_blocks=math_blocks,
            figures=[],
            overall_confidence=0.75 if text_blocks or math_blocks else 0.0,
            latency_seconds=time.perf_counter() - t0,
        )


RUNNER = NougatRunner
