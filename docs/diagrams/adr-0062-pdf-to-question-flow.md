# PDF → Question flow (ADR-0062 Phase 1)

End-to-end picture of what happens when a curator uploads a Bagrut reference PDF and a published question lands in the bank, with concept extraction folded into the canonical CAS-gated write path.

## Mermaid diagram (renders inline on GitHub / GitLab / VS Code Markdown preview)

```mermaid
flowchart TD
    subgraph Curator
        Upload["Curator uploads<br/>Bagrut reference PDF"]
        Review["Curator reviews kanban item<br/>(visual + metadata + concepts)"]
        Confirm["Curator confirms concepts<br/>(POST /items/{id}/concepts)"]
        Trigger["Curator triggers<br/>'Generate variants'"]
        Publish["Curator clicks Publish"]
    end

    subgraph IngestSurface["Bagrut ingest surface (admin-api)"]
        IngestEp["POST /api/admin/ingestion/bagrut<br/>(SuperAdminOnly)"]
        BagrutStore["BagrutPdfStore<br/>persists bytes to<br/>/var/cena/source-pdfs"]
        IngestSvc["BagrutPdfIngestionService<br/>OCR cascade (ADR-0033)<br/>pdftoppm + tesseract"]
    end

    subgraph DraftPersist["Draft persistence"]
        DraftSvc["BagrutDraftPersistence"]
        Pipeline[("PipelineItemDocument<br/>kanban 'In Review'<br/>+ AutoExtractedMetadata")]
        DraftPayload[("BagrutDraftPayloadDocument<br/>prompt + LaTeX + figures")]
        DraftStream[("Draft event stream<br/>QuestionConceptsExtracted_V1<br/>(rules-tier from keywords)")]
    end

    subgraph CuratorPanel["Curator panels (Vuexy SPA)"]
        Visual["Visual review<br/>PDF + figures side-by-side"]
        MetadataPanel["Curator metadata panel<br/>subject / track / topic"]
        ConceptPanel["Concept review panel<br/>extracted vs. catalog picker"]
        ConceptEp["POST /items/{id}/concepts<br/>BagrutTaxonomyCatalog<br/>canonicalises every entry"]
    end

    subgraph VariantGen["Variant generation"]
        VariantJob["GenerateVariantsJobStrategy<br/>(SOURCE_ANCHORED, gated on PRR-249)"]
        AiSvc["AiGenerationService<br/>BatchGenerateAsync<br/>Anthropic Haiku/Sonnet"]
        QualityGate["IQualityGateService<br/>(8 sub-scorers)"]
    end

    subgraph SingleWriter["Single-writer ADR-0002 / ADR-0032 / ADR-0062"]
        QBS["QuestionBankService<br/>.CreateQuestionAsync"]
        Persister["CasGatedQuestionPersister<br/>★ THE only StartStream<QuestionState> ★"]
        CasGate["ICasVerificationGate<br/>SymPy round-trip"]
        Extractor["IQuestionConceptExtractor<br/>(rules-tier, closed-set)"]
        Stream[("Question event stream<br/>StartStream<QuestionState>:<br/>1. Authored/Ingested/AiGenerated<br/>2. QuestionConceptsExtracted_V1<br/>3. QualityEvaluated_V1<br/>4. Approved_V1 (auto)?")]
    end

    subgraph PublishGate["Publish gate (ADR-0062 Phase 1)"]
        PublishEp["POST /questions/{id}/publish"]
        Calibration["IConceptCurationCalibrationCounter<br/>(Marten distinct streams<br/>with Confirmed_V1)"]
        GateDecision{"Calibration phase<br/>active AND<br/>not yet confirmed?"}
        Block["409 Conflict<br/>'Concept review required'<br/>+ /concepts pointer"]
        Published[("Stream gets QuestionPublished_V1<br/>QuestionReadModel.Status = Published<br/>visible to MartenQuestionPool")]
    end

    Upload --> IngestEp
    IngestEp --> BagrutStore
    IngestEp --> IngestSvc
    BagrutStore -.->|read PDF bytes| IngestSvc
    IngestSvc --> DraftSvc
    DraftSvc --> Pipeline
    DraftSvc --> DraftPayload
    DraftSvc --> DraftStream

    Pipeline --> Review
    DraftPayload -.-> Visual
    BagrutStore -.->|GET .../source.pdf<br/>auth blob URL| Visual
    Pipeline --> MetadataPanel
    DraftStream -.-> ConceptPanel
    Review --> Confirm
    Confirm --> ConceptEp
    ConceptEp -->|appends| DraftStream

    Review --> Trigger
    Trigger --> VariantJob
    DraftPayload -.->|seed text<br/>+ track inference| VariantJob
    VariantJob --> AiSvc
    AiSvc --> QualityGate
    QualityGate -->|passing batch| QBS

    QBS --> Persister
    Persister --> CasGate
    Persister --> Extractor
    Persister --> Stream

    Stream --> Publish
    Publish --> PublishEp
    PublishEp --> Calibration
    Calibration --> GateDecision
    GateDecision -->|yes| Block
    Block -.->|curator goes back to<br/>concept review panel| ConceptPanel
    GateDecision -->|no| Published

    classDef event fill:#fff3cd,stroke:#856404,stroke-width:1px;
    classDef store fill:#d1ecf1,stroke:#0c5460,stroke-width:1px;
    classDef gate fill:#f8d7da,stroke:#721c24,stroke-width:2px;
    classDef adr fill:#d4edda,stroke:#155724,stroke-width:2px;

    class DraftStream,Stream,Pipeline,DraftPayload event;
    class BagrutStore store;
    class CasGate,QualityGate,GateDecision,Calibration gate;
    class Persister adr;
```

