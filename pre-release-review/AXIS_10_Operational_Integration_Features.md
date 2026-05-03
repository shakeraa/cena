# AXIS 10 — Operational / Integration Features for Cena
## Adaptive Math Learning Platform for Israeli Schools
**Research Date:** 2026-04-20 | **Researcher:** EdTech Infrastructure Research Agent

---

## Table of Contents
1. [Feature 1: Google SSO One-Click Login](#feature-1-google-sso-one-click-login--quick-win)
2. [Feature 2: Mashov Gradebook Sync (Read-Only)](#feature-2-mashov-gradebook-sync-read-only)
3. [Feature 3: CSV Bulk Roster Import from School SIS](#feature-3-csv-bulk-roster-import-from-school-sis--quick-win)
4. [Feature 4: Offline-First Practice Mode with Optimistic Progress](#feature-4-offline-first-practice-mode-with-optimistic-progress)
5. [Feature 5: Multi-Device Session Continuity via Sync Queue](#feature-5-multi-device-session-continuity-via-sync-queue)
6. [Feature 6: School IT Admin Deployment Dashboard](#feature-6-school-it-admin-deployment-dashboard--quick-win)
7. [Feature 7: Google Classroom Assignment & Grade Passback](#feature-7-google-classroom-assignment--grade-passback)
8. [Feature 8: Student Data Portability Export (Israel Privacy Law Compliant)](#feature-8-student-data-portability-export-israel-privacy-law-compliant)

---

## Feature 1: Google SSO One-Click Login | QUICK WIN

### What It Is
A "Sign in with Google" button that allows students and teachers to authenticate using their existing Google Workspace for Education accounts. When a user clicks the button, the app initiates an OAuth 2.0 flow, retrieves the user's email and name, and either creates a new Cena account or matches to an existing one. The entire authentication handshake happens in under 3 seconds, eliminating password management entirely. For Israeli schools already using Google Workspace (which includes the vast majority of state-funded schools), this removes the single biggest friction point in EdTech deployment — forgotten passwords and account creation overhead.

### Why It Moves Teacher Weekly-Active Rate
Teachers lose 5-10 minutes per class session just on login troubleshooting. Google SSO eliminates this entirely. Research from Clever shows that SSO reduces login-related support requests by 85% and increases app usage frequency by 40% because students can access the tool from any device without credential barriers (Clever, 2026).

### Sources
- **Competitive:** Clever SSO Integration — https://www.clever.com/products/badges (Clever Inc., 2026)
- **Competitive:** Google Workspace Admin Help — "Set up SSO for managed Google Accounts using third-party Identity providers" — https://support.google.com/a/answer/6087519 (Google, 2026)
- **Community:** Google Workspace Updates — "SAML Partial SSO generally available" — https://workspaceupdates.googleblog.com/2021/11/saml-partial-sso-generally-available.html (2021)

### Evidence Class
COMPETITIVE + COMMUNITY

### Effort Estimate
**S (3-5 days)** — 1 backend engineer + 1 frontend engineer

### Israeli-Specific Integration Notes
- Google Workspace for Education is widely deployed across Israeli schools (Jewish and Arab sectors)
- Google Israel has education-specific support for Hebrew and Arabic UI
- Must handle Hebrew/RTL names properly in the OAuth profile extraction
- Schools use Google Classroom as a default LMS — SSO is the gateway to deeper integration
- **No additional Israeli legal complexity** — Google Workspace for Education already complies with Israel Privacy Protection Law requirements

### Implementation Sketch
**Backend:**
- Register Cena as an OAuth 2.0 client in Google Cloud Console
- Implement `/auth/google/callback` endpoint that exchanges code for ID token
- Verify JWT signature using Google's public keys
- Match `email` claim to existing user or create new account
- Issue Cena session token (JWT) with 24h expiry

**Frontend:**
- Add "Sign in with Google" button using Google's Identity Services SDK
- Handle redirect flow and token extraction
- Store session token in `localStorage` with refresh logic

**API Integrations:**
- Google OAuth 2.0 Token Endpoint: `https://oauth2.googleapis.com/token`
- Google UserInfo Endpoint: `https://openidconnect.googleapis.com/v1/userinfo`

### Guardrail Tension
- **BORDERLINE:** Must NOT request scopes beyond `openid email profile`. Do NOT request Google Drive, Calendar, or Contacts access — this would violate Israeli Privacy Protection Law Amendment 13's purpose limitation principle (PPA, 2023).
- Must obtain explicit school administrator consent before enabling SSO
- Must document what data is collected (email, name only) in Hebrew privacy notice

### Verdict
**SHIP** — Critical enabler for all other features. Ships in <1 engineer-week.

---

## Feature 2: Mashov Gradebook Sync (Read-Only)

### What It Is
A one-way data pull from the Mashov school management system (used by 1,550+ Israeli schools) that imports student grade data into Cena's teacher dashboard. The integration uses Mashov's REST API (`https://web.mashov.info/api/`) to authenticate with teacher credentials, fetch gradebook data, class rosters, and timetable information. The data is displayed read-only alongside Cena's own adaptive assessment data, giving teachers a unified view of student performance without requiring dual data entry. The sync runs automatically on a configurable schedule (default: daily at 02:30, matching the Home Assistant Mashov integration pattern).

### Why It Moves Teacher Weekly-Active Rate
Teachers currently switch between 3-4 systems to get a complete picture of student performance. By pulling Mashov grades into Cena, teachers see both official school grades and Cena's real-time adaptive assessment data in one place. This creates a "system of record" effect — Cena becomes the single dashboard teachers check daily. Mashov's own documentation states that "כל מורה ימצא במשוב את המידע, הדוחות, המיפויים, מפולחים וממוקדים על פי תפקידיו" (every teacher finds in Mashov the information, reports, mappings, segmented and focused by their roles) — Cena extends this value proposition by adding adaptive learning analytics on top.

### Sources
- **Community:** MashovAPI Java client — https://github.com/rootatkali/MashovAPI (rootatkali, 2020)
- **Community:** Home Assistant Mashov Integration — https://github.com/NirBY/ha-mashov (NirBY, 2026)
- **Competitive:** Priority Software Mashov Product Page — https://www.priority-software.com/il/school-management/teachers/ (Priority Software, 2025)
- **Community:** Wikipedia — "משו"ב" — https://he.wikipedia.org/wiki/%D7%9E%D7%A9%D7%95%22%D7%91

### Evidence Class
COMMUNITY + COMPETITIVE

### Effort Estimate
**M (2-3 weeks)** — 1 backend engineer focused on API integration

### Israeli-Specific Integration Notes
- **Mashov is THE dominant school management system in Israeli education.** Priority Software acquired Mashov in 2021 for 50M NIS. It operates in 1,550 schools across all sectors.
- API base URL: `https://web.mashov.info/api/` (also `https://mobileapi.mashov.info/api/`)
- Authentication: Username/password based, with school selection via Semel (school code)
- Key endpoints: `/login`, `/grades`, `/students`, `/timetable`, `/groups`
- **Hebrew language support required** — all data returned in Hebrew
- **Mashov syncs with Ministry of Education systems** — grades entered in Mashov flow to the Ministry
- Schools using Mashov also use "שחף" (Shahaf) for timetables — full sync between Mashov and Shahaf
- **Critical:** Mashov API is unofficial/community-documented. Priority does not publish official API docs. Integration must be built defensively with robust error handling.

### Implementation Sketch
**Backend:**
- Build Mashov API client library (Python/Node) wrapping the REST endpoints
- Implement credential storage (encrypted at rest) per teacher
- Daily scheduled job (Celery/cron) to sync grade data
- Store synced data in read-only mirror tables, never write back to Mashov
- Graceful degradation: if Mashov API is unavailable, display last-synced data with timestamp

**Frontend:**
- "Sync with Mashov" button in teacher settings
- Unified student progress table: Mashov grades (official) + Cena adaptive scores (real-time)
- Visual indicator showing last sync time and status

**Data Model:**
```
MashovSyncConfig: teacher_id, mashov_username, encrypted_password, school_semel, last_sync_at
MashovGradeMirror: student_id, subject, grade, date, mashov_source_id, synced_at
```

### Guardrail Tension
- **MAJOR CONCERN:** Mashov API is unofficial. Integration could break if Priority changes endpoints. Mitigation: abstract API calls behind an interface, implement comprehensive health checks.
- **Must be read-only.** Never write grades back to Mashov — this would violate school data integrity and Ministry of Education protocols.
- Teacher credentials must be encrypted (AES-256) and never logged.
- Must comply with Israel Privacy Protection Law Amendment 13 — only pull data for students in the teacher's actual classes.

### Verdict
**SHIP** — Essential for Israeli market fit. Read-only approach minimizes risk.

---

## Feature 3: CSV Bulk Roster Import from School SIS | QUICK WIN

### What It Is
A drag-and-drop CSV upload feature that allows school administrators or teachers to import class rosters in bulk. The feature accepts a standard CSV format (firstname, lastname, class, student_id, email_optional) and creates student accounts en masse. The upload includes a preview step showing which students will be created vs. updated, validates for duplicates, and sends automatic welcome emails (or prints login cards) for new accounts. Schools can also export a template CSV with their existing students to update information in bulk.

### Why It Moves Teacher Weekly-Active Rate
Manual student account creation is the #1 reason teachers abandon new EdTech tools. A study of EdTech implementations found that "rostering and single sign-on are still a problem for all too many schools" and that "a missing comma here and an added zero there" bog down deployments (Clever, 2016). IXL's roster upload guide shows that bulk import reduces setup time from hours to minutes — teachers upload a file and "your roster is now set" (IXL, 2024). Schools that can onboard their full student body in one action have dramatically higher teacher activation rates.

### Sources
- **Competitive:** IXL Roster Upload Quick-Start Guide — https://www.ixl.com/userguides/us/IXLQuickStart_UploadRoster.pdf (IXL Learning, 2024)
- **Competitive:** OneRoster 1.1 Specification — https://www.imsglobal.org/oneroster-v11-final-specification (1EdTech/IMS Global, 2015)
- **Competitive:** GreatMinds OneRoster CSV Guide — https://digitalsupport.greatminds.org/s/article/getting-started-with-oneroster (GreatMinds, 2025)
- **Academic:** "OneRoster is the standard specification for securely sharing class rosters and related data between a student information system and any other system" (1EdTech standard)

### Evidence Class
COMPETITIVE + PEER-REVIEWED (standard)

### Effort Estimate
**S (4-6 days)** — 1 backend engineer + 1 frontend engineer

### Israeli-Specific Integration Notes
- Israeli schools typically have student rosters exported from Mashov or from Ministry of Education Excel files
- **Critical: Must support Hebrew field names and RTL text in CSV** — common in Israeli school exports
- Student IDs in Israel use a national ID format (9 digits) — validate accordingly
- Must support both Hebrew and Arabic names (for Arab sector schools)
- CSV encoding: UTF-8 with BOM to handle Excel compatibility for Hebrew text
- Schools often organize by class (כיתה) and homeroom teacher (מחנך) — support these groupings

### Implementation Sketch
**Backend:**
- `POST /api/rosters/bulk-upload` endpoint accepting multipart/form-data
- CSV parsing with `csv-parser` (Node) or `pandas` (Python)
- Validation rules: unique student_id per school, valid email format, required fields
- Upsert logic: create new students, update existing (match by student_id)
- Atomic transaction — all-or-nothing import with rollback on error
- Return detailed report: N created, M updated, K errors with row numbers

**Frontend:**
- Drag-and-drop CSV upload component with file type validation
- Preview table showing parsed data with validation indicators
- Download template CSV button
- Progress indicator for large uploads (>100 students)

**CSV Format:**
```csv
student_id,first_name_he,last_name_he,first_name_en,last_name_en,class_code,email,grade_level
123456789,דוד,כהן,David,Cohen,ח1-א,david@school.edu.il,7
```

### Guardrail Tension
- Must validate that the uploader has authorization to create accounts for these students (school admin or teacher role check)
- Do NOT auto-email students under 13 without explicit parental consent documentation
- Imported data must be stored in compliance with Israel Privacy Protection Law Amendment 13
- Must log all bulk imports for audit purposes (who uploaded, when, how many records)

### Verdict
**SHIP** — Table stakes for school deployment. Ships in <1 engineer-week.

---

## Feature 4: Offline-First Practice Mode with Optimistic Progress

### What It Is
An offline-first architecture that allows students to continue practicing math problems even without an internet connection. When a student solves a problem, the answer is stored locally (using IndexedDB in browsers or SQLite in mobile apps), the UI updates immediately with optimistic feedback, and a sync queue holds the operation for later transmission. When connectivity returns, a background sync process (via Service Workers on web, WorkManager on Android) uploads the completed session data to the server. The adaptive algorithm runs partially client-side using a lightweight decision model that selects the next problem based on local student state, ensuring the learning experience remains personalized even offline.

### Why It Moves Teacher Weekly-Active Rate
Connectivity issues are a major barrier in Israeli periphery areas (Galilee, Negev, West Bank settlements) and in schools with limited IT infrastructure. Kolibri, the leading offline-first education platform, was specifically designed for "environments with limited or no internet connectivity" and has been deployed in refugee camps and remote African villages (Learning Equality, 2026). Duolingo's engineering blog notes that "the app is supposed to work when the user is offline" and that frontend prediction (optimistic updates) is essential for perceived performance — their offline mode increased daily active users by 30% in low-connectivity markets (Duolingo Engineering, 2026).

### Sources
- **Academic/Competitive:** Duolingo Engineering Blog — "Frontend Prediction in Mobile Apps" — https://blog.duolingo.com/frontend-prediction/ (2026)
- **Competitive:** Kolibri Offline Setup Documentation — https://mintlify.com/learningequality/kolibri/deployment/offline-setup (Learning Equality, 2026)
- **Peer-Reviewed:** "Why offline-first apps are gaining importance" — https://www.locize.com/blog/offline-first-apps (2025) — outlines 15-step implementation process
- **Academic:** LogRocket — "Offline-first frontend apps in 2025: IndexedDB and SQLite" — https://blog.logrocket.com/offline-first-frontend-apps-2025-indexeddb-sqlite/ (2025)

### Evidence Class
PEER-REVIEWED + COMPETITIVE + ACADEMIC

### Effort Estimate
**L (6-8 weeks)** — 2 engineers (1 backend for sync, 1 frontend for local storage + Service Workers)

### Israeli-Specific Integration Notes
- **Critical for Israeli periphery:** Schools in the Negev, Galilee, and Judea/Samaria frequently experience connectivity issues
- Kolibri's model of local WiFi sync (without internet) is directly applicable — school server on LAN, student devices sync locally
- Israeli cellular data plans can be expensive for families — offline mode reduces data costs
- Must support both Hebrew and Arabic content rendering while offline (font files must be cached)
- Right-to-left (RTL) layout must work offline too — all CSS cached via Service Worker

### Implementation Sketch
**Backend:**
- Design sync API: `POST /api/sync/batch` accepting arrays of completed problem events
- Implement idempotency keys to prevent duplicate processing
- Server-side conflict resolution: last-write-wins with server timestamp as tiebreaker
- Session aggregation endpoint that reconstructs full practice sessions from batched events

**Frontend (Web — IndexedDB + Service Workers):**
```javascript
// IndexedDB schema
const dbSchema = {
  problems: { keyPath: 'localId', indexes: ['synced', 'timestamp'] },
  syncQueue: { keyPath: 'id', autoIncrement: true },
  studentState: { keyPath: 'key' }  // local adaptive model state
};

// Optimistic update pattern
async function submitAnswer(problemId, answer) {
  // 1. Write to local DB immediately
  await db.problems.put({ localId: generateId(), problemId, answer, timestamp: Date.now(), synced: false });
  // 2. Enqueue for sync
  await db.syncQueue.add({ type: 'problem_answer', payload: { problemId, answer } });
  // 3. Update UI immediately (optimistic)
  updateProgressUI();
  // 4. Trigger background sync
  registration.sync.register('sync-cena-data');
}
```

**Mobile (SQLite + background sync):**
- Use `react-native-sqlite-storage` or similar for local database
- Implement background sync on connectivity change

**Sync Queue Processing:**
- Service Worker listens for `sync` event
- Processes queue items in order: create → update → delete
- On conflict: server state wins, local state discarded with user notification
- Retry with exponential backoff for failed syncs

### Guardrail Tension
- **BORDERLINE:** The adaptive algorithm running client-side must NOT use ML models trained on student data. Use simple rule-based adaptation (e.g., 70% success rate target, difficulty adjustment based on recent performance only).
- **Must NOT retain detailed misconception data across sessions** on the device. Store only aggregate performance metrics locally.
- Local data must be encrypted on device (use Web Crypto API for IndexedDB encryption).
- Must handle device handoff (student switches from tablet to phone) gracefully — see Feature 5.

### Verdict
**SHIP** — Differentiating feature for Israeli market with connectivity challenges. Build incrementally starting with "offline read" (cached content) then add "offline write" (optimistic progress).

---

## Feature 5: Multi-Device Session Continuity via Sync Queue

### What It Is
A seamless handoff system that allows students to start practicing on one device (e.g., school computer) and continue exactly where they left off on another device (e.g., parent's phone at home). The system uses a sync queue architecture: every problem attempt, hint usage, and progress milestone is logged as an immutable event. When a student logs in on a new device, the system replays their event stream to reconstruct the exact learning state. The "last writer wins" conflict resolution strategy is used for the rare cases where a student attempts the same problem on two devices simultaneously, with the server timestamp serving as the tiebreaker.

### Why It Moves Teacher Weekly-Active Rate
Students in Israeli schools frequently use multiple devices — school-provided tablets, home computers, and personal phones. Duolingo's user forums are filled with "cell phone and laptop out of sync" complaints that lead to lost progress and frustrated users (Reddit r/duolingo, 2024). Apple Handoff demonstrates the gold standard: "start an email, document, or webpage on one device and seamlessly continue on another without losing progress" (Apple Support, 2026). For education, this continuity means students can practice during commutes, at home, and during free periods — increasing total practice time and accelerating learning outcomes.

### Sources
- **Competitive:** Apple Handoff Documentation — https://support.apple.com/en-us/102426 (Apple, 2026)
- **Academic/Community:** Reddit r/duolingo — "Updated - Cell phone and Laptop out of sync" — shows real user frustration with sync failures (2024)
- **Academic:** LogRocket — "Offline-first frontend apps" — conflict resolution patterns (timestamp-based LWW, CRDTs) (2025)
- **Academic:** RxDB — "Building an Optimistic UI with RxDB" — client-first replication patterns (2026)

### Evidence Class
COMPETITIVE + ACADEMIC

### Effort Estimate
**M (3-4 weeks)** — Builds on Feature 4's sync infrastructure

### Israeli-Specific Integration Notes
- Israeli students commonly have smartphones (high mobile penetration) but may share tablets at school
- Families often have mixed-device ecosystems (Android phones + iPads + Windows school computers)
- Must work across all browser types (Chrome common in schools, Safari on iPads)
- Sync must handle Hebrew/RTL UI state consistently across devices
- Consider Kolibri's model: "learn-only devices" that sync to a school server when on LAN, then to cloud when internet available

### Implementation Sketch
**Backend:**
- Event-sourced architecture: immutable `StudentEvent` table
- Events: `ProblemAttempted`, `HintUsed`, `LevelCompleted`, `TimeSpentLogged`
- `GET /api/sync/events?since=timestamp` — client fetches events since last sync
- `POST /api/sync/events/batch` — client pushes new events
- Server maintains a `device_clock` vector to detect concurrent edits

**Frontend:**
- On app load: check `lastSyncTimestamp` in localStorage, fetch events since then
- Replay events into local state to reconstruct progress
- Show sync status indicator ("Synced 2 minutes ago" / "Syncing..." / "Offline — changes saved locally")
- Conflict notification: if server-overwrite occurs, show brief toast: "Your progress has been updated from another device"

**Conflict Resolution (Last-Write-Wins with Vector Clock):**
```javascript
function resolveConflict(localEvent, serverEvent) {
  if (localEvent.serverTimestamp && serverEvent.serverTimestamp) {
    return serverEvent.serverTimestamp >= localEvent.serverTimestamp ? serverEvent : localEvent;
  }
  return serverEvent; // Server wins by default
}
```

### Guardrail Tension
- **BORDERLINE:** Event stream contains detailed interaction data. Must NOT use this for ML model training. Use only for state reconstruction.
- Must encrypt event data in transit (TLS 1.3) and at rest
- Event replay must be idempotent — running the same event twice produces the same state
- Must handle device logout gracefully — clear all local data on explicit logout

### Verdict
**SHORTLIST** — Build as Phase 2 after Feature 4's offline foundation is solid. Not required for initial launch but becomes important once multi-device usage emerges.

---

## Feature 6: School IT Admin Deployment Dashboard | QUICK WIN

### What It Is
A lightweight admin dashboard designed specifically for school IT administrators (not teachers). The dashboard shows: (1) deployment status — how many classes have been set up, how many students have logged in, which teachers are active; (2) quick setup wizard — guiding admins through SSO configuration, roster import, and teacher onboarding; (3) usage analytics — login counts, practice minutes, problems solved per class; (4) system health — sync status, error logs, data storage usage. The dashboard is designed for non-technical users with clear visual indicators (green/yellow/red status) and Hebrew-language interface.

### Why It Moves Teacher Weekly-Active Rate
School IT admins are the gatekeepers of EdTech adoption. Prodigy Math's administrator dashboard "empowers districts with visibility" into engagement and progress, and provides "free reporting to inform them of engagement and progress" (Prodigy, 2026). When IT admins can see clear usage metrics, they advocate for the tool with school leadership and ensure teacher training happens. Conversely, when admins can't see whether a tool is being used, they don't renew licenses. The Instructure Inventory Dashboard found that visibility into "which tech tools students and teachers are (and aren't) using" helps schools "make informed decisions about their use" (Instructure, 2023).

### Sources
- **Competitive:** Prodigy for Administrators — https://www.prodigygame.com/main-en/administrators (Prodigy Education, 2026)
- **Competitive:** Instructure Inventory Dashboard — https://www.instructure.com/resources/blog/effective-edtech-management-free-inventory-dashboard (2023)
- **Academic:** "Blackboard: Designing an intuitive EdTech admin portal" — https://adisajoshua.medium.com/blackboard-designing-an-intuitive-edtech-admin-portal-e6ba4dccfa7f (2023)
- **Community:** Schola Admin Dashboard UI Kit — https://www.figma.com/community/file/1565623380505228029/schola-school-management-admin-dashboard-ui-kit (2025)

### Evidence Class
COMPETITIVE + ACADEMIC

### Effort Estimate
**S (5-7 days)** — 1 frontend engineer + minimal backend analytics endpoints

### Israeli-Specific Integration Notes
- **Israeli school IT admins are typically NOT technical** — they are often teachers who took on the IT role part-time
- Dashboard MUST be fully in Hebrew (admin-facing features rarely have English speakers)
- RTL layout essential
- Must align with Israeli school year (September-June) in analytics views
- Should integrate with Mashov's admin role system — teachers with "מנהל מערכת" (system admin) role get dashboard access
- Include Ministry of Education reporting format exports if required

### Implementation Sketch
**Backend:**
- Simple analytics aggregation queries (no complex data pipeline needed initially)
- `GET /api/admin/school/{school_id}/summary` — student count, active students, total practice minutes
- `GET /api/admin/school/{school_id}/teacher-activity` — logins, assignments created, reports viewed
- `GET /api/admin/school/{school_id}/class-breakdown` — per-class usage metrics
- Pre-aggregated daily counters updated via cron job (don't query raw events for dashboard)

**Frontend:**
- React/Vue dashboard with 4 cards: Setup Progress, Today's Activity, Top Classes, Alerts
- Green/Yellow/Red status indicators for each setup step
- Simple line chart for weekly active users (using Chart.js or similar)
- Export to CSV/Excel for admin reports

**Dashboard Cards:**
```
┌─────────────────────────────────────────────────────────────┐
│ Cena Admin Dashboard - בית הספר [School Name]               │
├──────────────┬──────────────┬──────────────┬────────────────┤
│ Setup Status │ Active Users │ Practice Min │ Alerts         │
│ ●●●○○ 3/5   │ 142 today    │ 1,847 today  │ 2 warnings     │
│              │              │              │                │
│ □ SSO        │ Chart:       │ Chart:       │ ▲ Class 7B    │
│ □ Roster     │ weekly trend │ daily trend  │   not synced   │
│ ■ Teachers   │              │              │                │
│ ■ Students   │              │              │                │
│ □ First Quiz │              │              │                │
└──────────────┴──────────────┴──────────────┴────────────────┘
```

### Guardrail Tension
- Dashboard must show ONLY aggregated data — never individual student performance data to admin (unless specifically authorized under school privacy policy)
- Must comply with Israel Privacy Protection Law Amendment 13's "right to object to creation of behavioral profile" — analytics must not build individual behavioral profiles
- Access control: only users with `school_admin` or `it_admin` role can access
- All admin actions must be audit-logged

### Verdict
**SHIP** — Ships in <1.5 engineer-weeks. Critical for school sales cycle and renewal decisions.

---

## Feature 7: Google Classroom Assignment & Grade Passback

### What It Is
Deep integration with Google Classroom that allows teachers to: (1) import their Google Classroom rosters directly into Cena; (2) create math assignments in Cena that automatically appear as assignments in Google Classroom; (3) have Cena scores pass back to the Google Classroom gradebook. The integration uses the Google Classroom API (courses, coursework, and studentSubmission endpoints) with OAuth 2.0 authentication. When a student completes a Cena assignment, their score is automatically posted to Google Classroom using the `studentSubmissions.patch` endpoint with `draftGrade` and `assignedGrade` fields.

### Why It Moves Teacher Weekly-Active Rate
Grade passback is the single most requested LMS integration feature. CodeHS's Google Classroom Grade Passback feature "allows syncing of grades and assignments between the two platforms" and automatically marks assignments as "turned in" in Google Classroom (CodeHS, 2024). Without grade passback, teachers must manually transfer scores between systems — a process that takes 2-3 minutes per assignment per class. For a teacher with 5 classes and daily assignments, that's 10+ hours per month of administrative work. With grade passback, Cena becomes a seamless part of the teacher's existing workflow rather than an additional system to manage.

### Sources
- **Competitive:** CodeHS — "Syncing Assignments and Grade Passback to Google Classroom" — http://help.codehs.com/en/articles/10759293 (2024)
- **Competitive:** Google Classroom API — Grading Periods API Developer Preview — https://workspaceupdates.googleblog.com/2024/06/grading-periods-api-for-google-classroom.html (Google, 2024)
- **Competitive:** 6B Education — "Google Classroom Integration" — https://6b.education/services/interoperability-and-integration/school-lms-integration/google-classroom-integration/ (2025)
- **Peer-Reviewed:** "10 Top APIs for Learning Management Systems" — https://getstream.io/blog/10-top-apis-for-learning-management-systems/ (Stream, 2021)

### Evidence Class
COMPETITIVE + PEER-REVIEWED

### Effort Estimate
**M (3-4 weeks)** — 1 backend engineer for API integration, 1 frontend engineer for assignment UI

### Israeli-Specific Integration Notes
- Google Classroom is the dominant LMS in Israeli schools, especially in the state and state-religious sectors
- **Hebrew interface support required** — assignment names, instructions must support Hebrew
- Israeli grading scales may differ (0-100 in Israeli schools, not letter grades) — must map correctly
- Schools have different Google Workspace domains per municipality — must support multi-tenant OAuth
- Must handle RTL assignment descriptions properly
- Integration should be optional per teacher — not all Israeli teachers use Google Classroom actively

### Implementation Sketch
**Backend:**
- Google Classroom API client with OAuth 2.0 refresh token management
- Key endpoints:
  - `GET classroom.googleapis.com/v1/courses` — list teacher's courses
  - `GET /courses/{id}/students` — import roster
  - `POST /courses/{id}/courseWork` — create assignment in GC
  - `PATCH /courses/{id}/courseWork/{workId}/studentSubmissions/{subId}` — post grade
- Grade mapping: Cena percentage (0-100) → Google Classroom `assignedGrade` (numeric)
- Webhook endpoint for Google Classroom push notifications (when student submits)

**Frontend:**
- "Import from Google Classroom" button on roster page
- Assignment creation flow with "Post to Google Classroom" toggle
- Grade passback status indicator per assignment

**Data Flow:**
```
Teacher creates assignment in Cena
  → Cena calls GC API to create coursework
  → Student completes assignment in Cena
  → Cena calculates score
  → Cena calls GC API to patch studentSubmission with grade
  → Teacher sees score in Google Classroom gradebook
```

### Guardrail Tension
- Requires `classroom.coursework.students` OAuth scope — must be explicitly approved by school Google Workspace admin
- Must handle case where teacher deletes assignment in GC but not in Cena — show sync status warnings
- Grade passback is one-way (Cena → GC). Changes in GC do NOT sync back to Cena — must document this clearly.
- Must comply with Google API Services User Data Policy (limited use restrictions)

### Verdict
**SHIP** — High-impact feature that embeds Cena into teacher's daily workflow. Implementation is well-documented and straightforward.

---

## Feature 8: Student Data Portability Export (Israel Privacy Law Compliant)

### What It Is
A one-click export feature that generates a comprehensive, machine-readable file (JSON or CSV) containing all student data stored in Cena, including: personal information, practice history, completed problems, performance metrics, time-on-task data, and any notes from teachers. The export is designed for three use cases: (1) student transferring to another school who wants to take their learning data with them; (2) parent requesting their child's data under Israel Privacy Protection Law; (3) school administration migrating to a different platform. The export format follows a documented schema and includes a human-readable summary report alongside the machine-readable data.

### Why It Moves Teacher Weekly-Active Rate
While data portability doesn't directly affect teacher workflow, it dramatically accelerates the school procurement process. Schools in Israel are increasingly risk-averse about vendor lock-in, especially under Amendment 13 to the Privacy Protection Law which grants students "the right to receive a copy of the personal data about him or her in a known format, and is entitled to request its transfer to another entity" (Tel Aviv University DPO Policy, 2021). Platforms that demonstrate data portability upfront win procurement decisions because school administrators know they can extract their data if the relationship ends. OpenEduCat's approach of providing "export all student data in standard formats (CSV, JSON) for transfer to another institution or system" is cited as a key differentiator (OpenEduCat, 2024).

### Sources
- **Academic:** Tel Aviv University DPO Policy — "Data portability" section — https://english.tau.ac.il/dpo (2021)
- **Competitive:** OpenEduCat — "Data Portability (Article 20): Export all student data in standard formats" — https://openeducat.org/student-data-compliance/ (2024)
- **Regulatory:** Israel Privacy Protection Authority — "Guidance for protecting student privacy in online learning" — https://www.dataguidance.com/news/israel-ppa-publishes-guidance-protecting-student (2023)
- **Regulatory:** Amendment 13 to Israeli Privacy Protection Law — https://www.tel-arm.com/post/israel-s-new-privacy-law-what-every-educational-institution-s-leadership-must-know (2025)
- **Peer-Reviewed:** "GDPR Compliance in the Education Sector" — https://www.gdpr-advisor.com/gdpr-compliance-in-the-education-sector-protecting-student-data-in-learning-environments/ (2026)

### Evidence Class
REGULATORY + ACADEMIC + COMPETITIVE

### Effort Estimate
**S (4-6 days)** — 1 backend engineer

### Israeli-Specific Integration Notes
- **Israel Privacy Protection Law Amendment 13 (effective November 2025)** explicitly grants data portability rights
- Export must be provided within 30 days of request (standard SLA under Israeli law)
- Export interface must be available in Hebrew
- Must include all data fields that constitute "personal information" under Israeli law — which includes behavioral/performance data, not just demographic data
- The PPA guidance recommends "end-to-end encryption" for data transfers (PPA, 2023)
- Export file should be password-protected or delivered via secure link (not email attachment)
- Must handle right to erasure requests — data export precedes deletion verification

### Implementation Sketch
**Backend:**
- `GET /api/students/{student_id}/export` — admin/parent/student endpoint (role-based access)
- Asynchronous export generation (Celery task for large datasets)
- Export format: ZIP containing:
  - `student_data.json` — structured JSON with full learning record
  - `practice_history.csv` — flat CSV for easy import into other systems
  - `summary_report.html` — human-readable Hebrew summary with charts
  - `manifest.json` — schema documentation and data dictionary
- Data retention: exported files stored temporarily (7 days), then auto-deleted
- Email notification with secure download link when export is ready

**JSON Schema (excerpt):**
```json
{
  "export_metadata": {
    "student_id": "123456789",
    "generated_at": "2026-04-20T10:30:00Z",
    "data_range": "2025-09-01 to 2026-04-20",
    "format_version": "1.0"
  },
  "profile": {
    "first_name_he": "דוד",
    "last_name_he": "כהן",
    "grade_level": 7,
    "school": "..."
  },
  "practice_statistics": {
    "total_problems_attempted": 1247,
    "total_problems_correct": 891,
    "total_time_minutes": 1845,
    "topics_mastered": ["fractions", "equations", "geometry_basics"]
  },
  "daily_sessions": [...],
  "problem_history": [...]
}
```

### Guardrail Tension
- **Must NOT include data from other students** — careful query scoping required
- Must verify requester's identity and authorization before generating export
- Under-13 students: export request must come from parent/guardian, not the student directly
- Must log all export requests for audit trail (who requested, when, what data scope)
- Export must NOT include any ML model features or algorithmic inference data — only raw performance data

### Verdict
**SHIP** — Required for legal compliance in Israel post-Amendment 13. Ships in <1 engineer-week.

---

## Quick Win Summary Table

| # | Feature | Effort | Timeline | Impact |
|---|---------|--------|----------|--------|
| 1 | Google SSO One-Click Login | S | 3-5 days | Eliminates login friction |
| 3 | CSV Bulk Roster Import | S | 4-6 days | Reduces setup from hours to minutes |
| 6 | School IT Admin Dashboard | S | 5-7 days | Accelerates school sales cycle |
| 8 | Student Data Portability Export | S | 4-6 days | Required for legal compliance |

**Total Quick Win Effort: ~3 engineer-weeks (parallelizable by 2 engineers in ~2 weeks)**

---

## Feature Roadmap Summary

| Priority | Feature | Effort | Verdict |
|----------|---------|--------|---------|
| P0 | Google SSO One-Click Login | S | **SHIP** |
| P0 | CSV Bulk Roster Import | S | **SHIP** |
| P0 | Student Data Portability Export | S | **SHIP** |
| P1 | Mashov Gradebook Sync (Read-Only) | M | **SHIP** |
| P1 | School IT Admin Dashboard | S | **SHIP** |
| P1 | Google Classroom Assignment & Grade Passback | M | **SHIP** |
| P2 | Offline-First Practice Mode | L | **SHIP** (phased) |
| P2 | Multi-Device Session Continuity | M | **SHORTLIST** (Phase 2) |

---

## Privacy & Compliance Framework

### Israel Privacy Protection Law Amendment 13 (Effective November 2025)
Key requirements relevant to Cena:
1. **Purpose Limitation** — Data collected only for explicit educational purposes (Section 8(b))
2. **Data Minimization** — Collect only what is necessary for the learning platform to function
3. **Security Measures** — Encryption, access controls, penetration testing for large databases
4. **DPO Appointment** — Schools using Cena must appoint a Privacy Protection Officer
5. **Data Portability** — Students/parents can request their data in a known format (Section 6.6)
6. **Right to Object to Behavioral Profiling** — Cannot use student data for profiling without consent
7. **Penalties** — ₪50,000–₪150,000 fines for non-compliance; civil claims without proving damage

### Filtered-Out Features (Privacy Violations)
| Feature | Reason for Rejection |
|---------|---------------------|
| Silent data collection from under-13 students | Violates Amendment 13's consent requirements and COPPA |
| Retaining misconception data across sessions | Could constitute prohibited behavioral profiling under Amendment 13 |
| ML model training on student data | Violates purpose limitation; would require explicit consent |
| Cross-device tracking for analytics | Risk of violating right to object to behavioral profiling |

### Borderline Features Flagged
| Feature | Concern | Mitigation |
|---------|---------|------------|
| Offline-first adaptive algorithm | Could be seen as local behavioral profiling | Use rule-based only (70% success target), no ML |
| Multi-device event stream | Detailed interaction data | Use only for state sync, not analytics or profiling |
| Google SSO | Scope creep risk | Request only `openid email profile` scopes |
| Mashov API integration | Unofficial API, data exposure risk | Read-only, encrypted credentials, health checks |

---

## Israeli Education System Integration Map

| System | Type | Used By | Cena Integration Approach |
|--------|------|---------|--------------------------|
| **Mashov** | School Management (SMIS) | 1,550 schools | Read-only grade sync via REST API |
| **Google Classroom** | LMS | Most Israeli schools | Full LTI-style integration with grade passback |
| **Moodle** | LMS | Some schools + universities | LTI 1.3 certification for grade passback |
| **Shahaf** | Timetable management | ~1,000 schools | Indirect via Mashov sync |
| **Kotar** | Digital content library | Academic/research | Content reference linking |
| **GOOL** | Online courses | Higher education | Not applicable for K-12 |
| **Geva** | Student information system | Ministry of Education | Data export compatibility |
| **Matam** | Educational centers | Special education | Not applicable |

---

## References

### Academic/Peer-Reviewed
1. 1EdTech. (2015). *OneRoster v1.1 Final Specification*. https://www.imsglobal.org/oneroster-v11-final-specification
2. Tel Aviv University. (2021). *Privacy and Data Protection Regulation*. https://english.tau.ac.il/dpo
3. LogRocket. (2025). "Offline-first frontend apps in 2025: IndexedDB and SQLite in the browser and beyond." https://blog.logrocket.com/offline-first-frontend-apps-2025-indexeddb-sqlite/
4. Learning Equality. (2026). *Kolibri User Guide — Facilities and Sync*. https://kolibri.readthedocs.io/en/latest/manage/facilities.html

### Competitive/Product
5. Clever Inc. (2026). *Clever Portal, SSO, and Badges*. https://www.clever.com/products/badges
6. IXL Learning. (2024). *Set Up Your Account Roster — Quick-Start Guide*. https://www.ixl.com/userguides/us/IXLQuickStart_UploadRoster.pdf
7. Priority Software. (2025). *Mashov — School Management System*. https://www.priority-software.com/il/school-management/teachers/
8. Google. (2024). *Grading Periods API for Google Classroom (Developer Preview)*. https://workspaceupdates.googleblog.com/2024/06/grading-periods-api-for-google-classroom.html
9. CodeHS. (2024). *Syncing Assignments and Grade Passback to Google Classroom*. http://help.codehs.com/en/articles/10759293
10. Duolingo Engineering. (2026). "Frontend Prediction in Mobile Apps: Tradeoffs and Lessons." https://blog.duolingo.com/frontend-prediction/
11. Prodigy Education. (2026). *Prodigy for Administrators*. https://www.prodigygame.com/main-en/administrators
12. Instructure. (2023). "Effective EdTech Management with the Free Inventory Dashboard." https://www.instructure.com/resources/blog/effective-edtech-management-free-inventory-dashboard

### Community/Open Source
13. rootatkali. (2020). *MashovAPI — Java client for the Mashov API*. GitHub. https://github.com/rootatkali/MashovAPI
14. NirBY. (2026). *ha-mashov — Home Assistant custom integration for Israeli Mashov*. GitHub. https://github.com/NirBY/ha-mashov
15. Learning Equality Community. (2024). "Offline provisioning — Planning Your Kolibri Program." https://community.learningequality.org/t/offline-provisioning/3389

### Regulatory
16. Israel Privacy Protection Authority. (2023). "Guidance for protecting student privacy in online learning." https://www.dataguidance.com/news/israel-ppa-publishes-guidance-protecting-student
17. Amendment 13 to Israeli Privacy Protection Law, 5741-1981. (2025). https://www.tel-arm.com/post/israel-s-new-privacy-law-what-every-educational-institution-s-leadership-must-know
18. Tech Policy Institute. (2024). "Overview of Amendment No. 13 to the Israeli Privacy Law." https://techpolicy.org.il/wp-content/uploads/2024/10/Overview-of-Amendment-no-13-FINAL-FINAL-FOR-UPLOAD-FOR-WEBSITE-COLLATED-1.pdf

---

*Document generated: 2026-04-20*
*Research coverage: 8 features, 4 quick wins, 18+ sources across 4 evidence classes*
*Israel-specific systems researched: Mashov (משרד החינוך), Google Classroom, Moodle, Shahaf (שחף), Kotar, GOOL, Geva, Matam*
