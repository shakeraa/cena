# 01 — Navigation & Information Architecture

## Top-Level Sitemap

```
/                              → Redirect to /home if authed, /login otherwise
/login                         → Sign-in (Firebase)
/register                      → Registration
/forgot-password               → Password reset
/onboarding                    → First-run wizard (role, goals, subjects, diagnostic)

/home                          → Home dashboard (default post-login)
/session                       → Active session launcher
/session/:sessionId            → Live session (full-screen immersive)
/session/:sessionId/summary    → Post-session summary
/session/:sessionId/replay     → Replay a finished session

/challenges                    → Challenges hub
/challenges/boss               → Boss battles menu
/challenges/daily              → Daily challenge
/challenges/chains/:chainId    → Card chain progress

/progress                      → Progress dashboard (mastery, streaks, time)
/progress/sessions             → Session history
/progress/sessions/:sessionId  → Session detail/replay
/progress/mastery              → Mastery breakdown
/progress/time                 → Learning time analytics

/knowledge-graph               → Concept graph & skill tree
/knowledge-graph/concept/:id   → Concept detail

/tutor                         → AI tutor chat
/tutor/:threadId               → Specific conversation thread

/social                        → Class feed
/social/peers                  → Peer solutions browser
/social/leaderboard            → Class / friend / global leaderboard
/social/friends                → Friends list (web-only)

/diagrams/:diagramId           → Diagram viewer (deep-linkable)

/notifications                 → Notification center
/profile                       → Profile overview
/profile/edit                  → Edit profile
/settings                      → Preferences
/settings/account              → Account & security
/settings/appearance           → Theme, language, accessibility
/settings/notifications        → Notification preferences
/settings/privacy              → Data & privacy (GDPR)
```

---

## Navigation Structure (Sidebar)

File: `src/student/full-version/src/navigation/vertical/index.ts`

```ts
export default [
  { heading: 'Learn' },
  {
    title: 'Home',
    icon: { icon: 'tabler-home' },
    to: { name: 'home' },
  },
  {
    title: 'Start Session',
    icon: { icon: 'tabler-player-play' },
    to: { name: 'session' },
    badge: { text: 'Resume', color: 'primary' }, // shown only if active session exists
  },
  {
    title: 'AI Tutor',
    icon: { icon: 'tabler-message-chatbot' },
    to: { name: 'tutor' },
  },

  { heading: 'Practice' },
  {
    title: 'Challenges',
    icon: { icon: 'tabler-swords' },
    children: [
      { title: 'Daily Challenge', to: { name: 'challenges-daily' } },
      { title: 'Boss Battles',   to: { name: 'challenges-boss' } },
      { title: 'Card Chains',    to: { name: 'challenges' } },
    ],
  },
  {
    title: 'Knowledge Graph',
    icon: { icon: 'tabler-affiliate' },
    to: { name: 'knowledge-graph' },
  },

  { heading: 'Progress' },
  {
    title: 'Overview',
    icon: { icon: 'tabler-chart-line' },
    to: { name: 'progress' },
  },
  {
    title: 'Session History',
    icon: { icon: 'tabler-history' },
    to: { name: 'progress-sessions' },
  },
  {
    title: 'Mastery',
    icon: { icon: 'tabler-target' },
    to: { name: 'progress-mastery' },
  },
  {
    title: 'Learning Time',
    icon: { icon: 'tabler-clock' },
    to: { name: 'progress-time' },
  },

  { heading: 'Community' },
  {
    title: 'Class Feed',
    icon: { icon: 'tabler-users' },
    to: { name: 'social' },
  },
  {
    title: 'Leaderboard',
    icon: { icon: 'tabler-trophy' },
    to: { name: 'social-leaderboard' },
  },
  {
    title: 'Peer Solutions',
    icon: { icon: 'tabler-bulb' },
    to: { name: 'social-peers' },
  },

  { heading: 'Account' },
  {
    title: 'Notifications',
    icon: { icon: 'tabler-bell' },
    to: { name: 'notifications' },
    badge: { text: '{unreadCount}', color: 'error' }, // dynamic
  },
  {
    title: 'Profile',
    icon: { icon: 'tabler-user' },
    to: { name: 'profile' },
  },
  {
    title: 'Settings',
    icon: { icon: 'tabler-settings' },
    to: { name: 'settings' },
  },
]
```