## Where each step lives in code

| Step | Type | File |
|---|---|---|
| Upload endpoint | Route | [BagrutIngestEndpoints.cs](../../src/api/Cena.Admin.Api/Ingestion/BagrutIngestEndpoints.cs) |
| OCR cascade | Service | [BagrutPdfIngestionService.cs](../../src/api/Cena.Admin.Api/Ingestion/BagrutPdfIngestionService.cs) |
| Draft kanban + extracted event | Service | [BagrutDraftPersistence.cs](../../src/api/Cena.Admin.Api/Ingestion/BagrutDraftPersistence.cs) |
| Curator concept panel | Routes | [QuestionConceptsEndpoints.cs](../../src/api/Cena.Admin.Api/Ingestion/QuestionConceptsEndpoints.cs) |
| Closed-set canonicaliser | Domain | [BagrutTaxonomyCatalog.cs](../../src/actors/Cena.Actors/Mastery/BagrutTaxonomyCatalog.cs) |
| Variant generation | Job | [GenerateVariantsJobStrategy.cs](../../src/api/Cena.Admin.Api/Ingestion/GenerateVariantsJobStrategy.cs) |
| Single-writer persister | Domain | [CasGatedQuestionPersister.cs](../../src/actors/Cena.Actors/Cas/CasGatedQuestionPersister.cs) |
| Concept extractor | Domain | [RulesOnlyConceptExtractor.cs](../../src/actors/Cena.Actors/Mastery/Extraction/RulesOnlyConceptExtractor.cs) |
| Calibration counter | Domain | [MartenConceptCurationCalibrationCounter.cs](../../src/actors/Cena.Actors/Mastery/Extraction/MartenConceptCurationCalibrationCounter.cs) |
| Publish-gate handler | Helper | [PublishCalibrationGate.cs](../../src/api/Cena.Admin.Api/Concepts/PublishCalibrationGate.cs) |

## What to expect at each stage

1. **Upload** — bytes hit local volume; OCR runs synchronously (cascade is blocking by design — small Bagrut PDFs, < 5 seconds).
2. **Kanban "In Review"** — a `PipelineItemDocument` row appears with auto-extracted `subject=math`, `track=5u/4u/3u` parsed from exam code, `taxonomyNode` keyword-classified from prompt+LaTeX.
3. **Visual review** — curator sees original PDF in browser via auth blob URL plus the system's recreated stem + figure.
4. **Concept review** — extracted set (one Primary, low confidence) shown next to the closed-set catalog (~73 leaves). Curator one-clicks confirm or overrides; `QuestionConceptsConfirmed_V1` lands on the draft stream.
5. **Generate variants** — Anthropic batch returns N candidates; each that passes the quality gate enters the persister.
6. **Persister atomic batch** — for every persisted variant: `QuestionAuthored/Ingested/AiGenerated_V2` → `QuestionConceptsExtracted_V1` → `QuestionQualityEvaluated_V1` → optional `QuestionApproved_V1`. ALL in one `StartStream<QuestionState>` call so partial commits are impossible.
7. **Publish** — for the first 200 variants in the calibration corpus, the curator must confirm concepts on each variant before publish. After 200, extraction stands; one-click override surfaced in the SPA.

## How to import to draw.io

draw.io natively imports Mermaid:
1. Open https://app.diagrams.net (or VS Code "Draw.io Integration" extension).
2. **Arrange → Insert → Advanced → Mermaid…**
3. Paste the entire ` ```mermaid ` block above.
4. Click **Insert**. The diagram lays itself out and is fully editable as native draw.io shapes.

The .drawio XML file with the same diagram lives at [adr-0062-pdf-to-question-flow.drawio](./adr-0062-pdf-to-question-flow.drawio) (importable directly).
