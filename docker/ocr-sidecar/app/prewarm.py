"""Init-container entry point.

Downloads the Surya + pix2tex model weights into the HF cache volume so the
first real request doesn't pay the 1+ GB download cost. Idempotent — skips
models already on disk.

Exit codes:
    0  models available (fresh download or cache hit)
    1  could not download / verify models
"""
from __future__ import annotations

import os
import sys
from pathlib import Path


def main() -> int:
    cache = Path(os.environ.get("HF_HOME", "/hf-cache"))
    cache.mkdir(parents=True, exist_ok=True)
    print(f"[prewarm] HF cache at {cache}")

    ok_surya = _prewarm_surya()
    ok_pix2tex = _prewarm_pix2tex()

    if ok_surya and ok_pix2tex:
        print("[prewarm] all models warm — sidecar ready to serve")
        return 0
    print("[prewarm] one or more models failed to warm", file=sys.stderr)
    return 1


def _prewarm_surya() -> bool:
    print("[prewarm] warming Surya…")
    try:
        from surya.model.detection.model import load_model as load_det
        from surya.model.recognition.model import load_model as load_rec
        det = load_det()
        rec = load_rec()
        # Drop references — we just wanted the side-effect of the download.
        del det, rec
        print("[prewarm]   Surya layout + recognition models present")
        return True
    except Exception as e:  # noqa: BLE001
        print(f"[prewarm]   Surya FAILED: {type(e).__name__}: {e}", file=sys.stderr)
        return False


def _prewarm_pix2tex() -> bool:
    print("[prewarm] warming pix2tex…")
    try:
        from pix2tex.cli import LatexOCR
        model = LatexOCR()
        del model
        print("[prewarm]   pix2tex LatexOCR model present")
        return True
    except Exception as e:  # noqa: BLE001
        print(f"[prewarm]   pix2tex FAILED: {type(e).__name__}: {e}", file=sys.stderr)
        return False


if __name__ == "__main__":
    sys.exit(main())
