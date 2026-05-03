// =============================================================================
// Cena Platform — Mock-exam paper draw (extracted from MockExamRunService)
//
// Single-responsibility: given a paper structure, the candidate question
// pools, and a request, produce the (PartAIds, PartBIds, VariantSeed) triple
// the runner persists into ExamSimulationState. Encapsulates:
//
//   * The two-pool draw strategy (multi-part preferred for Part B long-form
//     slots; single-cell fallback through exact-topic → section-fallback →
//     subject-wide pools, each transition emitting a SlotFallbacks counter
//     so ops can alarm on item-bank gaps).
//   * PRR-291 cohort fairness: when paperCode is set, the FIRST start in a
//     3-hour window seeds a deterministic BagrutPaperRunPool; subsequent
//     starts in the same window reuse the frozen lists verbatim. Self-pay
//     runs (paperCode null) keep per-student randomness.
//   * Phase 3 #2 stronger seed: per-process atomic counter mixed into the
//     RNG so two starts in the same tick still produce different shuffles
//     on the per-student path.
//
// ADR alignment:
//   * ADR-0043 — the draw produces ITEM IDs only. Provenance enforcement on
//     the items themselves (reference vs recreation) lives in IItemDeliveryGate
//     downstream, NOT here. This module sees raw IDs and trusts the pool
//     filter to have eliminated reference-only items already (PRR-246/E13).
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Questions;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Assessment;

public sealed class MockExamPaperDraw
{
    private readonly ILogger _logger;
    private readonly TimeProvider _clock;

    private static readonly Meter Meter = new("Cena.MockExam.PaperDraw", "1.0.0");
    private static readonly Counter<long> SlotFallbacks =
        Meter.CreateCounter<long>("cena_mock_exam_slot_fallback_total");

    /// <summary>Process-scoped counter mixed into the per-student shuffle
    /// seed so two starts within the same tick still produce different draws.
    /// Static because it must be unique across all instances.</summary>
    private static long s_seedCounter;

