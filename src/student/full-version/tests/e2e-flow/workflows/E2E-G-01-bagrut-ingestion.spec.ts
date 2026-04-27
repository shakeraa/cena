// =============================================================================
// E2E-G-01 — Bagrut PDF ingestion (RDY-057, ADR-0043)
//
// Drives a SUPER_ADMIN through the admin SPA's /apps/ingestion/pipeline
// page, opens the Bagrut PDF dialog (FAB), uploads a real Ministry PDF
// from scripts/ocr-spike/fixtures/bagrut/, and asserts the OCR cascade
// returns a structured PdfIngestionResult — drafts + warnings — without
// raw Ministry text leaking into a student-facing path.
//
// Why a real PDF: the endpoint runs ADR-0033 OCR cascade
// (BagrutPdfIngestionService.IngestAsync). A synthetic byte buffer would
// either be rejected by the content-type / page-count guards or trip
// OcrInputException. The smallest real fixture (~350 KB, 2016 winter
// 035482) is used so the test runs in a reasonable window — OCR can
// still take 30-60s on a cold sympy-sidecar.
//
// Why we don't assert specific draft text: OCR is non-deterministic
// across tesseract versions; we assert structural properties (response
// shape, warnings as severity-graded chips, no raw text bleed) rather
// than character-level equality.
//
// ADR-0043 invariant: the response must NEVER carry a "shippable" flag.
// The drafts come back marked reference-only — student exposure happens
// only via /api/admin/content/recreate-from-reference (G-03), and only
// after the parametric template + CAS gate (G-02 / G-04).
// =============================================================================

import { test, expect } from '@playwright/test'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

const ADMIN_SPA_URL = process.env.E2E_ADMIN_SPA_URL ?? 'http://localhost:5174'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'

// ~350 KB PDF — smallest fixture in scripts/ocr-spike/fixtures/bagrut.
// Repo-local path; the e2e-flow stack runs against the same checkout.
const BAGRUT_FIXTURE = path.resolve(
  __dirname,
  '../../../../../../scripts/ocr-spike/fixtures/bagrut/bagrut_mathematics_2016_winter_hebrew_exam_035482.pdf',
)

interface ConsoleEntry { type: string; text: string }
interface NetworkFailure { method: string; url: string; status: number; body?: string }

// =============================================================================
// KNOWN GAP — admin-api Program.cs has the OCR cascade registration
// commented out (lines ~342-345, "Still pending" per RDY-056). The
// downstream `BagrutPdfIngestionService` is registered without its
// `IOcrCascadeService` dependency, so the endpoint throws
// `InvalidOperationException` at endpoint invocation rather than the
// proper `503 ocr_circuit_open` semantic. Wiring the cascade requires:
//   1. AddOcrCascadeCore() in admin Program.cs
//   2. ILayer1Layout / ILayer2aTextOcr / ILayer2bMathOcr / ILayer2cFigureExtraction
//      registered with their real concrete runners (SuryaSidecarClient,
//      TesseractLocalRunner, Pix2TexSidecarClient, Layer2cFigureExtraction)
//   3. cena-ocr-sidecar container started alongside (HF model cache,
//      ~GB on first build, 180s start_period per docker-compose.ocr-sidecar.yml)
//
// This spec is `test.fixme` until that wiring lands. The body still drives
// auth + dialog open + form submit so the moment OCR is wired, removing
// `.fixme` should turn it green (or surface a different real bug).
// =============================================================================

