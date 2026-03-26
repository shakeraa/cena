# LLM-006: Hebrew+Arabic Quality Gate — 5 Criteria, Sampling, Escalation

**Priority:** P1 — blocks LLM output quality
**Blocked by:** LLM-005 (Prompts)
**Estimated effort:** 2 days
**Contract:** `contracts/llm/acl-interfaces.py` (HebrewQualityGateError)

---

## Context

LLM outputs in Hebrew and Arabic must pass quality checks before being shown to students. Five criteria: (1) language correctness (output in requested language), (2) mathematical accuracy (LaTeX renders correctly), (3) Bagrut terminology alignment, (4) no hallucinated concepts, (5) appropriate difficulty level. Outputs failing quality gate are retried or escalated.

## Subtasks

### LLM-006.1: 5-Criteria Quality Checker
- [ ] Language check: output contains Hebrew/Arabic characters matching requested locale
- [ ] Math check: LaTeX expressions parse without error
- [ ] Terminology check: key terms match HEBREW_MATH_GLOSSARY / ARABIC_MATH_GLOSSARY
- [ ] Hallucination check: referenced concept IDs exist in curriculum graph
- [ ] Difficulty check: question difficulty aligns with requested Bloom level

### LLM-006.2: Sampling Strategy
- [ ] 100% check on first 1000 outputs (calibration phase)
- [ ] After calibration: 10% random sampling
- [ ] 100% check on Kimi outputs (lower trust), 5% on Claude outputs (higher trust)
- [ ] All failed checks logged with full context for prompt improvement

### LLM-006.3: Escalation + Retry
- [ ] Score < 0.6: retry with same model (once)
- [ ] Score < 0.6 after retry: escalate to next model in fallback chain
- [ ] Score < 0.3: block output, log CRITICAL, return fallback response
- [ ] `HebrewQualityGateError` raised with score, threshold, task type

**Test:**
```python
def test_quality_gate_detects_wrong_language():
    checker = QualityChecker()
    result = checker.check(
        output="The answer is x = 5",  # English, not Hebrew
        requested_language="he",
        task_type=TaskType.SOCRATIC_QUESTION
    )
    assert result.score < 0.5
    assert "language_mismatch" in result.failures
```

---

## Definition of Done
- [ ] 5 quality criteria implemented
- [ ] Sampling strategy with escalation
- [ ] Quality gate integrated into LLM Gateway
- [ ] PR reviewed by architect