**Behavior**:
- Sidebar is collapsible (same as admin).
- Mobile / narrow viewport shows a bottom nav bar with the 5 most common destinations: Home, Session, Tutor, Progress, Profile.
- Headings are hidden when the sidebar is collapsed; only icons remain.
- The "Start Session" item shows a `Resume` badge only if `/api/sessions/active` returns a session.
- Notifications badge is dynamic and updates over SignalR.

---

## Route Meta (Guards, Layout, Title)

All routes declare metadata that drives layout, auth, and breadcrumbs:

```ts
{
  path: '/session/:sessionId',
  name: 'session-live',
  component: () => import('@/pages/session/[sessionId].vue'),
  meta: {
    layout: 'blank',             // full-screen immersive
    requiresAuth: true,
    title: 'Session',
    hideSidebar: true,
    hideAppBar: true,
    breadcrumbs: false,
  },
}
```

| Meta flag | Purpose |
|-----------|---------|
| `requiresAuth` | Redirect to `/login` if not signed in |
| `requiresOnboarded` | Redirect to `/onboarding` if first-run not complete |
| `layout` | `default` (sidebar + app bar), `blank` (immersive), `auth` (centered card) |
| `hideSidebar` | Hide sidebar even on default layout (e.g. during live session) |
| `breadcrumbs` | Show / hide breadcrumb bar |
| `title` | Used for `<title>` tag and breadcrumb leaf |

---

## Breadcrumbs

Shown below the app bar on all pages except `layout: blank` and `layout: auth`. Generated from `route.matched` + each route's `meta.title`. Final segment is plain text; earlier segments are links.

Example: `Home / Progress / Session History / #af0132...`

---

## Deep Linking & Shareable URLs

Every student-facing resource is deep-linkable so URLs can be pasted into the tutor, peer messages, or external chat:

- Question: `/session/:sessionId?q=:questionIndex`
- Concept: `/knowledge-graph/concept/:conceptId`
- Peer solution: `/social/peers/:solutionId`
- Diagram: `/diagrams/:diagramId`
- Challenge chain: `/challenges/chains/:chainId`

All deep links respect auth and ownership guards — linking to another student's session returns 403.

---

## URL Query Params

- `?lang=ar|en|he` — override current locale for a single page load (useful for sharing in a specific language)
- `?theme=light|dark` — override theme for a single page load (useful for screenshots)
- `?embed=1` — minimal chrome (no sidebar, no app bar) for embedding in an LMS iframe — also enforces CSP `frame-ancestors`

---

## Acceptance Criteria

- [ ] `STU-NAV-001` — File-based router generates all routes under `src/student/full-version/src/pages/` using `unplugin-vue-router`.
- [ ] `STU-NAV-002` — Sidebar navigation matches the structure above and is driven by a single `index.ts` file.
- [ ] `STU-NAV-003` — Auth guard redirects unauthenticated users to `/login` and preserves `returnTo`.
- [ ] `STU-NAV-004` — Onboarded guard redirects first-run users to `/onboarding` on first post-login navigation.
- [ ] `STU-NAV-005` — Active-session badge on the sidebar polls `/api/sessions/active` on route change and via SignalR event.
- [ ] `STU-NAV-006` — Breadcrumbs render from `route.matched` with correct titles and localized labels.
- [ ] `STU-NAV-007` — Deep-linked URLs load directly without requiring a home-screen detour.
- [ ] `STU-NAV-008` — `?embed=1` renders a minimal layout and enforces frame-ancestors CSP.
- [ ] `STU-NAV-009` — Mobile-viewport bottom nav switches in under 600 px width and hides the sidebar.
- [ ] `STU-NAV-010` — `document.title` updates on every navigation from `route.meta.title`.
