# Feature Flags

This document is the **single source of truth** for runtime-flippable feature
flags in the Cena platform. Each entry includes the canonical config key, env
variable, default, owning ADR/PRR, the implementation site that reads it, and
the gating reason. Flip a flag → update the table.

---

## `Cena:Variants:BagrutSeedToLlmEnabled`

| Field | Value |
|---|---|
| **Default** | `false` |
| **Env var** | `OCR_VARIANTS_BAGRUT_SEED_ENABLED` (substituted into compose) |
| **Owner** | Shaker + claude-code (coordinator) |
| **Owning ADR** | [ADR-0059 §15.5](../adr/0059-bagrut-reference-browse-and-variant-generation.md) |
| **Gating PRR** | PRR-249 (legal-delta memo, §6 sign-off required for flip-on) |
| **Reads at** | `Cena.Admin.Api.Ingestion.GenerateVariantsJobStrategy.ExecuteAsync` |
| **Surfaces to SPA via** | `GET /api/admin/ingestion/jobs/feature-flags` |
| **Symptoms when off** | `GenerateVariantsJobStrategy` throws `InvalidOperationException` with code `SOURCE_ANCHORED_VARIANTS_DISABLED`. Admin SPA "Generate variants" dialog renders the warning banner *"Disabled pending legal sign-off (PRR-249)"* and disables the **Enqueue** button. |

### What this flag actually gates

Source-anchored AI variant generation from Bagrut Ministry-published past papers
(ADR-0059 §15.5 structural-variant tier). When **on**, the LLM receives the
Bagrut draft prompt + LaTeX as a *creative seed* with explicit do-not-copy
guardrails (`[SOURCE-AS-CREATIVE-SEED]` marker handled in
`AiGenerationService.BuildPrompt`), and `IQuestionBankService.CreateQuestionAsync`
persists each passing candidate as a draft `QuestionReadModel` with provenance
lineage `variant-of:{draftId} · pdf:{pdfId} · exam:{examCode}`.

Implementation is **production-grade**: it routes through `CasGatedQuestionPersister`
(ADR-0002 SymPy oracle), `IQualityGateService`, and writes the canonical
`QuestionAiGenerated_V2` event on a new question stream. Quality gate + CAS
verdicts surface in the per-job log stream of the Ingestion Jobs drawer.

The flag is independent of the implementation: implementation correctness is
verified by code review + tests; **legal posture is verified by counsel signing
PRR-249 §6**. Flipping the flag without §6 sign-off is an ADR violation.

### How to flip on (after PRR-249 §6 signs)

```bash
# Local dev — repo-root .env (gitignored)
echo 'OCR_VARIANTS_BAGRUT_SEED_ENABLED=true' >> .env
docker compose -f docker-compose.yml -f docker-compose.app.yml up -d admin-api
```

For CI / staging / prod hosts, set `Cena__Variants__BagrutSeedToLlmEnabled=true`
on the runtime environment. The admin-api reads it at startup; no code change
required.

### Verification after flip

1. `GET /api/admin/ingestion/jobs/feature-flags` returns `{"bagrutSeedToLlmEnabled": true, ...}`.
2. Admin SPA "Generate variants" dialog renders the green ADR-0059 info banner
   (not the warning banner) and the **Enqueue** button is enabled.
3. Submitting the form enqueues a `GenerateVariants` job that runs through
   `BatchGenerateAsync` → CAS gate → quality gate → `CreateQuestionAsync`,
   producing one or more new `QuestionReadModel` rows visible on the Question
   Bank surface.

---

## `Cena:StuckClassifier:Enabled`

| Field | Value |
|---|---|
| **Default** | `true` (dev) |
| **Owner** | RDY-063 owner |
| **Owning task** | RDY-063 Phase 2a |
| **Reads at** | `Cena.Actors.StuckClassifier.*` registration in `CenaAdminServiceRegistration` |
| **Symptoms when off** | Repository functions but returns empty distributions; admin pages render "no data yet" instead of stuck-type charts. |

(Existing flag — documented here for completeness as the second known runtime-flippable flag in admin-api.)

---

## How to add a new flag

1. Add `Cena:<Area>:<Flag>` to `appsettings.{Environment}.json` with a `_doc` sibling that names the gating ADR/PRR.
2. Add `Cena__<Area>__<Flag>=${ENV_VAR_NAME:-default}` to the relevant compose stanza in `docker-compose.app.yml`.
3. Read via `IConfiguration.GetValue<bool>("Cena:<Area>:<Flag>")` at the call site. Throw a typed exception with a `SCREAMING_SNAKE` error code if the flag is required-on for the call site.
4. If the SPA needs to show the flag state, expose it via a small readback endpoint (e.g. `/api/admin/ingestion/jobs/feature-flags`) that returns a `{flagName: boolean, reason: string}` shape. Do **not** ship the env var or appsettings key to the SPA verbatim — the readback is the SPA's contract.
5. Append a row to this table.
