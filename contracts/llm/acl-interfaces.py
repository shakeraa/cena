"""
Cena Platform — LLM Anti-Corruption Layer: Interfaces & Domain Models
Layer: LLM ACL | Runtime: Python 3.12+ / FastAPI | Models: Kimi K2.5, Sonnet 4.6, Opus 4.6

This module defines the contract between the Cena domain layer and external LLM providers.
All LLM interactions flow through these typed interfaces. PII-annotated fields are stripped
before routing to non-trusted providers (Kimi).
"""

from __future__ import annotations

import uuid
from abc import ABC, abstractmethod
from datetime import datetime, timezone
from enum import StrEnum
from typing import Annotated, Any, Literal

from pydantic import BaseModel, Field, SecretStr, field_validator


# ═══════════════════════════════════════════════════════════════════════
# 1. ENUMS
# ═══════════════════════════════════════════════════════════════════════


class TaskType(StrEnum):
    """Every LLM-powered task in the Cena platform."""
    SOCRATIC_QUESTION = "socratic_question"
    ANSWER_EVALUATION = "answer_evaluation"
    ERROR_CLASSIFICATION = "error_classification"
    METHODOLOGY_SWITCH = "methodology_switch"
    CONTENT_FILTER = "content_filter"
    DIAGRAM_GENERATION = "diagram_generation"
    FEYNMAN_EXPLANATION = "feynman_explanation"
    STAGNATION_ANALYSIS = "stagnation_analysis"
    VIDEO_SCRIPT = "video_script"
    KNOWLEDGE_GRAPH_EXTRACTION = "knowledge_graph_extraction"


class ModelTier(StrEnum):
    """Model tier aligned with cost/capability tradeoffs (ADR-026 / routing-config.yaml)."""
    KIMI_FAST = "kimi_fast"             # Kimi K2 Turbo — lowest latency, content filtering
    KIMI_STANDARD = "kimi_standard"     # Kimi K2 0905 — classification, structured extraction
    KIMI_ADVANCED = "kimi_advanced"     # Kimi K2.5 — diagrams, long-context extraction
    SONNET = "sonnet"                   # Claude Sonnet 4.6 — tutoring, explanations
    OPUS = "opus"                       # Claude Opus 4.6 — multi-factor reasoning


class ErrorType(StrEnum):
    """Error taxonomy for student answer classification."""
    PROCEDURAL = "procedural"           # Correct approach, execution mistake
    CONCEPTUAL = "conceptual"           # Misunderstanding of the concept
    MOTIVATIONAL = "motivational"       # Disengagement / random answer
    NOTATION = "notation"               # Correct thinking, wrong notation
    NONE = "none"                       # No error — correct answer


class DiagramType(StrEnum):
    """Supported mathematical diagram types."""
    FUNCTION_GRAPH = "function_graph"
    NUMBER_LINE = "number_line"
    GEOMETRIC_FIGURE = "geometric_figure"
    TREE_DIAGRAM = "tree_diagram"
    VENN_DIAGRAM = "venn_diagram"
    COORDINATE_PLANE = "coordinate_plane"
    UNIT_CIRCLE = "unit_circle"


class SafetyVerdict(StrEnum):
    """Content safety classification result."""
    SAFE = "safe"
    NEEDS_REVIEW = "needs_review"
    BLOCKED = "blocked"


class MethodologyId(StrEnum):
    """Multi-Competency Model (MCM) methodology identifiers."""
    SOCRATIC = "socratic"
    SPACED_REPETITION = "spaced_repetition"
    FEYNMAN = "feynman"
    WORKED_EXAMPLES = "worked_examples"
    SCAFFOLDED_PRACTICE = "scaffolded_practice"
    VISUAL_SPATIAL = "visual_spatial"
    GAMIFIED_DRILL = "gamified_drill"
    PEER_EXPLANATION = "peer_explanation"


