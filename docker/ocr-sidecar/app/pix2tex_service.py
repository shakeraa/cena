"""pix2tex wrapper for Layer 2b (math-to-LaTeX OCR).

Expects a pre-cropped math region — the caller (Cena.Infrastructure.Ocr
Layer 2b) crops math bounding boxes from Surya's layout detection and
sends them here one by one. Full-page input is accepted but produces
lower-quality output; that's a caller concern, not a sidecar bug.
"""
from __future__ import annotations

import io
import logging
import time
from typing import Any

log = logging.getLogger(__name__)


class Pix2TexService:
    def __init__(self) -> None:
        self._model: Any | None = None
        self._load()

    def _load(self) -> None:
        try:
            from pix2tex.cli import LatexOCR
            self._model = LatexOCR()
            log.info("pix2tex LatexOCR model loaded")
        except Exception as e:  # noqa: BLE001
            log.warning("pix2tex not available yet: %s", e)

    def is_ready(self) -> bool:
        return self._model is not None

    def recognize(self, request, context):
        from ocr_pb2 import MathResponse
        if not self.is_ready():
            context.abort(5, "pix2tex model not ready")

        from PIL import Image
        img = Image.open(io.BytesIO(request.image_bytes)).convert("RGB")

        t0 = time.perf_counter()
        try:
            latex = self._model(img)
            conf = 0.75 if latex else 0.0
        except Exception as e:  # noqa: BLE001
            log.error("pix2tex failed: %s", e)
            context.abort(13, f"pix2tex_failed: {e}")  # INTERNAL

        return MathResponse(
            latex=latex or "",
            confidence=conf,
            latency_seconds=time.perf_counter() - t0,
        )
