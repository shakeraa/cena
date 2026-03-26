# LLM-005: Prompt Templates with Hebrew+Arabic Glossaries & Locale-Aware Injection

**Priority:** P1 — blocks tutoring quality
**Blocked by:** LLM-001 (FastAPI scaffold), LLM-002 (router)
**Estimated effort:** 3 days
**Contract:** `contracts/llm/prompt-templates.py` (TEMPLATE_REGISTRY, 7 templates, glossaries), `contracts/llm/acl-interfaces.py` (request/response models), `contracts/llm/routing-config.yaml` (section 6: prompt caching)

---

## Context
The prompt templates are the pedagogical brain of the Cena platform. Each template defines the system prompt, output schema, few-shot examples, and Hebrew/Arabic math glossary injection for one of the 7 LLM tasks. Templates are rendered with student context at call time, and the system prompt + student context are cached using Anthropic's prompt caching (5-min TTL for student context, 1-hour TTL for system prompt). The `TEMPLATE_REGISTRY` in `prompt-templates.py` defines 7 templates, each with required variables, output schema, and glossary injection rules.

## Subtasks

### LLM-005.1: Template Engine & Glossary Injection
**Files to create/modify:**
- `src/llm-acl/src/cena_llm/prompts/engine.py` — template rendering engine wrapping `PromptTemplate.render()`
- `src/llm-acl/src/cena_llm/prompts/glossary.py` — glossary loader, locale-aware selection
- `src/llm-acl/src/cena_llm/prompts/__init__.py` — package init

**Acceptance:**
- [ ] Renders all 7 templates from `TEMPLATE_REGISTRY` in `prompt-templates.py`:
  1. `socratic_question` — `SOCRATIC_SYSTEM_PROMPT`, glossary=True, max_output=500
  2. `answer_evaluation` — `ANSWER_EVALUATION_PROMPT`, glossary=True, max_output=800
  3. `error_classification` — `ERROR_CLASSIFICATION_PROMPT`, glossary=False, max_output=200
  4. `methodology_switch` — `METHODOLOGY_SWITCH_PROMPT`, glossary=False, max_output=800
  5. `content_filter` — `CONTENT_SAFETY_PROMPT`, glossary=False, max_output=50
  6. `feynman_explanation` — `FEYNMAN_EXPLANATION_PROMPT`, glossary=True, max_output=600
  7. `diagram_generation` — `DIAGRAM_GENERATION_PROMPT`, glossary=False, max_output=1500
- [ ] Locale-aware glossary injection using `get_math_glossary(locale)` from `prompt-templates.py`:
  - `locale="he"` -> injects `HEBREW_MATH_GLOSSARY` (36 terms from derivatives to matrices)
  - `locale="ar"` -> injects `ARABIC_MATH_GLOSSARY` (30 terms, MSA for Israeli Arab Bagrut students)
  - Glossary injected into `{glossary}` placeholder for templates with `injects_glossary=True`
- [ ] Missing required variables raise `KeyError` listing the missing variable names (matching `PromptTemplate.render()` behavior)
- [ ] Output schema automatically injected into `{output_schema}` placeholder for every template
- [ ] Few-shot examples preserved in rendered output (they use `{{` and `}}` escaping for JSON blocks)