# ═══════════════════════════════════════════════════════════════════════
# 2. PII ANNOTATION MARKER
# ═══════════════════════════════════════════════════════════════════════

# Fields annotated with PII are stripped before sending to Kimi (non-trusted provider).
# The ACL middleware inspects field metadata and replaces PII values with anonymized tokens.
PII = Annotated[str, Field(json_schema_extra={"pii": True})]


# ═══════════════════════════════════════════════════════════════════════
# 3. SHARED CONTEXT MODELS
# ═══════════════════════════════════════════════════════════════════════


class StudentContext(BaseModel):
    """Anonymized student profile passed to LLM calls.
    PII fields are stripped when routing to non-trusted providers.
    """
    student_id: PII = Field(..., description="Student UUID — PII, stripped for Kimi")
    grade_level: int = Field(..., ge=1, le=12, description="Israeli school grade (1-12)")
    mastery_map: dict[str, float] = Field(
        default_factory=dict,
        description="Concept ID -> P(known) from BKT model, 0.0-1.0",
    )
    active_methodology: MethodologyId = Field(
        default=MethodologyId.SOCRATIC,
        description="Currently active MCM methodology for this concept",
    )
    methodology_history: list[str] = Field(
        default_factory=list,
        description="Ordered list of previously attempted methodologies for current concept cluster",
    )
    session_count: int = Field(default=0, ge=0)
    current_streak: int = Field(default=0, ge=0)
    baseline_accuracy: float = Field(
        default=0.5, ge=0.0, le=1.0,
        description="Trailing 20-question median accuracy",
    )
    baseline_response_time_ms: float = Field(
        default=15000.0, ge=0.0,
        description="Trailing 20-question median response time in ms",
    )
    experiment_cohort: str | None = Field(default=None, description="A/B test cohort tag")
    preferred_language: str = Field(default="he", description="ISO 639-1 language code")


class DialogueTurn(BaseModel):
    """A single turn in a Socratic dialogue."""
    role: Literal["tutor", "student"] = Field(..., description="Who spoke")
    content: str = Field(..., min_length=1, max_length=4000)
    timestamp: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))


class ConceptInfo(BaseModel):
    """Concept metadata from the knowledge graph."""
    concept_id: str = Field(..., min_length=1)
    concept_name_he: str = Field(..., description="Hebrew display name (e.g., 'משוואות ריבועיות')")
    concept_name_en: str = Field(..., description="English display name for logs")
    prerequisite_ids: list[str] = Field(default_factory=list)
    difficulty_level: Literal["recall", "comprehension", "application", "analysis"] = "comprehension"
    bagrut_topic: str | None = Field(default=None, description="Bagrut exam topic tag")


class RubricCriterion(BaseModel):
    """A single criterion in an evaluation rubric."""
    criterion_id: str
    description_he: str
    max_points: float = Field(..., gt=0)
    partial_credit_allowed: bool = True
    keywords: list[str] = Field(
        default_factory=list,
        description="Expected keywords/terms in a correct answer",
    )


# ═══════════════════════════════════════════════════════════════════════
# 4. REQUEST / RESPONSE MODELS
# ═══════════════════════════════════════════════════════════════════════


# ── 4.1 Socratic Question ──


class SocraticQuestionRequest(BaseModel):
    """Generate the next Socratic question in a tutoring dialogue.

    Routed to: Claude Sonnet 4.6
    Prompt caching: system prompt + student context cached (60%+ hit rate target).
    """
    request_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    student: StudentContext
    concept: ConceptInfo
    dialogue_history: list[DialogueTurn] = Field(
        default_factory=list,
        max_length=50,
        description="Recent dialogue turns (capped at 50 for context window management)",
    )
    current_mastery: float = Field(..., ge=0.0, le=1.0, description="P(known) for this concept")
    hint_level: int = Field(
        default=0, ge=0, le=3,
        description="0=no hint, 1=nudge, 2=scaffolded, 3=near-answer",
    )
    max_output_tokens: int = Field(default=500, ge=50, le=2000)

    @field_validator("dialogue_history")
    @classmethod
    def validate_dialogue_alternation(cls, v: list[DialogueTurn]) -> list[DialogueTurn]:
        """Ensure dialogue alternates between tutor and student (soft validation)."""
        if len(v) >= 2:
            for i in range(1, len(v)):
                if v[i].role == v[i - 1].role:
                    # Allow consecutive same-role turns but log warning in production
                    pass
        return v


