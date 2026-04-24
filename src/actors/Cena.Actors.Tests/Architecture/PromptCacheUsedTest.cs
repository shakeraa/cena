// =============================================================================
// Cena Platform — Every LLM call site must sit behind a prompt cache (prr-047)
//
// Invariant: `ILlmClient.CompleteAsync(...)` must never execute on behalf of
// a student-facing feature unless that feature has a cache guard on the way
// in and a cache write on the way out. Without this, a single loop in the
// explain path can drain the daily Anthropic budget in minutes.
//
// Enforcement model (mirrors SchedulerNoLlmCallTest's approach):
//   1. Find every *.cs under src/actors/Cena.Actors/ that contains
//      `_llm.CompleteAsync(`, `llm.CompleteAsync(`, or any pattern matching
//      `<ident>.CompleteAsync(` where <ident> is a field of ILlmClient.
//   2. For each such file, require ONE of the following in the same file:
//        a. A field / parameter of type IPromptCache, IExplanationCacheService,
//           or the file carries a call to `_cache.TryGet…` / `_cache.Get…`
//           that shows a caching pattern.
//        b. A class-level `[AllowsUncachedLlm("<reason>")]` attribute.
//   3. Otherwise the build fails with a message pointing at the missing guard.
//
// Allowlist maintenance:
//   - `[AllowsUncachedLlm("...")]` lives in Cena.Infrastructure.Llm and
//     requires a written reason, so every bypass is reviewable in PR.
//   - No parallel YAML allowlist is needed; the C# attribute is the only
//     mechanism. This avoids the `allowlist-drift` problem where a YAML
//     entry stops matching the file it used to cover.
//
// The scan is textual, not reflection/Roslyn, to keep it fast (<200ms) and
// to catch the realistic failure mode: a developer adding an LLM call in a
// new service and forgetting the cache guard.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class PromptCacheUsedTest
{
    // Matches the realistic LLM call-site shapes in this codebase:
    //   await _llm.CompleteAsync(...)
    //   await llm.CompleteAsync(...)
    //   await _llmClient.CompleteAsync(...)
    // The lookahead on a word-boundary prevents matching `FooCompleteAsync`.
    private static readonly Regex LlmCallSite = new(
        @"(?<![A-Za-z0-9_])[A-Za-z_][A-Za-z0-9_]*\.CompleteAsync\s*\(",
        RegexOptions.Compiled);

    // Any of these tokens in the same file means the developer is clearly
    // using a cache seam. We do not try to prove that every call site is
    // guarded — we only prove that a cache exists in the same class.
    private static readonly string[] CacheGuardTokens =
    {
        "IPromptCache",
        "IExplanationCacheService",
        "_promptCache",
        "_explanationCache",
        ".TryGetAsync(",
        // `_cache.GetAsync(` covers ExplanationCacheService-shaped seams too.
        "_cache.GetAsync(",
    };

    // Class-level opt-out; requires a reason, validated by the attribute ctor.
    private const string OptOutMarker = "[AllowsUncachedLlm";

    private static string FindRepoRoot()
    {
        // Prefer the CLAUDE.md marker like SchedulerNoLlmCallTest does, but
        // fall back to a `src/actors/` sibling marker so the test works in
        // minimal container mounts that don't ship repo-root docs (e.g. the
        // dev container only mounts /src, /config, /docker).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "actors", "Cena.Actors"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Repo root not found — looked for CLAUDE.md or a src/actors/Cena.Actors/ directory.");
    }

    private static IEnumerable<string> ScannedFiles(string repoRoot)
    {
        var root = Path.Combine(repoRoot, "src", "actors", "Cena.Actors");
        if (!Directory.Exists(root)) yield break;

        foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            // Gateway types define the LLM abstraction itself — they implement
            // the caller pattern, not consume it. Excluding them avoids a
            // bootstrap-paradox false positive on LlmClientRouter /
            // AnthropicLlmClient / ILlmClient.
            if (f.Contains($"{Path.DirectorySeparatorChar}Gateway{Path.DirectorySeparatorChar}")) continue;
            yield return f;
        }
    }

    private static string StripCommentsAndStrings(string text)
    {
        // Remove `/* ... */` blocks (greedy is fine here — we only use the
        // stripped text for pattern existence, not positions).
        text = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        // Collapse `"..."` literals and line comments.
        var sb = new StringBuilder(text.Length);
        var inStr = false;
        var inLineComment = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inLineComment)
            {
                if (c == '\n') { inLineComment = false; sb.Append(c); }
                continue;
            }
            if (inStr)
            {
                if (c == '"' && (i == 0 || text[i - 1] != '\\')) { inStr = false; sb.Append('"'); }
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }
            if (c == '"') { inStr = true; sb.Append('"'); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    [Fact]
    public void EveryLlmCallSite_HasPromptCacheGuardOrIsAllowlisted()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var scanned = 0;
        var withCalls = 0;

        foreach (var file in ScannedFiles(repoRoot))
        {
            scanned++;
            var raw = File.ReadAllText(file);
            var text = StripCommentsAndStrings(raw);

            if (!LlmCallSite.IsMatch(text))
                continue;

            withCalls++;
            var rel = Path.GetRelativePath(repoRoot, file);

            // Opt-out via class attribute (with a required justification).
            if (raw.Contains(OptOutMarker, StringComparison.Ordinal))
                continue;

            // Any cache-seam token wins. We do not require the cache call to
            // dominate the LLM call — just that the caching pattern is
            // present in the same class file, which catches the realistic
            // failure mode (new service added, forgot to inject the cache).
            if (CacheGuardTokens.Any(tok => text.Contains(tok, StringComparison.Ordinal)))
                continue;

            violations.Add(
                $"{rel} — calls ILlmClient.CompleteAsync(...) but has no prompt-cache " +
                $"guard and no [AllowsUncachedLlm(\"reason\")] attribute. " +
                "See prr-047 / ADR-0026 §6.");
        }

        if (violations.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                $"prr-047 violation: {violations.Count} LLM call site(s) bypass the prompt cache.");
            sb.AppendLine(
                $"(scanned {scanned} files under src/actors/Cena.Actors, {withCalls} contain LLM calls)");
            sb.AppendLine();
            sb.AppendLine("Fix: inject IPromptCache and wrap the call in TryGetAsync → miss → CompleteAsync → SetAsync,");
            sb.AppendLine("or, if the prompt genuinely cannot be cached, annotate the class with:");
            sb.AppendLine("    [AllowsUncachedLlm(\"<one-sentence justification reviewed in PR>\")]");
            sb.AppendLine();
            foreach (var v in violations) sb.AppendLine("  " + v);
            Assert.Fail(sb.ToString());
        }

        // Safety: if the scan ever stops finding LLM calls altogether, either
        // the codebase changed shape or our regex broke. Fail loudly rather
        // than silently pass.
        Assert.True(
            withCalls > 0,
            "PromptCacheUsedTest found zero LLM call sites — scanner likely broken. " +
            "Check the LlmCallSite regex and the scanned root.");
    }
}
