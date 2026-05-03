# REV-001: Rotate Firebase Service Account Key & Purge Git History

**Priority:** P0 -- EMERGENCY (credential on disk with full GCP private key)
**Blocked by:** None
**Blocks:** All production deployment
**Estimated effort:** 2 hours
**Source:** System Review 2026-03-28 -- Cyber Officer 2 (Finding F-APP-03), DevOps Engineer (Finding #1)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

The file `scripts/firebase/service-account-key.json` contains a full GCP service account private key (`-----BEGIN PRIVATE KEY-----`) for the `cena-platform` project. While `.gitignore` blocks `**/service-account-key.json`, the key exists on disk and may exist in git history. A compromised key grants full Firebase Admin SDK access -- user management, Firestore, Cloud Functions invocation. For an education platform handling student PII, this is an immediate credential compromise risk.

## Architect's Decision

Do NOT simply delete the file. The key must be **rotated at the provider** (Google Cloud Console) so the existing key is invalid even if extracted from git history. The path reference in `appsettings.json` must be replaced with environment-variable injection to prevent future path disclosure.

## Subtasks

### REV-001.1: Rotate the Key in Google Cloud Console

**Manual steps (not automatable):**
- [ ] Go to Google Cloud Console > IAM & Admin > Service Accounts
- [ ] Select the `cena-platform` service account
- [ ] Under "Keys" tab, delete the existing key
- [ ] Generate a new key and download it
- [ ] Store the new key in a secrets manager (AWS Secrets Manager, GCP Secret Manager, or a `.env` file that is **never committed**)

### REV-001.2: Check & Purge Git History

**Commands:**
```bash
# Check if the key was ever committed
git log --all --full-history -- "**/service-account-key.json"

# If commits found, purge with git-filter-repo (preferred over BFG)
pip install git-filter-repo
git filter-repo --path scripts/firebase/service-account-key.json --invert-paths

# Force push all branches (requires team coordination)
git push --force --all
```

**Acceptance:**
- [ ] `git log --all -- "**/service-account-key.json"` returns zero commits
- [ ] Old key is disabled in GCP Console (returns 401 on any API call)
- [ ] New key works with Firebase Admin SDK

### REV-001.3: Replace Path Reference with Environment Variable

**Files to modify:**
- `src/api/Cena.Api.Host/appsettings.json` -- remove `ServiceAccountKeyPath`
- `src/api/Cena.Api.Host/Program.cs` -- read from env var
- `src/actors/Cena.Actors.Host/Program.cs` -- read from env var if Firebase used

**Pattern:**
```csharp
// BEFORE (path disclosure in committed config)
"ServiceAccountKeyPath": "../../scripts/firebase/service-account-key.json"

// AFTER (environment variable injection)
var keyPath = builder.Configuration["Firebase:ServiceAccountKeyPath"]
    ?? Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_KEY_PATH")
    ?? throw new InvalidOperationException(
        "Firebase service account key path not configured. " +
        "Set FIREBASE_SERVICE_ACCOUNT_KEY_PATH environment variable.");
```

**Acceptance:**
- [ ] No file path for service account key exists in any committed config file
- [ ] Application fails fast with clear error message if env var is missing
- [ ] `.env.example` documents the required variable (without the actual value)

### REV-001.4: Harden .gitignore

**File to modify:** `.gitignore`

**Acceptance:**
- [ ] `.gitignore` contains: `**/service-account-key*.json`, `**/*.pem`, `**/*.key`, `**/*.p12`
- [ ] `.gitignore` contains: `.env`, `.env.local`, `.env.production`