class SocraticQuestionResponse(BaseModel):
    """Response from Socratic question generation."""
    request_id: str
    question_he: str = Field(..., description="Generated Socratic question in Hebrew")
    question_type: Literal["guiding", "probing", "clarifying", "challenge"] = Field(
        ..., description="Pedagogical intent of the question",
    )
    scaffolding_level: int = Field(
        ..., ge=1, le=5,
        description="1=minimal scaffolding (discovery), 5=heavy scaffolding (near-direct instruction)",
    )
    expected_concepts: list[str] = Field(
        default_factory=list,
        description="Concept IDs that a correct answer would demonstrate understanding of",
    )
    model_used: ModelTier
    input_tokens: int = Field(..., ge=0)
    output_tokens: int = Field(..., ge=0)
    latency_ms: int = Field(..., ge=0)
    cached: bool = Field(default=False, description="Whether prompt cache was hit")


# ── 4.2 Answer Evaluation ──


class AnswerEvaluationRequest(BaseModel):
    """Evaluate a student's free-text answer against a rubric.

    Routed to: Claude Sonnet 4.6
    Supports partial credit, Hebrew mathematical notation.
    """
    request_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    student: StudentContext
    concept: ConceptInfo
    question_text_he: str = Field(..., min_length=1, description="The question asked, in Hebrew")
    student_answer_he: str = Field(..., min_length=1, description="Student's free-text answer in Hebrew")
    expected_answer_he: str = Field(..., description="Reference correct answer in Hebrew")
    rubric: list[RubricCriterion] = Field(..., min_length=1, description="Evaluation rubric criteria")
    dialogue_context: list[DialogueTurn] = Field(
        default_factory=list,
        max_length=10,
        description="Recent dialogue for context (last 10 turns max)",
    )
    max_output_tokens: int = Field(default=800, ge=50, le=2000)


class CriterionScore(BaseModel):
    """Score for a single rubric criterion."""
    criterion_id: str
    score: float = Field(..., ge=0.0)
    max_score: float = Field(..., gt=0.0)
    feedback_he: str = Field(..., description="Criterion-level feedback in Hebrew")
    evidence: str = Field(
        default="",
        description="Quoted text from student answer supporting this score",
    )


class AnswerEvaluationResponse(BaseModel):
    """Structured evaluation of a student answer."""
    request_id: str
    is_correct: bool = Field(..., description="True if total score >= 80% of max")
    total_score: float = Field(..., ge=0.0)
    max_score: float = Field(..., gt=0.0)
    score_percentage: float = Field(..., ge=0.0, le=1.0)
    criterion_scores: list[CriterionScore]
    overall_feedback_he: str = Field(..., description="Synthesized feedback in Hebrew")
    error_type: ErrorType = Field(
        ..., description="Dominant error type if answer is incorrect",
    )
    partial_credit_awarded: bool = False
    model_used: ModelTier
    input_tokens: int = Field(..., ge=0)
    output_tokens: int = Field(..., ge=0)
    latency_ms: int = Field(..., ge=0)


# ── 4.3 Error Classification ──


class AttemptData(BaseModel):
    """Raw attempt data for error classification."""
    question_id: str
    question_type: str
    student_answer: str
    expected_answer: str
    response_time_ms: int = Field(..., ge=0)
    hint_count_used: int = Field(default=0, ge=0)
    backspace_count: int = Field(default=0, ge=0)
    answer_change_count: int = Field(default=0, ge=0)
    was_skipped: bool = False


