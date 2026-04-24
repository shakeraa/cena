# MOB-033: Onboarding V2 — Try Before Signup

**Priority:** P1.4 — Critical
**Phase:** 1 — Foundation (Months 1-3)
**Source:** onboarding-first-time-ux-research.md Sections 1-2
**Blocked by:** MOB-013 (Onboarding)
**Estimated effort:** M (1-3 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Current onboarding is 5 pages: Welcome → Subjects → Grade → Diagnostic → Ready. Research shows inserting a sample question BEFORE signup cuts time-to-first-interaction from ~90s to ~25s. Duolingo, Headspace, and TikTok all demonstrate this yields 3-5x better Day 7 retention.

## Subtasks

### MOB-033.1: Reorder Onboarding Flow
- [ ] New flow: Welcome → **Try a Question** → Subject Selection → Grade → Goals → Diagnostic → Ready
- [ ] "Try a Question" shows one engaging, medium-difficulty question immediately
- [ ] No login required for the sample question — anonymous session
- [ ] Celebrate correct answer with full reward animation

### MOB-033.2: Diagnostic Reframing
- [ ] Rename diagnostic from "Assessment" to "Discovery Tour" (Hebrew: "סיור גילוי")
- [ ] Show mini knowledge graph lighting up in real-time as student answers
- [ ] Frame as exploration, not judgment: "Let's discover what you already know"
- [ ] Never use the word "test" or "exam"

### MOB-033.3: Goal Setting Screen
- [ ] "What do you want to achieve?" with visual cards (Bagrut prep, homework help, get ahead, review)
- [ ] Daily time commitment: 5min / 10min / 15min / 20min selector
- [ ] Visual, not form-like — large tappable cards, not dropdowns

### MOB-033.4: Role-Based Routing
- [ ] Welcome screen: 3 large cards — Student / Teacher / Parent
- [ ] Student: 7-page flow (as above)
- [ ] Teacher: 4 pages (Welcome → School → Classes → Dashboard tour)
- [ ] Parent: 4 pages (Welcome → Link child → Privacy overview → Dashboard tour)

### MOB-033.5: Notification Permission Timing
- [ ] Do NOT request notification permission during onboarding
- [ ] Request after first completed learning session
- [ ] Frame as: "Want a daily reminder to keep your streak?"

**Definition of Done:**
- Student experiences a sample question within 25 seconds of first app open
- Diagnostic framed as "Discovery Tour" with live knowledge graph reveal
- Notification permission deferred to post-first-session
- Three role-specific onboarding paths

**Test:**
```dart
testWidgets('Sample question appears before signup', (tester) async {
  await tester.pumpWidget(OnboardingFlow());
  await tester.tap(find.text('Get Started'));
  await tester.pumpAndSettle();
  // Second screen should be the sample question, not subject selection
  expect(find.byType(SampleQuestionCard), findsOneWidget);
  expect(find.byType(SubjectSelector), findsNothing);
});
```
