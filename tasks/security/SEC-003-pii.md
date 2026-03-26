# SEC-003: PII Stripping for Kimi — Annotations, Tokenization, Audit Log

**Priority:** P0 — blocks LLM integration with Kimi (non-trusted provider)
**Blocked by:** LLM-001 (ACL scaffold)
**Estimated effort:** 2 days
**Contract:** `contracts/llm/acl-interfaces.py` (PII annotation, PIIStripper), `contracts/REVIEW_security.md` (C-1, C-2)

---

## Context

Kimi (Moonshot AI, China-hosted) handles error classification, content filtering, and diagram generation. Student PII must NEVER reach Kimi. The ACL defines a `PII` type annotation (`pii=True` in Pydantic field metadata) and a `PIIStripper` abstract class. Student free-text answers can contain self-identifying information ("my name is...", "in my school..."). This task implements the stripping pipeline, tokenization for consistent anonymization within a session, and an audit log proving compliance.

## Subtasks

### SEC-003.1: PII Field Discovery + Annotation Enforcement

**Files to create/modify:**
- `src/llm_acl/pii/field_scanner.py` — scan Pydantic models for PII annotations
- `src/llm_acl/pii/annotation_validator.py` — build-time check that all request models have PII annotations where required
- `tests/llm_acl/test_pii_annotations.py`

**Acceptance:**
- [ ] Scanner discovers all fields with `pii=True` in JSON schema extra across all request models
- [ ] Fields currently annotated: `StudentContext.student_id`, `ErrorClassificationRequest.student_id`
- [ ] NEW: `AnswerEvaluationRequest.student_answer_he` annotated as `pii=True` (fix for C-1)
- [ ] NEW: `ContentFilterRequest.text` annotated as `pii=True` when `source="student_input"`
- [ ] Build-time validator fails CI if a new request model with `student_*` fields lacks PII annotation
- [ ] Validator generates a report: `pii_fields_report.json` listing all annotated fields per model

**Test:**
```python
def test_all_student_fields_annotated():
    """Every field starting with 'student_' in request models must have PII annotation."""
    from llm_acl.pii.field_scanner import scan_all_request_models
    report = scan_all_request_models()
    unannotated = [
        f for f in report
        if f.field_name.startswith("student_") and not f.is_pii_annotated
    ]
    assert unannotated == [], f"Unannotated PII fields: {unannotated}"

def test_student_answer_is_pii():
    from contracts.llm.acl_interfaces import AnswerEvaluationRequest
    schema = AnswerEvaluationRequest.model_json_schema()
    answer_field = schema["properties"]["student_answer_he"]
    assert answer_field.get("pii") is True
```

**Edge cases:**
- Nested model fields (e.g., `student.student_id` inside `SocraticQuestionRequest`) -> scanner traverses nested models
- New developer adds a request model without PII annotations -> CI fails with clear message

---

### SEC-003.2: PII Stripping + Tokenization Middleware

**Files to create/modify:**
- `src/llm_acl/pii/stripper.py` — implements `PIIStripper` contract
- `src/llm_acl/pii/tokenizer.py` — session-scoped anonymization token map
- `src/llm_acl/pii/ner_filter.py` — NER-based content filter for free-text fields
- `src/llm_acl/middleware/pii_middleware.py` — FastAPI middleware that applies stripping before Kimi routing

**Acceptance:**
- [ ] All `pii=True` fields replaced with anonymized tokens: `student_id` -> `ANON_STU_<hash8>`
- [ ] Token map consistent within a session: same `student_id` always maps to same token
- [ ] Free-text fields (`student_answer_he`) run through NER filter before Kimi routing
- [ ] NER filter detects: Hebrew names, school names, addresses, phone numbers, ID numbers (teudat zehut format: 9 digits)
- [ ] Detected PII in free text replaced with `[REDACTED_NAME]`, `[REDACTED_SCHOOL]`, etc.
- [ ] NER model: spaCy `he_core_news_sm` or equivalent for Hebrew entity recognition
- [ ] Stripping only applied for non-trusted providers (`ModelConfig.is_trusted_provider == False`)
- [ ] Claude (trusted) receives full PII fields (except display name, per H-2 mitigation)
- [ ] `PIIStripper.restore()` maps anonymized tokens in responses back to original values
- [ ] Stripping is idempotent: double-stripping produces same result

