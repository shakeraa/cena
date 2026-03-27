# MST-011: Scaffolding Level Determiner

**Priority:** P1 — controls how much help the LLM gives the student
**Blocked by:** MST-005 (effective mastery)
**Estimated effort:** 1-2 days (S)
**Contract:** `docs/mastery-engine-architecture.md` section 5 step 4
**Research ref:** `docs/mastery-measurement-research.md` section 3.3

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

The scaffolding level determines how much instructional support the LLM provides during a learning session. It maps from the student's effective mastery and prerequisite satisfaction index (PSI) to one of four scaffolding levels: full (worked example), partial (faded example), hints on request, or no scaffolding (independent practice). The scaffolding level is used by the LLM prompt construction system to select the appropriate system prompt variant. This is a simple, stateless mapping function.

## Subtasks

### MST-011.1: Scaffolding Level Enum and Mapper

**Files to create:**
- `src/Cena.Pedagogy/Domain/Services/ScaffoldingService.cs`
- `src/Cena.Pedagogy/Domain/ScaffoldingLevel.cs`

**Acceptance:**
- [ ] `ScaffoldingLevel` enum: `Full`, `Partial`, `HintsOnly`, `None`
- [ ] `ScaffoldingService.DetermineLevel(float effectiveMastery, float psi) → ScaffoldingLevel`
- [ ] Rules (from architecture section 5 step 4):
  - `mastery < 0.20 AND psi < 0.80` → `Full` (worked example)
  - `mastery < 0.40 AND psi >= 0.80` → `Partial` (faded example)
  - `mastery < 0.70` → `HintsOnly`
  - `mastery >= 0.70` → `None` (independent practice)
- [ ] Edge case: `mastery < 0.20 AND psi >= 0.80` → `Partial` (prereqs OK, just new material)
- [ ] Method is `static`, pure function

### MST-011.2: Scaffolding Metadata for LLM Prompts

**Files to create/modify:**
- `src/Cena.Pedagogy/Domain/Services/ScaffoldingService.cs` (add `GetScaffoldingMetadata`)

**Acceptance:**
- [ ] `ScaffoldingService.GetScaffoldingMetadata(ScaffoldingLevel level) → ScaffoldingMetadata`
- [ ] `ScaffoldingMetadata` record: `Level`, `PromptVariant` (string key), `ShowWorkedExample` (bool), `ShowHintButton` (bool), `MaxHints` (int), `RevealAnswer` (bool)
- [ ] `Full`: `ShowWorkedExample=true`, `ShowHintButton=true`, `MaxHints=3`, `RevealAnswer=true`
- [ ] `Partial`: `ShowWorkedExample=false`, `ShowHintButton=true`, `MaxHints=2`, `RevealAnswer=true`
- [ ] `HintsOnly`: `ShowWorkedExample=false`, `ShowHintButton=true`, `MaxHints=1`, `RevealAnswer=false`
- [ ] `None`: `ShowWorkedExample=false`, `ShowHintButton=false`, `MaxHints=0`, `RevealAnswer=false`

**Test:**
```csharp
[Theory]
[InlineData(0.15f, 0.60f, ScaffoldingLevel.Full)]       // low mastery + weak prereqs
[InlineData(0.15f, 0.85f, ScaffoldingLevel.Partial)]     // low mastery + strong prereqs
[InlineData(0.35f, 0.90f, ScaffoldingLevel.Partial)]     // developing + strong prereqs
[InlineData(0.55f, 0.90f, ScaffoldingLevel.HintsOnly)]   // mid mastery
[InlineData(0.75f, 0.95f, ScaffoldingLevel.None)]         // proficient
[InlineData(0.92f, 1.00f, ScaffoldingLevel.None)]         // mastered
public void DetermineLevel_CorrectMapping(float mastery, float psi, ScaffoldingLevel expected)
{
    var level = ScaffoldingService.DetermineLevel(mastery, psi);
    Assert.Equal(expected, level);
}

[Fact]
public void GetMetadata_FullScaffolding_ShowsWorkedExample()
{
    var meta = ScaffoldingService.GetScaffoldingMetadata(ScaffoldingLevel.Full);

    Assert.True(meta.ShowWorkedExample);
    Assert.True(meta.ShowHintButton);
    Assert.Equal(3, meta.MaxHints);
    Assert.True(meta.RevealAnswer);
}

[Fact]
public void GetMetadata_NoScaffolding_NoHelp()
{
    var meta = ScaffoldingService.GetScaffoldingMetadata(ScaffoldingLevel.None);

    Assert.False(meta.ShowWorkedExample);
    Assert.False(meta.ShowHintButton);
    Assert.Equal(0, meta.MaxHints);
    Assert.False(meta.RevealAnswer);
}

[Fact]
public void DetermineLevel_BoundaryAt70_TransitionsToNone()
{
    Assert.Equal(ScaffoldingLevel.HintsOnly,
        ScaffoldingService.DetermineLevel(0.69f, 0.90f));
    Assert.Equal(ScaffoldingLevel.None,
        ScaffoldingService.DetermineLevel(0.70f, 0.90f));
}
```
