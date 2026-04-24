"""Shared contracts for OCR runners.

The `Runner` protocol is the plugin boundary. Every concrete runner is a thin
adapter that normalises a tool's native output into `RecognitionResult`. The
benchmark harness and the cascade prototype only ever see this normalised
shape, so swapping a runner downstream does not touch the harness or cascade.
"""
from __future__ import annotations

import enum
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Protocol, runtime_checkable


# ── Hints (consumer-side contract for RDY-019e CuratorMetadata) ─────────────
class Language(str, enum.Enum):
    HE = "he"
    EN = "en"
    AR = "ar"
    UNKNOWN = "unknown"


class SourceType(str, enum.Enum):
    STUDENT_PHOTO = "student_photo"
    STUDENT_PDF = "student_pdf"
    BAGRUT_REFERENCE = "bagrut_reference"
    ADMIN_UPLOAD = "admin_upload"
    CLOUD_DIR = "cloud_dir"


class Track(str, enum.Enum):
    UNITS_3 = "3u"
    UNITS_4 = "4u"
    UNITS_5 = "5u"
    UNKNOWN = "unknown"


@dataclass(frozen=True)
class OcrContextHints:
    """Optional curator/client hints. Absent → cascade infers.

    Mirrors the future C# contract `OcrContextHints` that RDY-019e's
    `CuratorMetadata` will satisfy. Keep fields small and stable; extensions
    belong in a tagged union, not new top-level fields.
    """
    subject: str | None = None           # "math", "physics", …
    language: Language | None = None     # primary language hint
    track: Track | None = None           # Bagrut difficulty tier
    source_type: SourceType | None = None
    taxonomy_node: str | None = None     # e.g. "algebra.quadratics.discriminant"
    expected_figures: bool | None = None

    def as_dict(self) -> dict[str, Any]:
        return {
            "subject": self.subject,
            "language": self.language.value if self.language else None,
            "track": self.track.value if self.track else None,
            "source_type": self.source_type.value if self.source_type else None,
            "taxonomy_node": self.taxonomy_node,
            "expected_figures": self.expected_figures,
        }


# ── Recognition results ─────────────────────────────────────────────────────
@dataclass
class BoundingBox:
    x: float
    y: float
    w: float
    h: float
    page: int = 1   # 1-indexed for humans; 0 = unknown


@dataclass
class TextBlock:
    text: str
    bbox: BoundingBox | None
    language: Language = Language.UNKNOWN
    confidence: float = 0.0        # [0, 1]
    is_rtl: bool = False


@dataclass
class MathBlock:
    latex: str
    bbox: BoundingBox | None
    confidence: float = 0.0
    sympy_parsed: bool = False     # did our canonical parser accept it?
    canonical_form: str | None = None


@dataclass
class FigureRef:
    bbox: BoundingBox
    kind: str = "figure"           # "figure" | "diagram" | "table" | "plot"
    cropped_path: Path | None = None
    caption: str | None = None


@dataclass
class RecognitionResult:
    """Normalised output for every runner."""
    runner_name: str
    input_path: Path
    text_blocks: list[TextBlock] = field(default_factory=list)
    math_blocks: list[MathBlock] = field(default_factory=list)
    figures: list[FigureRef] = field(default_factory=list)

    overall_confidence: float = 0.0
    latency_seconds: float = 0.0
    errors: list[str] = field(default_factory=list)
    raw_output: dict[str, Any] = field(default_factory=dict)

    # Derived convenience
    @property
    def full_text(self) -> str:
        return "\n".join(b.text for b in self.text_blocks)

    @property
    def all_latex(self) -> list[str]:
        return [m.latex for m in self.math_blocks]

    def to_dict(self) -> dict[str, Any]:
        return {
            "runner": self.runner_name,
            "input": str(self.input_path),
            "text_blocks": [
                {
                    "text": b.text,
                    "bbox": _bbox_dict(b.bbox),
                    "language": b.language.value,
                    "confidence": b.confidence,
                    "is_rtl": b.is_rtl,
                }
                for b in self.text_blocks
            ],
            "math_blocks": [
                {
                    "latex": m.latex,
                    "bbox": _bbox_dict(m.bbox),
                    "confidence": m.confidence,
                    "sympy_parsed": m.sympy_parsed,
                    "canonical_form": m.canonical_form,
                }
                for m in self.math_blocks
            ],
            "figures": [
                {
                    "bbox": _bbox_dict(f.bbox),
                    "kind": f.kind,
                    "cropped_path": str(f.cropped_path) if f.cropped_path else None,
                    "caption": f.caption,
                }
                for f in self.figures
            ],
            "overall_confidence": self.overall_confidence,
            "latency_seconds": self.latency_seconds,
            "errors": self.errors,
        }


def _bbox_dict(b: BoundingBox | None) -> dict[str, float] | None:
    if b is None:
        return None
    return {"x": b.x, "y": b.y, "w": b.w, "h": b.h, "page": b.page}


# ── Runner protocol ─────────────────────────────────────────────────────────
class RunnerUnavailable(RuntimeError):
    """Raised from `Runner.setup` when deps/models aren't available.

    The benchmark harness catches this and records the runner as `skipped`
    in results.json with the exception message as the reason, so partial
    installs still produce a useful comparison table.
    """


@runtime_checkable
class Runner(Protocol):
    """Every OCR tool wrapper implements this."""
    name: str
    supports_math: bool
    supports_layout: bool
    supports_hebrew: bool
    requires_gpu: bool
    setup_friction: int  # 0..3; author's manual score

    def setup(self, cache_dir: Path) -> None:
        """Download models, verify binaries, warm caches.

        Must be idempotent. Raise `RunnerUnavailable` with a human-readable
        reason if the tool cannot run on the current machine.
        """

    def recognize(
        self,
        input_path: Path,
        hints: OcrContextHints | None = None,
    ) -> RecognitionResult:
        """Run OCR on a single file (image or PDF)."""
