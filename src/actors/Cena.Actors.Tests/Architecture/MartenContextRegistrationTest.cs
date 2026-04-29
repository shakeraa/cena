// =============================================================================
// Cena Platform — Pin per-bounded-context Marten registrations (PRR-304)
//
// PRR-304 extracted MartenSocialRegistration.RegisterSocialContext and
// MartenChallengeRegistration.RegisterChallengeContext out of
// MartenConfiguration.cs. The extracts are behaviour-preserving by
// inspection — these tests pin them so:
//
//   1. The set of documents registered in each bounded context cannot
//      silently shrink (drop a doc → projection stops materializing →
//      production read-model goes stale).
//   2. Each registered document has its Identity wired (Marten throws at
//      DocumentSession-time if missing; better to catch at compile-arch
//      level by static inspection).
//   3. MartenConfiguration.cs actually CALLS the extension methods
//      (forgetting the call is a regression that compiles fine but
//      silently drops the entire bounded context's schema wiring).
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class MartenContextRegistrationTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static string ConfigDir() =>
        Path.Combine(FindRepoRoot(), "src", "actors", "Cena.Actors", "Configuration");

    private static string ReadFile(string fileName)
    {
        var path = Path.Combine(ConfigDir(), fileName);
        Assert.True(File.Exists(path), $"Registration file missing: {path}");
        return File.ReadAllText(path);
    }

    private static readonly string[] ExpectedSocialDocs = new[]
    {
        "CommentDocument",
        "FriendRequestDocument",
        "FriendshipDocument",
        "StudyRoomDocument",
        "StudyRoomMembershipDocument",
        "ClassFeedItemDocument",
        "PeerSolutionDocument",
    };

    private static readonly string[] ExpectedChallengeDocs = new[]
    {
        "BossAttemptDocument",
        "DailyChallengeDocument",
        "DailyChallengeCompletionDocument",
        "CardChainDefinitionDocument",
        "CardChainProgressDocument",
        "TournamentDocument",
        "TournamentRegistrationDocument",
    };

    [Fact]
    public void Social_context_registers_exactly_expected_doc_set()
    {
        var src = ReadFile("MartenSocialRegistration.cs");
        var found = Regex.Matches(src, @"opts\.Schema\.For<(\w+)>\(\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var expected = ExpectedSocialDocs.ToHashSet();
        var missing = expected.Except(found).ToList();
        var extra = found.Except(expected).ToList();

        Assert.True(missing.Count == 0,
            "Social-context documents missing from MartenSocialRegistration:\n  "
            + string.Join("\n  ", missing));
        Assert.True(extra.Count == 0,
            "Unexpected documents in MartenSocialRegistration "
            + "(update ExpectedSocialDocs in this test if intentional):\n  "
            + string.Join("\n  ", extra));
    }

    [Fact]
    public void Challenge_context_registers_exactly_expected_doc_set()
    {
        var src = ReadFile("MartenChallengeRegistration.cs");
        var found = Regex.Matches(src, @"opts\.Schema\.For<(\w+)>\(\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var expected = ExpectedChallengeDocs.ToHashSet();
        var missing = expected.Except(found).ToList();
        var extra = found.Except(expected).ToList();

        Assert.True(missing.Count == 0,
            "Challenge-context documents missing from MartenChallengeRegistration:\n  "
            + string.Join("\n  ", missing));
        Assert.True(extra.Count == 0,
            "Unexpected documents in MartenChallengeRegistration "
            + "(update ExpectedChallengeDocs in this test if intentional):\n  "
            + string.Join("\n  ", extra));
    }

    [Fact]
    public void Every_social_doc_has_identity_expression()
    {
        AssertEveryDocHasIdentity("MartenSocialRegistration.cs", ExpectedSocialDocs);
    }

    [Fact]
    public void Every_challenge_doc_has_identity_expression()
    {
        AssertEveryDocHasIdentity("MartenChallengeRegistration.cs", ExpectedChallengeDocs);
    }

    [Fact]
    public void MartenConfiguration_calls_both_context_extensions()
    {
        // Forgetting the call would compile fine but drop the entire
        // bounded context's schema wiring at runtime.
        var src = ReadFile("MartenConfiguration.cs");

        Assert.Contains("opts.RegisterSocialContext();", src);
        Assert.Contains("opts.RegisterChallengeContext();", src);
    }

    [Fact]
    public void Extension_methods_have_stable_signature()
    {
        var social = ReadFile("MartenSocialRegistration.cs");
        Assert.Matches(
            @"public static void RegisterSocialContext\(this StoreOptions opts\)",
            social);

        var challenge = ReadFile("MartenChallengeRegistration.cs");
        Assert.Matches(
            @"public static void RegisterChallengeContext\(this StoreOptions opts\)",
            challenge);
    }

    private static void AssertEveryDocHasIdentity(string fileName, IEnumerable<string> docs)
    {
        var src = ReadFile(fileName);
        var missing = new List<string>();
        foreach (var doc in docs)
        {
            // Match: opts.Schema.For<Doc>() ... .Identity(...)  on the same statement.
            // Marten chains are multi-line; allow [\s\S]*? non-greedy across newlines,
            // bounded by the next `;` so we don't cross statement boundaries.
            var pattern = @"opts\.Schema\.For<" + Regex.Escape(doc)
                + @">\(\)[\s\S]*?\.Identity\([\s\S]*?\)[\s\S]*?;";
            if (!Regex.IsMatch(src, pattern))
                missing.Add(doc);
        }

        Assert.True(missing.Count == 0,
            $"Documents in {fileName} missing .Identity(...) wiring:\n  "
            + string.Join("\n  ", missing));
    }
}
