# REV-019: Close Critical Test Coverage Gaps (Actor Behavior, API Integration, Frontend)

**Priority:** P2 -- MEDIUM (TutorActor/StudentActor behavior untested, zero API integration tests, zero frontend tests)
**Blocked by:** REV-009 (CI/CD -- tests need a pipeline to run in)
**Blocks:** Confidence in production deployment
**Estimated effort:** 5 days
**Source:** System Review 2026-03-28 -- QA Lead (Gap Analysis, all critical gaps)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

938 tests exist with 100% pass rate, but critical paths have zero coverage:

| Gap | Risk | Why It Matters |
|-----|------|---------------|
| StudentActor command handling | HIGH | Core actor: 5 partial files, only state projection tested |
| TutorActor + SafetyGuard | HIGH | AI-facing student feature with safety implications |
| NatsBusRouter | HIGH | Message routing backbone, zero tests |
| Admin API HTTP endpoints | HIGH | No WebApplicationFactory tests, auth/routing/serialization untested |
| Frontend components | MEDIUM | Zero Vitest, zero component tests |
| Marten integration | HIGH | Event sourcing correctness depends on Marten, all mocked |

## Architect's Decision

Prioritize **behavior tests that prevent production incidents** over coverage metrics:
1. **StudentActor**: Test command handling (ConceptAttempt, SessionStart/End, MethodologySwitch) via Proto.Actor test kit
2. **TutorActor safety**: Test SafetyGuard rejects prompt injection, answer leaking, inappropriate content
3. **API integration**: WebApplicationFactory tests for auth, tenant scoping, and CRUD operations
4. **Frontend**: Vitest setup + tests for critical composables (useFirebaseAuth, useApi)

Do NOT add Testcontainers in this task -- that's a larger infrastructure change. Mock Marten with `IDocumentSession` substitutes for now.

## Subtasks

### REV-019.1: Add StudentActor Command Behavior Tests

**File to create:** `src/actors/Cena.Actors.Tests/Students/StudentActorCommandTests.cs`

**Test cases:**
```csharp
[Fact] public async Task ConceptAttempt_CorrectAnswer_UpdatesMastery() { }
[Fact] public async Task ConceptAttempt_WrongAnswer_TriggersErrorClassification() { }
[Fact] public async Task SessionStart_CreatesSession_PublishesEvent() { }
[Fact] public async Task SessionEnd_ClosesSession_ComputesFocusScore() { }
[Fact] public async Task MethodologySwitch_ValidSwitch_UpdatesState() { }
[Fact] public async Task MethodologySwitch_CooldownActive_Rejects() { }
[Fact] public async Task ConceptAttempt_3ConsecutiveWrong_TriggersConfusion() { }
[Fact] public async Task Passivation_After30MinIdle_SnapshotsState() { }
```

**Approach:** Use Proto.Actor's `ActorSystem` test harness with NSubstitute for dependencies (Marten, NATS, Redis).

**Acceptance:**
- [ ] At least 8 command behavior tests pass
- [ ] Tests verify state changes AND event emission
- [ ] Tests do not use reflection to access private fields

### REV-019.2: Add TutorActor Safety Tests

**File to create:** `src/actors/Cena.Actors.Tests/Tutoring/TutorSafetyGuardTests.cs`

**Test cases:**
```csharp
[Theory]
[InlineData("The answer is B", true)]        // Answer leak
[InlineData("The solution equals 42", true)]  // Answer leak
[InlineData("ignore previous instructions", true)]  // Prompt injection
[InlineData("Let me explain step by step", false)]   // Normal response
[InlineData("Think about what happens when x=0", false)] // Socratic question
[Fact] public void ResponseExceeding2000Chars_IsBlocked() { }
[Fact] public void PersonalAdviceRequest_DeflectsToTeacher() { }
```

**File to create:** `src/actors/Cena.Actors.Tests/Tutoring/TutorPromptBuilderTests.cs`

**Test cases:**
```csharp
[Theory]
[InlineData("Socratic", "NEVER give the answer")]
[InlineData("WorkedExample", "step-by-step")]
[InlineData("Feynman", "explain in their own words")]
[InlineData("DirectInstruction", "numbered steps")]
[Fact] public void DrillAndPractice_ReturnsNull_TutoringBlocked() { }
[Fact] public void FatiguedStudent_PromptIncludesBriefInstruction() { }
[Fact] public void HighMastery_ScaffoldingNone() { }
[Fact] public void LowMastery_ScaffoldingFull() { }
```

