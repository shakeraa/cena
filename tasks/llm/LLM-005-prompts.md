# LLM-005: 7 Prompt Templates, Hebrew+Arabic Glossaries, Locale-Aware

**Priority:** P0 — blocks all LLM interactions
**Blocked by:** LLM-001 (ACL scaffold)
**Estimated effort:** 3 days
**Contract:** `contracts/llm/prompt-templates.py` (HEBREW_MATH_GLOSSARY, ARABIC_MATH_GLOSSARY, template structures)

---

## Context

Seven prompt templates drive all LLM interactions: Socratic Question, Answer Evaluation, Error Classification, Methodology Switch, Content Filter, Diagram Generation, Feynman Explanation. Each template includes system prompt, glossary injection, few-shot examples, and structured JSON output schema. Templates are locale-aware: Hebrew glossary for he, Arabic glossary for ar.

## Subtasks

### LLM-005.1: Template Engine + Glossary Injection
- [ ] Jinja2-style templates with variable substitution
- [ ] Glossary injected based on `student.preferred_language`: he -> HEBREW_MATH_GLOSSARY, ar -> ARABIC_MATH_GLOSSARY
- [ ] Template validation: all required variables present before rendering
- [ ] Template versioning: `v1.0.0` with A/B test support

### LLM-005.2: Socratic + Answer Evaluation + Error Classification Templates
- [ ] Socratic: Claude Sonnet 4.6, system prompt with Bagrut tutor role, 3 few-shot examples
- [ ] Answer Evaluation: Claude Sonnet 4.6, rubric-based scoring with structured JSON output
- [ ] Error Classification: Kimi K2 0905, structured extraction with confidence score

### LLM-005.3: Methodology Switch + Content Filter + Diagram Templates
- [ ] Methodology Switch: Claude Opus 4.6, multi-factor reasoning with risk assessment
- [ ] Content Filter: Kimi K2 Turbo, binary safe/unsafe classification with Hebrew safety concerns
- [ ] Diagram Generation: Kimi K2.5, SVG output with accessibility alt text

### LLM-005.4: Feynman Template + Integration Tests
- [ ] Feynman: Claude Sonnet 4.6, evaluate student explanation with 4 rubric dimensions
- [ ] Integration test: render each template with sample data, verify valid prompt structure
- [ ] Token count estimation: each rendered template within model's context window

**Test:**
```python
def test_socratic_template_renders():
    from llm_acl.prompts import render_socratic_prompt
    prompt = render_socratic_prompt(
        concept_name="משוואות ריבועיות", mastery=0.4, hint_level=1, language="he"
    )
    assert "משוואות ריבועיות" in prompt
    assert "מילון מונחים" in prompt  # Glossary injected

def test_arabic_glossary_injected():
    prompt = render_socratic_prompt(concept_name="معادلة تربيعية", language="ar")
    assert "قاموس المصطلحات" in prompt
```

---

## Definition of Done
- [ ] All 7 templates implemented and tested
- [ ] Hebrew and Arabic glossaries injected correctly
- [ ] Token counts within model limits
- [ ] PR reviewed by architect
