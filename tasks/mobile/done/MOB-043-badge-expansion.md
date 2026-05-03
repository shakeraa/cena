# MOB-043: Badge Expansion — Subject Mastery, Behavior, Hidden

**Priority:** P2.6 — Medium
**Phase:** 2 — Engagement Layer (Months 3-5)
**Source:** gamification-motivation-research.md Section 5
**Blocked by:** MOB-008 (Gamification)
**Estimated effort:** M (1-3 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Subtasks

### MOB-043.1: Badge Categories (expand from 10 → 30+)
- [ ] **Subject Mastery** (8 badges): per-subject mastery milestones (25%, 50%, 75%, 100% of concepts)
- [ ] **Learning Behavior** (8 badges): streak milestones, review consistency, methodology explorer
- [ ] **Social** (6 badges): helper, team player, peer tutor, class contributor
- [ ] **Meta/Hidden** (8+ badges): secret achievements discovered by exploration

### MOB-043.2: Badge Rarity Tiers
- [ ] Common (60% of students earn): basic milestones
- [ ] Uncommon (30%): moderate effort
- [ ] Rare (10%): significant achievement
- [ ] Epic (3%): exceptional dedication
- [ ] Secret (discovery): hidden criteria, revealed on earn

### MOB-043.3: Badge Showcase
- [ ] Profile screen: badge display case (grid layout)
- [ ] "Pin" up to 3 badges for public display
- [ ] Badge detail view: criteria, date earned, rarity percentage
- [ ] Silhouettes for unearned badges (discovery motivation)

### MOB-043.4: Badge Events
- [ ] `BadgeEarned_V1` already exists — extend with rarity tier and category
- [ ] `BadgeDisplayed_V1`: when student pins a badge
- [ ] Social feed integration: badge earns appear in class feed (opt-in)

**Definition of Done:**
- 30+ badges across 4 categories with 5 rarity tiers
- Badge showcase on profile with pin/display feature
- Hidden badges with silhouette placeholders
- Rarity percentages computed from cohort data

**Test:**
```dart
test('Badge catalogue has 30+ badges across 4 categories', () {
  final catalogue = BadgeCatalogue.all;
  expect(catalogue.length, greaterThanOrEqualTo(30));
  expect(catalogue.where((b) => b.category == BadgeCategory.subjectMastery).length, greaterThanOrEqualTo(8));
  expect(catalogue.where((b) => b.rarity == BadgeRarity.secret).length, greaterThanOrEqualTo(4));
});
```
