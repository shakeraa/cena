# Bagrut Anchor Calibration Methodology (RDY-028)

## Overview

Cena's adaptive engine uses IRT (Item Response Theory) to select questions and estimate student ability. Without calibration against real exam data, Cena's difficulty scale is arbitrary — "hard" in Cena may be trivially easy on the actual Bagrut, or vice versa.

Anchor calibration links Cena's internal difficulty scale to the Bagrut exam's empirical difficulty distribution, enabling:
- Predictive validity: Cena mastery predicts Bagrut score brackets
- Band alignment: Cena's Easy/Medium/Hard match actual exam difficulty
- Honest difficulty labels: students and teachers see calibrated, not arbitrary, difficulty

## Methodology: Rasch Anchor Calibration

### Step 1: Anchor Selection

Select 14-22 anchor items per exam track that closely match real Bagrut questions. Criteria:
- Cover all major topics (algebra, functions, calculus, geometry, trigonometry, probability, vectors)
- Span the full difficulty range (easy Part A openers through hard Part B problems)
- Use published Bagrut pass rates as the ground truth for difficulty

Source: Ministry of Education published exam statistics (`meyda.education.gov.il`).

### Step 2: Pass Rate to IRT Difficulty

Convert Bagrut pass rate `p` to Rasch difficulty `b` using the logit transform:

```
b = -ln(p / (1 - p))
```

| Pass Rate | Difficulty (b) | Interpretation |
|-----------|----------------|----------------|
| 90% | -2.20 | Very easy |
| 75% | -1.10 | Easy |
| 50% | 0.00 | Average |
| 25% | 1.10 | Hard |
| 10% | 2.20 | Very hard |

### Step 3: Difficulty Band Thresholds

Based on anchor distribution, define three bands:

| Band | IRT Difficulty (logit) | Elo Rating | Bagrut Equivalent |
|------|------------------------|------------|-------------------|
| Easy | b < -0.75 | Elo < 1350 | Part A Q1-Q2, >70% pass rate |
| Medium | -0.75 <= b <= 0.50 | 1350-1600 | Part A Q3-Q5, 35-70% pass rate |
| Hard | b > 0.50 | Elo > 1600 | Part B, <35% pass rate |

### Step 4: Concurrent Calibration (Post-Pilot)

Once pilot data is available:

1. Fix anchor item difficulties at their Bagrut-derived values
2. Estimate non-anchor item difficulties via Rasch logit: `b_raw = -ln(p_correct / (1 - p_correct))`
3. Compute anchor scale shift: `shift = mean(b_raw_anchor - b_stored_anchor)`
4. Apply shift to all non-anchor items: `b_calibrated = b_raw - shift`

This links Cena's internal scale to the Bagrut reference frame.

Reference: Kolen & Brennan (2014), *Test Equating, Scaling, and Linking*, ch. 6.

### Step 5: Elo ↔ IRT Conversion

Cena uses Elo ratings for runtime item selection (continuous, self-updating). The IRT logit scale is used for psychometric calibration. Conversion:

```
irt_difficulty = (elo - 1500) / 200
elo = irt_difficulty * 200 + 1500
```

Elo 1500 = IRT b=0 (average difficulty). Each 200 Elo points = 1 logit.

## Predictive Validity

### Metric

Given a student's Cena mastery >= 0.85 on all concepts within a Bagrut topic, predict the student will pass (>= 55%) on that topic's questions in the actual exam.

### Baseline Target

70% prediction accuracy (minimum acceptable). Target improves to 80%+ after Phase B calibration with real student-exam outcome pairs.

### Required Sample

N >= 200 students who both (a) used Cena extensively and (b) took the Bagrut exam. Available after the first full exam cycle following pilot.

## Data Files

| File | Purpose |
|------|---------|
| `config/bagrut-anchors.json` | Anchor items with pass rates, difficulties, band assignments |
| `scripts/bagrut-taxonomy.json` | Bagrut exam topic taxonomy mapped to Cena concept IDs |
| `scripts/bagrut-calibration.py` | Calibration pipeline (validate, calibrate, report) |

## Limitations

1. Pass rates are national averages — school-level variance is high
2. Rasch model assumes equal discrimination (a=1.0) — 2PL requires N >= 500 per item
3. Concurrent calibration assumes stable anchor difficulty across years — validate annually
4. Predictive validity requires exam outcome data not yet available (post-pilot)
