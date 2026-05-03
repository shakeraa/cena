"""PaddleOCR runner.

General-purpose OCR with a formula plugin (PP-FormulaNet). Multilingual but
Hebrew support is patchy — we verify with the fixture set.

macOS arm64 caveat: `paddlepaddle` wheels historically lag behind Linux, and
some versions crash on Apple Silicon. The runner raises RunnerUnavailable
cleanly when that happens so the rest of the matrix still completes.
"""
from __future__ import annotations

import time
from pathlib import Path

from .base import (
    BoundingBox, Language, OcrContextHints, RecognitionResult,
    Runner, RunnerUnavailable, TextBlock,
)


class PaddleOcrRunner:
    name = "paddleocr"
    supports_math = False      # formula plugin optional; we test without first
    supports_layout = True
    supports_hebrew = False    # patchy; benchmark decides
    requires_gpu = False
    setup_friction = 2
    heavy = True

    def __init__(self) -> None:
        self._ocr = None

    def setup(self, cache_dir: Path) -> None:
        try:
            import paddle
            import paddleocr
        except ImportError as e:
            raise RunnerUnavailable(f"paddle/paddleocr missing: {e}")
        except Exception as e:
            # PaddlePaddle sometimes raises at import time on arm64
            raise RunnerUnavailable(f"paddle import crashed: {e}")

        try:
            self._ocr = paddleocr.PaddleOCR(
                use_angle_cls=True,
                lang="hebrew",              # falls back to latin if hebrew model absent
                show_log=False,
                det_model_dir=str(cache_dir / "paddle_det"),
                rec_model_dir=str(cache_dir / "paddle_rec"),
                cls_model_dir=str(cache_dir / "paddle_cls"),
            )
        except Exception as e:
            # Try English fallback
            try:
                self._ocr = paddleocr.PaddleOCR(use_angle_cls=True, lang="en", show_log=False)
            except Exception as e2:
                raise RunnerUnavailable(f"paddleocr init failed: {e} / {e2}")

    def recognize(self, input_path: Path, hints: OcrContextHints | None = None) -> RecognitionResult:
        from PIL import Image
        from pdf2image import convert_from_path
        import numpy as np

        t0 = time.perf_counter()
        if input_path.suffix.lower() == ".pdf":
            pages = convert_from_path(str(input_path), dpi=300)
        else:
            pages = [Image.open(input_path).convert("RGB")]

        blocks: list[TextBlock] = []
        confidences: list[float] = []

        for page_idx, img in enumerate(pages, start=1):
            arr = np.array(img)
            try:
                result = self._ocr.ocr(arr, cls=True)
            except Exception as e:
                return RecognitionResult(
                    runner_name=self.name, input_path=input_path,
                    errors=[f"ocr_failed: {e}"],
                    latency_seconds=time.perf_counter() - t0,
                )
            if not result:
                continue
            # PaddleOCR 2.7 returns [[line_detections]], where each is [bbox, (text, conf)]
            for det in result[0] if isinstance(result[0], list) else result:
                if not det:
                    continue
                try:
                    bbox, payload = det
                    text, conf = payload
                except Exception:
                    continue
                xs = [p[0] for p in bbox]
                ys = [p[1] for p in bbox]
                has_he = any(0x0590 <= ord(c) <= 0x05FF for c in text)
                blocks.append(TextBlock(
                    text=text,
                    bbox=BoundingBox(
                        x=min(xs), y=min(ys),
                        w=max(xs) - min(xs), h=max(ys) - min(ys),
                        page=page_idx,
                    ),
                    language=Language.HE if has_he else Language.EN,
                    confidence=float(conf),
                    is_rtl=has_he,
                ))
                confidences.append(float(conf))

        return RecognitionResult(
            runner_name=self.name,
            input_path=input_path,
            text_blocks=blocks,
            math_blocks=[],
            figures=[],
            overall_confidence=(sum(confidences) / len(confidences)) if confidences else 0.0,
            latency_seconds=time.perf_counter() - t0,
        )


RUNNER = PaddleOcrRunner
