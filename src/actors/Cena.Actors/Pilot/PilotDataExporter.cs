// =============================================================================
// Cena Platform — Pilot Data Exporter (RDY-032)
//
// Produces analysis-ready CSV files for post-pilot calibration (BKT, DIF,
// Bagrut baseline). Reads directly from Marten event streams; never
// touches live document projections so a long-running export doesn't
// contend with the hot path.
//
// Privacy contract:
//   • Student IDs are SHA-256 hashed + truncated to 16 hex chars, salted
//     via the CENA_PILOT_EXPORT_SALT environment variable. Salt is
//     required — we fail fast if unset rather than silently producing
//     deterministic hashes that could be inverted.
//   • Misconception events are filtered via the ML-exclusion helper
//     (ADR-0003). Schema intentionally does not carry any field that
//     could re-introduce misconception data.
//   • Test/emulator students filtered by configurable prefixes.
//
// The exporter is stateless + idempotent on the same (from, to) window.
// Two runs over the same window produce byte-identical files (ordering
// is deterministic: attempts by (student_id_hash, timestamp), sessions
// by (student_id_hash, started_at)).
// =============================================================================

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cena.Actors.Events;
using Cena.Infrastructure.Compliance;
using Marten;
using Marten.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Pilot;

/// <summary>
/// RDY-032: pilot data export service contract.
/// </summary>
public interface IPilotDataExporter
{
    /// <summary>
    /// Runs an export for the (FromUtc, ToUtc) window. Throws
    /// <see cref="ArgumentException"/> on invalid range.
    /// Throws <see cref="InvalidOperationException"/> when the pseudo-
    /// nymization salt is missing.
    /// </summary>
    Task<PilotExportResult> ExportAsync(
        PilotExportRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// RDY-032: Marten-backed pilot data exporter.
/// </summary>
public sealed class PilotDataExporter : IPilotDataExporter
{
    public const string SaltEnvVar = "CENA_PILOT_EXPORT_SALT";

    // Default test-student prefixes filtered out; can be overridden via
    // configuration "Pilot:Export:TestStudentPrefixes".
    private static readonly string[] DefaultTestPrefixes = { "test-", "load-", "emu-" };

    private readonly IDocumentStore _store;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PilotDataExporter> _logger;
    private readonly Func<string> _saltProvider;

    public PilotDataExporter(
        IDocumentStore store,
        IConfiguration configuration,
        ILogger<PilotDataExporter> logger)
        : this(store, configuration, logger,
            saltProvider: () => Environment.GetEnvironmentVariable(SaltEnvVar)
                                 ?? string.Empty)
    { }

    // Internal ctor allows test injection without touching the env var.
    internal PilotDataExporter(
        IDocumentStore store,
        IConfiguration configuration,
        ILogger<PilotDataExporter> logger,
        Func<string> saltProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _saltProvider = saltProvider ?? throw new ArgumentNullException(nameof(saltProvider));
    }

    public async Task<PilotExportResult> ExportAsync(
        PilotExportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.FromUtc >= request.ToUtc)
            throw new ArgumentException("FromUtc must be strictly less than ToUtc.",
                nameof(request));

        var salt = _saltProvider();
        if (string.IsNullOrWhiteSpace(salt))
            throw new InvalidOperationException(
                $"Pseudonymization salt unset. Provision the {SaltEnvVar} environment variable " +
                "before running the pilot export.");

        var runId = $"pilot-export-{Guid.NewGuid():N}";
        _logger.LogInformation(
            "[PILOT_EXPORT] run={RunId} from={From:O} to={To:O} dryRun={Dry}",
            runId, request.FromUtc, request.ToUtc, request.DryRun);

        var testPrefixes = _configuration
            .GetSection("Pilot:Export:TestStudentPrefixes")
            .Get<string[]>() ?? DefaultTestPrefixes;

        var (attempts, sessions, studentCount) = await QueryWindowAsync(
            request.FromUtc, request.ToUtc, salt, testPrefixes, ct);

        var qualityErrors = RunQualityChecks(attempts, sessions);

        string? attemptsPath = null, sessionsPath = null, metadataPath = null;
        if (!request.DryRun)
        {
            var outDir = request.OutputDirectory
                         ?? _configuration["Pilot:Export:OutputDirectory"]
                         ?? "data/pilot";
            Directory.CreateDirectory(outDir);

            attemptsPath = Path.Combine(outDir,
                $"attempts_{request.FromUtc:yyyyMMdd}_{request.ToUtc:yyyyMMdd}.csv");
            sessionsPath = Path.Combine(outDir,
                $"sessions_{request.FromUtc:yyyyMMdd}_{request.ToUtc:yyyyMMdd}.csv");
            metadataPath = Path.Combine(outDir,
                $"metadata_{request.FromUtc:yyyyMMdd}_{request.ToUtc:yyyyMMdd}.json");

            await WriteAttemptsCsvAsync(attemptsPath, attempts, ct);
            await WriteSessionsCsvAsync(sessionsPath, sessions, ct);

            var metadata = new PilotExportMetadata(
                RunId: runId,
                FromUtc: request.FromUtc,
                ToUtc: request.ToUtc,
                ExportedAt: DateTimeOffset.UtcNow,
                StudentCount: studentCount,
                AttemptRowCount: attempts.Count,
                SessionRowCount: sessions.Count,
                QualityCheckErrors: qualityErrors,
                DryRun: false);

            await File.WriteAllTextAsync(metadataPath,
                JsonSerializer.Serialize(metadata,
                    new JsonSerializerOptions { WriteIndented = true }),
                ct);
        }

        _logger.LogInformation(
            "[PILOT_EXPORT] run={RunId} students={Students} attempts={Attempts} sessions={Sessions} qcErrors={Errors}",
            runId, studentCount, attempts.Count, sessions.Count, qualityErrors.Count);

        return new PilotExportResult(
            RunId: runId,
            DryRun: request.DryRun,
            FromUtc: request.FromUtc,
            ToUtc: request.ToUtc,
            StudentCount: studentCount,
            AttemptRowCount: attempts.Count,
            SessionRowCount: sessions.Count,
            AttemptsFilePath: attemptsPath,
            SessionsFilePath: sessionsPath,
            MetadataFilePath: metadataPath,
            QualityCheckErrors: qualityErrors);
    }

    // ── Marten query ─────────────────────────────────────────────────────

    private async Task<(List<PilotAttemptRow> Attempts,
                       List<PilotSessionRow> Sessions,
                       int StudentCount)>
        QueryWindowAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            string salt,
            string[] testPrefixes,
            CancellationToken ct)
    {
        await using var session = _store.QuerySession();

        var rawEvents = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.Timestamp >= from && e.Timestamp < to)
            .ToListAsync(token: ct);

