# RDY-019a: Bagrut Content Follow-ups (Curriculum Expert Required)

- **Priority**: High — blocks content validity
- **Complexity**: Curriculum expert (Amjad) + engineer
- **Source**: RDY-019 review gaps
- **Tier**: 2 (pre-pilot for 5-unit, post-pilot for 3u/4u)
- **Effort**: 3-4 weeks (translator/expert-dependent)
- **Blocked on**: Amjad's availability

## Parent: RDY-019 (Bagrut Corpus Ingestion + Topic Taxonomy)

Engineering infrastructure is complete:
- `scripts/bagrut-taxonomy.json` — formal 3-track taxonomy (5u/4u/3u)
- `scripts/bagrut-scraper.py` — Ministry exam scraper framework with OCR pipeline
- Quality gate Rule 18 — flags unmapped concepts (UNMAPPED_CONCEPT)

## Sub-tasks

### 1. Ingest 100+ real Bagrut exam items (Amjad + engineer)

- [ ] Download Math 5-unit exams (806/807) from `meyda.education.gov.il/sheeloney_bagrut/`
- [ ] Run through OCR pipeline (Gemini Vision / Mathpix — clients exist)
- [ ] Structure as QuestionDocument events via `scripts/bagrut-scraper.py --extract`
- [ ] Map each item to taxonomy node + Bagrut alignment metadata
- [ ] Quality gate pass on all ingested items
- [ ] Target: years 2020-2024, both moed A and B

### 2. Add 3-unit and 4-unit seed questions (10 each)

- [ ] 10 seed questions for `math_3u` covering: linear equations, quadratic equations, basic geometry, basic probability
- [ ] 10 seed questions for `math_4u` covering: functions, sequences, conditional probability, coordinate geometry
- [ ] All in Hebrew (primary), Arabic translation queued as part of RDY-004
- [ ] Each question tagged with taxonomy conceptId and BagrutAlignment

### 3. Taxonomy review by curriculum expert

- [ ] Amjad reviews `scripts/bagrut-taxonomy.json` against official Ministry syllabus
- [ ] Confirm: are all 5-unit exam topics covered?
- [ ] Confirm: is the 3u/4u split correct?
- [ ] Add any missing subtopics
- [ ] Sign off: taxonomy is accurate for pilot

### 4. Coverage gap analysis

After items 1-3, run:
```bash
python scripts/bagrut-scraper.py --coverage
```
- [ ] Identify topics with 0 questions
- [ ] Prioritize gap-filling for 5-unit (pilot track)
- [ ] Document coverage percentage per topic cluster

## Immediate Action

Contact Amjad this week to confirm:
1. Calendar availability for taxonomy review
2. Can he source 3u/4u exam papers for seed question authoring?
