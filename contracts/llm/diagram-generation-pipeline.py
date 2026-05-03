"""
═══════════════════════════════════════════════════════════════════════
Cena Platform — Diagram Generation & Caching Pipeline
Layer: LLM | Runtime: Python FastAPI | Batch: Overnight Kimi K2.5

Generates interactive SVG diagrams for every concept in the curriculum.
Diagrams are pre-generated (not real-time), cached on S3/CDN, and served
instantly during learning sessions. Inspired by SmartyMe/FigureLabs style.
═══════════════════════════════════════════════════════════════════════
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Optional

from pydantic import BaseModel, Field


# ─────────────────────────────────────────────────────────────────────
# 1. DIAGRAM TYPES & GENERATION CONFIG
# ─────────────────────────────────────────────────────────────────────

class DiagramType(str, Enum):
    """Maps to Kimi K2.5 generation prompt templates."""
    FUNCTION_PLOT = "function_plot"       # Math: graphs, coordinate geometry
    CIRCUIT = "circuit"                    # Physics: series/parallel/logic gates
    GEOMETRY = "geometry"                  # Math: constructions, proofs
    MOLECULAR = "molecular"               # Chemistry: bonds, structures
    BIOLOGICAL = "biological"             # Biology: cells, organs
    FLOWCHART = "flowchart"               # CS: algorithms, data structures
    PHYSICS_VECTOR = "physics_vector"     # Physics: forces, waves, motion
    WORKED_EXAMPLE = "worked_example"     # All: step-by-step with fading
    CHALLENGE_CARD = "challenge_card"     # All: SmartyMe-style game card


class DiagramFormat(str, Enum):
    SVG = "svg"               # Interactive, tappable hotspots
    PNG = "png"               # Fallback for complex renders
    RIVE = "rive"             # Animated (circuit flow, wave motion)
    REMOTION_JSON = "remotion" # Video generation input


class BloomLevel(str, Enum):
    RECALL = "recall"                     # Label diagram, identify parts
    COMPREHENSION = "comprehension"       # Explain what the diagram shows
    APPLICATION = "application"           # Use the diagram to solve a problem
    ANALYSIS = "analysis"                 # Compare, contrast, predict from diagram


# ─────────────────────────────────────────────────────────────────────
# 2. GENERATION REQUEST (input to Kimi K2.5 batch pipeline)
# ─────────────────────────────────────────────────────────────────────

class DiagramGenerationRequest(BaseModel):
    """One request = one diagram for one concept at one Bloom level."""

    concept_id: str = Field(description="Concept UUID from curriculum graph")
    concept_name_he: str = Field(description="Hebrew concept name for labeling")
    concept_name_en: str = Field(description="English name for Kimi prompt")
    subject: str = Field(description="math | physics | chemistry | biology | cs")
    diagram_type: DiagramType
    bloom_level: BloomLevel
    format: DiagramFormat = DiagramFormat.SVG

    # Context from curriculum graph (injected by pipeline)
    prerequisite_concepts: list[str] = Field(
        default_factory=list,
        description="Names of prerequisite concepts (for context in prompt)"
    )
    difficulty_rating: float = Field(
        ge=0.0, le=1.0,
        description="0.0=easy, 1.0=hard — affects visual complexity"
    )
    bagrut_exam_references: list[str] = Field(
        default_factory=list,
        description="Real Bagrut question IDs this diagram relates to"
    )

    # Styling constraints
    max_svg_size_kb: int = Field(default=50, description="Max SVG file size for inline embedding")
    color_palette: str = Field(
        default="subject_default",
        description="Color palette override or 'subject_default'"
    )
    hebrew_labels: bool = Field(default=True, description="Labels in Hebrew (LTR for formulas)")

    class Config:
        json_schema_extra = {
            "example": {
                "concept_id": "math-calc-chain-rule",
                "concept_name_he": "כלל השרשרת",
                "concept_name_en": "Chain Rule (Derivatives)",
                "subject": "math",
                "diagram_type": "worked_example",
                "bloom_level": "application",
                "format": "svg",
                "difficulty_rating": 0.6,
                "hebrew_labels": True,
            }
        }


# ─────────────────────────────────────────────────────────────────────
# 3. GENERATION RESULT (output from Kimi K2.5)
# ─────────────────────────────────────────────────────────────────────

class Hotspot(BaseModel):
    """Interactive tap target within an SVG diagram."""
    id: str
    svg_element_id: Optional[str] = None
    x: float = Field(ge=0.0, le=1.0, description="Normalized X (0=left, 1=right)")
    y: float = Field(ge=0.0, le=1.0, description="Normalized Y (0=top, 1=bottom)")
    width: float = Field(ge=0.0, le=1.0)
    height: float = Field(ge=0.0, le=1.0)
    label_he: str
    explanation_he: str
    linked_concept_id: Optional[str] = None
    style: str = "outline"  # outline | highlight | numbered | hidden


class DiagramGenerationResult(BaseModel):
    """Output from Kimi K2.5 diagram generation."""

    # Content
    svg_content: Optional[str] = Field(None, description="Raw SVG string (if format=svg)")
    png_base64: Optional[str] = Field(None, description="Base64 PNG (if format=png)")
    rive_bytes: Optional[bytes] = Field(None, description="Rive binary (if format=rive)")

    # Metadata extracted by Kimi
    title_he: str = Field(description="Hebrew title for display")
    description_he: str = Field(description="Hebrew description / challenge prompt")
    formulas: list[str] = Field(default_factory=list, description="LaTeX formulas in diagram")
    hotspots: list[Hotspot] = Field(default_factory=list)

    # Quality self-assessment (Kimi rates its own output)
    confidence: float = Field(
        ge=0.0, le=1.0,
        description="Kimi's self-assessed quality score. >0.95 = auto-approve, <0.7 = reject"
    )
    generation_notes: str = Field(
        default="",
        description="Kimi's notes on the generation (e.g., 'simplified topology for clarity')"
    )

    # Token usage
    input_tokens: int = 0
    output_tokens: int = 0


# ─────────────────────────────────────────────────────────────────────
# 4. CHALLENGE CARD GENERATION (SmartyMe-style)
# ─────────────────────────────────────────────────────────────────────

class ChallengeCardRequest(BaseModel):
    """Generate a game-like challenge card for a concept."""
    concept_id: str
    concept_name_he: str
    subject: str
    diagram: DiagramGenerationResult = Field(description="Pre-generated diagram to embed")
    bloom_level: BloomLevel
    answer_type: str = Field(description="multiple_choice | numeric | expression | drag_label | tap_hotspot")
    difficulty_tier: str = "intermediate"  # beginner | intermediate | advanced | expert


class ChallengeCardResult(BaseModel):
    """Generated challenge card content."""
    question_he: str
    options: list[dict] = Field(
        default_factory=list,
        description="[{id, text_he, is_correct, feedback_he}] for MCQ"
    )
    expected_value: Optional[float] = None
    tolerance: Optional[float] = None
    expected_expression: Optional[str] = None
    hint_he: Optional[str] = None
    xp_reward: int = 10


# ─────────────────────────────────────────────────────────────────────
# 5. BATCH PIPELINE ORCHESTRATOR
# ─────────────────────────────────────────────────────────────────────

class DiagramPipelineConfig(BaseModel):
    """Configuration for the overnight batch generation pipeline."""

    # Batch settings
    max_concurrent_generations: int = Field(default=20, description="Parallel Kimi API calls")
    batch_size: int = Field(default=100, description="Concepts per batch")
    max_retries: int = 3

    # Quality gates
    auto_approve_threshold: float = Field(default=0.95, description="Kimi confidence > this = auto-approve")
    reject_threshold: float = Field(default=0.70, description="Kimi confidence < this = reject, regenerate")
    max_regeneration_attempts: int = 2

    # Cost control
    max_cost_per_run_usd: float = Field(default=50.0, description="Kill switch: stop pipeline if cost exceeds this")
    max_tokens_per_diagram: int = Field(default=4000, description="Output token limit per diagram")

    # Storage
    s3_bucket: str = "cena-diagrams"
    s3_prefix: str = "v1/{subject}/{curriculum_version}/"
    cdn_base_url: str = "https://cdn.cena.app/diagrams/"

    # Schedule
    cron_schedule: str = "0 2 * * *"  # 2:00 AM Israel time, daily
    subjects_to_generate: list[str] = Field(
        default_factory=lambda: ["math"],
        description="Subjects to generate diagrams for (start with math)"
    )


class PipelineRunResult(BaseModel):
    """Result of a batch pipeline run."""
    run_id: str
    started_at: datetime
    completed_at: Optional[datetime] = None
    concepts_processed: int = 0
    diagrams_generated: int = 0
    diagrams_auto_approved: int = 0
    diagrams_pending_review: int = 0
    diagrams_rejected: int = 0
    challenge_cards_generated: int = 0
    total_cost_usd: float = 0.0
    total_input_tokens: int = 0
    total_output_tokens: int = 0
    errors: list[str] = Field(default_factory=list)


# ─────────────────────────────────────────────────────────────────────
# 6. PIPELINE INTERFACE
# ─────────────────────────────────────────────────────────────────────

class DiagramGenerationPipeline(ABC):
    """
    Abstract interface for the overnight diagram generation pipeline.

    Flow:
    1. Load curriculum graph (concepts needing diagrams)
    2. For each concept × bloom_level × diagram_type:
       a. Build DiagramGenerationRequest
       b. Send to Kimi K2.5 (batch API for cost savings)
       c. Validate result (confidence check)
       d. Auto-approve or queue for expert review
       e. Upload to S3, register in CDN
    3. Generate challenge cards for approved diagrams
    4. Emit NATS event: DiagramsPublished (triggers client cache invalidation)
    """

    @abstractmethod
    async def run_full_pipeline(
        self,
        config: DiagramPipelineConfig,
    ) -> PipelineRunResult:
        """Run the complete batch generation pipeline."""
        ...

    @abstractmethod
    async def generate_single_diagram(
        self,
        request: DiagramGenerationRequest,
    ) -> DiagramGenerationResult:
        """Generate a single diagram (for testing / manual generation)."""
        ...

    @abstractmethod
    async def generate_challenge_card(
        self,
        request: ChallengeCardRequest,
    ) -> ChallengeCardResult:
        """Generate a challenge card for an existing diagram."""
        ...

    @abstractmethod
    async def upload_to_cdn(
        self,
        result: DiagramGenerationResult,
        request: DiagramGenerationRequest,
        config: DiagramPipelineConfig,
    ) -> str:
        """Upload diagram to S3/CDN, return CDN URL."""
        ...

    @abstractmethod
    async def invalidate_client_caches(
        self,
        concept_ids: list[str],
    ) -> None:
        """Emit NATS event to invalidate client-side diagram caches."""
        ...


# ─────────────────────────────────────────────────────────────────────
# 7. KIMI PROMPT TEMPLATE FOR SVG GENERATION
# ─────────────────────────────────────────────────────────────────────

DIAGRAM_GENERATION_SYSTEM_PROMPT = """You are a scientific diagram generator for an Israeli high school STEM learning app.