        var attempts = new List<PilotAttemptRow>();
        var sessionStarts = new Dictionary<string, LearningSessionStarted_V1>(StringComparer.Ordinal);
        var sessionEnds = new Dictionary<string, LearningSessionEnded_V1>(StringComparer.Ordinal);
        var students = new HashSet<string>(StringComparer.Ordinal);
        var sessionNumberByStudent = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var ev in rawEvents.OrderBy(e => e.Timestamp))
        {
            if (ct.IsCancellationRequested) break;

            // ADR-0003: misconception events never enter a training-adjacent pipe.
            if (ev.Data is not null && StudentDataExporter.IsMlExcluded(ev.Data.GetType()))
                continue;

            switch (ev.Data)
            {
                case ConceptAttempted_V1 a:
                    if (IsTestStudent(a.StudentId, testPrefixes)) break;
                    students.Add(a.StudentId);
                    attempts.Add(BuildAttemptRow(a, salt, sessionNumberByStudent));
                    break;

                case ConceptAttempted_V2 b:
                    if (IsTestStudent(b.StudentId, testPrefixes)) break;
                    students.Add(b.StudentId);
                    attempts.Add(BuildAttemptRowV2(b, salt, sessionNumberByStudent));
                    break;

                case LearningSessionStarted_V1 s:
                    if (IsTestStudent(s.StudentId, testPrefixes)) break;
                    students.Add(s.StudentId);
                    sessionStarts[s.SessionId] = s;
                    break;

                case LearningSessionEnded_V1 e:
                    if (IsTestStudent(e.StudentId, testPrefixes)) break;
                    students.Add(e.StudentId);
                    sessionEnds[e.SessionId] = e;
                    break;
            }
        }

        // Join starts + ends into session rows. Sessions that started in
        // window but didn't end (crashed / abandoned) still emit a row
        // with null EndedAt + 0 duration so analysts can filter them.
        var sessions = sessionStarts.Values
            .OrderBy(s => s.StudentId, StringComparer.Ordinal)
            .ThenBy(s => s.StartedAt)
            .Select(s =>
            {
                sessionEnds.TryGetValue(s.SessionId, out var e);
                return new PilotSessionRow(
                    StudentIdHash: HashStudentId(s.StudentId, salt),
                    SessionId: s.SessionId,
                    StartedAt: s.StartedAt,
                    EndedAt: e?.EndedAt,
                    DurationMs: e is not null
                        ? (long)(e.EndedAt - s.StartedAt).TotalMilliseconds
                        : 0,
                    QuestionsAttempted: e?.QuestionsAttempted ?? 0,
                    QuestionsCorrect: e?.QuestionsCorrect ?? 0,
                    Accuracy: (e is not null && e.QuestionsAttempted > 0)
                        ? (double)e.QuestionsCorrect / e.QuestionsAttempted
                        : 0);
            })
            .ToList();

        return (attempts, sessions, students.Count);
    }

    // ── Row builders (V1 / V2 share the same shape post-schema-evolution) ─

    private static PilotAttemptRow BuildAttemptRow(
        ConceptAttempted_V1 a, string salt,
        Dictionary<string, int> sessionNumberByStudent)
    {
        var studentHash = HashStudentId(a.StudentId, salt);
        sessionNumberByStudent.TryGetValue(a.StudentId, out var n);
        n++;
        sessionNumberByStudent[a.StudentId] = n;

        return new PilotAttemptRow(
            StudentIdHash: studentHash,
            ConceptId: a.ConceptId,
            Subject: SubjectFromConcept(a.ConceptId),
            QuestionId: a.QuestionId,
            Correct: a.IsCorrect,
            HintsUsed: a.HintCountUsed,
            ResponseTimeMs: a.ResponseTimeMs,
            SessionId: a.SessionId,
            SessionNumber: n,
            PriorMastery: a.PriorMastery,
            PosteriorMastery: a.PosteriorMastery,
            Timestamp: a.Timestamp);
    }

    private static PilotAttemptRow BuildAttemptRowV2(
        ConceptAttempted_V2 a, string salt,
        Dictionary<string, int> sessionNumberByStudent)
    {
        var studentHash = HashStudentId(a.StudentId, salt);
        sessionNumberByStudent.TryGetValue(a.StudentId, out var n);
        n++;
        sessionNumberByStudent[a.StudentId] = n;

        return new PilotAttemptRow(
            StudentIdHash: studentHash,
            ConceptId: a.ConceptId,
            Subject: SubjectFromConcept(a.ConceptId),
            QuestionId: a.QuestionId,
            Correct: a.IsCorrect,
            HintsUsed: a.HintCountUsed,
            ResponseTimeMs: a.ResponseTimeMs,
            SessionId: a.SessionId,
            SessionNumber: n,
            PriorMastery: a.PriorMastery,
            PosteriorMastery: a.PosteriorMastery,
            Timestamp: a.Timestamp);
    }

    // ── Quality checks ───────────────────────────────────────────────────

    internal static List<string> RunQualityChecks(
        IReadOnlyList<PilotAttemptRow> attempts,
        IReadOnlyList<PilotSessionRow> sessions)
    {
        var errors = new List<string>();

        foreach (var a in attempts)
        {
            if (string.IsNullOrEmpty(a.StudentIdHash))
                errors.Add($"attempts: student_id_hash empty (session={a.SessionId})");
            if (string.IsNullOrEmpty(a.ConceptId))
                errors.Add($"attempts: concept_id empty (session={a.SessionId})");
            if (string.IsNullOrEmpty(a.QuestionId))
                errors.Add($"attempts: question_id empty (session={a.SessionId})");
        }

        var sessionIds = sessions.Select(s => s.SessionId).ToHashSet(StringComparer.Ordinal);
        var orphans = attempts.Select(a => a.SessionId)
            .Where(id => !sessionIds.Contains(id))
            .Distinct()
            .Take(5) // cap so the error list doesn't explode
            .ToList();

        foreach (var orphan in orphans)
            errors.Add($"attempts: orphan session_id (no session row) session={orphan}");

        return errors;
    }

    // ── CSV writers (RFC 4180 — quoted when needed, LF line endings) ─────

    private static async Task WriteAttemptsCsvAsync(
        string path, IReadOnlyList<PilotAttemptRow> rows, CancellationToken ct)
    {
        await using var w = new StreamWriter(path, append: false, Encoding.UTF8)
        {
            NewLine = "\n"
        };

        await w.WriteLineAsync(
            "student_id_hash,concept_id,subject,question_id,correct,hints_used,response_time_ms,session_id,session_number,prior_mastery,posterior_mastery,timestamp");

        var ordered = rows.OrderBy(r => r.StudentIdHash, StringComparer.Ordinal)
                          .ThenBy(r => r.Timestamp);
        foreach (var r in ordered)
        {
            if (ct.IsCancellationRequested) break;
            await w.WriteLineAsync(string.Join(",",
                Csv(r.StudentIdHash),
                Csv(r.ConceptId),
                Csv(r.Subject),
                Csv(r.QuestionId),
                r.Correct ? "true" : "false",
                r.HintsUsed.ToString(CultureInfo.InvariantCulture),
                r.ResponseTimeMs.ToString(CultureInfo.InvariantCulture),
                Csv(r.SessionId),
                r.SessionNumber.ToString(CultureInfo.InvariantCulture),
                r.PriorMastery.ToString("0.######", CultureInfo.InvariantCulture),
                r.PosteriorMastery.ToString("0.######", CultureInfo.InvariantCulture),
                r.Timestamp.ToString("O", CultureInfo.InvariantCulture)));
        }
    }

    private static async Task WriteSessionsCsvAsync(
        string path, IReadOnlyList<PilotSessionRow> rows, CancellationToken ct)
    {
        await using var w = new StreamWriter(path, append: false, Encoding.UTF8)
        {
            NewLine = "\n"
        };

        await w.WriteLineAsync(
            "student_id_hash,session_id,started_at,ended_at,duration_ms,questions_attempted,questions_correct,accuracy");

        var ordered = rows.OrderBy(r => r.StudentIdHash, StringComparer.Ordinal)
                          .ThenBy(r => r.StartedAt);
        foreach (var r in ordered)
        {
            if (ct.IsCancellationRequested) break;
            await w.WriteLineAsync(string.Join(",",
                Csv(r.StudentIdHash),
                Csv(r.SessionId),
                r.StartedAt.ToString("O", CultureInfo.InvariantCulture),
                r.EndedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                r.DurationMs.ToString(CultureInfo.InvariantCulture),
                r.QuestionsAttempted.ToString(CultureInfo.InvariantCulture),
                r.QuestionsCorrect.ToString(CultureInfo.InvariantCulture),
                r.Accuracy.ToString("0.######", CultureInfo.InvariantCulture)));
        }
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    // ── Privacy helpers ──────────────────────────────────────────────────

    /// <summary>
    /// SHA-256(studentId + salt), hex-encoded, truncated to 16 chars.
    /// Truncation is intentional — full SHA output is unique-enough for a
    /// pilot-sized corpus (&lt; 10k students) and shorter hashes are easier
    /// for analysts to work with. Salt is mandatory.
    /// </summary>
    internal static string HashStudentId(string studentId, string salt)
    {
        if (string.IsNullOrWhiteSpace(salt))
            throw new InvalidOperationException("Salt required.");

        var bytes = Encoding.UTF8.GetBytes(studentId + ":" + salt);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).Substring(0, 16).ToLowerInvariant();
    }

    private static bool IsTestStudent(string studentId, string[] prefixes)
    {
        if (string.IsNullOrEmpty(studentId)) return false;
        foreach (var p in prefixes)
            if (studentId.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string SubjectFromConcept(string conceptId)
    {
        // Bagrut taxonomy keys look like "math_5u.calculus.derivatives" —
        // the leading token before the first "_" or "." is the subject.
        if (string.IsNullOrEmpty(conceptId)) return "unknown";
        var idx = conceptId.IndexOfAny(new[] { '_', '.' });
        return idx <= 0 ? conceptId : conceptId[..idx].ToLowerInvariant();
    }
}
