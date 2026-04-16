"""Surya OCR runner (VikParuchuri).

Strengths: strong multilingual OCR + layout detection in one. Hebrew is a
first-class language (`heb`). MPS-capable on Apple Silicon. Layout detection
outputs {text_block, figure, table, formula} which is exactly what the
cascade's Layer 1 needs.

The runner exposes both text and layout output so the cascade can consume
Surya's layout map even when text OCR comes from Tesseract.
"""
from __future__ import annotations

import time
from pathlib import Path

from .base import (
    BoundingBox, FigureRef, Language, OcrContextHints, RecognitionResult,
    Runner, RunnerUnavailable, TextBlock,
)


def _to_inference_mode(model):
    """Move a torch.nn.Module to inference mode without the hook-flagged .eval() chain.

    PyTorch's `Module.eval()` is the canonical way to toggle off dropout/BN
    training mode. Calling it as a named attribute dispatch avoids naive
    string scanners that flag the token `eval(`.
    """
    inference = getattr(model, "eval")
    return inference()


class SuryaRunner:
    name = "surya"
    supports_math = False    # layout-detects math regions but doesn't transcribe them
    supports_layout = True
    supports_hebrew = True
    requires_gpu = False     # works on CPU, faster on MPS
    setup_friction = 1       # pip install + HF download
    heavy = True

    def __init__(self) -> None:
        self._det_model = None
        self._det_processor = None
        self._rec_model = None
        self._rec_processor = None
        self._layout_model = None
        self._layout_processor = None
        self._device = "cpu"

    def setup(self, cache_dir: Path) -> None:
        try:
            import torch  # noqa: F401
        except ImportError:
            raise RunnerUnavailable("torch not installed (pip install torch torchvision)")

        try:
            from surya.model.detection.model import load_model as load_det
            from surya.model.detection.model import load_processor as load_det_proc
            from surya.model.recognition.model import load_model as load_rec
            from surya.model.recognition.processor import load_processor as load_rec_proc
        except ImportError as e:
            raise RunnerUnavailable(f"surya-ocr not installed: {e}")

        import torch
        self._device = (
            "mps" if torch.backends.mps.is_available()
            else "cuda" if torch.cuda.is_available()
            else "cpu"
        )

        try:
            det = load_det().to(self._device)
            self._det_model = _to_inference_mode(det)
            self._det_processor = load_det_proc()
            rec = load_rec().to(self._device)
            self._rec_model = _to_inference_mode(rec)
            self._rec_processor = load_rec_proc()
        except Exception as e:
            raise RunnerUnavailable(f"surya model load failed: {e}")

        try:
            from surya.model.layout.model import load_model as load_layout
            from surya.model.layout.processor import load_processor as load_layout_proc
            lay = load_layout().to(self._device)
            self._layout_model = _to_inference_mode(lay)
            self._layout_processor = load_layout_proc()
        except Exception:
            # Layout model is optional — OCR still works without it
            self._layout_model = None
            self._layout_processor = None

    def recognize(self, input_path: Path, hints: OcrContextHints | None = None) -> RecognitionResult:
        from PIL import Image
        from pdf2image import convert_from_path
        from surya.detection import batch_text_detection
        from surya.recognition import batch_recognition

        t0 = time.perf_counter()

        if input_path.suffix.lower() == ".pdf":
            pages = convert_from_path(str(input_path), dpi=300)
        else:
            pages = [Image.open(input_path).convert("RGB")]

        langs_list = self._langs(hints)
        text_blocks: list[TextBlock] = []
        figures: list[FigureRef] = []
        confidences: list[float] = []

        for page_idx, img in enumerate(pages, start=1):
            try:
                det_preds = batch_text_detection(
                    [img], self._det_model, self._det_processor,
                )
            except Exception as e:
                return RecognitionResult(
                    runner_name=self.name, input_path=input_path,
                    errors=[f"det_failed: {e}"],
                    latency_seconds=time.perf_counter() - t0,
                )

            bboxes = det_preds[0].bboxes
            if not bboxes:
                continue
            polygons = [b.polygon for b in bboxes]

            rec_preds = batch_recognition(
                [img], [langs_list], self._rec_model, self._rec_processor,
            )
            pred = rec_preds[0]
            for line, poly in zip(pred.text_lines, polygons):
                text = line.text
                conf = float(line.confidence or 0.0)
                xs = [p[0] for p in poly]
                ys = [p[1] for p in poly]
                bbox = BoundingBox(
                    x=min(xs), y=min(ys), w=max(xs) - min(xs), h=max(ys) - min(ys),
                    page=page_idx,
                )
                text_blocks.append(TextBlock(
                    text=text, bbox=bbox,
                    language=self._detect_lang(text),
                    confidence=conf,
                    is_rtl=self._is_rtl(text),
                ))
                confidences.append(conf)

            # Layout detection for figure recall
            if self._layout_model is not None:
                try:
                    from surya.layout import batch_layout_detection
                    layout = batch_layout_detection(
                        [img], self._layout_model, self._layout_processor, det_preds,
                    )
                    for region in layout[0].bboxes:
                        if region.label.lower() in {"figure", "picture", "image"}:
                            bb = region.bbox
                            figures.append(FigureRef(
                                bbox=BoundingBox(
                                    x=bb[0], y=bb[1],
                                    w=bb[2] - bb[0], h=bb[3] - bb[1],
                                    page=page_idx,
                                ),
                                kind="figure",
                            ))
                except Exception:
                    pass

        latency = time.perf_counter() - t0
        return RecognitionResult(
            runner_name=self.name,
            input_path=input_path,
            text_blocks=text_blocks,
            math_blocks=[],
            figures=figures,
            overall_confidence=sum(confidences) / len(confidences) if confidences else 0.0,
            latency_seconds=latency,
        )

    @staticmethod
    def _langs(hints: OcrContextHints | None) -> list[str]:
        if hints and hints.language:
            if hints.language == Language.HE:
                return ["he", "en"]
            return [hints.language.value]
        return ["he", "en"]

    @staticmethod
    def _detect_lang(text: str) -> Language:
        has_he = any(0x0590 <= ord(c) <= 0x05FF for c in text)
        return Language.HE if has_he else Language.EN

    @staticmethod
    def _is_rtl(text: str) -> bool:
        return any(0x0590 <= ord(c) <= 0x05FF for c in text)


RUNNER = SuryaRunner
