# LLM-007: PII Stripping for Kimi Boundary

**Priority:** P0 — blocks Kimi routing
**Blocked by:** SEC-003 (PII annotations)
**Estimated effort:** 1 day
**Contract:** `contracts/llm/acl-interfaces.py` (PIIStripper, PII annotation)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

This is the implementation of the PIIStripper abstract class defined in the ACL contracts. It runs as middleware in the FastAPI LLM service, stripping PII fields before any request is routed to Kimi (non-trusted provider).

## Subtasks

### LLM-007.1: PIIStripper Implementation
- [ ] Implements `PIIStripper.strip()`: iterate Pydantic model fields, replace `pii=True` fields with anonymized tokens
- [ ] Token format: `ANON_{FIELD_TYPE}_{first8chars_of_sha256(value)}`
- [ ] Session-scoped anonymization map: same value -> same token within one session
- [ ] Nested model traversal: strip PII in `StudentContext` within `SocraticQuestionRequest`

### LLM-007.2: PIIStripper.restore()
- [ ] Maps anonymized tokens in LLM response back to original values
- [ ] Only restores for trusted-provider responses (Claude) if needed
- [ ] Kimi responses should not contain PII references (stripping was one-way)

### LLM-007.3: Middleware Integration
- [ ] FastAPI middleware: check `ModelConfig.is_trusted_provider`
- [ ] If not trusted: `strip()` before API call, `restore()` after
- [ ] If trusted: pass through unchanged
- [ ] Audit log entry for every strip operation (per SEC-003)

**Test:**
```python
def test_pii_stripper_middleware():
    request = SocraticQuestionRequest(
        student=StudentContext(student_id="real-uid-123"),
        concept=ConceptInfo(concept_id="math-1", concept_name_he="חיבור", concept_name_en="Addition"),
        current_mastery=0.5,
    )
    stripped = middleware.process_request(request, model_config=kimi_config)
    assert stripped.student.student_id != "real-uid-123"
    assert stripped.student.student_id.startswith("ANON_")
```

---

## Definition of Done
- [ ] PIIStripper fully implemented
- [ ] No PII reaches Kimi in testing
- [ ] Audit log entries created
- [ ] PR reviewed by architect