RULES:
1. Output valid SVG that renders correctly in Flutter's flutter_svg package
2. Use the provided color palette for the subject
3. All text labels MUST be in Hebrew (right-to-left) EXCEPT mathematical formulas which stay LTR
4. Include id attributes on interactive elements (for hotspot mapping)
5. Keep SVG file size under {max_svg_size_kb}KB
6. Use viewBox="0 0 400 300" (4:3 aspect ratio) unless the diagram needs a different ratio
7. Style: clean vector, flat design, 2px stroke width, rounded corners (8px radius)
8. Font: system Hebrew font (Heebo) for labels, monospace for formulas

COLOR PALETTE for {subject}:
- Primary: {primary_color}
- Accent: {accent_color}
- Background: {bg_color}
- Text: {text_color}

CONCEPT: {concept_name_he} ({concept_name_en})
DIFFICULTY: {difficulty_rating}/1.0
BLOOM LEVEL: {bloom_level}
DIAGRAM TYPE: {diagram_type}

Generate the SVG diagram. After the SVG, output a JSON block with:
{{
  "title_he": "Hebrew title",
  "description_he": "Hebrew description",
  "formulas": ["LaTeX formula 1", ...],
  "hotspots": [
    {{"id": "hs1", "svg_element_id": "element-id", "x": 0.3, "y": 0.5, "width": 0.1, "height": 0.1, "label_he": "label", "explanation_he": "explanation"}}
  ],
  "confidence": 0.85
}}"""
