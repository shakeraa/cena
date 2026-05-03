# RDY-027: Math/Physics Glossary Curation & Validation

- **Priority**: Critical — terminology inconsistency causes wrong answers
- **Complexity**: Curriculum expert + engineer
- **Source**: Cross-review — Amjad (Curriculum)
- **Tier**: 1 (prerequisite to RDY-004 Arabic translations)
- **Effort**: 2-3 weeks

## Problem

Current glossary has 30 terms. No validation against actual Bagrut exams. Seed questions may use non-standard Hebrew terminology that doesn't match official exam language. Translating from non-canonical Hebrew produces non-canonical Arabic — compounding terminology errors.

Additionally, physics terminology is entirely unvalidated:
- Greek letters in formulae (α, β, γ, ω) — usage conventions differ
- Symbol conventions (I for current, V for voltage) need standardization
- Force notation varies between Hebrew and Arabic physics textbooks

## Scope

### 1. Hebrew glossary validation

- Cross-reference all math terms in seed data against 5+ years of Hebrew Bagrut exams
- Document official term variants (e.g., alternative terms for same concept)
- Flag seed questions using non-standard terminology
- Target: 150+ validated math terms, 80+ validated physics terms

### 2. Arabic glossary expansion

- Expand from 30 → 150+ math terms (matching Hebrew glossary)
- Add 80+ physics terms (Hebrew ↔ Arabic mapping)
- Validate against Arabic math textbooks used in Israeli Arab schools
- Include gender agreement notes for gendered Arabic math terms

### 3. Physics-specific terminology

- Map Greek letters: usage in Hebrew vs. Arabic physics contexts
- Standardize unit abbreviations (metric, SI)
- Document notation conventions per subject (forces, circuits, optics)

### 4. Glossary infrastructure

- Version-controlled glossary file: `config/glossary.json`
- Structured format: `{ term_id, hebrew, arabic, english, domain, notes, source }`
- CI validation: every question's terms must exist in glossary

## Files to Modify

- `contracts/llm/prompt-templates.py` — expand inline glossary
- New: `config/glossary.json` — canonical glossary (version-controlled)
- New: `scripts/glossary-validator.ts` — validates questions use glossary terms
- `src/shared/Cena.Infrastructure/Content/QualityGateService.cs` — glossary term check

## Acceptance Criteria

- [ ] 150+ math terms validated against official Hebrew Bagrut exams
- [ ] 80+ physics terms with Hebrew ↔ Arabic mapping
- [ ] Glossary in structured JSON format, version-controlled
- [ ] Gender agreement notes for Arabic gendered terms
- [ ] Seed questions flagged if using non-standard terminology
- [ ] CI validation: new questions must use glossary terms
- [ ] Physics notation conventions documented per subject

> **Dependency**: This task is a prerequisite to RDY-004 (Arabic Translations). Translate from canonical Hebrew first.
