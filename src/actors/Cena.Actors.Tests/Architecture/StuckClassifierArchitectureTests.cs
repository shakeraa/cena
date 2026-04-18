// =============================================================================
// Cena Platform — Stuck-type classifier architecture tests (RDY-063)
//
// Invariants enforced mechanically (NOT by convention / reviewer eyeballs):
//
//   1. Classifier surface never imports the concrete student document
//      types that carry PII. A regression that adds `using Cena.Infrastructure.Documents`
//      and references StudentProfileSnapshot or AdminUser in the
//      Diagnosis namespace fails this test.
//
//   2. StuckDiagnosis output type has no free-text fields — only enums
//      + numeric confidence + short machine-readable reason code. Catches
//      someone adding a `string HintText` or similar to the DTO.
//
//   3. The PII guard in StuckContextBuilder is non-bypassable: given a
//      context serialising to a string that contains the raw student id,
//      Build() throws rather than returning.
//
//   4. Disabling the feature flag returns Unknown/None without calling
//      the LLM (hot-path guarantee).
// =============================================================================

using System.Reflection;
using Cena.Actors.Diagnosis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Tests.Architecture;

public class StuckClassifierArchitectureTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }

    [Fact]
    public void DiagnosisNamespace_DoesNotReferenceStudentProfileSnapshot()
    {
        // Scan all .cs files in Cena.Actors/Diagnosis/ for forbidden
        // student-document type references. The classifier works from
        // StuckContext only; it must never reach for StudentProfileSnapshot
        // or AdminUser directly.
        var repoRoot = FindRepoRoot();
        var diagnosisDir = Path.Combine(repoRoot, "src", "actors", "Cena.Actors", "Diagnosis");
        Assert.True(Directory.Exists(diagnosisDir), $"Diagnosis dir missing: {diagnosisDir}");

        var forbidden = new[]
        {
            "StudentProfileSnapshot",
            "StudentDocument",
            "AdminUser",
            "TutorMessageDocument",
            "TutorThreadDocument",
        };

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(diagnosisDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var f in forbidden)
            {
                if (text.Contains(f, StringComparison.Ordinal))
                    violations.Add($"{Path.GetFileName(file)} references {f}");
            }
        }

        Assert.True(violations.Count == 0,
            "Diagnosis namespace must not reference student-profile types:\n  " +
            string.Join("\n  ", violations));
    }

    [Fact]
    public void StuckDiagnosis_HasNoFreeTextFields()
    {
        // Output type surface: every property must be one of
        //   - enum (StuckType, StuckScaffoldStrategy, StuckDiagnosisSource)
        //   - primitive numeric
        //   - bool
        //   - DateTimeOffset
        //   - nullable string (allowed ONLY for: FocusChapterId, ClassifierVersion, SourceReasonCode)
        var allowlistedStringProps = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(StuckDiagnosis.FocusChapterId),
            nameof(StuckDiagnosis.ClassifierVersion),
            nameof(StuckDiagnosis.SourceReasonCode),
        };

        var violations = new List<string>();
        foreach (var prop in typeof(StuckDiagnosis).GetProperties())
        {
            var t = prop.PropertyType;
            var underlying = Nullable.GetUnderlyingType(t) ?? t;

            bool ok =
                underlying.IsEnum ||
                underlying == typeof(bool) ||
                underlying == typeof(int) ||
                underlying == typeof(long) ||
                underlying == typeof(float) ||
                underlying == typeof(double) ||
                underlying == typeof(DateTimeOffset) ||
                (underlying == typeof(string) && allowlistedStringProps.Contains(prop.Name));

            if (!ok)
                violations.Add($"{prop.Name}: {t.FullName}");
        }

        Assert.True(violations.Count == 0,
            "StuckDiagnosis must contain only enums, primitives, and allowlisted strings:\n  " +
            string.Join("\n  ", violations));
    }

    [Fact]
    public void StuckDiagnosisDocument_HasNoStudentIdField()
    {
        // The persisted document must never carry the raw studentId —
        // only the anonymized hash.
        var props = typeof(StuckDiagnosisDocument).GetProperties();
        Assert.DoesNotContain(props, p =>
            string.Equals(p.Name, "StudentId", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(props, p => p.Name == nameof(StuckDiagnosisDocument.StudentAnonId));
    }

    [Fact]
    public void ContextBuilder_PiiGuard_RejectsRawStudentIdInAnyField()
    {
        // Smuggle the studentId into a chapter-id field (simulating a
        // coding error where the caller misconfigured the context). The
        // builder's post-assembly PII scan must catch it.
        var anon = new StuckAnonymizer("salt-v1");
        var builder = new StuckContextBuilder(anon);

        Assert.Throws<InvalidOperationException>(() =>
            builder.Build(new StuckContextInputs(
                StudentId: "stu-leak-42",
                SessionId: "sess-1",
                Locale: "en",
                Question: new StuckContextQuestion("q", null,
                    ChapterId: "stu-leak-42",   // raw studentId leaked into chapterId
                    Array.Empty<string>(), null, null),
                Advancement: new StuckContextAdvancement(null, null, 0, 0, 0),
                Attempts: Array.Empty<StuckContextAttempt>(),
                SessionSignals: new StuckContextSessionSignals(0, 0, 0, 0, 0, 0),
                AsOf: DateTimeOffset.UtcNow)));
    }

    [Fact]
    public async Task FlagOff_ReturnsUnknownNone_WithoutCallingLlm()
    {
        var opts = new StuckClassifierOptions { Enabled = false };
        var monitor = new StaticOptionsMonitor(opts);

        // Track whether the LLM path was invoked.
        var throwingLlm = new ThrowingLlmClassifier();
        var heuristic = new HeuristicStuckClassifier(opts);
        var metrics = new StuckClassifierMetrics(
            new DummyMeterFactory());
        var hybrid = new HybridStuckClassifier(
            heuristic,
            throwingLlm,
            monitor,
            NullLogger<HybridStuckClassifier>.Instance,
            metrics);

        var ctx = new StuckContext(
            "s", "a",
            new StuckContextQuestion("q", null, null, Array.Empty<string>(), null, null),
            new StuckContextAdvancement(null, null, 0, 0, 0),
            Array.Empty<StuckContextAttempt>(),
            new StuckContextSessionSignals(0, 0, 0, 0, 0, 0),
            "en",
            DateTimeOffset.UtcNow);

        var result = await hybrid.DiagnoseAsync(ctx);
        Assert.Equal(StuckType.Unknown, result.Primary);
        Assert.Equal(StuckDiagnosisSource.None, result.Source);
        Assert.False(throwingLlm.Called, "LLM must not be called when flag is off");
    }

    // ── Test doubles ───────────────────────────────────────────────────

    private sealed class StaticOptionsMonitor : IOptionsMonitor<StuckClassifierOptions>
    {
        public StaticOptionsMonitor(StuckClassifierOptions value) { CurrentValue = value; }
        public StuckClassifierOptions CurrentValue { get; }
        public StuckClassifierOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<StuckClassifierOptions, string?> listener) => null;
    }

    private sealed class ThrowingLlmClassifier : IStuckTypeClassifier
    {
        public bool Called { get; private set; }
        public Task<StuckDiagnosis> DiagnoseAsync(StuckContext ctx, CancellationToken ct = default)
        {
            Called = true;
            throw new InvalidOperationException("LLM classifier must not be called in this test");
        }
    }

    private sealed class DummyMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options)
            => new(options);
        public void Dispose() { }
    }
}
