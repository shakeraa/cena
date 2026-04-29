// =============================================================================
// Cena Platform -- BagrutDraftPersistence tests (curator metadata auto-fill)
//
// Verifies the 2026-04-29 fix that pre-populates AutoExtractedMetadata on
// every Bagrut draft so the curator UI opens with Subject/Language/
// SourceType/ExpectedFigures filled, Track inferred from the examCode
// prefix, and TaxonomyNode classified PER DRAFT from the prompt+LaTeX
// keyword content (NOT a fixed `math_{track}` root, which was the prior
// bug surfaced by the user 2026-04-29 — "math_5u is not a calculus/...").
//
// Marten IDocumentSession is NSubstitute'd; we capture every Store() call
// and inspect the resulting PipelineItemDocument directly.
// =============================================================================

using Cena.Actors.Ingest;
using Cena.Admin.Api.Ingestion;
using Cena.Api.Contracts.Admin.Ingestion;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class BagrutDraftPersistenceTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();
    private readonly BagrutDraftPersistence _persistence;

    private readonly List<PipelineItemDocument> _storedItems = new();
    private readonly List<BagrutDraftPayloadDocument> _storedPayloads = new();

    public BagrutDraftPersistenceTests()
    {
        _store.LightweightSession().Returns(_session);

        _session.WhenForAnyArgs(s => s.Store<PipelineItemDocument>(default!))
            .Do(ci =>
            {
                foreach (var arg in ci.Args())
                {
                    if (arg is PipelineItemDocument doc) _storedItems.Add(doc);
                    else if (arg is PipelineItemDocument[] docs) _storedItems.AddRange(docs);
                }
            });
        _session.WhenForAnyArgs(s => s.Store<BagrutDraftPayloadDocument>(default!))
            .Do(ci =>
            {
                foreach (var arg in ci.Args())
                {
                    if (arg is BagrutDraftPayloadDocument doc) _storedPayloads.Add(doc);
                    else if (arg is BagrutDraftPayloadDocument[] docs) _storedPayloads.AddRange(docs);
                }
            });

        _persistence = new BagrutDraftPersistence(
            _store, NullLogger<BagrutDraftPersistence>.Instance);
    }

    private static IngestionDraftQuestion MakeDraft(
        int page = 1,
        string examCode = "math-5u-2026-35581",
        string prompt = "Question prompt",
        string latex = "x^2 + 2x = 3") =>
        new(
            DraftId: $"draft-{examCode}-p{page}",
            SourcePage: page,
            Prompt: prompt,
            LatexContent: latex,
            AnswerChoices: Array.Empty<string>(),
            CorrectAnswer: null,
            ExamCode: examCode,
            FigureSpecJson: null,
            ExtractionConfidence: 0.9,
            ReviewNotes: new[] { "bagrut-reference:auto-extracted;requires-curator-recreation" });

    // --------------------------------------------------------------------
    // Auto-fill happy path — content-driven taxonomy classification
    // --------------------------------------------------------------------

    [Fact]
    public async Task PersistAsync_Populates_AutoExtractedMetadata_With_Six_Fields_When_Taxonomy_Matches()
    {
        // Hebrew prompt mentions "אינטגרל" → ClassifyTaxonomy returns
        // calculus.integrals_intro AND DetectLanguage picks "he". The
        // five inferred fields plus the classified TaxonomyNode = six
        // populated fields.
        var drafts = new[]
        {
            MakeDraft(prompt: "חשב את האינטגרל של הפונקציה", latex: @"\int f(x) dx"),
        };

        var ids = await _persistence.PersistAsync(
            examCode: "math-5u-2026-35581",
            sourcePdfId: "pdf-abc",
            sourceFilename: "math-5u-2026-35581.pdf",
            submittedBy: "curator@cena.dev",
            drafts: drafts);

        Assert.Single(ids);
        Assert.Single(_storedItems);

        var stored = _storedItems[0];
        Assert.Equal("auto_extracted", stored.MetadataState);
        Assert.Equal(BagrutDraftPersistence.ExtractionStrategy, stored.MetadataExtractionStrategy);

        var meta = stored.AutoExtractedMetadata;
        Assert.NotNull(meta);
        Assert.Equal("math",                       meta!.Subject);
        Assert.Equal("he",                         meta.Language);
        Assert.Equal("5u",                         meta.Track);
        Assert.Equal("bagrut_reference",           meta.SourceType);
        Assert.Equal("calculus.integrals_intro",   meta.TaxonomyNode);
        Assert.True(meta.ExpectedFigures);
    }

    [Fact]
    public async Task PersistAsync_Populates_FieldConfidences_With_Expected_Values()
    {
        // Hebrew-only content for a clean script-dominance signal:
        // DetectLanguage returns ("he", 0.95).
        var drafts = new[]
        {
            MakeDraft(prompt: "חשב את האינטגרל של הפונקציה", latex: ""),
        };

        await _persistence.PersistAsync(
            examCode: "math-5u-2026-35581",
            sourcePdfId: "pdf-abc",
            sourceFilename: null,
            submittedBy: "curator@cena.dev",
            drafts: drafts);

        var conf = _storedItems[0].MetadataFieldConfidences;
        Assert.Equal(0.95, conf[nameof(CuratorMetadata.Subject)]);
        Assert.Equal(0.95, conf[nameof(CuratorMetadata.Language)]);
        Assert.Equal(0.95, conf[nameof(CuratorMetadata.Track)]);
        Assert.Equal(0.99, conf[nameof(CuratorMetadata.SourceType)]);
        // 0.65 = strong-keyword match in ClassifyTaxonomy (calculus.integrals_intro).
        Assert.Equal(0.65, conf[nameof(CuratorMetadata.TaxonomyNode)]);
        Assert.Equal(0.70, conf[nameof(CuratorMetadata.ExpectedFigures)]);
    }

    // --------------------------------------------------------------------
    // Track parsing (covers 3u, 4u, 5u, fallback) — taxonomy is
    // independent of track, classified from prompt content.
    // --------------------------------------------------------------------

    [Theory]
    [InlineData("math-3u-2026-12345", "3u")]
    [InlineData("math-4u-2026-12345", "4u")]
    [InlineData("math-5u-2026-12345", "5u")]
    [InlineData("MATH-5U-2026-12345", "5u")]   // case-insensitive
    [InlineData("math_5u_2026_12345", "5u")]   // underscore separator
    public async Task PersistAsync_Parses_Track_From_ExamCode(string examCode, string expectedTrack)
    {
        var draft = MakeDraft(examCode: examCode);

        await _persistence.PersistAsync(
            examCode: examCode,
            sourcePdfId: "pdf-abc",
            sourceFilename: null,
            submittedBy: "curator@cena.dev",
            drafts: new[] { draft });

        var meta = _storedItems[0].AutoExtractedMetadata!;
        Assert.Equal(expectedTrack, meta.Track);

        var conf = _storedItems[0].MetadataFieldConfidences;
        Assert.Equal(0.95, conf[nameof(CuratorMetadata.Track)]);
    }

    [Theory]
    [InlineData("physics-5u-2026-12345")]   // wrong subject prefix
    [InlineData("math-2u-2026-12345")]      // unsupported track
    [InlineData("math-2026-12345")]         // missing track segment
    [InlineData("")]                        // empty
    [InlineData("   ")]                     // whitespace
    public async Task PersistAsync_Falls_Back_To_Null_Track_When_ExamCode_Unrecognised(string examCode)
    {
        // Hebrew prompt so DetectLanguage returns "he" — keeps the
        // fixed-field assertions stable while exercising the track-
        // parsing fallback path.
        var draft = new IngestionDraftQuestion(
            DraftId: $"draft-fallback-{Guid.NewGuid():N}",
            SourcePage: 1,
            Prompt: "שאלה כללית ללא מילות מפתח",
            LatexContent: "",
            AnswerChoices: Array.Empty<string>(),
            CorrectAnswer: null,
            ExamCode: examCode,
            FigureSpecJson: null,
            ExtractionConfidence: 0.9,
            ReviewNotes: Array.Empty<string>());

        await _persistence.PersistAsync(
            examCode: examCode,
            sourcePdfId: "pdf-abc",
            sourceFilename: "fallback.pdf",
            submittedBy: "curator@cena.dev",
            drafts: new[] { draft });

        var meta = _storedItems[0].AutoExtractedMetadata!;
        Assert.Null(meta.Track);

        var conf = _storedItems[0].MetadataFieldConfidences;
        Assert.Equal(0.5, conf[nameof(CuratorMetadata.Track)]);

        // The non-track fields stay populated even when track parsing
        // fails. Language is content-driven (Hebrew prompt → "he").
        Assert.Equal("math",             meta.Subject);
        Assert.Equal("he",               meta.Language);
        Assert.Equal("bagrut_reference", meta.SourceType);
        Assert.True(meta.ExpectedFigures);
    }

    // --------------------------------------------------------------------
    // ClassifyTaxonomy — content-driven keyword classification
    // --------------------------------------------------------------------

    [Theory]
    // Calculus
    [InlineData("Find the definite integral from 0 to 1", "x^2 dx", "calculus.definite_integrals")]
    [InlineData("Compute the integral", @"\int x dx", "calculus.integrals_intro")]
    [InlineData("Apply the chain rule to find the derivative", "f(x) = (x+1)^2", "calculus.derivative_rules")]
    [InlineData("Find the maximum value using the derivative", "f'(x) = 0", "calculus.applications_of_derivatives")]
    [InlineData("Differentiate the following function", "f(x) = x^3", "calculus.derivative_definition")]
    [InlineData("Evaluate the limit", @"\lim_{x \to 0} \sin x", "calculus.limits")]
    // Trigonometry
    [InlineData("Apply the law of sines in triangle ABC", "sin A / a = sin B / b", "trigonometry.sine_cosine_rules")]
    [InlineData("Prove the trig identity", "sin^2 + cos^2 = 1", "trigonometry.trig_identities")]
    [InlineData("Convert 90 degrees to radians", "", "trigonometry.radian_measure")]
    // Vectors
    [InlineData("Compute the dot product of u and v", "u . v", "vectors.dot_product")]
    [InlineData("Find a vector perpendicular to both", "u, v", "vectors.vector_basics")]
    // Probability
    [InlineData("X follows a normal distribution", "mu = 0, sigma = 1", "probability.normal_distribution")]
    [InlineData("Find the probability of event A", "P(A)", "probability.basic_probability")]
    // Geometry
    [InlineData("In triangle ABC find side a", "", "geometry.triangles")]
    [InlineData("A circle has radius r", "", "geometry.circle_properties")]
    // Functions
    [InlineData("Solve for x in the logarithm equation", "log(x) = 2", "functions.logarithmic_functions")]
    [InlineData("Solve the exponential equation", "2^x = 8", "functions.exponential_functions")]
    // Algebra
    [InlineData("Factor the polynomial completely", "x^3 - 1", "algebra.polynomials")]
    [InlineData("Find the next term in the sequence", "1, 2, 4, 8", "algebra.sequences_series")]
    [InlineData("Solve the system of equations", "x + y = 3, 2x - y = 0", "algebra.systems_of_equations")]
    public void ClassifyTaxonomy_Returns_Expected_Leaf_For_English_Prompts(
        string prompt, string latex, string expected)
    {
        var (node, confidence) = BagrutDraftPersistence.ClassifyTaxonomy(prompt, latex);

        Assert.Equal(expected, node);
        Assert.True(confidence > 0.0,
            $"expected positive confidence for matched prompt '{prompt}', got {confidence}");
    }

    [Theory]
    // Hebrew calculus / trig / vectors / probability / algebra keywords —
    // the classifier MUST handle Hebrew because real Bagrut prompts are
    // Hebrew. Each row covers one Hebrew keyword path.
    [InlineData("חשב את האינטגרל המסוים מ 0 עד 1", "calculus.definite_integrals")]
    [InlineData("חשב את האינטגרל של הפונקציה", "calculus.integrals_intro")]
    [InlineData("מצא את הנגזרת לפי כלל המכפלה", "calculus.derivative_rules")]
    [InlineData("מצא את הנגזרת של הפונקציה ואת ערך המקסימום שלה", "calculus.applications_of_derivatives")]
    [InlineData("חשב את הגבול של הפונקציה", "calculus.limits")]
    [InlineData("הוכח את הזהות הטריגונומטרית", "trigonometry.trig_identities")]
    [InlineData("חשב את המכפלה הסקלרית של u ו-v", "vectors.dot_product")]
    [InlineData("חשב את ההסתברות של המאורע", "probability.basic_probability")]
    [InlineData("פרק את הפולינום לגורמים", "algebra.polynomials")]
    [InlineData("פתור את מערכת המשוואות הבאה", "algebra.systems_of_equations")]
    public void ClassifyTaxonomy_Returns_Expected_Leaf_For_Hebrew_Prompts(
        string prompt, string expected)
    {
        var (node, confidence) = BagrutDraftPersistence.ClassifyTaxonomy(prompt, latex: null);

        Assert.Equal(expected, node);
        Assert.True(confidence > 0.0);
    }

    [Theory]
    // Arabic Bagrut-paper keywords (Israeli Arab schools take the
    // Arabic-language Bagrut). Each row covers one keyword path with
    // typical Arabic definite-article prefixes (ال) on the noun.
    [InlineData("احسب التكامل المحدد من 0 إلى 1", "calculus.definite_integrals")]
    [InlineData("احسب التكامل للدالة التالية", "calculus.integrals_intro")]
    [InlineData("جد المشتقة باستخدام قاعدة الجداء", "calculus.derivative_rules")]
    [InlineData("جد المشتقة وأوجد الحد الأقصى للدالة", "calculus.applications_of_derivatives")]
    [InlineData("احسب نهاية الدالة", "calculus.limits")]
    [InlineData("أثبت المتطابقة المثلثية", "trigonometry.trig_identities")]
    [InlineData("احسب الجداء النقطي للمتجهين", "vectors.dot_product")]
    [InlineData("احسب احتمال الحدث", "probability.basic_probability")]
    [InlineData("حلّل كثير الحدود إلى عوامل", "algebra.polynomials")]
    [InlineData("حلّ نظام المعادلات التالي", "algebra.systems_of_equations")]
    [InlineData("جد المتجه العمودي على المتجهين", "vectors.vector_basics")]
    [InlineData("ارسم الدالة الأسية", "functions.exponential_functions")]
    public void ClassifyTaxonomy_Returns_Expected_Leaf_For_Arabic_Prompts(
        string prompt, string expected)
    {
        var (node, confidence) = BagrutDraftPersistence.ClassifyTaxonomy(prompt, latex: null);

        Assert.Equal(expected, node);
        Assert.True(confidence > 0.0);
    }

    // --------------------------------------------------------------------
    // DetectLanguage — script-ratio based detection (he / ar / en)
    // --------------------------------------------------------------------

    [Theory]
    [InlineData("חשב את האינטגרל של x בריבוע", null,                                  "he", 0.95)]
    [InlineData("احسب التكامل للدالة التالية",   null,                                  "ar", 0.95)]
    [InlineData("Compute the integral",          null,                                  "en", 0.95)]
    [InlineData(null,                            "x^2 + 2x = 3",                        "en", 0.95)]
    public void DetectLanguage_Picks_Dominant_Script(
        string? prompt, string? latex, string expected, double expectedConfidence)
    {
        var (lang, conf) = BagrutDraftPersistence.DetectLanguage(prompt, latex);

        Assert.Equal(expected, lang);
        Assert.Equal(expectedConfidence, conf);
    }

    [Fact]
    public void DetectLanguage_Falls_Back_To_He_With_Low_Confidence_For_Empty_Input()
    {
        var (lang, conf) = BagrutDraftPersistence.DetectLanguage(prompt: "", latex: "");

        Assert.Equal("he", lang);
        Assert.Equal(0.50, conf);
    }

    [Fact]
    public void DetectLanguage_Picks_Hebrew_Over_Latex_Symbols_When_Hebrew_Dominates()
    {
        // A Hebrew prompt with ASCII-heavy LaTeX still classifies as "he"
        // because the prompt's Hebrew letters dominate over the LaTeX's
        // single-letter Latin variables. Confidence may drop below 0.95
        // when Latin chars push the ratio under 70%.
        var (lang, _) = BagrutDraftPersistence.DetectLanguage(
            prompt: "חשב את האינטגרל של הפונקציה",
            latex: @"\int x^2 dx");

        Assert.Equal("he", lang);
    }

    [Fact]
    public async Task PersistAsync_Sets_Language_To_Ar_When_Prompt_Is_Arabic()
    {
        // 2026-04-29 user feedback ("שאלון בערבית استمارة"): Arabic
        // Bagrut papers must auto-fill Language="ar", not the
        // hardcoded "he".
        var drafts = new[]
        {
            MakeDraft(prompt: "احسب التكامل للدالة التالية", latex: @"\int f(x) dx"),
        };

        await _persistence.PersistAsync(
            examCode: "math-5u-2026-35581",
            sourcePdfId: "pdf-arabic",
            sourceFilename: "math-arabic.pdf",
            submittedBy: "curator@cena.dev",
            drafts: drafts);

        var meta = _storedItems[0].AutoExtractedMetadata!;
        Assert.Equal("ar", meta.Language);

        // Taxonomy still classifies — Arabic keyword path covers the
        // bilingual content of Israeli math Bagrut.
        Assert.Equal("calculus.integrals_intro", meta.TaxonomyNode);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("Question prompt", "x^2 + 2x = 3")]   // no domain keyword
    [InlineData("Generic statement with no math", null)]
    public void ClassifyTaxonomy_Returns_Null_When_No_Keyword_Matches(string? prompt, string? latex)
    {
        var (node, confidence) = BagrutDraftPersistence.ClassifyTaxonomy(prompt, latex);

        Assert.Null(node);
        Assert.Equal(0.0, confidence);
    }

    // --------------------------------------------------------------------
    // PersistAsync end-to-end — taxonomy reflects per-draft content
    // --------------------------------------------------------------------

    [Fact]
    public async Task PersistAsync_TaxonomyNode_Differs_Per_Draft_When_Content_Differs()
    {
        // Two drafts, same examCode (track), different content. Expected:
        // both share Track but TaxonomyNode is computed independently.
        var drafts = new[]
        {
            MakeDraft(page: 1, prompt: "Compute the integral", latex: @"\int x dx"),
            MakeDraft(page: 2, prompt: "Factor the polynomial", latex: "x^3 - 1"),
        };

        await _persistence.PersistAsync(
            examCode: "math-5u-2026-35581",
            sourcePdfId: "pdf-abc",
            sourceFilename: null,
            submittedBy: "curator@cena.dev",
            drafts: drafts);

        Assert.Equal(2, _storedItems.Count);
        Assert.Equal("calculus.integrals_intro", _storedItems[0].AutoExtractedMetadata?.TaxonomyNode);
        Assert.Equal("algebra.polynomials",      _storedItems[1].AutoExtractedMetadata?.TaxonomyNode);
        // Both share track parsed from examCode.
        Assert.Equal("5u", _storedItems[0].AutoExtractedMetadata?.Track);
        Assert.Equal("5u", _storedItems[1].AutoExtractedMetadata?.Track);
    }

    [Fact]
    public async Task PersistAsync_Sets_TaxonomyNode_Null_When_Prompt_Has_No_Keyword()
    {
        // Generic prompt without any classifier keywords. Track still
        // resolves (parsed from examCode) but TaxonomyNode stays null —
        // the curator picks the leaf manually.
        var drafts = new[] { MakeDraft(prompt: "See attached figure", latex: "") };

        await _persistence.PersistAsync(
            examCode: "math-5u-2026-35581",
            sourcePdfId: "pdf-abc",
            sourceFilename: null,
            submittedBy: "curator@cena.dev",
            drafts: drafts);

        var meta = _storedItems[0].AutoExtractedMetadata!;
        Assert.Equal("5u", meta.Track);            // parsed from examCode
        Assert.Null(meta.TaxonomyNode);            // no keyword matched

        var conf = _storedItems[0].MetadataFieldConfidences;
        Assert.Equal(0.95, conf[nameof(CuratorMetadata.Track)]);
        Assert.Equal(0.0,  conf[nameof(CuratorMetadata.TaxonomyNode)]);
    }

    // --------------------------------------------------------------------
    // Idempotency / shape preservation
    // --------------------------------------------------------------------

    [Fact]
    public async Task PersistAsync_Does_Not_Touch_CuratorMetadata_Field()
    {
        // CuratorMetadata is the curator-edited side of the handshake;
        // auto-fill must NOT pre-populate that — only the AutoExtracted
        // side.
        await _persistence.PersistAsync(
            examCode: "math-5u-2026-35581",
            sourcePdfId: "pdf-abc",
            sourceFilename: null,
            submittedBy: "curator@cena.dev",
            drafts: new[] { MakeDraft() });

        Assert.Null(_storedItems[0].CuratorMetadata);
    }

    [Fact]
    public async Task PersistAsync_Empty_Drafts_Returns_Empty_Without_Storing()
    {
        var ids = await _persistence.PersistAsync(
            examCode: "math-5u-2026-35581",
            sourcePdfId: "pdf-abc",
            sourceFilename: null,
            submittedBy: "curator@cena.dev",
            drafts: Array.Empty<IngestionDraftQuestion>());

        Assert.Empty(ids);
        Assert.Empty(_storedItems);
        Assert.Empty(_storedPayloads);
    }

    [Fact]
    public async Task PersistAsync_Multi_Draft_Shares_Track_Per_Draft()
    {
        // Three drafts with the same content keyword → all three classify
        // into the same taxonomy leaf and share the parsed track.
        var drafts = new[]
        {
            MakeDraft(page: 1, prompt: "Compute the integral", latex: @"\int x dx"),
            MakeDraft(page: 2, prompt: "Compute the integral", latex: @"\int 2x dx"),
            MakeDraft(page: 3, prompt: "Compute the integral", latex: @"\int x^2 dx"),
        };

        await _persistence.PersistAsync(
            examCode: "math-5u-2026-35581",
            sourcePdfId: "pdf-abc",
            sourceFilename: null,
            submittedBy: "curator@cena.dev",
            drafts: drafts);

        Assert.Equal(3, _storedItems.Count);
        Assert.Equal(3, _storedPayloads.Count);
        Assert.All(_storedItems, item =>
        {
            Assert.Equal("auto_extracted",          item.MetadataState);
            Assert.Equal("5u",                      item.AutoExtractedMetadata?.Track);
            Assert.Equal("calculus.integrals_intro", item.AutoExtractedMetadata?.TaxonomyNode);
        });
    }
}
