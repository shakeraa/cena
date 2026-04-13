# PWA-002: Web App Manifest + Install Experience

## Goal
Create a production-grade `manifest.webmanifest` and implement a custom install prompt flow that guides students to install the PWA on their home screen. This is the first thing students see — it must be polished, RTL-aware, and accessible.

## Context
- Architecture doc: `docs/research/cena-mobile-pwa-approach.md` §2.1
- Primary color: #7367F0 (Vuexy, locked — see memory)
- Languages: Arabic (primary), Hebrew (secondary)
- Target devices: iOS 15+, Android 10+
- Students are minors (13-18) — no manipulative install prompts, no dark patterns

## Scope of Work

### 1. Manifest File
Create `src/student/full-version/public/manifest.webmanifest`:

```json
{
  "name": "Cena — تحضير بجروت",
  "short_name": "Cena",
  "description": "Math & Physics Bagrut preparation",
  "start_url": "/",
  "display": "standalone",
  "orientation": "portrait",
  "theme_color": "#7367F0",
  "background_color": "#FFFFFF",
  "dir": "auto",
  "lang": "ar",
  "icons": [...],
  "screenshots": [...],
  "categories": ["education"],
  "prefer_related_applications": false
}
```

### 2. App Icons
Generate and place in `src/student/full-version/public/icons/`:

| Size | Purpose | File |
|------|---------|------|
| 192×192 | `any` | `icon-192.png` |
| 512×512 | `any` | `icon-512.png` |
| 192×192 | `maskable` | `icon-192-maskable.png` |
| 512×512 | `maskable` | `icon-512-maskable.png` |
| 180×180 | Apple touch icon | `apple-touch-icon.png` |

- Maskable icons must have safe zone (inner 80% circle contains the meaningful content)
- Use the Cena logo on #7367F0 background
- If no logo asset exists yet, create a clean text-based placeholder ("C" or "سينا") that meets maskable safe zone requirements — document that it needs designer replacement

### 3. HTML Meta Tags
Add to `index.html`:
```html
<link rel="manifest" href="/manifest.webmanifest">
<meta name="theme-color" content="#7367F0">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-status-bar-style" content="default">
<meta name="apple-mobile-web-app-title" content="Cena">
<link rel="apple-touch-icon" href="/icons/apple-touch-icon.png">
```

### 4. Custom Install Prompt
Create `src/student/full-version/src/components/InstallPrompt.vue`:

- Capture `beforeinstallprompt` event (Chrome/Edge/Samsung)
- Show install banner **only after 2nd visit** (stored in `localStorage`)
- Banner text: Arabic primary, Hebrew secondary (use i18n)
- Banner includes: app icon, name, "Install" button, "Not now" dismiss
- Dismiss stores preference for 7 days (don't nag)
- **No manipulation**: "Not now" is equally prominent as "Install"; no countdown, no urgency language, no restriction of access

### 5. iOS Install Guide
iOS Safari does not fire `beforeinstallprompt`. Create `src/student/full-version/src/components/IOSInstallGuide.vue`:

- Detect iOS Safari: `navigator.userAgent` + `!navigator.standalone`
- Show a gentle tooltip on 2nd visit: "Tap ⎙ then 'Add to Home Screen'" with a visual guide
- Dismiss stores preference for 14 days
- Only show if NOT already running in standalone mode (`window.matchMedia('(display-mode: standalone)').matches`)

### 6. Splash Screen
Configure `vite-plugin-pwa` to generate splash screens for:
- iPhone SE (4.7") through iPhone 15 Pro Max (6.7")
- iPad (10th gen)
- Use #7367F0 background + centered logo/text

## Files to Create/Modify
- `src/student/full-version/public/manifest.webmanifest`
- `src/student/full-version/public/icons/` — icon set (6 files)
- `src/student/full-version/index.html` — meta tags
- `src/student/full-version/src/components/InstallPrompt.vue`
- `src/student/full-version/src/components/IOSInstallGuide.vue`
- `src/student/full-version/src/composables/useInstallPrompt.ts` — install state management

## Non-Negotiables
- **No dark patterns** in install prompts — this is a children's educational app under COPPA/GDPR-K
- **RTL-aware** — install prompt and iOS guide must render correctly in both Arabic and Hebrew
- **Maskable safe zone** — icons must pass the maskable icon validator (maskable.app)
- **`dir: "auto"`** in manifest — let the OS choose direction based on content

## Acceptance Criteria
- [ ] Manifest validates against W3C Web App Manifest spec (use manifest validator)
- [ ] All 6 icon sizes present and correct
- [ ] Chrome DevTools → Application → Manifest shows no warnings
- [ ] `beforeinstallprompt` fires and custom banner appears on 2nd visit (Chrome)
- [ ] iOS Safari shows install guide on 2nd visit
- [ ] Install prompt respects dismiss preference (7d Chrome, 14d iOS)
- [ ] Standalone mode detection works — no install prompt when already installed
- [ ] Lighthouse PWA audit: installable = yes
- [ ] All text is i18n'd (Arabic + Hebrew)
- [ ] Install prompt is accessible (keyboard navigable, aria-labels, high contrast)

## Testing Requirements
- **Unit**: `useInstallPrompt.ts` — mock `beforeinstallprompt`, test 2nd-visit logic, test dismiss persistence
- **Integration**: Playwright — verify manifest loads, verify meta tags present
- **Manual**: Real device testing on iOS Safari 16.4+ and Android Chrome — install flow differs significantly
- **Accessibility**: axe-core audit on InstallPrompt and IOSInstallGuide components

## DoD
- PR merged to `main`
- Screenshot of install prompt (Arabic + Hebrew) attached to PR
- Screenshot of iOS install guide attached to PR
- Manifest validator output (0 errors) attached to PR

## Reporting
Complete with: `branch=<worker>/<task-id>-pwa-manifest,icons=<count>,lighthouse_installable=<yes|no>`
