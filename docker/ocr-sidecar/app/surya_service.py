"""Surya wrapper for Layer 1 (layout detection) + Layer 2a (Hebrew text OCR).

Loaded lazily so server boot doesn't block on ~800 MB model downloads. The
`is_ready()` method drives the gRPC health signal; `recognize()` performs
the actual inference.

Pinned to surya-ocr 0.5.* (see docker/ocr-sidecar/requirements.txt) —
newer Surya has a restructured API (FoundationPredictor + RecognitionPredictor)
that requires a different loader path. The RDY-OCR-PORT body tracks the
decision to stay on 0.5 until the benchmark harness validates 0.6+.
"""
from __future__ import annotations

import io
import logging
import time
from typing import Any

log = logging.getLogger(__name__)


class SuryaService:
    def __init__(self) -> None:
        self._det_model: Any | None = None
        self._det_proc: Any | None = None
        self._rec_model: Any | None = None
        self._rec_proc: Any | None = None
        self._device = "cpu"
        self._load()

    def _load(self) -> None:
        try:
            import torch
            from surya.model.detection.model import (
                load_model as load_det,
                load_processor as load_det_proc,
            )
            from surya.model.recognition.model import load_model as load_rec
            from surya.model.recognition.processor import load_processor as load_rec_proc

            self._device = (
                "mps" if torch.backends.mps.is_available()
                else "cuda" if torch.cuda.is_available()
                else "cpu"
            )
            self._det_model = load_det().to(self._device)
            self._det_model = getattr(self._det_model, "eval")()
            self._det_proc = load_det_proc()
            self._rec_model = load_rec().to(self._device)
            self._rec_model = getattr(self._rec_model, "eval")()
            self._rec_proc = load_rec_proc()
            log.info("Surya models loaded on device=%s", self._device)
        except Exception as e:  # noqa: BLE001
            log.warning("Surya not available yet: %s", e)

    def is_ready(self) -> bool:
        return all(
            x is not None
            for x in (self._det_model, self._det_proc, self._rec_model, self._rec_proc)
        )

    def recognize(self, request, context):
        # Import here so server boot doesn't depend on generated stubs.
        from ocr_pb2 import RecognizeResponse, TextLine, BoundingBox
        if not self.is_ready():
            context.abort(5, "surya models not ready")  # NOT_FOUND ~ unavailable

        from PIL import Image
        from surya.detection import batch_text_detection
        from surya.recognition import batch_recognition

        img = Image.open(io.BytesIO(request.image_bytes)).convert("RGB")
        langs = _langs(request.language_hint)

        t0 = time.perf_counter()
        det_preds = batch_text_detection([img], self._det_model, self._det_proc)
        rec_preds = batch_recognition([img], [langs], self._rec_model, self._rec_proc)
        elapsed = time.perf_counter() - t0

        out = RecognizeResponse(latency_seconds=elapsed)
        bboxes = det_preds[0].bboxes
        confidences: list[float] = []

        for line, poly in zip(rec_preds[0].text_lines, (b.polygon for b in bboxes)):
            xs = [p[0] for p in poly]
            ys = [p[1] for p in poly]
            is_rtl = any(0x0590 <= ord(c) <= 0x05FF for c in line.text)
            out.text_lines.append(TextLine(
                text=line.text,
                bbox=BoundingBox(
                    x=float(min(xs)), y=float(min(ys)),
                    w=float(max(xs) - min(xs)), h=float(max(ys) - min(ys)),
                    page=1,
                ),
                confidence=float(line.confidence or 0.0),
                is_rtl=is_rtl,
                language="he" if is_rtl else "en",
            ))
            confidences.append(float(line.confidence or 0.0))

        out.overall_confidence = (
            sum(confidences) / len(confidences) if confidences else 0.0
        )
        return out


def _langs(hint: str) -> list[str]:
    if hint == "en":
        return ["en"]
    if hint == "he":
        return ["he", "en"]
    return ["he", "en"]