**Test:**
```python
import pytest
from cena_llm.prompts.engine import PromptEngine
from cena_llm.prompts.glossary import get_math_glossary

@pytest.fixture
def engine():
    return PromptEngine()

def test_renders_socratic_with_hebrew_glossary(engine):
    rendered = engine.render("socratic_question",
        grade_level=10,
        concept_name_he="משוואות ריבועיות",
        concept_name_en="Quadratic Equations",
        current_mastery=0.45,
        active_methodology="socratic",
        hint_level=1,
        prerequisites_status="Addition: mastered, Multiplication: mastered",
        dialogue_history="Student: אני לא מבין",
        locale="he",
    )
    assert "משוואה" in rendered  # Hebrew glossary term
    assert "Socratic" in rendered
    assert "question_he" in rendered  # Output schema injected

def test_renders_socratic_with_arabic_glossary(engine):
    rendered = engine.render("socratic_question",
        grade_level=10,
        concept_name_he="معادلة تربيعية",
        concept_name_en="Quadratic Equations",
        current_mastery=0.45,
        active_methodology="socratic",
        hint_level=1,
        prerequisites_status="Addition: mastered",
        dialogue_history="Student: لا أفهم",
        locale="ar",
    )
    assert "معادلة" in rendered  # Arabic glossary term
    assert "مشتقة" in rendered  # Arabic derivative term

def test_error_classification_no_glossary(engine):
    rendered = engine.render("error_classification",
        concept_name_he="שברים",
        concept_name_en="Fractions",
        question_type="numeric",
        expected_answer="7",
        student_answer="5",
        response_time_ms=3000,
        hint_count_used=0,
        backspace_count=2,
        answer_change_count=1,
        was_skipped=False,
        recent_error_history="[procedural, procedural]",
    )
    assert "מילון מונחים" not in rendered  # No glossary for classification
    assert "procedural" in rendered

def test_missing_required_variable_raises(engine):
    with pytest.raises(KeyError, match="grade_level"):
        engine.render("socratic_question",
            concept_name_he="t", concept_name_en="t",
            current_mastery=0.5, active_methodology="socratic",
            hint_level=0, prerequisites_status="",
            dialogue_history="",
        )

def test_all_seven_templates_renderable(engine):
    from contracts.llm.prompt_templates import TEMPLATE_REGISTRY
    assert len(TEMPLATE_REGISTRY) == 7
    for name in TEMPLATE_REGISTRY:
        template = TEMPLATE_REGISTRY[name]
        assert template.name == name
        assert len(template.output_schema) > 10

def test_hebrew_glossary_has_36_terms():
    glossary = get_math_glossary("he")
    lines = [l for l in glossary.split("\n") if l.startswith("|") and "English" not in l and "---" not in l]
    assert len(lines) >= 36

def test_arabic_glossary_has_30_terms():
    glossary = get_math_glossary("ar")
    lines = [l for l in glossary.split("\n") if l.startswith("|") and "English" not in l and "---" not in l]
    assert len(lines) >= 30
```

**Edge cases:**
- Template variable contains `{braces}` -> must not break Python str.format(); use safe rendering
- Locale not "he" or "ar" -> default to "he" with logged WARNING
- Glossary updated (new terms added) -> no code change needed, just update the constant string

---

### LLM-005.2: Anthropic Prompt Caching Integration
**Files to create/modify:**
- `src/llm-acl/src/cena_llm/prompts/cache.py` — Anthropic cache_control block builder
- `src/llm-acl/src/cena_llm/prompts/cache_key.py` — cache key computation

**Acceptance:**
- [ ] Implements prompt caching from `routing-config.yaml` section 6:
  - System prompt cached with `cache_control: {"type": "ephemeral"}` at position 0
  - Student context cached with `cache_control: {"type": "ephemeral"}` at position 1
  - Dialogue history NOT cached (changes every turn)
- [ ] Cache key for system prompt: `sha256(template_name + model_id)` (from routing-config.yaml)
- [ ] Cache key for student context: `sha256(student_id + session_id + mastery_snapshot_hash)` (from routing-config.yaml)
- [ ] Applies ONLY to Anthropic models: `claude_sonnet_4_6`, `claude_sonnet_4_5`, `claude_opus_4_6` (from routing-config.yaml `applies_to`)
- [ ] Kimi models use provider-managed automatic caching (no explicit cache_control needed)
- [ ] Target cache hit rate: 60% (from routing-config.yaml `target_cache_hit_rate: 0.60`)
- [ ] Metric `llm_cache_hits_total` counter updated on every LLM response, labeled by `model_id` and `cache_type` (system/student_context)
- [ ] Builds Anthropic Messages API format with proper `cache_control` blocks:
  ```python
  messages = [
      {"role": "user", "content": [
          {"type": "text", "text": system_prompt, "cache_control": {"type": "ephemeral"}},
          {"type": "text", "text": student_context, "cache_control": {"type": "ephemeral"}},
          {"type": "text", "text": dialogue_and_question},
      ]}
  ]
  ```

**Test:**
```python
import pytest
import hashlib
from cena_llm.prompts.cache import PromptCacheBuilder

@pytest.fixture
def builder():
    return PromptCacheBuilder()

def test_system_prompt_gets_cache_control(builder):
    blocks = builder.build_message_blocks(
        template_name="socratic_question",
        model_id="claude-sonnet-4-6-20260215",
        system_prompt="You are a tutor...",
        student_context="Grade 10, mastery 45%...",
        dialogue="Student said...",
    )
    assert blocks[0]["cache_control"]["type"] == "ephemeral"
    assert blocks[1]["cache_control"]["type"] == "ephemeral"
    assert "cache_control" not in blocks[2]

def test_no_cache_control_for_kimi(builder):
    blocks = builder.build_message_blocks(
        template_name="error_classification",
        model_id="kimi-k2-0905-preview",
        system_prompt="You are a classifier...",
        student_context="",
        dialogue="",
    )
    for block in blocks:
        assert "cache_control" not in block

def test_cache_key_deterministic(builder):
    key1 = builder.compute_system_cache_key("socratic_question", "claude-sonnet-4-6-20260215")
    key2 = builder.compute_system_cache_key("socratic_question", "claude-sonnet-4-6-20260215")
    assert key1 == key2

def test_cache_key_differs_by_model(builder):
    key_sonnet = builder.compute_system_cache_key("socratic_question", "claude-sonnet-4-6-20260215")
    key_opus = builder.compute_system_cache_key("socratic_question", "claude-opus-4-6-20260215")
    assert key_sonnet != key_opus

def test_student_context_cache_key(builder):
    key = builder.compute_student_cache_key("student-001", "session-abc", {"math-fractions": 0.45})
    assert len(key) == 64  # SHA-256 hex digest
```

