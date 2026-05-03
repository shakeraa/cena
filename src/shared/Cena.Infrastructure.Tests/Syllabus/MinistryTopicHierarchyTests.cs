// =============================================================================
// Cena Platform — MinistryTopicHierarchy tests (RDY-070 Phase 1A)
//
// Exercises the YAML-backed Ministry topic hierarchy against a hand-written
// fixture (avoids coupling the test to the authored math-bagrut-5unit.yaml
// which can evolve). Also verifies the live config/syllabi directory loads
// cleanly so a broken manifest would surface in CI.
// =============================================================================

using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Syllabus;

namespace Cena.Infrastructure.Tests.Syllabus;

public class MinistryTopicHierarchyTests
{
    // ── Synthetic YAML fixtures ──────────────────────────────────────────────

    private const string FixtureYaml = """
        track: track-math-test-5u
        version: 0.0.1-test
        bagrutTrack: FiveUnit
        ministryCodes: ["806", "807"]
        chapters:
          - slug: algebra-basics
            order: 1
            title:
              en: Algebra Basics
              he: יסודות אלגברה
              ar: أساسيات الجبر
            learningObjectiveIds:
              - lo-alg-a
              - lo-alg-b
            prerequisiteChapterSlugs: []
            expectedWeeks: 3
            ministryCode: "806.1"
          - slug: derivatives-intro
            order: 2
            title:
              en: Derivatives Intro
            learningObjectiveIds:
              - lo-calc-a
            prerequisiteChapterSlugs: [algebra-basics]
            expectedWeeks: 4
            ministryCode: "807.4"
        """;

    private static string WriteFixture(string content)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"cena-syllabus-test-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, content);
        return path;
    }

    // ── Basic exposure ───────────────────────────────────────────────────────

    [Fact]
    public void LoadFromFiles_ExposesAllTopicsInManifestOrder()
    {
        var path = WriteFixture(FixtureYaml);
        try
        {
            var hierarchy = MinistryTopicHierarchy.LoadFromFiles(new[] { path });
            var topics = hierarchy.TopicsFor(BagrutTrack.FiveUnit);

            Assert.Equal(2, topics.Count);
            Assert.Equal("algebra-basics",    topics[0].Slug);
            Assert.Equal("derivatives-intro", topics[1].Slug);
            Assert.Equal(1, topics[0].Order);
            Assert.Equal(2, topics[1].Order);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GetMinistryCode_ReturnsAuthoredCode()
    {
        var path = WriteFixture(FixtureYaml);
        try
        {
            var hierarchy = MinistryTopicHierarchy.LoadFromFiles(new[] { path });
            Assert.Equal("806.1", hierarchy.GetMinistryCode("algebra-basics"));
            Assert.Equal("807.4", hierarchy.GetMinistryCode("derivatives-intro"));
            Assert.Null(hierarchy.GetMinistryCode("not-authored"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parent_DerivesSyntheticMinistryRoot()
    {
        var path = WriteFixture(FixtureYaml);
        try
        {
            var hierarchy = MinistryTopicHierarchy.LoadFromFiles(new[] { path });
            Assert.Equal("ministry-806", hierarchy.Parent("algebra-basics"));
            Assert.Equal("ministry-807", hierarchy.Parent("derivatives-intro"));
            Assert.Null(hierarchy.Parent("not-authored"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TopicSlugForLearningObjective_ResolvesChapterOwner()
    {
        var path = WriteFixture(FixtureYaml);
        try
        {
            var hierarchy = MinistryTopicHierarchy.LoadFromFiles(new[] { path });
            Assert.Equal("algebra-basics",    hierarchy.TopicSlugForLearningObjective("lo-alg-a"));
            Assert.Equal("algebra-basics",    hierarchy.TopicSlugForLearningObjective("lo-alg-b"));
            Assert.Equal("derivatives-intro", hierarchy.TopicSlugForLearningObjective("lo-calc-a"));
            Assert.Null(hierarchy.TopicSlugForLearningObjective("lo-unknown"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GetTopic_ReturnsLocaleTitles()
    {
        var path = WriteFixture(FixtureYaml);
        try
        {
            var hierarchy = MinistryTopicHierarchy.LoadFromFiles(new[] { path });
            var topic = hierarchy.GetTopic("algebra-basics");
            Assert.NotNull(topic);
            Assert.Equal("Algebra Basics",    topic!.TitleByLocale["en"]);
            Assert.Equal("יסודות אלגברה",      topic.TitleByLocale["he"]);
            Assert.Equal("أساسيات الجبر",       topic.TitleByLocale["ar"]);
        }
        finally { File.Delete(path); }
    }

    // ── Failure modes ────────────────────────────────────────────────────────

    [Fact]
    public void LoadFromFiles_MissingFile_Throws()
    {
        var ex = Assert.Throws<FileNotFoundException>(() =>
            MinistryTopicHierarchy.LoadFromFiles(new[] { "/nope/does-not-exist.yaml" }));
        Assert.Contains("/nope/does-not-exist.yaml", ex.Message);
    }

    [Fact]
    public void DuplicateTopicSlugs_AcrossManifests_Throws()
    {
        var path1 = WriteFixture(FixtureYaml);
        var path2 = WriteFixture(FixtureYaml); // same slug "algebra-basics"
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                MinistryTopicHierarchy.LoadFromFiles(new[] { path1, path2 }));
            Assert.Contains("algebra-basics", ex.Message);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    [Fact]
    public void LearningObjectiveInMultipleChapters_Throws()
    {
        const string bad = """
            track: track-bad
            version: 0.0.1-test
            bagrutTrack: FiveUnit
            chapters:
              - slug: a
                order: 1
                learningObjectiveIds: [lo-same]
                expectedWeeks: 1
                ministryCode: "806.1"
              - slug: b
                order: 2
                learningObjectiveIds: [lo-same]
                expectedWeeks: 1
                ministryCode: "806.2"
            """;
        var path = WriteFixture(bad);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                MinistryTopicHierarchy.LoadFromFiles(new[] { path }));
            Assert.Contains("lo-same", ex.Message);
        }
        finally { File.Delete(path); }
    }

    // ── Live authored manifest smoke ─────────────────────────────────────────

    [Fact]
    public void LiveManifest_LoadsAndExposesTenBagrutChapters()
    {
        // Locate the repo's config/syllabi — the project test runs from
        // bin/Debug/net9.0, so walk up.
        var dir = LocateRepoDirectory("config/syllabi");
        if (dir is null)
        {
            // Not every CI runner layouts the repo identically; skip rather
            // than fail if we're detached from the source tree.
            return;
        }

        var hierarchy = MinistryTopicHierarchy.LoadFromDirectory(dir);
        var fiveUnit = hierarchy.TopicsFor(BagrutTrack.FiveUnit);

        Assert.Equal(10, fiveUnit.Count);
        Assert.Equal("algebra-review", fiveUnit[0].Slug);
        Assert.Equal("probability-and-statistics", fiveUnit[^1].Slug);
        Assert.All(fiveUnit, t => Assert.False(string.IsNullOrEmpty(t.MinistryCode)));
    }

    private static string? LocateRepoDirectory(string relative)
    {
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && cursor is not null; i++, cursor = cursor.Parent)
        {
            var probe = Path.Combine(cursor.FullName, relative);
            if (Directory.Exists(probe)) return probe;
        }
        return null;
    }
}