**Test:**
```python
def test_strip_student_id():
    stripper = CenaPIIStripper()
    request = ErrorClassificationRequest(
        student_id="real-student-uuid-123",
        concept=ConceptInfo(concept_id="math-1", concept_name_he="חיבור", concept_name_en="Addition"),
        attempt=AttemptData(question_id="q1", question_type="mcq", student_answer="2", expected_answer="4", response_time_ms=5000),
    )
    stripped, anon_map = stripper.strip(request)
    assert stripped.student_id != "real-student-uuid-123"
    assert stripped.student_id.startswith("ANON_STU_")
    assert anon_map[stripped.student_id] == "real-student-uuid-123"

def test_ner_detects_hebrew_name():
    from llm_acl.pii.ner_filter import detect_pii_in_text
    text = "שמי דוד כהן ואני לומד בבית ספר הרצליה"
    result = detect_pii_in_text(text, language="he")
    assert any(e.entity_type == "PERSON" for e in result.entities)
    assert any(e.entity_type == "ORG" for e in result.entities)
    assert "[REDACTED_NAME]" in result.sanitized_text

def test_teudat_zehut_detection():
    from llm_acl.pii.ner_filter import detect_pii_in_text
    text = "מספר הזהות שלי 123456789"
    result = detect_pii_in_text(text, language="he")
    assert any(e.entity_type == "ID_NUMBER" for e in result.entities)

def test_stripping_idempotent():
    stripper = CenaPIIStripper()
    request = make_test_request(student_id="uuid-123")
    stripped1, _ = stripper.strip(request)
    stripped2, _ = stripper.strip(stripped1)
    assert stripped1.student_id == stripped2.student_id
```

**Edge cases:**
- Student writes answer entirely in Arabic -> NER filter supports Arabic names
- Student answer contains no PII -> passes through unchanged (no false positives on math notation)
- NER model unavailable at startup -> fail open with WARNING log, still strip annotated fields
- Emoji-heavy student text -> NER handles gracefully, no crash

---

### SEC-003.3: PII Audit Log

**Files to create/modify:**
- `src/llm_acl/pii/audit_logger.py` — structured audit log for every PII strip operation
- `config/audit/pii_audit_config.yaml` — retention policy, alert thresholds
- `tests/llm_acl/test_pii_audit.py`

**Acceptance:**
- [ ] Every PII strip operation logged: `{ timestamp, request_id, model_tier, fields_stripped: [...], ner_entities_found: int, provider: "kimi" }`
- [ ] Log does NOT contain the original PII values (that would defeat the purpose)
- [ ] Log stored in structured JSON format, shipped to Grafana Loki
- [ ] Retention: 365 days (GDPR compliance evidence)
- [ ] Alert: if `ner_entities_found > 0` for `student_input` fields, increment counter `cena.pii.ner_detections_total`
- [ ] Alert threshold: >100 NER detections/day -> Slack alert to security team
- [ ] Monthly compliance report: total requests stripped, NER detection rate, models routed to non-trusted providers
- [ ] Audit log queryable: "show all PII strip operations for student X in date range"

**Test:**
```python
def test_audit_log_created_on_strip():
    stripper = CenaPIIStripper(audit_logger=MockAuditLogger())
    request = make_test_request(student_id="uuid-123")
    stripper.strip(request)

    logs = stripper.audit_logger.get_logs()
    assert len(logs) == 1
    assert "student_id" in logs[0]["fields_stripped"]
    assert "uuid-123" not in str(logs[0])  # PII value NOT in log

def test_audit_log_records_ner_detections():
    stripper = CenaPIIStripper(audit_logger=MockAuditLogger())
    request = make_test_request(student_answer_he="שמי דוד כהן")
    stripper.strip(request)

    logs = stripper.audit_logger.get_logs()
    assert logs[0]["ner_entities_found"] >= 1
```

---

## Rollback Criteria
If PII stripping causes LLM response quality degradation:
- Disable NER-based free-text filtering (keep field-level PII stripping)
- Route all traffic to Claude (trusted) temporarily, bypassing Kimi
- Log all unstripped requests for manual review

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] `pytest tests/llm_acl/ -k pii` -> 0 failures
- [ ] No PII fields reach Kimi in staging (verified via audit log)
- [ ] Audit log captures 100% of Kimi-routed requests
- [ ] NER filter detects Hebrew names and ID numbers with >90% recall
- [ ] PR reviewed by architect