**Edge cases:**
- Cache miss on first request of the day -> expected, baseline cold-start
- Mastery map changes mid-session (after BKT update) -> new cache key, old cache entry expires naturally
- Multiple instances computing same cache key -> no conflict, Anthropic handles dedup

---

### LLM-005.3: Output Schema Validation & JSON Parsing
**Files to create/modify:**
- `src/llm-acl/src/cena_llm/prompts/output_parser.py` — parse and validate LLM JSON output against schema

**Acceptance:**
- [ ] Parses JSON output from all 7 templates:
  - `SOCRATIC_OUTPUT_SCHEMA`: `question_he`, `question_type` (guiding|probing|clarifying|challenge), `scaffolding_level` (1-5), `expected_concepts`
  - `ANSWER_EVALUATION_OUTPUT_SCHEMA`: `is_correct`, `total_score`, `max_score`, `score_percentage`, `criterion_scores`, `overall_feedback_he`, `error_type`, `partial_credit_awarded`
  - `ERROR_CLASSIFICATION_OUTPUT_SCHEMA`: `primary_error_type`, `secondary_error_type`, `confidence`, `error_description_he`, `is_repeated_pattern`, `suggested_intervention`
  - `METHODOLOGY_SWITCH_OUTPUT_SCHEMA`: `should_switch`, `recommended_methodology`, `confidence`, `reasoning_he`, `reasoning_en`, `expected_improvement`, `risk_factors`, `fallback_methodology`
  - `CONTENT_SAFETY_OUTPUT_SCHEMA`: `verdict` (safe|needs_review|blocked), `flagged_categories`, `confidence`, `sanitized_text`
  - `FEYNMAN_OUTPUT_SCHEMA`: `completeness_score`, `accuracy_score`, `clarity_score`, `depth_score`, `overall_score`, `gaps_identified`, `feedback_he`, `demonstrates_mastery`
  - `DIAGRAM_OUTPUT_SCHEMA`: `svg_content`, `alt_text_he`, `alt_text_en`
- [ ] Strips markdown code fences (```json ... ```) if LLM wraps output
- [ ] Handles LLM returning extra text before/after JSON block
- [ ] Validates parsed JSON against the corresponding Pydantic response model
- [ ] On parse failure: retry once with a "Please output valid JSON" nudge, then raise `PromptRenderError` (from `acl-interfaces.py`)

**Test:**
```python
import pytest
from cena_llm.prompts.output_parser import OutputParser
from cena_llm.models import SocraticQuestionResponse, AnswerEvaluationResponse

@pytest.fixture
def parser():
    return OutputParser()

def test_parses_clean_json(parser):
    raw = '{"question_he": "מה קורה?", "question_type": "guiding", "scaffolding_level": 3, "expected_concepts": ["c1"]}'
    result = parser.parse("socratic_question", raw)
    assert result["question_he"] == "מה קורה?"

def test_strips_markdown_fences(parser):
    raw = '```json\n{"question_he": "test", "question_type": "probing", "scaffolding_level": 2, "expected_concepts": []}\n```'
    result = parser.parse("socratic_question", raw)
    assert result["question_type"] == "probing"

def test_handles_extra_text(parser):
    raw = 'Here is the output:\n\n{"verdict": "safe", "flagged_categories": [], "confidence": 0.95, "sanitized_text": null}\n\nI hope this helps!'
    result = parser.parse("content_filter", raw)
    assert result["verdict"] == "safe"

def test_validates_against_pydantic_model(parser):
    raw = '{"question_he": "test", "question_type": "invalid_type", "scaffolding_level": 99, "expected_concepts": []}'
    with pytest.raises(Exception):  # Validation error for invalid enum/range
        parser.parse_and_validate("socratic_question", raw, SocraticQuestionResponse)

def test_feynman_score_validation(parser):
    raw = '{"completeness_score": 0.8, "accuracy_score": 0.9, "clarity_score": 0.7, "depth_score": 0.6, "overall_score": 0.78, "gaps_identified": [], "feedback_he": "טוב מאוד", "demonstrates_mastery": true}'
    result = parser.parse("feynman_explanation", raw)
    assert result["demonstrates_mastery"] is True

def test_methodology_switch_parsing(parser):
    raw = '{"should_switch": true, "recommended_methodology": "worked_examples", "confidence": 0.81, "reasoning_he": "שגיאות חוזרות", "reasoning_en": "repeated errors", "expected_improvement": 0.15, "risk_factors": ["passive learning"], "fallback_methodology": "scaffolded_practice"}'
    result = parser.parse("methodology_switch", raw)
    assert result["recommended_methodology"] == "worked_examples"
    assert result["confidence"] == 0.81
```

