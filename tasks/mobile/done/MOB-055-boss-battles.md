# MOB-055: Boss Battles — Narrative-Framed Assessments

**Priority:** P4.8 — Medium
**Phase:** 4 — Advanced Intelligence (Months 8-12)
**Source:** gamification-motivation-research.md Section 7
**Blocked by:** MOB-042 (Quest System), MOB-050 (Celebration System)
**Estimated effort:** L (3-6 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Major assessments (unit tests, chapter reviews) framed as "boss battles" in the quest narrative. Transforms anxiety-inducing tests into exciting challenges.

## Subtasks

### MOB-055.1: Boss Battle Flow
- [ ] Triggered when student has mastered 80%+ of a module's concepts
- [ ] Framed as: "The [Module Name] Challenge awaits!" with dramatic intro
- [ ] 10-15 questions covering all module concepts at Bloom's Apply/Analyze level
- [ ] No hints available (assessment mode)
- [ ] Timer optional (no timer default, opt-in for test-prep practice)

### MOB-055.2: Battle Mechanics
- [ ] HP bar for the "boss" (decreases with each correct answer)
- [ ] Student has 3 "lives" (wrong answers deplete lives)
- [ ] Power-ups earned from daily quests: "Extra Life", "50/50 Eliminator"
- [ ] Boss defeated when HP reaches 0 (all questions correct or lives remain)

### MOB-055.3: Boss Battle Results
- [ ] Victory: Tier 5 (epic) celebration + unique badge + significant XP
- [ ] Defeat: encouraging "Almost there! You got X/Y right. Review these concepts and try again."
- [ ] No penalty for defeat — can retry after reviewing weak concepts
- [ ] Results feed into BKT and identify remaining gaps

### MOB-055.4: Narrative Integration
- [ ] Boss battles are end-of-chapter milestones in monthly campaigns
- [ ] Each subject has a unique boss aesthetic
- [ ] Boss difficulty reflects actual module content difficulty

**Definition of Done:**
- Boss battles trigger at 80%+ module mastery
- HP bar and lives mechanic make assessment feel like gameplay
- Defeat is encouraging, victory is epic
- Results feed into mastery engine

**Test:**
```dart
test('Boss battle triggers at 80% module mastery', () {
  final module = Module(concepts: 10, masteredCount: 8);
  expect(module.isBossBattleReady, isTrue);

  final lowModule = Module(concepts: 10, masteredCount: 6);
  expect(lowModule.isBossBattleReady, isFalse);
});
```
