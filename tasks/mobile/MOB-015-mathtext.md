# MOB-015: MathText Widget — RTL Hebrew/Arabic + LTR LaTeX Bidi

**Priority:** P0 — blocks math question display
**Blocked by:** MOB-001 (scaffold)
**Estimated effort:** 2 days
**Contract:** `contracts/llm/prompt-templates.py` (HEBREW_MATH_GLOSSARY)

---

## Context

Math expressions are LTR (LaTeX) embedded in RTL Hebrew/Arabic text. The MathText widget must handle bidirectional text rendering correctly: Hebrew text flows RTL, but `x^2 + 3x - 4 = 0` renders LTR within the RTL context.

## Subtasks

### MOB-015.1: MathText Widget Implementation
- [ ] Renders LaTeX via `flutter_math_fork` or `katex_flutter`
- [ ] Inline math: `$expression$` within Hebrew text
- [ ] Display math: `$$expression$$` centered on own line
- [ ] RTL context: math expressions wrapped in LTR embed (`‪...‬`)

### MOB-015.2: Bidi Text Handling
- [ ] Hebrew text + LaTeX: "פתור את המשוואה $x^2 + 3x = 0$"
- [ ] Arabic text + LaTeX: "حل المعادلة $x^2 + 3x = 0$"
- [ ] Mixed: Hebrew label, LaTeX expression, Hebrew unit -> correct order
- [ ] No visual artifacts at RTL/LTR boundaries

### MOB-015.3: Accessibility + Fallback
- [ ] Screen reader: LaTeX read as "x squared plus 3 x equals zero"
- [ ] LaTeX parse failure: show raw LaTeX text with WARNING log
- [ ] Large expressions: horizontal scroll if wider than screen

**Test:**
```dart
testWidgets('MathText renders inline LaTeX in Hebrew', (tester) async {
  await tester.pumpWidget(MathText(text: 'פתור: \$x^2 + 3x = 0\$'));
  expect(find.byType(Math), findsOneWidget);
});
```

---

## Definition of Done
- [ ] LaTeX renders correctly in RTL Hebrew and Arabic context
- [ ] Bidi boundaries clean (no visual artifacts)
- [ ] PR reviewed by architect
