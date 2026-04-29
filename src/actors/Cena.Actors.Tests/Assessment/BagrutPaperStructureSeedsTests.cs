// =============================================================================
// Cena Platform — BagrutPaperStructureSeeds tests (PRR-290)
//
// Pins the generated catalog so a careless edit can't silently shrink the
// structure pool below the coordinator's "50+" target. Also asserts the
// invariants the runner depends on (deterministic Ids, valid section
// counts, all-Slots have valid topic ids).
// =============================================================================

using Cena.Actors.Assessment;

namespace Cena.Actors.Tests.Assessment;

public sealed class BagrutPaperStructureSeedsTests
{
    [Fact]
    public void BuildSyntheticStructures_EmitsAtLeast50_PaperStructures()
    {
        // Coordinator m_f578dc757ac8 set the bar: 50+ structures across
        // years × seasons × moeds × {math 5U, math 4U, physics 5U}. We
        // emit 6 years × 4 sittings × 3 exams = 72.
        var built = BagrutPaperStructureSeeds.BuildSyntheticStructures();
        Assert.True(built.Count >= 50,
            $"Expected ≥50 synthetic structures, got {built.Count}.");
        Assert.Equal(BagrutPaperStructureSeeds.ExpectedCount, built.Count);
    }

    [Fact]
    public void BuildSyntheticStructures_AllIds_AreUnique()
    {
        // Marten upserts on Id collision; collisions inside the synthetic
        // set would silently lose papers. Pin by asserting all-distinct.
        var built = BagrutPaperStructureSeeds.BuildSyntheticStructures();
        var ids = built.Select(d => d.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void BuildSyntheticStructures_AllIds_ConformToCanonicalShape()
    {
        // Composite Id is "{examCode}/{paperCode}". The catalog's GET
        // path keys on this composite, so any drift breaks lookup.
        var built = BagrutPaperStructureSeeds.BuildSyntheticStructures();
        foreach (var d in built)
        {
            Assert.Equal(BagrutPaperStructureDocument.ComposeId(d.ExamCode, d.PaperCode), d.Id);
            Assert.False(string.IsNullOrWhiteSpace(d.PaperCode),
                $"Synthetic structures must have non-null PaperCode (default-papers stay hand-authored). Got Id={d.Id}.");
        }
    }

    [Theory]
    [InlineData("806")]
    [InlineData("807")]
    [InlineData("036")]
    public void BuildSyntheticStructures_CoversCanonicalExamCodes(string examCode)
    {
        var built = BagrutPaperStructureSeeds.BuildSyntheticStructures();
        Assert.True(built.Any(d => d.ExamCode == examCode),
            $"Synthetic catalog must cover examCode {examCode}.");
    }

    [Fact]
    public void BuildSyntheticStructures_AllStructures_HaveBothSections()
    {
        // The runner requires Section A and Section B on every structure
        // (MockExamRunService.StartAsync throws otherwise). Pin so a
        // generator regression can't ship a one-section paper.
        var built = BagrutPaperStructureSeeds.BuildSyntheticStructures();
        foreach (var d in built)
        {
            var hasA = d.Sections.Any(s => s.SectionLabel == "A");
            var hasB = d.Sections.Any(s => s.SectionLabel == "B");
            Assert.True(hasA && hasB,
                $"Structure {d.Id} must have both A and B sections.");
        }
    }

    [Fact]
    public void BuildSyntheticStructures_AllSlots_HaveTopicIdsAndPositivePoints()
    {
        // The slot-aware draw matches questions by TopicId; an empty
        // TopicId silently degrades to FallbackTopicId. Points feed the
        // mark-sheet; non-positive points break section-weighted scoring.
        var built = BagrutPaperStructureSeeds.BuildSyntheticStructures();
        foreach (var d in built)
        foreach (var s in d.Sections)
        foreach (var slot in s.Slots)
        {
            Assert.False(string.IsNullOrWhiteSpace(slot.TopicId),
                $"{d.Id} Section {s.SectionLabel} Slot {slot.SlotNumber}: TopicId is empty.");
            Assert.True(slot.Points > 0,
                $"{d.Id} Section {s.SectionLabel} Slot {slot.SlotNumber}: Points must be > 0.");
        }
    }

    [Fact]
    public void BuildSyntheticStructures_PaperCodes_FollowMinistryNumberingFamilies()
    {
        // Synthetic codes follow the Ministry numbering convention so
        // they coexist cleanly with real Ministry codes ingested via the
        // PRR-251 BagrutCorpus pipeline (collisions are intentional;
        // real-Ministry upserts override synthetic stubs).
        var built = BagrutPaperStructureSeeds.BuildSyntheticStructures();
        foreach (var d in built)
        {
            // Math 5U → 035xxx, Math 4U → 037xxx, Physics 5U → 036xxx.
            var expectedPrefix = d.ExamCode switch
            {
                "806" => "0355",
                "807" => "0375",
                "036" => "0365",
                _ => throw new InvalidOperationException($"Unexpected examCode {d.ExamCode}"),
            };
            Assert.StartsWith(expectedPrefix, d.PaperCode);
            Assert.Equal(6, d.PaperCode!.Length);
        }
    }

    [Fact]
    public void YearRange_IsWithin2020To2025()
    {
        // The "production" year range: Bagrut papers published since
        // 2020 are the most-likely to be referenced. Older papers are
        // out of scope per coordinator m_f578dc757ac8.
        Assert.Equal(2020, BagrutPaperStructureSeeds.MinYear);
        Assert.Equal(2025, BagrutPaperStructureSeeds.MaxYear);
    }

    [Fact]
    public void BuildSyntheticStructures_IsDeterministic_AcrossInvocations()
    {
        // Idempotent: re-running the seeder MUST produce identical Ids
        // and topic distributions so Marten upserts are no-ops on second
        // run. A non-deterministic generator would silently drift the
        // catalog under retry.
        var first = BagrutPaperStructureSeeds.BuildSyntheticStructures();
        var second = BagrutPaperStructureSeeds.BuildSyntheticStructures();
        Assert.Equal(first.Select(d => d.Id), second.Select(d => d.Id));
    }
}
