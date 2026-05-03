// =============================================================================
// Cena Platform — LegalEndpoints integration tests (prr-123)
//
// Directly exercises LegalEndpoints.HandleGetPrivacyPolicyAsync via the
// internal marker class (InternalsVisibleTo grants access). The handler
// is pure w.r.t. HTTP-pipeline middleware — its only inputs are the
// query parameter, the content root, and the on-disk doc files. That
// makes the test a straight-line call against a temp content root.
//
// Scenarios covered:
//   (a) audience=parent   → 200 + parent-specific markdown + version
//   (b) audience=student  → 200 + student-specific markdown + version
//   (c) audience=parent   → parent markdown is distinct from student
//   (d) audience=unknown  → 400
//   (e) audience missing  → 400
//   (f) doc missing       → 503
//   (g) front-matter absent → 500 (malformed)
// =============================================================================

using Cena.Api.Host.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public sealed class LegalEndpointsTests : IDisposable
{
    private readonly string _tempRoot;

    public LegalEndpointsTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(), $"cena-legal-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "docs", "legal"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    // ── Fixture helpers ──────────────────────────────────────────────────

    private void WriteDoc(string fileName, string audience, string body, string version = "v1.0.0 2026-04-21")
    {
        var path = Path.Combine(_tempRoot, "docs", "legal", fileName);
        var doc = $"""
---
audience: {audience}
version: {version}
effective_from: 2026-04-21
doc_id: cena-privacy-policy-{audience}-v1.0.0
---

{body}
""";
        File.WriteAllText(path, doc);
    }

    private void WriteMalformedDoc(string fileName)
    {
        // no front matter → parser must reject
        var path = Path.Combine(_tempRoot, "docs", "legal", fileName);
        File.WriteAllText(path, "# No front matter here\n\nBody only.\n");
    }

    private IHostEnvironment Env()
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(_tempRoot);
        env.SetupGet(e => e.EnvironmentName).Returns("Testing");
        env.SetupGet(e => e.ApplicationName).Returns("Cena.Student.Api.Host.Tests");
        return env.Object;
    }

    private static Task<IResult> Invoke(
        IHostEnvironment env, string? audience)
    {
        var http = new DefaultHttpContext();
        return LegalEndpoints.HandleGetPrivacyPolicyAsyncForTests(
            http, env, NullLogger<LegalEndpoints.LegalEndpointMarker>.Instance, audience, default);
    }

    // ── (a) parent audience returns parent copy ──────────────────────────

    [Fact]
    public async Task GetPrivacyPolicy_parent_returns_parent_copy()
    {
        WriteDoc("privacy-policy-parent.md", "parent",
            "# Parent policy body\n\nContent for adults.");
        WriteDoc("privacy-policy-student.md", "student",
            "# Student policy body\n\nContent for 13+.");

        var result = await Invoke(Env(), "parent");

        var ok = Assert.IsType<Ok<PrivacyPolicyDocumentDto>>(result);
        Assert.Equal("parent", ok.Value!.Audience);
        Assert.Equal("v1.0.0 2026-04-21", ok.Value.Version);
        Assert.Equal("cena-privacy-policy-parent-v1.0.0", ok.Value.DocumentId);
        Assert.Contains("Parent policy body", ok.Value.Markdown);
        Assert.DoesNotContain("Student policy body", ok.Value.Markdown);
    }

    // ── (b) student audience returns student copy ────────────────────────

    [Fact]
    public async Task GetPrivacyPolicy_student_returns_student_copy()
    {
        WriteDoc("privacy-policy-parent.md", "parent",
            "# Parent policy body");
        WriteDoc("privacy-policy-student.md", "student",
            "# Student policy body");

        var result = await Invoke(Env(), "student");

        var ok = Assert.IsType<Ok<PrivacyPolicyDocumentDto>>(result);
        Assert.Equal("student", ok.Value!.Audience);
        Assert.Contains("Student policy body", ok.Value.Markdown);
        Assert.DoesNotContain("Parent policy body", ok.Value.Markdown);
    }

    // ── (c) versions + bodies are distinct ───────────────────────────────

    [Fact]
    public async Task GetPrivacyPolicy_parent_and_student_bodies_are_distinct()
    {
        WriteDoc("privacy-policy-parent.md", "parent", "ADULT-UNIQUE-MARKER");
        WriteDoc("privacy-policy-student.md", "student", "TEEN-UNIQUE-MARKER");

        var parentResult = await Invoke(Env(), "parent");
        var studentResult = await Invoke(Env(), "student");

        var parentDoc = Assert.IsType<Ok<PrivacyPolicyDocumentDto>>(parentResult).Value!;
        var studentDoc = Assert.IsType<Ok<PrivacyPolicyDocumentDto>>(studentResult).Value!;
        Assert.NotEqual(parentDoc.Markdown, studentDoc.Markdown);
        Assert.Contains("ADULT-UNIQUE-MARKER", parentDoc.Markdown);
        Assert.Contains("TEEN-UNIQUE-MARKER", studentDoc.Markdown);
    }

    // ── (d) unknown audience → 400 ───────────────────────────────────────

    [Fact]
    public async Task GetPrivacyPolicy_unknown_audience_returns_400()
    {
        WriteDoc("privacy-policy-parent.md", "parent", "body");
        WriteDoc("privacy-policy-student.md", "student", "body");

        var result = await Invoke(Env(), "operator");

        AssertBadRequest(result);
    }

    // ── (e) missing audience → 400 ───────────────────────────────────────

    [Fact]
    public async Task GetPrivacyPolicy_missing_audience_returns_400()
    {
        WriteDoc("privacy-policy-parent.md", "parent", "body");
        WriteDoc("privacy-policy-student.md", "student", "body");

        var result = await Invoke(Env(), null);

        AssertBadRequest(result);
    }

    // ── (f) doc missing → 503 ────────────────────────────────────────────

    [Fact]
    public async Task GetPrivacyPolicy_missing_doc_returns_503()
    {
        // Intentionally do NOT write any docs.
        var result = await Invoke(Env(), "parent");

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
    }

    // ── (g) malformed doc (no front matter) → 500 ────────────────────────

    [Fact]
    public async Task GetPrivacyPolicy_malformed_doc_returns_500()
    {
        WriteMalformedDoc("privacy-policy-parent.md");

        var result = await Invoke(Env(), "parent");

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
    }

    // ── Test infrastructure ──────────────────────────────────────────────

    /// <summary>
    /// Results.BadRequest returns a closed generic <c>BadRequest&lt;T&gt;</c>
    /// where T is the anonymous response shape. We assert via reflection
    /// on the open generic so the test doesn't need to name T.
    /// </summary>
    private static void AssertBadRequest(IResult result)
    {
        var t = result.GetType();
        Assert.True(
            t.IsGenericType && t.GetGenericTypeDefinition() == typeof(BadRequest<>),
            $"Expected BadRequest<T>, got {t.FullName}");
    }
}