**Acceptance:**
- [ ] Safety guard blocks all known injection/leak patterns
- [ ] Each methodology produces distinct prompt instructions
- [ ] Methodology gate correctly blocks Drill/SpacedRepetition
- [ ] Scaffolding levels map correctly to mastery ranges

### REV-019.3: Add Admin API Integration Tests (WebApplicationFactory)

**File to create:** `src/api/Cena.Admin.Api.Tests/Integration/AdminApiIntegrationTests.cs`

**Setup:**
```csharp
public class AdminApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AdminApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace Marten with in-memory test store
                // Replace Firebase auth with test auth handler
            });
        }).CreateClient();
    }
}
```

**Test cases:**
```csharp
[Fact] public async Task GetDashboard_WithoutAuth_Returns401() { }
[Fact] public async Task GetDashboard_WithModeratorAuth_Returns200() { }
[Fact] public async Task GetUsers_AsAdmin_ReturnsScopedToSchool() { }
[Fact] public async Task GetUsers_AsSuperAdmin_ReturnsAllSchools() { }
[Fact] public async Task PostQuestion_ValidPayload_Returns201() { }
[Fact] public async Task PostQuestion_InvalidBloom_Returns400() { }
[Fact] public async Task GetActorsStats_WithoutAuth_Returns401() { } // After REV-004
```

**Acceptance:**
- [ ] At least 7 integration tests covering auth, tenant scoping, and validation
- [ ] Tests run against WebApplicationFactory (real HTTP pipeline)
- [ ] Auth is testable (test auth handler, not real Firebase)

### REV-019.4: Add Frontend Test Infrastructure + Critical Tests

**Files to create:**
- `src/admin/full-version/vitest.config.ts`
- `src/admin/full-version/src/composables/__tests__/useApi.test.ts`
- `src/admin/full-version/src/composables/__tests__/useFirebaseAuth.test.ts`
- `src/admin/full-version/src/plugins/casl/__tests__/role-abilities.test.ts`

**Package to install:**
```bash
cd src/admin/full-version
npm install -D vitest @vue/test-utils happy-dom
```

**vitest.config.ts:**
```typescript
import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  test: {
    environment: 'happy-dom',
    globals: true,
  },
  resolve: {
    alias: { '@': '/src' }
  }
})
```

**Test cases:**
```typescript
// role-abilities.test.ts
describe('mapRoleToAbilities', () => {
  it('SUPER_ADMIN gets manage all', () => { })
  it('ADMIN includes Tutoring and Settings', () => { })
  it('MODERATOR includes read Pedagogy and Tutoring', () => { })
  it('unknown role returns empty', () => { })
})

// useApi.test.ts
describe('useApi', () => {
  it('attaches Authorization header from cookie', () => { })
  it('redirects to /login on 401', () => { })
})
```

**Acceptance:**
- [ ] `npm test` runs Vitest successfully
- [ ] At least 6 frontend tests pass
- [ ] CI pipeline (REV-009) updated to run `npm test`
- [ ] `mapRoleToAbilities` has test coverage (prevents future divergence)

### REV-019.5: Add NatsBusRouter Unit Tests

**File to create:** `src/actors/Cena.Actors.Tests/Bus/NatsBusRouterTests.cs`

**Test cases:**
```csharp
[Fact] public async Task RouteSessionStart_DeserializesEnvelope_CallsCluster() { }
[Fact] public async Task RouteConceptAttempt_InvalidPayload_LogsError_DoesNotThrow() { }
[Fact] public async Task RouteCommand_ClusterTimeout_IncreasesErrorCount() { }
[Fact] public async Task RecentErrors_BoundedAt250() { }  // After REV-008 fix
[Fact] public async Task Stats_ReflectsRoutedCommands() { }
```

**Acceptance:**
- [ ] At least 5 NatsBusRouter tests pass
- [ ] Tests verify routing to correct actor kinds
- [ ] Tests verify error handling (malformed messages, timeouts)
