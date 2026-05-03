// =============================================================================
// Cena Platform — Corpus → isomorph-seed wiring test (prr-242)
//
// End-to-end intent: PDF ingestion → BagrutCorpusItem rows → the Strategy-2
// isomorph pipeline (MinistrySimilarityChecker via IMinistryReferenceCorpus)
// uses those rows to reject candidates that sit too close to Ministry text.
//
// This test drives the seam: a BagrutCorpusService populated from the
// extractor's output feeds the similarity checker, and a candidate stem
// that matches the ingested Ministry text is flagged as too close.
//
// Marten is not required — we wire a thin in-memory document store that
// implements just the slice of IDocumentStore BagrutCorpusService uses.
// The point is the CONTRACT between corpus ingest and the isomorph gate,
// not Marten round-tripping.
// =============================================================================

using Cena.Actors.QuestionBank.Coverage;
using Cena.Admin.Api.Ingestion;
using Cena.Infrastructure.Documents;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class BagrutCorpusIsomorphSeedTests
{
    /// <summary>
    /// Build a fake IMinistryReferenceCorpus by reading directly from an
    /// in-memory list, projecting the corpus extractor's output into the
    /// stem-only shape the similarity checker consumes. We do NOT spin
    /// Marten because the point of the test is the PIPE: extractor →
    /// similarity check, not the database round-trip (covered in
    /// BagrutCorpusServiceTests via NSubstitute).
    /// </summary>
    private sealed class InMemoryCorpus : IMinistryReferenceCorpus
    {
        private readonly List<BagrutCorpusItemDocument> _items = new();

        public void Add(BagrutCorpusItemDocument item) => _items.Add(item);

        public IReadOnlyList<MinistryReferenceItem> GetReferences(string subject, string trackKey)
        {
            var subjectCode = BagrutCorpusService.MapSubjectToMinistryCode(subject);
            return _items
                .Where(i => i.MinistrySubjectCode == subjectCode && i.TrackKey == trackKey)
                .Select(i => new MinistryReferenceItem(i.Id, i.NormalisedStem))
                .ToList();
        }
    }

    [Fact]
    public void Similarity_checker_rejects_candidate_matching_ingested_corpus_item()
    {
        // 1. Ingest: one past-Bagrut question → one corpus item (via extractor).
        var drafts = new[]
        {
            new IngestionDraftQuestion(
                DraftId: "d-1",
                SourcePage: 1,
                Prompt: "Find the roots of the quadratic equation x^2 - 5x + 6 = 0 and evaluate their product.",
                LatexContent: null,
                AnswerChoices: Array.Empty<string>(),
                CorrectAnswer: null,
                ExamCode: "bagrut-math-5u",
                FigureSpecJson: null,
                ExtractionConfidence: 0.9,
                ReviewNotes: Array.Empty<string>()),
        };

        var ctx = new BagrutCorpusIngestContext(
            ExamCode: "bagrut-math-5u",
            MinistrySubjectCode: "035",
            MinistryQuestionPaperCode: "035581",
            Units: 5,
            Year: 2022,
            Season: BagrutCorpusSeason.Summer,
            Moed: "A",
            Stream: BagrutCorpusStream.Hebrew,
            DefaultTopicId: "algebra.quadratics",
            SourceFilename: "bagrut-math-5u-2022-summer-A.pdf",
            SourcePdfId: "pdf-test",
            UploadedBy: "admin",
            IngestedAt: DateTimeOffset.UtcNow);

        var corpusItems = BagrutCorpusExtractor.Extract(drafts, ctx);
        Assert.Single(corpusItems);

        var corpus = new InMemoryCorpus();
        foreach (var item in corpusItems) corpus.Add(item);

        // 2. Isomorph candidate: verbatim Ministry stem (should be flagged).
        var checker = new MinistrySimilarityChecker(corpus, threshold: 0.82);
        var verbatim = checker.Score(
            candidateStem: drafts[0].Prompt,
            subject: "math",
            trackKey: "5U");
        Assert.True(verbatim.IsTooClose,
            $"Verbatim Ministry candidate should be flagged; score={verbatim.Score}");

        // 3. A genuinely different candidate should pass.
        var different = checker.Score(
            candidateStem: "A ladder leans against a wall at 60 degrees; find its height.",
            subject: "math",
            trackKey: "5U");
        Assert.False(different.IsTooClose,
            $"Unrelated candidate should pass; score={different.Score}");
    }

    [Fact]
    public void Empty_corpus_passes_every_candidate()
    {
        // Null-case: no Ministry corpus loaded → nothing rejects.
        var corpus = new InMemoryCorpus();
        var checker = new MinistrySimilarityChecker(corpus, threshold: 0.82);
        var verdict = checker.Score("anything goes", "math", "5U");
        Assert.False(verdict.IsTooClose);
    }
}
