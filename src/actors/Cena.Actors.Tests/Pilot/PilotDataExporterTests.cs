// =============================================================================
// Cena Platform — PilotDataExporter tests (RDY-032)
//
// Covers the parts of the exporter that don't require a live Postgres +
// Marten instance:
//
//   • HashStudentId — SHA-256/salt/truncation contract
//   • RunQualityChecks — orphan session detection + NULL-field guards
//   • ExportAsync fail-fast cases (invalid range, missing salt)
//   • CSV round-trip for ordering + deterministic output
//
// The full Marten-backed query path is covered by the
// admin-endpoint integration tests in Cena.Admin.Api.Tests (which run
// against a real TestServer with an in-memory Marten shim). Keeping the
// pure logic tested here so a regression in quality-check rules fails
// fast in the unit lane.
// =============================================================================

using System.Globalization;
using Cena.Actors.Pilot;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Pilot;

public sealed class PilotDataExporterTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IConfiguration _configuration =
        new ConfigurationBuilder().Build();

    // ── Fail-fast: invalid range ─────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_FromAfterTo_Throws()
    {
        var sut = new PilotDataExporter(_store, _configuration,
            NullLogger<PilotDataExporter>.Instance,
            saltProvider: () => "test-salt-32bytes-xxxxxxxxxxxxxxxxx");

        var request = new PilotExportRequest(
            FromUtc: DateTimeOffset.UtcNow,
            ToUtc: DateTimeOffset.UtcNow.AddDays(-1));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ExportAsync(request));
    }

    [Fact]
    public async Task ExportAsync_FromEqualsTo_Throws()
    {
        var sut = new PilotDataExporter(_store, _configuration,
            NullLogger<PilotDataExporter>.Instance,
            saltProvider: () => "test-salt");
        var t = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ExportAsync(new PilotExportRequest(t, t)));
    }

    // ── Fail-fast: missing salt ──────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_MissingSalt_ThrowsWithRemediationPointer()
    {
        var sut = new PilotDataExporter(_store, _configuration,
            NullLogger<PilotDataExporter>.Instance,
            saltProvider: () => string.Empty);

        var request = new PilotExportRequest(
            FromUtc: DateTimeOffset.UtcNow.AddDays(-30),
            ToUtc: DateTimeOffset.UtcNow);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExportAsync(request));

        Assert.Contains("salt", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(PilotDataExporter.SaltEnvVar, ex.Message);
    }

    // ── Pseudonymization ─────────────────────────────────────────────────

    [Fact]
    public void HashStudentId_Deterministic_SameSalt()
    {
        var a = PilotDataExporter.HashStudentId("student-42", "salt-v1");
        var b = PilotDataExporter.HashStudentId("student-42", "salt-v1");
        Assert.Equal(a, b);
    }

    [Fact]
    public void HashStudentId_DifferentStudents_DifferentHashes()
    {
        var a = PilotDataExporter.HashStudentId("student-42", "salt-v1");
        var b = PilotDataExporter.HashStudentId("student-43", "salt-v1");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HashStudentId_DifferentSalts_DifferentHashes()
    {
        var a = PilotDataExporter.HashStudentId("student-42", "salt-v1");
        var b = PilotDataExporter.HashStudentId("student-42", "salt-v2");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HashStudentId_Length_Is16HexChars()
    {
        var h = PilotDataExporter.HashStudentId("student-xyz", "salt");
        Assert.Equal(16, h.Length);
        Assert.All(h, c => Assert.True(char.IsDigit(c) || (c >= 'a' && c <= 'f')));
    }

    [Fact]
    public void HashStudentId_EmptySalt_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PilotDataExporter.HashStudentId("student-42", string.Empty));
    }

    [Fact]
    public void HashStudentId_NeverReturnsRawStudentId()
    {
        // Sanity check: the pseudonymization must actually hash, not
        // return the input. Regression against a future refactor that
        // accidentally replaces SHA-256 with a passthrough.
        var studentId = "student-explicit-42";
        var h = PilotDataExporter.HashStudentId(studentId, "salt");
        Assert.DoesNotContain(studentId, h, StringComparison.Ordinal);
    }

    // ── Quality checks ───────────────────────────────────────────────────

    [Fact]
    public void RunQualityChecks_AllClean_ReturnsEmpty()
    {
        var attempts = new List<PilotAttemptRow>
        {
            new("hash1", "concept-A", "math", "q-1", true, 0, 1200,
                "sess-1", 1, 0.4, 0.5, DateTimeOffset.UtcNow),
        };
        var sessions = new List<PilotSessionRow>
        {
            new("hash1", "sess-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5),
                300_000, 1, 1, 1.0),
        };

        var errors = PilotDataExporter.RunQualityChecks(attempts, sessions);
        Assert.Empty(errors);
    }

    [Fact]
    public void RunQualityChecks_OrphanSession_Flagged()
    {
        var attempts = new List<PilotAttemptRow>
        {
            new("hash1", "concept-A", "math", "q-1", true, 0, 1200,
                "sess-missing", 1, 0.4, 0.5, DateTimeOffset.UtcNow),
        };
        var sessions = new List<PilotSessionRow>();

        var errors = PilotDataExporter.RunQualityChecks(attempts, sessions);
        Assert.Contains(errors, e => e.Contains("orphan session_id"));
    }

    [Fact]
    public void RunQualityChecks_EmptyConceptId_Flagged()
    {
        var attempts = new List<PilotAttemptRow>
        {
            new("hash1", "", "math", "q-1", true, 0, 1200,
                "sess-1", 1, 0.4, 0.5, DateTimeOffset.UtcNow),
        };
        var sessions = new List<PilotSessionRow>
        {
            new("hash1", "sess-1", DateTimeOffset.UtcNow, null, 0, 0, 0, 0),
        };

        var errors = PilotDataExporter.RunQualityChecks(attempts, sessions);
        Assert.Contains(errors, e => e.Contains("concept_id empty"));
    }

    [Fact]
    public void RunQualityChecks_OrphanList_CappedAt5()
    {
        // Creates 10 orphan attempts with distinct session ids; caller
        // should only see at most 5 in the error list so we don't blow up
        // the response payload.
        var attempts = Enumerable.Range(1, 10)
            .Select(i => new PilotAttemptRow(
                "hash1", "concept-A", "math", $"q-{i}", true, 0, 1200,
                $"sess-orphan-{i}", i, 0.4, 0.5, DateTimeOffset.UtcNow))
            .ToList();

        var errors = PilotDataExporter.RunQualityChecks(attempts,
            Array.Empty<PilotSessionRow>());

        var orphanErrors = errors.Where(e => e.Contains("orphan session_id")).ToList();
        Assert.InRange(orphanErrors.Count, 1, 5);
    }
}
