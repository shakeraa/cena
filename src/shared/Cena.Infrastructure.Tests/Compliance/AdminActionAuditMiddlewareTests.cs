// =============================================================================
// Cena Platform — AdminActionAuditMiddleware tests (RDY-029 sub-task 5)
//
// The middleware is a thin orchestrator over three pure helpers:
//   • ShouldAudit(method, path)  — decides whether the request flows
//                                   through the audit sink
//   • BuildAction(method, path)  — canonical "resource.verb" action name
//   • ExtractTarget(path, req)   — pulls (targetType, targetId) from the
//                                   route or query
//
// The pure helpers are exhaustively unit-tested here. The full InvokeAsync
// path (which requires an IDocumentSession + HttpContext) is covered by
// integration tests at the admin-endpoint layer — where real auth claims
// and real Marten sessions are in play. Mocking those here would only
// test the mock.
// =============================================================================

using Cena.Infrastructure.Compliance;
using Microsoft.AspNetCore.Http;

namespace Cena.Infrastructure.Tests.Compliance;

public sealed class AdminActionAuditMiddlewareTests
{
    // ── ShouldAudit: admin writes only ───────────────────────────────────

    [Theory]
    [InlineData("POST",   "/api/admin/questions",          true)]
    [InlineData("PUT",    "/api/admin/users/u-1",          true)]
    [InlineData("PATCH",  "/api/admin/users/u-1/role",     true)]
    [InlineData("DELETE", "/api/admin/questions/q-5",      true)]
    [InlineData("post",   "/api/admin/questions",          true)]  // case-insensitive
    public void ShouldAudit_AdminWrites_True(string method, string path, bool expected)
    {
        Assert.Equal(expected, AdminActionAuditMiddleware.ShouldAudit(method, path));
    }

    [Theory]
    [InlineData("GET",     "/api/admin/users",             false)]  // reads → StudentDataAuditMiddleware
    [InlineData("HEAD",    "/api/admin/users",             false)]
    [InlineData("OPTIONS", "/api/admin/users",             false)]
    [InlineData("POST",    "/api/sessions/flow-state/assess", false)] // student API
    [InlineData("POST",    "/api/v1/admin/content/coverage",  false)] // versioned read
    [InlineData("POST",    "/",                            false)]
    [InlineData("POST",    "",                             false)]
    public void ShouldAudit_ReadsAndNonAdmin_False(string method, string path, bool expected)
    {
        Assert.Equal(expected, AdminActionAuditMiddleware.ShouldAudit(method, path));
    }

    // ── BuildAction: resource.verb canonical ────────────────────────────

    [Theory]
    [InlineData("POST",   "/api/admin/questions",                "questions.create")]
    [InlineData("PUT",    "/api/admin/users/u-1",                "users.update")]
    [InlineData("PATCH",  "/api/admin/users/u-1/role",           "users.update")]
    [InlineData("DELETE", "/api/admin/questions/q-5",            "questions.delete")]
    [InlineData("POST",   "/api/admin/content/recreate-from-reference",
                                                                 "content.create")]
    [InlineData("POST",   "/api/admin/questions/expand-corpus",  "questions.create")]
    [InlineData("POST",   "/api/admin",                          "admin.create")]   // unknown resource
    public void BuildAction_CanonicalMapping(string method, string path, string expected)
    {
        Assert.Equal(expected, AdminActionAuditMiddleware.BuildAction(method, path));
    }

    [Fact]
    public void BuildAction_UnknownMethod_Lowercased()
    {
        Assert.Equal("questions.options",
            AdminActionAuditMiddleware.BuildAction("OPTIONS", "/api/admin/questions"));
    }

    // ── ExtractTarget: (type, id) from route / query ─────────────────────

    [Fact]
    public void ExtractTarget_ResourceWithId_ReturnsBoth()
    {
        var req = MakeRequest("/api/admin/users/u-123");
        var (type, id) = AdminActionAuditMiddleware.ExtractTarget(req.Path.Value!, req);
        Assert.Equal("users", type);
        Assert.Equal("u-123", id);
    }

    [Fact]
    public void ExtractTarget_ResourceWithNestedPath_ReturnsParentId()
    {
        var req = MakeRequest("/api/admin/users/u-123/role");
        var (type, id) = AdminActionAuditMiddleware.ExtractTarget(req.Path.Value!, req);
        Assert.Equal("users", type);
        Assert.Equal("u-123", id);
    }

    [Fact]
    public void ExtractTarget_CollectionLevel_ReturnsTypeOnly()
    {
        var req = MakeRequest("/api/admin/questions");
        var (type, id) = AdminActionAuditMiddleware.ExtractTarget(req.Path.Value!, req);
        Assert.Equal("questions", type);
        Assert.Null(id);
    }

    [Fact]
    public void ExtractTarget_ActionKeywordAfterResource_TreatedAsCollectionLevel()
    {
        // /api/admin/questions/expand-corpus  ← "expand-corpus" is a verb,
        // not an id. Same for recreate-from-reference, bulk-retry, flow-state.
        var req = MakeRequest("/api/admin/questions/expand-corpus");
        var (type, id) = AdminActionAuditMiddleware.ExtractTarget(req.Path.Value!, req);
        Assert.Equal("questions", type);
        Assert.Null(id);
    }

    [Fact]
    public void ExtractTarget_QueryParamFallback_PicksUpId()
    {
        var req = MakeRequest("/api/admin/bulk-users", query: "?id=u-42");
        var (type, id) = AdminActionAuditMiddleware.ExtractTarget(req.Path.Value!, req);
        Assert.Equal("bulk-users", type);
        Assert.Equal("u-42", id);
    }

    [Fact]
    public void ExtractTarget_ShortPath_ReturnsNullNull()
    {
        var req = MakeRequest("/api/admin");
        var (type, id) = AdminActionAuditMiddleware.ExtractTarget(req.Path.Value!, req);
        Assert.Null(type);
        Assert.Null(id);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static HttpRequest MakeRequest(string path, string? query = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        if (!string.IsNullOrEmpty(query))
            ctx.Request.QueryString = new QueryString(query);

        return ctx.Request;
    }
}