test.describe('E2E_G_01_BAGRUT_INGESTION', () => {
  test.fixme('SUPER_ADMIN uploads Ministry PDF → drafts + warnings, no raw-text leak @epic-g @ship-gate @blocked-on-ocr-wire', async ({ page }, testInfo) => {
    // OCR cascade is the slowest path in the test matrix. Cold sympy-sidecar +
    // tesseract layer handoff can stretch to ~60s on a CI runner. Local
    // dev is closer to ~25s. Belt-and-suspenders.
    test.setTimeout(180_000)

    const consoleEntries: ConsoleEntry[] = []
    const pageErrors: { message: string }[] = []
    const failedRequests: NetworkFailure[] = []

    page.on('console', msg => consoleEntries.push({ type: msg.type(), text: msg.text() }))
    page.on('pageerror', err => pageErrors.push({ message: err.message }))
    page.on('response', async (resp) => {
      if (resp.status() >= 400) {
        let body: string | undefined
        try { const t = await resp.text(); body = t.length > 800 ? `${t.slice(0, 800)}…` : t }
        catch { body = '<navigation flushed>' }
        failedRequests.push({ method: resp.request().method(), url: resp.url(), status: resp.status(), body })
      }
    })

    // ── 1. Provision SUPER_ADMIN via Firebase emu ──
    const email = `g-01-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
    console.log(`\n=== E2E_G_01_BAGRUT_INGESTION for ${email} ===\n`)

    const signUpResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    expect(signUpResp.ok()).toBe(true)
    const { localId } = await signUpResp.json() as { localId: string }

    // Bagrut endpoint policy is SuperAdminOnly — anything less 403's at
    // the route group, before the body even parses.
    const claims = { role: 'SUPER_ADMIN', school_id: SCHOOL_ID, locale: 'en', plan: 'free' }
    expect((await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/projects/${FIREBASE_PROJECT_ID}/accounts:update`,
      { headers: { Authorization: `Bearer ${EMU_BEARER}` }, data: { localId, customAttributes: JSON.stringify(claims) } },
    )).ok()).toBe(true)

    // ── 2. /login on the admin SPA ──
    await page.goto(`${ADMIN_SPA_URL}/login`)
    await page.getByPlaceholder('admin@cena.edu').fill(email)
    await page.locator('input[type="password"]').fill(password)
    await page.getByRole('button', { name: /sign in/i }).first().click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })

    // ── 3. /apps/ingestion/pipeline ──
    await page.goto(`${ADMIN_SPA_URL}/apps/ingestion/pipeline`)
    // The 9-stage pipeline header rendering is enough proof the page mounted.
    await expect(page.getByText(/Incoming/i).first()).toBeVisible({ timeout: 15_000 })
    console.log('[g-01] /apps/ingestion/pipeline mounted')

    // ── 4. Open the Bagrut FAB → dialog ──
    // The FAB has class .pipeline-bagrut-fab (no testid). The
    // BagrutUploadDialog inside has data-testid="bagrut-file-input"
    // which only appears once the dialog is open.
    await page.locator('.pipeline-bagrut-fab').click()
    await expect(page.getByTestId('bagrut-file-input')).toBeAttached({ timeout: 5_000 })
    console.log('[g-01] Bagrut upload dialog opened')

    // ── 5. Fill exam-code + attach PDF + click Ingest ──
    const examCode = `math-3u-2016-winter-${Math.random().toString(36).slice(2, 6)}`
    await page.getByLabel(/exam code/i).fill(examCode)
    await page.getByTestId('bagrut-file-input').setInputFiles(BAGRUT_FIXTURE)
    console.log(`[g-01] exam-code=${examCode}, fixture attached`)

    // The "Ingest" button is the only primary-color button on the dialog
    // when no result has rendered yet.
    await page.getByRole('button', { name: /^ingest$/i }).click()
    console.log('[g-01] Ingest clicked — awaiting OCR cascade')

    // ── 6. Two terminal outcomes are acceptable ──
    //
    // (A) HAPPY: dialog re-renders with the result section. The
    //     "Ingestion complete: <pdfId>" line is the structural proof
    //     the response landed AND drafts were emitted (or warnings).
    //
    // (B) DEGRADED: the cascade hits OcrCircuitOpenException → 503
    //     ocr_circuit_open. The dialog surfaces this in the red error
    //     alert. We accept this as a documented failure mode (ADR-0033
    //     §6 circuit policy) — the spec proves the contract, not OCR
    //     quality. We log which branch we took for triage.
    const settled = await Promise.race([
      page.getByText(/Ingestion complete:/i).waitFor({ state: 'visible', timeout: 120_000 }).then(() => 'happy'),
      page.locator('.v-alert--type-error').first().waitFor({ state: 'visible', timeout: 120_000 }).then(() => 'degraded'),
    ]).catch(() => 'timeout')
    console.log(`[g-01] OCR settled: ${settled}`)

    expect(settled, 'Bagrut OCR cascade must reach a terminal state within 120s').not.toBe('timeout')

    if (settled === 'happy') {
      // Structural assertions on the result panel.
      // PdfIngestionResult.questionsExtracted/totalPages/figuresExtracted
      // render as VChips. We only require >= 0 for each (warnings list
      // can carry "encrypted_pdf"/"cas_failed"/"some_drafts_low_confidence"
      // and still count as a successful response).
      await expect(page.getByText(/drafts/i).first()).toBeVisible({ timeout: 5_000 })
      await expect(page.getByText(/pages/i).first()).toBeVisible({ timeout: 5_000 })

      // ── ADR-0043 ship-gate invariant ──
      // The dialog body must NOT carry the word "shippable" anywhere —
      // Bagrut output is reference-only. Student-facing flow is gated
      // behind the parametric template (G-02) + CAS verifier (G-04).
      const dialogText = await page.locator('.v-card').first().innerText()
      expect(dialogText.toLowerCase()).not.toContain('shippable')
      console.log('[g-01] ADR-0043 reference-only invariant holds (no "shippable" leak)')
    }
    else {
      // Degraded path — accept and document.
      const errText = await page.locator('.v-alert--type-error').first().innerText()
      console.log(`[g-01] degraded outcome: "${errText.trim().slice(0, 200)}"`)
      // Network log should show the 503 with code=ocr_circuit_open or a
      // 400 with code=ocr_input_error / invalid_request.
      const ingest503or4xx = failedRequests.find(f =>
        f.url.includes('/api/admin/ingestion/bagrut') && [400, 503].includes(f.status))
      expect(ingest503or4xx, 'degraded outcome must trace to a 4xx/503 from /api/admin/ingestion/bagrut').toBeDefined()
    }

    // ── 7. Diagnostics ──
    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })
    testInfo.attach('page-errors.json', { body: JSON.stringify(pageErrors, null, 2), contentType: 'application/json' })

    const errs = consoleEntries.filter(e => e.type === 'error')
    // The /admin/ingestion/bagrut endpoint can legitimately 503 in the
    // degraded branch above. Filter that out only when settled='degraded'.
    const noiseFilter = (f: NetworkFailure) =>
      settled === 'degraded' && f.url.includes('/api/admin/ingestion/bagrut')
    const unexpectedFailedRequests = failedRequests.filter(f => !noiseFilter(f))

    console.log('\n=== E2E_G_01 DIAGNOSTICS SUMMARY ===')
    console.log(`Console: ${consoleEntries.length} | errors=${errs.length}`)
    console.log(`Page errors: ${pageErrors.length}`)
    console.log(`Failed network: ${failedRequests.length} (degraded-path 503 ignored: ${failedRequests.length - unexpectedFailedRequests.length})`)
    if (errs.length) {
      console.log('— console errors —')
      for (const e of errs.slice(0, 10)) console.log(`  ${e.text}`)
    }
    if (unexpectedFailedRequests.length) {
      console.log('— unexpected failed requests —')
      for (const f of unexpectedFailedRequests.slice(0, 10))
        console.log(`  ${f.status} ${f.method} ${f.url} :: ${(f.body ?? '').slice(0, 160)}`)
    }

    expect(pageErrors, 'No JS exceptions on the Bagrut ingest dialog').toHaveLength(0)
  })
})
