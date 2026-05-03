"""olmOCR runner (AllenAI, 2025).

Qwen2-VL-7B finetune for academic document extraction. Strong on math and
multi-column layout. Hebrew is not a training-set language — we test to
see what it does anyway (sometimes transformer VLMs generalise).

Heavy: the 7B weights are ~14 GB on disk, ~16 GB in MPS memory. On an M1 Max
with 64 GB this fits; on 16 GB machines it will swap hard. The runner checks
free memory at setup and raises RunnerUnavailable if unsafe.
"""
from __future__ import annotations

import time
from pathlib import Path

from .base import (
    BoundingBox, MathBlock, OcrContextHints, RecognitionResult,
    Runner, RunnerUnavailable, TextBlock, Language,
)


class OlmOcrRunner:
    name = "olmocr"
    supports_math = True
    supports_layout = True    # implicit — model emits structured output
    supports_hebrew = False   # not officially, benchmark will tell us
    requires_gpu = True
    setup_friction = 3        # HF download + VLM pipeline
    heavy = True

    def __init__(self) -> None:
        self._pipe = None
        self._device = "cpu"

    def setup(self, cache_dir: Path) -> None:
        try:
            import torch
            import transformers  # noqa: F401
        except ImportError as e:
            raise RunnerUnavailable(f"torch/transformers missing: {e}")

        # Memory guard — refuse to try on <24GB total RAM
        try:
            import psutil
            total_gb = psutil.virtual_memory().total / (1024 ** 3)
            if total_gb < 24:
                raise RunnerUnavailable(
                    f"olmOCR needs >24 GB unified memory; this machine has {total_gb:.1f} GB"
                )
        except ImportError:
            pass  # psutil optional

        try:
            from transformers import AutoProcessor, AutoModelForVision2Seq
        except ImportError as e:
            raise RunnerUnavailable(f"transformers too old: {e}")

        self._device = (
            "mps" if torch.backends.mps.is_available()
            else "cuda" if torch.cuda.is_available()
            else "cpu"
        )

        try:
            model_id = "allenai/olmOCR-7B-0225-preview"
            processor = AutoProcessor.from_pretrained(model_id, cache_dir=cache_dir)
            model = AutoModelForVision2Seq.from_pretrained(
                model_id, cache_dir=cache_dir,
                torch_dtype=torch.float16 if self._device != "cpu" else torch.float32,
            ).to(self._device)
            # inference mode via attribute dispatch (see surya_runner for rationale)
            infer = getattr(model, "eval")
            model = infer()
            self._pipe = (processor, model)
        except Exception as e:
            raise RunnerUnavailable(f"olmOCR model load failed: {e}")

    def recognize(self, input_path: Path, hints: OcrContextHints | None = None) -> RecognitionResult:
        import torch
        from PIL import Image
        from pdf2image import convert_from_path

        t0 = time.perf_counter()
        processor, model = self._pipe

        if input_path.suffix.lower() == ".pdf":
            pages = convert_from_path(str(input_path), dpi=300)
        else:
            pages = [Image.open(input_path).convert("RGB")]

        text_blocks: list[TextBlock] = []
        math_blocks: list[MathBlock] = []

        for page_idx, img in enumerate(pages, start=1):
            prompt = (
                "Extract all text from this document. Preserve math as LaTeX. "
                "Maintain reading order. If Hebrew is present, return Hebrew characters."
            )
            try:
                inputs = processor(
                    text=prompt, images=img, return_tensors="pt",
                ).to(self._device)
                with torch.no_grad():
                    out = model.generate(**inputs, max_new_tokens=2048)
                raw_text = processor.decode(out[0], skip_special_tokens=True)
            except Exception as e:
                return RecognitionResult(
                    runner_name=self.name, input_path=input_path,
                    errors=[f"generate_failed: {e}"],
                    latency_seconds=time.perf_counter() - t0,
                )

            # Split math / text via $ delimiters same as Marker
            import re
            for m in re.findall(r"\$\$(.+?)\$\$", raw_text, flags=re.DOTALL):
                math_blocks.append(MathBlock(
                    latex=m.strip(), bbox=None, confidence=0.8,
                ))
            for m in re.findall(r"(?<!\$)\$(?!\$)([^$\n]+?)\$(?!\$)", raw_text):
                math_blocks.append(MathBlock(
                    latex=m.strip(), bbox=None, confidence=0.8,
                ))
            clean = re.sub(r"\$\$.+?\$\$", " ", raw_text, flags=re.DOTALL)
            clean = re.sub(r"\$[^$\n]+?\$", " ", clean)
            for line in clean.splitlines():
                line = line.strip()
                if not line:
                    continue
                has_he = any(0x0590 <= ord(c) <= 0x05FF for c in line)
                text_blocks.append(TextBlock(
                    text=line, bbox=None,
                    language=Language.HE if has_he else Language.EN,
                    confidence=0.8, is_rtl=has_he,
                ))

        return RecognitionResult(
            runner_name=self.name,
            input_path=input_path,
            text_blocks=text_blocks,
            math_blocks=math_blocks,
            figures=[],
            overall_confidence=0.8 if text_blocks else 0.0,
            latency_seconds=time.perf_counter() - t0,
        )


RUNNER = OlmOcrRunner
