// =============================================================================
// Cena Platform — RetakeTargetUsesRetrievalStrengthFramingTest (prr-238)
//
// Architectural ratchet: when a session or admin surface reads an
// ExamTarget's ReasonTag and that tag is Retake, the code MUST consult
// RetakeRetrievalStrengthBias (scheduler side) or the retake-cohort
// surface flag (admin side). No "re-teach" path for retake students.
//
// Enforcement is a two-part check:
//
//   (a) RetakeRetrievalStrengthBias exists and exposes the documented API
//       so downstream callers can consume it without defining their own
//       retake-vs-not logic.
//
//   (b) The retake-cohort endpoint (RetakeCohortEndpoint) exists and its
//       response type names the retrieval-strength framing flag. A future
//       refactor that silently renames the flag or removes it would be
//       caught here.
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.StudentPlan;

namespace Cena.Actors.Tests.Architecture;

public sealed class RetakeTargetUsesRetrievalStrengthFramingTest
{
    [Fact]
    public void RetrievalStrengthBias_AppliesOnlyToRetakeReason()
    {
        Assert.True(RetakeRetrievalStrengthBias.AppliesTo(ReasonTag.Retake));
        Assert.False(RetakeRetrievalStrengthBias.AppliesTo(ReasonTag.NewSubject));
        Assert.False(RetakeRetrievalStrengthBias.AppliesTo(ReasonTag.ReviewOnly));
        Assert.False(RetakeRetrievalStrengthBias.AppliesTo(ReasonTag.Enrichment));
        Assert.False(RetakeRetrievalStrengthBias.AppliesTo(ReasonTag.SafetyFlag));
        Assert.False(RetakeRetrievalStrengthBias.AppliesTo(null));
    }

    [Fact]
    public void RetrievalStrengthBias_PrefersRetrievalForRetake()
    {
        var (retrieval, worked) = RetakeRetrievalStrengthBias.ForReason(ReasonTag.Retake);
        Assert.True(retrieval > worked,
            "prr-238: retake targets must weight retrieval practice ABOVE worked examples. "
            + "Karpicke & Roediger 2008 d≈0.50 dictates retrieval-first surface.");
        Assert.True(retrieval > RetakeRetrievalStrengthBias.DefaultRetrievalWeight,
            "prr-238: retake retrieval weight must exceed the default — otherwise the "
            + "bias is a no-op and retake students get the same mix as first-time learners.");
    }

    [Fact]
    public void RetrievalStrengthBias_DefaultsPreservedForNonRetake()
    {
        foreach (var tag in new ReasonTag?[]
                 {
                     null, ReasonTag.NewSubject, ReasonTag.ReviewOnly,
                     ReasonTag.Enrichment, ReasonTag.SafetyFlag,
                 })
        {
            var (retrieval, worked) = RetakeRetrievalStrengthBias.ForReason(tag);
            Assert.Equal(RetakeRetrievalStrengthBias.DefaultRetrievalWeight, retrieval);
            Assert.Equal(RetakeRetrievalStrengthBias.DefaultWorkedExampleWeight, worked);
        }
    }

    [Fact]
    public void RetakeCohortEndpoint_ResponseType_NamesRetrievalStrengthFlag()
    {
        // Load the endpoint from Cena.Admin.Api via reflection so this
        // test can live in Cena.Actors.Tests without a compile-time
        // reference. If the endpoint exists AND its response carries the
        // `retrievalStrengthFraming` field, the ratchet holds.
        var adminAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Cena.Admin.Api");
        if (adminAsm is null)
        {
            // Cena.Admin.Api may not be loaded in this test assembly —
            // fall back to reading the source file directly.
            AssertEndpointSourceReferencesFraming();
            return;
        }

        var responseType = adminAsm.GetTypes()
            .FirstOrDefault(t => t.Name == "RetakeCohortResponseDto");
        Assert.NotNull(responseType);
        var property = responseType!.GetProperty("RetrievalStrengthFraming");
        Assert.NotNull(property);
    }

    private static void AssertEndpointSourceReferencesFraming()
    {
        var repoRoot = FindRepoRoot();
        var endpointFile = Path.Combine(repoRoot,
            "src", "api", "Cena.Admin.Api", "Features", "RetakeCohort",
            "RetakeCohortEndpoint.cs");
        Assert.True(File.Exists(endpointFile),
            "prr-238: RetakeCohortEndpoint.cs missing — the retake-cohort surface "
            + "is the sole admin-facing consumer of the retrieval-strength framing "
            + "flag; removing it breaks the contract.");
        var text = File.ReadAllText(endpointFile);
        Assert.Contains("RetrievalStrengthFraming", text);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Repo root (CLAUDE.md) not found from test base directory.");
    }
}
