# LLM-009: Batch Diagram Generation Pipeline, S3, CDN

**Priority:** P2 — blocks visual learning
**Blocked by:** LLM-005 (Prompts), INF-005 (S3/CDN), CNT-001 (Math Graph)
**Estimated effort:** 2 days
**Contract:** `contracts/llm/diagram-generation-pipeline.py`

---

## Context

Diagrams are pre-generated overnight by Kimi K2.5 in batch, uploaded to S3, and served via CloudFront. Each concept gets multiple diagrams at different Bloom levels. SVG format with accessibility alt text.

## Subtasks

### LLM-009.1: Batch Generation Pipeline
- [ ] Input: concept list from Neo4j with diagram type, Bloom level, difficulty
- [ ] Kimi K2.5 generates SVG via structured output prompt
- [ ] SVG validation: XML parse, viewBox present, < 100KB
- [ ] Alt text generated in Hebrew and Arabic
- [ ] Batch size: 100 concepts/run, ~15 min per batch

### LLM-009.2: S3 Upload + CDN Invalidation
- [ ] S3 path: `s3://cena-diagrams/{conceptId}/{version}/{bloomLevel}.svg`
- [ ] Upload with content-type `image/svg+xml` and cache-control headers
- [ ] CloudFront invalidation for updated diagrams
- [ ] Manifest: `diagrams-manifest.json` listing all generated diagrams

### LLM-009.3: Quality Validation
- [ ] SVG renders correctly in Flutter `flutter_svg`
- [ ] Alt text present and non-empty
- [ ] Diagram matches concept (basic check via Kimi K2 Turbo)
- [ ] High contrast mode variant generated for accessibility

**Test:**
```python
def test_batch_generates_valid_svg():
    results = batch_generate(concepts=["math-fractions"], bloom_level="application")
    assert len(results) == 1
    assert results[0].is_valid_svg
    assert len(results[0].svg_content) < 100_000
    assert results[0].alt_text_he
```

---

## Definition of Done
- [ ] Batch pipeline generates SVGs for all concepts
- [ ] S3 upload + CDN serving verified
- [ ] Quality validation passes > 95%
- [ ] PR reviewed by architect