    public MockExamPaperDraw(ILogger logger, TimeProvider? clock = null)
    {
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<MockExamPaperDrawResult> DrawAsync(
        IDocumentSession session,
        BagrutPaperStructureDocument structure,
        string paperCode_OrNull,
        string subject,
        string studentId,
        string examCode,
        CancellationToken ct)
    {
        var multipartPool = await session.Query<BagrutMultipartQuestion>()
            .Where(q => q.Subject == subject)
            .ToListAsync(ct);

        var pool = await session.Query<QuestionReadModel>()
            .Where(q => q.Subject == subject && q.Status == "Published")
            .ToListAsync(ct);

        if (pool.Count == 0 && multipartPool.Count == 0)
        {
            throw new InvalidOperationException(
                $"No published questions for subject '{subject}'. The exam-prep dev seeder may not have run, or the production pool is empty.");
        }

        var poolByConcept = pool
            .SelectMany(q => q.Concepts.Select(c => (concept: c, q)))
            .GroupBy(t => t.concept)
            .ToDictionary(g => g.Key, g => g.Select(t => t.q).ToList());

        var multipartByTopic = multipartPool
            .GroupBy(q => q.Topic)
            .ToDictionary(g => g.Key, g => g.ToList());

        var nowForCohort = _clock.GetUtcNow();
        var cohortPool = !string.IsNullOrWhiteSpace(paperCode_OrNull)
            ? await session.LoadAsync<BagrutPaperRunPool>(
                BagrutPaperRunPool.ComposeId(paperCode_OrNull!,
                    BagrutPaperRunPool.ComputeWindowStart(nowForCohort)),
                ct)
            : null;

        var rng = cohortPool is not null
            ? new Random(cohortPool.VariantSeed)
            : !string.IsNullOrWhiteSpace(paperCode_OrNull)
                ? new Random(BagrutPaperRunPool.ComputeCohortSeed(
                    paperCode_OrNull!,
                    BagrutPaperRunPool.ComputeWindowStart(nowForCohort)))
                : new Random(($"{studentId}|{examCode}|{Guid.NewGuid():N}|" +
                    $"{System.Threading.Interlocked.Increment(ref s_seedCounter)}").GetHashCode());

        var partAIds = new List<string>();
        var partBIds = new List<string>();
        var drawn = new HashSet<string>(StringComparer.Ordinal);

        var partASection = structure.Sections.FirstOrDefault(s => s.SectionLabel == "A")
            ?? throw new InvalidOperationException(
                $"Paper structure {structure.Id} must have an A section.");
        var partBSection = structure.Sections.FirstOrDefault(s => s.SectionLabel == "B")
            ?? throw new InvalidOperationException(
                $"Paper structure {structure.Id} must have a B section.");

        if (cohortPool is not null)
        {
            partAIds.AddRange(cohortPool.PartAQuestionIds);
            partBIds.AddRange(cohortPool.PartBQuestionIds);
            foreach (var id in partAIds) drawn.Add(id);
            foreach (var id in partBIds) drawn.Add(id);
            _logger.LogInformation(
                "[MOCK-EXAM-COHORT] reuse pool studentId={StudentId} paperCode={PaperCode} windowStart={Window} partA={A} partB={B}",
                studentId, paperCode_OrNull, cohortPool.WindowStart, partAIds.Count, partBIds.Count);
        }
        else
        {
            DrawForSlots(partASection, poolByConcept, pool, rng, drawn,
                preferMultipart: false, multipartByTopic, partAIds);
            DrawForSlots(partBSection, poolByConcept, pool, rng, drawn,
                preferMultipart: true, multipartByTopic, partBIds);

            if (partAIds.Count < partASection.RequiredAnswers)
            {
                throw new InvalidOperationException(
                    $"Could not draw enough Part A items: have {partAIds.Count}, need {partASection.RequiredAnswers}.");
            }
            if (partBIds.Count < partBSection.Slots.Count)
            {
                var deficit = partBSection.Slots.Count - partBIds.Count;
                var extras = Shuffle(pool.Where(q => !drawn.Contains(q.Id)).ToList(), rng)
                    .Take(deficit).Select(q => q.Id).ToList();
                partBIds.AddRange(extras);
                foreach (var id in extras) drawn.Add(id);
            }
        }

        var variantSeed = cohortPool?.VariantSeed ?? rng.Next();

        // Seed the cohort pool when this is the first start in the window
        // and a paperCode was supplied. Marten UPSERT means a race between
        // two concurrent first-starts is benign (both used the same
        // deterministic seed → same PartA/PartB lists).
        if (cohortPool is null && !string.IsNullOrWhiteSpace(paperCode_OrNull))
        {
            var pool291 = new BagrutPaperRunPool
            {
                Id = BagrutPaperRunPool.ComposeId(paperCode_OrNull!,
                    BagrutPaperRunPool.ComputeWindowStart(nowForCohort)),
                PaperCode = paperCode_OrNull!,
                WindowStart = BagrutPaperRunPool.ComputeWindowStart(nowForCohort),
                PartAQuestionIds = new List<string>(partAIds),
                PartBQuestionIds = new List<string>(partBIds),
                VariantSeed = variantSeed,
                SeededAt = nowForCohort,
            };
            session.Store(pool291);
            _logger.LogInformation(
                "[MOCK-EXAM-COHORT] seed pool studentId={StudentId} paperCode={PaperCode} windowStart={Window} partA={A} partB={B}",
                studentId, paperCode_OrNull, pool291.WindowStart, partAIds.Count, partBIds.Count);
        }

        return new MockExamPaperDrawResult(partAIds, partBIds, variantSeed);
    }

    private void DrawForSlots(
        PaperSection section,
        Dictionary<string, List<QuestionReadModel>> poolByConcept,
        IReadOnlyList<QuestionReadModel> fullPool,
        Random rng,
        HashSet<string> drawn,
        bool preferMultipart,
        Dictionary<string, List<BagrutMultipartQuestion>> multipartByTopic,
        List<string> dest)
    {
        foreach (var slot in section.Slots)
        {
            string? pickedId = null;

            if (preferMultipart)
            {
                pickedId = TryPickMultipart(multipartByTopic, slot.TopicId, slot, drawn, rng)?.Id
                        ?? TryPickMultipart(multipartByTopic, section.FallbackTopicId, slot, drawn, rng)?.Id;
            }

            if (pickedId is null)
            {
                var pick = TryPick(poolByConcept, slot.TopicId, slot, drawn, rng);
                if (pick is null)
                {
                    pick = TryPick(poolByConcept, section.FallbackTopicId, slot, drawn, rng);
                    if (pick is not null)
                    {
                        SlotFallbacks.Add(1, new KeyValuePair<string, object?>("level", "section"));
                        _logger.LogInformation(
                            "[MOCK-EXAM-FALLBACK] slot {Slot} topic={Topic} fell to section topic={Section}",
                            slot.SlotNumber, slot.TopicId, section.FallbackTopicId);
                    }
                }
                if (pick is null)
                {
                    pick = Shuffle(fullPool.Where(q => !drawn.Contains(q.Id)).ToList(), rng).FirstOrDefault();
                    if (pick is not null)
                    {
                        SlotFallbacks.Add(1, new KeyValuePair<string, object?>("level", "subject"));
                        _logger.LogWarning(
                            "[MOCK-EXAM-FALLBACK-SUBJECT] slot {Slot} topic={Topic} could not be matched; falling to ANY subject item. The pool likely needs more items for {Topic}.",
                            slot.SlotNumber, slot.TopicId, slot.TopicId);
                    }
                }
                pickedId = pick?.Id;
            }

            if (pickedId is null) continue;

            drawn.Add(pickedId);
            dest.Add(pickedId);
        }
    }

    private static BagrutMultipartQuestion? TryPickMultipart(
        Dictionary<string, List<BagrutMultipartQuestion>> byTopic,
        string topicId,
        PaperSlot slot,
        HashSet<string> drawn,
        Random rng)
    {
        if (!byTopic.TryGetValue(topicId, out var candidates)) return null;
        var matching = candidates
            .Where(q => !drawn.Contains(q.Id)
                     && q.BloomsLevel >= slot.MinBloom
                     && q.BloomsLevel <= slot.MaxBloom)
            .ToList();
        if (matching.Count == 0) return null;
        return matching[rng.Next(matching.Count)];
    }

    private static QuestionReadModel? TryPick(
        Dictionary<string, List<QuestionReadModel>> poolByConcept,
        string conceptId,
        PaperSlot slot,
        HashSet<string> drawn,
        Random rng)
    {
        if (!poolByConcept.TryGetValue(conceptId, out var candidates)) return null;
        var matching = candidates
            .Where(q => !drawn.Contains(q.Id)
                     && q.BloomsLevel >= slot.MinBloom
                     && q.BloomsLevel <= slot.MaxBloom)
            .ToList();
        if (matching.Count == 0) return null;
        return matching[rng.Next(matching.Count)];
    }

    private static IEnumerable<T> Shuffle<T>(IList<T> source, Random rng)
    {
        var arr = source.ToArray();
        for (var i = arr.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        return arr;
    }
}

public sealed record MockExamPaperDrawResult(
    IReadOnlyList<string> PartAIds,
    IReadOnlyList<string> PartBIds,
    int VariantSeed);
