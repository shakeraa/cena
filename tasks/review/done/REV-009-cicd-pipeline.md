# REV-009: Establish CI/CD Pipeline (GitHub Actions)

**Priority:** P1 -- HIGH (zero automated testing, scanning, or deployment gates)
**Blocked by:** None
**Blocks:** Automated deployments, security scanning, PR quality gates
**Estimated effort:** 2 days
**Source:** System Review 2026-03-28 -- DevOps Engineer (Finding #3), QA Lead (Finding #4), Cyber Officer 2 (F-CICD-01)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

The project has 938 passing tests but no CI pipeline. Tests only run when someone remembers to run them locally. No automated build verification, no dependency scanning, no lint gates. Every merge to main is blind.

## Architect's Decision

Start with a **minimal, high-value pipeline** that covers the 80% case:
1. Build verification (both .NET hosts compile)
2. Test execution (938 existing tests)
3. Frontend lint (TypeScript check + ESLint)
4. Dependency vulnerability scanning (Dependabot + `dotnet list package --vulnerable`)

Do NOT attempt full Docker build + push + deploy in this task. That belongs in INF-007 after Dockerfiles exist (REV-010).

## Subtasks

### REV-009.1: Backend Build & Test Pipeline

**File to create:** `.github/workflows/backend.yml`

```yaml
name: Backend CI
on:
  push:
    branches: [main]
    paths: ['src/actors/**', 'src/api/**', 'src/shared/**', 'src/emulator/**']
  pull_request:
    branches: [main]
    paths: ['src/actors/**', 'src/api/**', 'src/shared/**', 'src/emulator/**']

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 9
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Restore dependencies
        run: |
          dotnet restore src/actors/Cena.Actors.Host/Cena.Actors.Host.csproj
          dotnet restore src/api/Cena.Api.Host/Cena.Api.Host.csproj
          dotnet restore src/emulator/Cena.Emulator.csproj

      - name: Build (Release)
        run: |
          dotnet build src/actors/Cena.Actors.Host/ -c Release --no-restore
          dotnet build src/api/Cena.Api.Host/ -c Release --no-restore
          dotnet build src/emulator/ -c Release --no-restore

      - name: Run Actor Tests
        run: dotnet test src/actors/Cena.Actors.Tests/ -c Release --no-build --logger "trx;LogFileName=actors.trx"

      - name: Run Admin API Tests
        run: dotnet test src/api/Cena.Admin.Api.Tests/ -c Release --no-build --logger "trx;LogFileName=admin-api.trx"

      - name: Check for vulnerable packages
        run: |
          dotnet list src/actors/Cena.Actors.Host/ package --vulnerable --include-transitive 2>&1 | tee vuln.txt
          if grep -q "has the following vulnerable packages" vuln.txt; then
            echo "::warning::Vulnerable packages detected"
          fi

      - name: Upload test results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results
          path: '**/*.trx'
```

**Acceptance:**
- [ ] Pipeline triggers on push/PR to main for backend paths
- [ ] All 938 tests run and pass
- [ ] Build failure blocks merge
- [ ] Vulnerable packages are flagged (warning, not blocking initially)

### REV-009.2: Frontend Lint Pipeline

**File to create:** `.github/workflows/frontend.yml`

```yaml
name: Frontend CI
on:
  push:
    branches: [main]
    paths: ['src/admin/full-version/**']
  pull_request:
    branches: [main]
    paths: ['src/admin/full-version/**']

jobs:
  lint:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: src/admin/full-version
    steps:
      - uses: actions/checkout@v4

      - name: Setup Node 20
        uses: actions/setup-node@v4
        with:
          node-version: '20'
          cache: 'npm'
          cache-dependency-path: src/admin/full-version/package-lock.json

      - name: Install dependencies
        run: npm ci

      - name: TypeScript check
        run: npx vue-tsc --noEmit

      - name: ESLint
        run: npm run lint

      - name: Build
        run: npm run build
```

**Acceptance:**
- [ ] Pipeline triggers on push/PR for frontend paths
- [ ] TypeScript errors block merge
- [ ] ESLint violations block merge
- [ ] Production build completes without errors

### REV-009.3: Enable Dependabot

**File to create:** `.github/dependabot.yml`

```yaml
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/src/actors/Cena.Actors.Host"
    schedule:
      interval: "weekly"
    open-pull-requests-limit: 5

  - package-ecosystem: "npm"
    directory: "/src/admin/full-version"
    schedule:
      interval: "weekly"
    open-pull-requests-limit: 5

  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "monthly"
```

**Acceptance:**
- [ ] Dependabot creates PRs for outdated/vulnerable NuGet packages
- [ ] Dependabot creates PRs for outdated/vulnerable npm packages
- [ ] Dependabot updates GitHub Actions versions