class ErrorClassificationRequest(BaseModel):
    """Classify the type of error in a student's incorrect answer.

    Routed to: Kimi K2 0905 (structured extraction, 6.7x cheaper than Sonnet).
    PII is stripped before sending — only anonymized attempt data is transmitted.
    """
    request_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    student_id: PII = Field(..., description="Stripped before Kimi — replaced with anon token")
    concept: ConceptInfo
    attempt: AttemptData
    recent_error_history: list[ErrorType] = Field(
        default_factory=list,
        max_length=20,
        description="Last 20 error types for pattern detection",
    )
    max_output_tokens: int = Field(default=200, ge=50, le=500)


class ErrorClassificationResponse(BaseModel):
    """Structured error classification result."""
    request_id: str
    primary_error_type: ErrorType
    secondary_error_type: ErrorType | None = None
    confidence: float = Field(..., ge=0.0, le=1.0)
    error_description_he: str = Field(..., description="Brief Hebrew explanation of the error")
    is_repeated_pattern: bool = Field(
        default=False,
        description="True if same error type appeared 3+ times in recent history",
    )
    suggested_intervention: str = Field(
        default="",
        description="Brief suggestion: 'hint', 'different_approach', 'easier_problem', 'break'",
    )
    model_used: ModelTier
    input_tokens: int = Field(..., ge=0)
    output_tokens: int = Field(..., ge=0)
    latency_ms: int = Field(..., ge=0)


# ── 4.4 Methodology Switch ──


class StagnationData(BaseModel):
    """Composite stagnation signal from the detection engine."""
    composite_score: float = Field(..., ge=0.0, le=1.0, description="Overall stagnation score")
    accuracy_plateau: float = Field(..., ge=0.0, le=1.0)
    response_time_drift: float = Field(..., ge=-1.0, le=1.0, description="Positive = slowing down")
    session_abandonment: float = Field(..., ge=0.0, le=1.0)
    error_repetition: float = Field(..., ge=0.0, le=1.0)
    annotation_sentiment: float = Field(..., ge=0.0, le=1.0, description="0=negative, 1=positive")
    consecutive_stagnant_sessions: int = Field(..., ge=0)
    dominant_error_type: ErrorType


class MCMCandidate(BaseModel):
    """A candidate methodology for the MCM switch decision."""
    methodology_id: MethodologyId
    predicted_effectiveness: float = Field(
        ..., ge=0.0, le=1.0,
        description="MCM model's predicted effectiveness for this student+concept",
    )
    times_attempted: int = Field(default=0, ge=0)
    last_outcome: Literal["improved", "neutral", "declined", "never_tried"] = "never_tried"
    rationale: str = Field(default="", description="Why this methodology might work")


class MethodologySwitchRequest(BaseModel):
    """Decide whether and which methodology to switch to.

    Routed to: Claude Opus 4.6 — this is the highest-stakes reasoning task.
    Multi-factor decision: student history, learning style, stagnation signals, MCM predictions.
    Infrequent (2/day) but high-impact.
    """
    request_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    student: StudentContext
    concept: ConceptInfo
    stagnation: StagnationData
    current_methodology: MethodologyId
    candidates: list[MCMCandidate] = Field(
        ..., min_length=1, max_length=8,
        description="MCM-ranked candidate methodologies",
    )
    session_history_summary: str = Field(
        ...,
        description="Structured summary of last 5 sessions (accuracy, time, errors, methodology used)",
    )
    max_output_tokens: int = Field(default=800, ge=100, le=2000)