**Edge cases:**
- LLM returns empty string -> raise `PromptRenderError`
- LLM returns valid JSON but wrong schema (e.g., missing required field) -> raise with field name
- LLM returns multiple JSON blocks -> take the first one
- Hebrew text in JSON values with unescaped characters -> ensure UTF-8 handling

---

### LLM-005.4: Hebrew Quality Gate (Sampling)
**Files to create/modify:**
- `src/llm-acl/src/cena_llm/prompts/quality_gate.py` — quality gate sampling and evaluation logic

**Acceptance:**
- [ ] Implements quality gate from `routing-config.yaml` section 7:
  - `minimum_score_threshold: 3.5` (average across 5 criteria on 1-5 scale)
  - `blocker_threshold: 2.0` (any single criterion below this = blocker)
  - 5 criteria with weights:
    1. `terminology_accuracy` (0.25) — uses standard Hebrew math terms
    2. `socratic_quality` (0.20) — guides without revealing answer
    3. `mathematical_correctness` (0.25) — formulas and steps correct
    4. `pedagogical_appropriateness` (0.15) — Bagrut level, not university
    5. `hebrew_fluency` (0.15) — natural phrasing, not translated
- [ ] Arabic quality gate from section 7B:
  - Same thresholds, Arabic-specific criteria (MSA terminology, not transliterated Hebrew)
  - Higher sampling rate: 10% during Arabic rollout vs 5% for Hebrew
  - Stricter escalation: `consecutive_failures: 2` (vs 3 for Hebrew)
  - On failure: `fallback_to_opus` (stricter than Hebrew's `log_and_alert`)
- [ ] Sampling mode: `percentage` at 5% for Hebrew, 10% for Arabic
- [ ] Force-check on: `new_concept_introduction`, `methodology_switch`, `first_arabic_session`
- [ ] On quality gate failure:
  - Hebrew: `log_and_alert`, escalate to `fallback_to_opus` after 3 consecutive failures
  - Arabic: `fallback_to_opus` immediately, escalate after 2 consecutive failures
- [ ] Quality gate results stored for weekly quality reports
- [ ] Raises `HebrewQualityGateError` (from `acl-interfaces.py`) when score < threshold

**Test:**
```python
import pytest
from cena_llm.prompts.quality_gate import QualityGate, QualityCheckResult

@pytest.fixture
def gate():
    return QualityGate()

def test_passes_high_quality_hebrew(gate):
    result = gate.evaluate(
        text="בוא נבדוק: אם נציב x=2 במשוואה 2x+4=10, מה נקבל?",
        locale="he",
        task_type="socratic_question",
    )
    assert result.passes
    assert result.average_score >= 3.5

def test_fails_low_quality_hebrew(gate):
    result = gate.evaluate(
        text="So, like, what do you think the answer is? Try again.",
        locale="he",
        task_type="socratic_question",
    )
    assert not result.passes  # English text for Hebrew locale

def test_sampling_rate_hebrew(gate):
    checked = sum(1 for _ in range(1000) if gate.should_check(locale="he", is_forced=False))
    assert 30 < checked < 80  # ~5% = 50 +/- margin

def test_sampling_rate_arabic(gate):
    checked = sum(1 for _ in range(1000) if gate.should_check(locale="ar", is_forced=False))
    assert 70 < checked < 140  # ~10% = 100 +/- margin

def test_force_check_on_methodology_switch(gate):
    assert gate.should_check(locale="he", is_forced=False, trigger="methodology_switch")

def test_force_check_on_first_arabic_session(gate):
    assert gate.should_check(locale="ar", is_forced=False, trigger="first_arabic_session")

def test_arabic_failure_escalates_to_opus(gate):
    result = QualityCheckResult(
        passes=False, average_score=2.8, locale="ar",
        criterion_scores={"terminology_accuracy": 2.0},
    )
    action = gate.get_failure_action(result, consecutive_failures=1)
    assert action == "fallback_to_opus"

def test_hebrew_failure_logs_first_then_escalates(gate):
    result = QualityCheckResult(
        passes=False, average_score=3.0, locale="he",
        criterion_scores={"terminology_accuracy": 2.5},
    )
    assert gate.get_failure_action(result, consecutive_failures=1) == "log_and_alert"
    assert gate.get_failure_action(result, consecutive_failures=3) == "fallback_to_opus"
```

**Edge cases:**
- Quality gate itself calls an LLM (Opus for evaluation) -> budget consumed, but quality gate calls are exempt from per-student budget (charged to system account)
- Quality gate fails on a time-sensitive request (student waiting) -> return the response anyway, log the failure, fix asynchronously
- New criterion added to quality gate -> requires routing-config.yaml update, weights must sum to 1.0

---

## Integration Test (all subtasks combined)

```python
import pytest
from cena_llm.prompts.engine import PromptEngine
from cena_llm.prompts.cache import PromptCacheBuilder
from cena_llm.prompts.output_parser import OutputParser
from cena_llm.prompts.quality_gate import QualityGate

def test_full_prompt_pipeline():
    engine = PromptEngine()
    cache = PromptCacheBuilder()
    parser = OutputParser()

    # 1. Render socratic template with Hebrew glossary
    rendered = engine.render("socratic_question",
        grade_level=10,
        concept_name_he="נגזרות",
        concept_name_en="Derivatives",
        current_mastery=0.55,
        active_methodology="socratic",
        hint_level=1,
        prerequisites_status="Functions: mastered",
        dialogue_history="Student: כלל השרשרת?",
        locale="he",
    )
    assert "כלל השרשרת" in rendered  # Hebrew glossary term for chain rule
    assert "Klal HaSharsheret" in rendered  # Transliteration from glossary

    # 2. Build cache control blocks for Anthropic
    blocks = cache.build_message_blocks(
        template_name="socratic_question",
        model_id="claude-sonnet-4-6-20260215",
        system_prompt=rendered,
        student_context="Student context...",
        dialogue="Dialogue...",
    )
    assert len(blocks) == 3
    assert blocks[0]["cache_control"]["type"] == "ephemeral"

    # 3. Parse a mock LLM response
    mock_response = '```json\n{"question_he": "מה הנגזרת של sin(x²)?", "question_type": "probing", "scaffolding_level": 2, "expected_concepts": ["chain_rule"]}\n```'
    result = parser.parse("socratic_question", mock_response)
    assert result["question_type"] == "probing"
    assert result["scaffolding_level"] == 2

    # 4. Render with Arabic glossary
    rendered_ar = engine.render("socratic_question",
        grade_level=10,
        concept_name_he="مشتقات",
        concept_name_en="Derivatives",
        current_mastery=0.55,
        active_methodology="socratic",
        hint_level=1,
        prerequisites_status="Functions: mastered",
        dialogue_history="Student: قاعدة السلسلة?",
        locale="ar",
    )
    assert "مشتقة" in rendered_ar  # Arabic glossary term for derivative

    # 5. All 7 templates have correct output schemas
    from contracts.llm.prompt_templates import TEMPLATE_REGISTRY
    for name, template in TEMPLATE_REGISTRY.items():
        assert len(template.output_schema) > 20, f"Empty schema for {name}"
        assert len(template.required_variables) > 0, f"No required vars for {name}"
```

## Rollback Criteria
If prompt quality degrades or quality gate has too many false positives:
- Disable quality gate sampling (set `quality_gate.enabled: false` in routing-config.yaml)
- Revert to hardcoded English prompts with post-hoc Hebrew translation (lower quality, but functional)
- Arabic support can be disabled independently (set `arabic_quality_gate.enabled: false`)

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `pytest tests/ -k "prompts"` -> 0 failures
- [ ] All 7 templates render without errors with sample data
- [ ] Hebrew glossary includes all 36 terms from `prompt-templates.py`
- [ ] Arabic glossary includes all 30 terms from `prompt-templates.py`
- [ ] Prompt caching blocks correctly built for Anthropic models, absent for Kimi
- [ ] Output parser handles markdown fences, extra text, and all 7 schema formats
- [ ] Quality gate sampling rates match routing-config.yaml (5% Hebrew, 10% Arabic)
- [ ] PR reviewed by architect (you)
