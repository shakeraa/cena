# Cross-browser PDF `#page=N` anchor — manual test runbook

**Status:** manual runbook (chosen over an automated Playwright test — see "Why a runbook" below).
**Owner:** admin-spa curators / QA.
**Last verified:** 2026-05-03 — runbook lands; first verification pass not yet performed.

## What we're testing

The Bagrut curator panel (`ItemDetailPanel.vue` → recreated-questions section)
renders a per-card PDF thumbnail anchored to a specific page via the
`#page=N` URL fragment, e.g.:

```html
<embed src="<blob-url>#page=5&toolbar=0&navpanes=0" />
```

The browser's built-in PDF viewer is expected to honour the `#page=N`
hash and jump directly to that page. This is not a JavaScript
feature — it is a viewer-level convention (PDF Open Parameters spec
§3.3) implemented by each browser's PDF rendering pipeline (PDFium in
Chromium, pdf.js in Firefox, PDFKit in Safari/macOS Preview).

Because the behaviour is implemented below the JS layer, automated
in-browser tests cannot reliably observe the rendered page. This
runbook documents the manual verification.

## Why a runbook (not a Playwright test)

Three options exist; we picked the runbook deliberately.

| Option | Pros | Cons | Decision |
|---|---|---|---|
| Playwright cross-browser test | Automatable | Headless PDF rendering is browser-specific and fragile: Chromium can disable PDFium with `--disable-pdf-extension`; Firefox's pdf.js is JS so observable but Playwright cannot read pdf.js's internal `currentPage` state without reaching into private DOM; Safari/WebKit headless does not include the PDF viewer at all. The test would be silently skipped on Safari and brittle on Chromium/Firefox. | **Rejected** — a flaky test that nobody runs is worse than an honest runbook. |
| Visual regression (screenshot diff) | Catches "wrong page" loudly | Same headless PDF support gap as above; on Safari we'd be comparing nothing | **Rejected** — same root cause. |
| Manual runbook | Honest about what humans verify; cheap to run; covers the actual user environments curators use | Requires a human; not blocking on every PR | **Selected.** |

If browser PDF support stabilises in a future Playwright/WebKit
release, swap this runbook for an automated test. Until then the
runbook is the senior answer.

## Prerequisites

- The Cena admin SPA running at `http://localhost:5174`.
- A logged-in moderator account (Firebase Auth, `cena-platform` project).
- At least one Bagrut item in `InReview` state with:
  - a source PDF that has ≥5 pages, AND
  - a recreated question whose `sourcePage` is **5** (so the embed URL ends in `#page=5`).

You can prepare a test fixture by uploading the official 5-unit 2026
exam paper (806/807, ~10 pages) — the recreated questions land on
pages 1–N and one of them will hit page 5.

If no such item exists, ingest one before starting:

```bash
# From the repo root
./scripts/cena-up.sh                    # start docker stack
# Then in the admin SPA, use the Bagrut upload dialog and pick an
# exam paper from src/admin/full-version/test-fixtures/bagrut-pdfs/
```

## Test steps

For **each** browser in the matrix (Chrome, Firefox, Safari):

### 1. Open the curator panel

1. Navigate to `http://localhost:5174/apps/ingestion/queue`.
2. Find a Bagrut item in `InReview` state and click it.
3. The fullscreen detail panel opens.

### 2. Trigger the per-card PDF thumbnail

1. Scroll to the "Recreated questions" section.
2. Find a card with `· p5` in its header (the source page indicator).
3. Wait for the right-hand PDF thumbnail to load (the spinner clears
   when the blob URL resolves).

### 3. Verify the anchor

1. Confirm the embed shows **page 5** of the source PDF (not page 1
   nor a blank viewer).
2. The page should be centred in the visible area; the toolbar and
   navigation panes should be hidden (`toolbar=0&navpanes=0` in the
   URL).

### 4. Pass criterion

The viewer is showing the correct page. Record:

- Browser + version (e.g. Chrome 132.0.6834.110)
- macOS / Windows / Linux
- Pass / Fail
- Screenshot if Fail

### 5. Repeat for ≥2 different cards

Pick one card with `sourcePage` = 5 and one with a different page
(e.g. 8). Both must land on the correct page.

## Browser matrix

Run the steps above on at least:

| Browser | Min version | Notes |
|---|---|---|
| Chrome / Chromium | 120+ | The default curator browser; PDFium honours `#page=N`. |
| Firefox | 130+ | Uses pdf.js which honours `#page=N` since v1.x. |
| Safari (macOS) | 17+ | Uses PDFKit; honours `#page=N` since macOS 12. |
| Edge | 120+ (optional) | Same engine as Chrome. Not strictly required if Chrome passes. |

Do NOT test on:
- Mobile Safari / iOS — the curator UI is desktop-only (admin SPA at 5174).
- Chrome with `--disable-pdf-extension` — out of scope; that flag is for kiosk lockdown.

## Pass / Fail summary template

Paste this block into the relevant PR or QA report:

```
## Cross-browser PDF #page=N verification

Date: YYYY-MM-DD
Tester: <name>
Build: <git sha or admin-spa version>

| Browser | Version | OS | sourcePage | Result |
|---|---|---|---|---|
| Chrome | 132.x | macOS 14 | 5 | PASS |
| Chrome | 132.x | macOS 14 | 8 | PASS |
| Firefox | 132.x | macOS 14 | 5 | PASS |
| Safari | 17.x  | macOS 14 | 5 | PASS |

Notes:
- (any browser-specific quirks observed)
```

## Known issues / edge cases

- **Safari sometimes ignores `navpanes=0`** and shows the side
  thumbnail rail. This is cosmetic — the page anchor still works.
  Treated as PASS for the page-anchor test.
- **Chrome on Linux** (ChromeOS / Ubuntu) sometimes hands the PDF
  off to a system viewer (Evince, Okular) which may NOT honour
  `#page=N`. If a curator on Linux reports "PDF opens at page 1",
  recommend they install the in-browser PDF viewer extension or use
  Chrome's built-in PDFium.
- **PDF blob URLs** generated via `URL.createObjectURL()` from an
  authenticated `fetch()` are scoped to the document; refreshing the
  panel revokes the URL and a new one is allocated. The runbook tests
  the freshly-allocated URL.

## When to re-run this runbook

Re-run on:
- any change to `ItemDetailPanel.vue` near the `<embed>` element or
  the blob-URL allocation path;
- a major upgrade of Chromium / Firefox / Safari (annual release
  cycle);
- a switch in PDF source — e.g. if `BagrutPdfStore` starts emitting a
  different bytes-on-disk format that some viewers might reject.

A passing runbook is captured in the relevant ADR or PR description;
a failing run blocks merge until the underlying issue is identified
(usually a bad URL fragment or a viewer-level regression).