class MethodologySwitchResponse(BaseModel):
    """Decision output from methodology switch reasoning."""
    request_id: str
    should_switch: bool = Field(..., description="Whether a switch is recommended")
    recommended_methodology: MethodologyId | None = Field(
        None, description="None if should_switch=False",
    )
    confidence: float = Field(..., ge=0.0, le=1.0)
    reasoning_he: str = Field(
        ..., description="Hebrew explanation of reasoning (shown to teacher dashboard)",
    )
    reasoning_en: str = Field(
        ..., description="English reasoning for analytics/logging",
    )
    expected_improvement: float = Field(
        ..., ge=-1.0, le=1.0,
        description="Predicted mastery improvement (negative = expected decline, positive = improvement)",
    )
    risk_factors: list[str] = Field(
        default_factory=list,
        description="Identified risks of switching (e.g., 'student frustrated with changes')",
    )
    fallback_methodology: MethodologyId | None = Field(
        None, description="Second-best option if recommended fails",
    )
    model_used: ModelTier
    input_tokens: int = Field(..., ge=0)
    output_tokens: int = Field(..., ge=0)
    latency_ms: int = Field(..., ge=0)


# ── 4.5 Content Filter ──


class ContentFilterRequest(BaseModel):
    """Check content for safety before presenting to students.

    Routed to: Kimi K2 Turbo (lowest latency gate, binary classification).
    Must handle Hebrew-specific safety concerns.
    """
    request_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    text: str = Field(..., min_length=1, max_length=10000)
    source: Literal["llm_output", "student_input", "teacher_content", "generated_diagram"]
    language: str = Field(default="he", description="ISO 639-1 code")
    context: Literal[
        "tutoring_dialogue", "exercise_content", "feedback", "explanation", "free_text"
    ] = "tutoring_dialogue"
    max_output_tokens: int = Field(default=50, ge=20, le=200)


class ContentFilterResponse(BaseModel):
    """Content safety classification result."""
    request_id: str
    verdict: SafetyVerdict
    flagged_categories: list[str] = Field(
        default_factory=list,
        description="E.g., 'inappropriate_content', 'off_topic', 'hate_speech', 'self_harm'",
    )
    confidence: float = Field(..., ge=0.0, le=1.0)
    sanitized_text: str | None = Field(
        None,
        description="Sanitized version if verdict=needs_review; None if safe or blocked",
    )
    model_used: ModelTier
    input_tokens: int = Field(..., ge=0)
    output_tokens: int = Field(..., ge=0)
    latency_ms: int = Field(..., ge=0)


# ── 4.6 Diagram Generation ──


class DiagramGenerationRequest(BaseModel):
    """Generate an SVG diagram for a mathematical concept.

    Routed to: Kimi K2.5 (structured SVG output, 6.7x cheaper than Sonnet).
    """
    request_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    concept: ConceptInfo
    diagram_type: DiagramType
    parameters: dict[str, Any] = Field(
        default_factory=dict,
        description="Diagram-specific params (e.g., function expression, domain, range)",
    )
    labels_language: str = Field(default="he", description="Label language for axis/annotations")
    width: int = Field(default=600, ge=200, le=1200)
    height: int = Field(default=400, ge=200, le=800)
    style: Literal["clean", "colorful", "accessible"] = Field(
        default="accessible",
        description="Visual style; 'accessible' ensures high contrast + screen reader hints",
    )
    max_output_tokens: int = Field(default=1500, ge=200, le=4000)


class DiagramGenerationResponse(BaseModel):
    """Generated SVG diagram."""
    request_id: str
    svg_content: str = Field(..., description="Complete SVG markup")
    svg_size_bytes: int = Field(..., ge=0)
    alt_text_he: str = Field(..., description="Hebrew alt text for accessibility")
    alt_text_en: str = Field(default="", description="English alt text for logging")
    is_valid_svg: bool = Field(
        ..., description="Whether SVG passes basic XML validation",
    )
    model_used: ModelTier
    input_tokens: int = Field(..., ge=0)
    output_tokens: int = Field(..., ge=0)
    latency_ms: int = Field(..., ge=0)


# ── 4.7 Feynman Explanation (student explains back) ──


class FeynmanExplanationRequest(BaseModel):
    """Grade a student's Feynman-style explanation of a concept.

    The student attempts to explain a concept in their own words.
    The LLM evaluates completeness, accuracy, and depth.
    Routed to: Claude Sonnet 4.6.
    """
    request_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    student: StudentContext
    concept: ConceptInfo
    student_explanation_he: str = Field(
        ..., min_length=10, max_length=5000,
        description="Student's Hebrew explanation of the concept",
    )
    target_audience: Literal["classmate", "younger_student", "teacher"] = Field(
        default="classmate",
        description="Who the student is explaining to (affects expected depth)",
    )
    max_output_tokens: int = Field(default=600, ge=100, le=1500)


class FeynmanExplanationResponse(BaseModel):
    """Evaluation of a Feynman explanation attempt."""
    request_id: str
    completeness_score: float = Field(
        ..., ge=0.0, le=1.0,
        description="How much of the concept was covered",
    )
    accuracy_score: float = Field(
        ..., ge=0.0, le=1.0,
        description="Factual/mathematical correctness",
    )
    clarity_score: float = Field(
        ..., ge=0.0, le=1.0,
        description="How clear and understandable the explanation is",
    )
    depth_score: float = Field(
        ..., ge=0.0, le=1.0,
        description="Level of conceptual depth vs surface recitation",
    )
    overall_score: float = Field(
        ..., ge=0.0, le=1.0,
        description="Weighted average: completeness(0.3) + accuracy(0.3) + clarity(0.2) + depth(0.2)",
    )
    gaps_identified: list[str] = Field(
        default_factory=list,
        description="Concept sub-topics the student missed or got wrong",
    )
    feedback_he: str = Field(..., description="Hebrew feedback for the student")
    demonstrates_mastery: bool = Field(
        ..., description="True if overall_score >= 0.75",
    )
    model_used: ModelTier
    input_tokens: int = Field(..., ge=0)
    output_tokens: int = Field(..., ge=0)
    latency_ms: int = Field(..., ge=0)


# ═══════════════════════════════════════════════════════════════════════
# 5. TOKEN BUDGET & COST TRACKING MODELS
# ═══════════════════════════════════════════════════════════════════════


class TokenBudget(BaseModel):
    """Per-student daily token budget.

    Hard cutoff at daily_limit to prevent runaway costs.
    Aligned with routing-config.yaml: 25,000 output tokens/day default.
    """
    student_id: str
    daily_limit_output_tokens: int = Field(
        default=25_000,
        description="Max output tokens per student per day",
    )
    used_input_tokens: int = Field(default=0, ge=0)
    used_output_tokens: int = Field(default=0, ge=0)
    remaining_output_tokens: int = Field(default=25_000, ge=0)
    reset_at: datetime = Field(
        ..., description="UTC timestamp when budget resets (midnight IL time)",
    )
    is_exhausted: bool = Field(default=False, description="True when remaining <= 0")
    warning_threshold_pct: float = Field(
        default=0.80,
        description="Alert when usage exceeds this percentage of daily limit",
    )

    @property
    def usage_percentage(self) -> float:
        if self.daily_limit_output_tokens == 0:
            return 1.0
        return self.used_output_tokens / self.daily_limit_output_tokens


class CostRecord(BaseModel):
    """A single cost record for one LLM invocation."""
    request_id: str
    student_id: str
    task_type: TaskType
    model_tier: ModelTier
    input_tokens: int = Field(..., ge=0)
    output_tokens: int = Field(..., ge=0)
    input_cost_usd: float = Field(..., ge=0.0)
    output_cost_usd: float = Field(..., ge=0.0)
    total_cost_usd: float = Field(..., ge=0.0)
    cached: bool = False
    timestamp: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))


class CostAggregation(BaseModel):
    """Aggregated cost view (per-student, per-model, or per-task)."""
    dimension: Literal["student", "model", "task"]
    dimension_value: str = Field(..., description="student_id, model_tier name, or task_type name")
    period_start: datetime
    period_end: datetime
    total_requests: int = Field(default=0, ge=0)
    total_input_tokens: int = Field(default=0, ge=0)
    total_output_tokens: int = Field(default=0, ge=0)
    total_cost_usd: float = Field(default=0.0, ge=0.0)
    avg_cost_per_request_usd: float = Field(default=0.0, ge=0.0)
    avg_latency_ms: float = Field(default=0.0, ge=0.0)
    cache_hit_rate: float = Field(default=0.0, ge=0.0, le=1.0)


# ═══════════════════════════════════════════════════════════════════════
# 6. MODEL CONFIGURATION
# ═══════════════════════════════════════════════════════════════════════


class ModelConfig(BaseModel):
    """Configuration for a specific model endpoint, returned by the Router."""
    model_tier: ModelTier
    model_id: str = Field(
        ..., description="Provider model ID, e.g., 'claude-sonnet-4-6-20260215'",
    )
    api_base_url: str
    max_tokens: int = Field(..., gt=0)
    temperature: float = Field(default=0.3, ge=0.0, le=2.0)
    timeout_seconds: float = Field(default=30.0, gt=0.0)
    retry_count: int = Field(default=2, ge=0, le=5)
    cache_ttl_seconds: int = Field(
        default=300,
        description="Prompt cache TTL; 300s for Anthropic 5-min cache tier",
    )
    cost_per_input_mtok: float = Field(..., ge=0.0, description="$/MTok for input")
    cost_per_output_mtok: float = Field(..., ge=0.0, description="$/MTok for output")
    cost_per_cached_input_mtok: float = Field(..., ge=0.0, description="$/MTok for cached input")
    is_trusted_provider: bool = Field(
        ..., description="If False, PII fields are stripped before sending",
    )


# ═══════════════════════════════════════════════════════════════════════
# 7. ABSTRACT INTERFACES
# ═══════════════════════════════════════════════════════════════════════


class LLMRouter(ABC):
    """Routes task types to model configurations with fallback chain support.

    The router inspects the task type and optional context to select the optimal
    model tier. It returns a ModelConfig that the ACL middleware uses to construct
    the API call.
    """

    @abstractmethod
    def route(self, task_type: TaskType, student: StudentContext | None = None) -> ModelConfig:
        """Select the optimal model for a given task.

        Args:
            task_type: The type of LLM task to route.
            student: Optional student context for adaptive routing
                     (e.g., escalate to Opus if student is struggling).

        Returns:
            ModelConfig with full endpoint configuration.

        Raises:
            BudgetExhaustedError: If student's daily budget is exhausted.
            CircuitOpenError: If all models in the fallback chain are circuit-broken.
        """
        ...

    @abstractmethod
    def get_fallback_chain(self, task_type: TaskType) -> list[ModelConfig]:
        """Return the ordered fallback chain for a task type.

        Returns:
            List of ModelConfig in priority order. First is primary, rest are fallbacks.
        """
        ...

    @abstractmethod
    def report_failure(self, model_tier: ModelTier, error_code: int) -> None:
        """Report a model failure for circuit breaker tracking.

        Args:
            model_tier: Which model failed.
            error_code: HTTP status code or internal error code.
        """
        ...

    @abstractmethod
    def get_circuit_state(self, model_tier: ModelTier) -> Literal["closed", "open", "half_open"]:
        """Get the current circuit breaker state for a model."""
        ...


class PIIStripper(ABC):
    """Strips PII-annotated fields from requests before sending to non-trusted providers."""

    @abstractmethod
    def strip(self, request: BaseModel, anonymization_map: dict[str, str] | None = None) -> BaseModel:
        """Strip PII fields and replace with anonymized tokens.

        Args:
            request: Any Pydantic request model.
            anonymization_map: Optional mapping of original -> anonymized values
                               for consistent anonymization within a session.

        Returns:
            A copy of the request with PII fields replaced.
        """
        ...

    @abstractmethod
    def restore(self, response: BaseModel, anonymization_map: dict[str, str]) -> BaseModel:
        """Restore anonymized tokens in response back to original values.

        Args:
            response: Response model that may contain anonymized references.
            anonymization_map: Mapping of anonymized -> original values.

        Returns:
            Response with anonymized tokens replaced back to original values.
        """
        ...


class LLMGateway(ABC):
    """Unified gateway for all LLM interactions.

    Orchestrates: routing -> PII stripping -> API call -> response parsing -> cost tracking.
    """

    @abstractmethod
    async def socratic_question(self, request: SocraticQuestionRequest) -> SocraticQuestionResponse:
        """Generate a Socratic tutoring question."""
        ...

    @abstractmethod
    async def evaluate_answer(self, request: AnswerEvaluationRequest) -> AnswerEvaluationResponse:
        """Evaluate a student's free-text answer."""
        ...

    @abstractmethod
    async def classify_error(self, request: ErrorClassificationRequest) -> ErrorClassificationResponse:
        """Classify the error type in an incorrect answer."""
        ...

    @abstractmethod
    async def decide_methodology_switch(
        self, request: MethodologySwitchRequest
    ) -> MethodologySwitchResponse:
        """Decide whether to switch learning methodology."""
        ...

    @abstractmethod
    async def filter_content(self, request: ContentFilterRequest) -> ContentFilterResponse:
        """Check content safety."""
        ...

    @abstractmethod
    async def generate_diagram(self, request: DiagramGenerationRequest) -> DiagramGenerationResponse:
        """Generate an SVG diagram."""
        ...

    @abstractmethod
    async def evaluate_feynman(
        self, request: FeynmanExplanationRequest
    ) -> FeynmanExplanationResponse:
        """Evaluate a student's Feynman explanation."""
        ...


# ═══════════════════════════════════════════════════════════════════════
# 8. CUSTOM EXCEPTIONS
# ═══════════════════════════════════════════════════════════════════════


class LLMACLError(Exception):
    """Base exception for all LLM ACL errors."""

    def __init__(self, message: str, request_id: str | None = None):
        self.request_id = request_id
        super().__init__(message)


class BudgetExhaustedError(LLMACLError):
    """Raised when a student's daily token budget is exhausted."""

    def __init__(self, student_id: str, used: int, limit: int, reset_at: datetime):
        self.student_id = student_id
        self.used = used
        self.limit = limit
        self.reset_at = reset_at
        super().__init__(
            f"Budget exhausted for student {student_id}: {used}/{limit} tokens. "
            f"Resets at {reset_at.isoformat()}"
        )


class CircuitOpenError(LLMACLError):
    """Raised when all models in a fallback chain have open circuits."""

    def __init__(self, task_type: TaskType, model_tiers: list[ModelTier]):
        self.task_type = task_type
        self.model_tiers = model_tiers
        super().__init__(
            f"All circuits open for task {task_type}: {[str(m) for m in model_tiers]}"
        )


class HebrewQualityGateError(LLMACLError):
    """Raised when LLM output fails the Hebrew quality minimum threshold."""

    def __init__(self, score: float, threshold: float, task_type: TaskType):
        self.score = score
        self.threshold = threshold
        self.task_type = task_type
        super().__init__(
            f"Hebrew quality score {score:.2f} below threshold {threshold:.2f} "
            f"for task {task_type}"
        )


class PromptRenderError(LLMACLError):
    """Raised when a prompt template fails to render."""

    def __init__(self, template_name: str, error: str):
        self.template_name = template_name
        super().__init__(f"Failed to render prompt template '{template_name}': {error}")
