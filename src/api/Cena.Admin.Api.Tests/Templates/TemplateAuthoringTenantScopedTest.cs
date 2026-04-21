// =============================================================================
// Cena Platform — Architecture ratchet: Template authoring tenant-scoped test
//
// Asserts two invariants that are easy to break during a refactor:
//
//   1. ParametricTemplateAuthoringService hits a tenant-aware session helper
//      on EVERY mutation path — via TenantScope.GetSchoolFilter(user). If a
//      refactor accidentally drops the call on (say) SoftDeleteAsync, the
//      event stream loses its ActorSchoolId stamp and the audit trail becomes
//      unreconstructible. This test catches that BEFORE it ships.
//
//   2. TemplateCrudEndpoints routes the five CRUD paths + preview under
//      AdminOnly authorization. Non-admin roles must 403.
//
// Implementation: reflection + file-text grep. Keeping the test entirely in
// the test project means it runs on every CI push alongside the unit tests
// without needing the full ASP.NET host.
// =============================================================================

using System.Reflection;
using Cena.Admin.Api.Templates;

namespace Cena.Admin.Api.Tests.Templates;

public sealed class TemplateAuthoringTenantScopedTest
{
    // ── Service file — tenant scope on every mutation ─────────────────

    [Fact]
    public void ParametricTemplateAuthoringService_Source_CallsTenantScope_OnAllPublicMethods()
    {
        var source = LoadSource("ParametricTemplateAuthoringService.cs");

        // At minimum, the service must import and invoke the canonical helper.
        Assert.Contains("using Cena.Infrastructure.Tenancy;", source);
        Assert.Contains("TenantScope.GetSchoolFilter(user)", source);

        // Each of the six public methods must be present by name — if the
        // contract shrinks this test fails loudly.
        string[] required =
        {
            "public async Task<TemplateListResponseDto> ListAsync(",
            "public async Task<TemplateDetailDto?> GetAsync(",
            "public async Task<TemplateDetailDto> CreateAsync(",
            "public async Task<TemplateDetailDto?> UpdateAsync(",
            "public async Task<bool> SoftDeleteAsync(",
            "public async Task<TemplatePreviewResponseDto?> PreviewAsync("
        };
        foreach (var sig in required)
            Assert.True(source.Contains(sig),
                $"Expected method signature '{sig}' in ParametricTemplateAuthoringService.cs");

        // Every mutation must pipe through ResolveActor(user) which internally
        // calls TenantScope.GetSchoolFilter. If someone hand-rolls a mutation
        // without that gate the test trips.
        var mutationCount = CountOccurrences(source, "ResolveActor(user)");
        Assert.True(mutationCount >= 4, // Create, Update, Delete, Preview
            $"Expected ResolveActor(user) to be called on every mutation; found {mutationCount}");

        // Every mutation must Append to the event stream.
        var appendCount = CountOccurrences(source, "session.Events.Append(");
        Assert.True(appendCount >= 4,
            $"Expected ≥4 event appends (create/update/delete/preview); found {appendCount}");

        // Audit event for every mutation.
        var auditCount = CountOccurrences(source, "WriteAuditEvent(session, user,");
        Assert.True(auditCount >= 4,
            $"Expected ≥4 WriteAuditEvent calls; found {auditCount}");
    }

    [Fact]
    public void ParametricTemplateAuthoringService_AllPublicMutations_AcceptClaimsPrincipal()
    {
        var svc = typeof(IParametricTemplateAuthoringService);
        string[] mutators = { "CreateAsync", "UpdateAsync", "SoftDeleteAsync", "PreviewAsync", "ListAsync", "GetAsync" };
        foreach (var name in mutators)
        {
            var method = svc.GetMethod(name);
            Assert.NotNull(method);
            Assert.Contains(method!.GetParameters(), p => p.ParameterType == typeof(System.Security.Claims.ClaimsPrincipal));
        }
    }

    // ── Endpoint file — AdminOnly on every route ──────────────────────

    [Fact]
    public void TemplateCrudEndpoints_RequiresAdminOnly()
    {
        var source = LoadSource("TemplateCrudEndpoints.cs");
        Assert.Contains("CenaAuthPolicies.AdminOnly", source);

        // Both the main CRUD group AND the /preview group must require AdminOnly.
        var adminOnlyCount = CountOccurrences(source, "CenaAuthPolicies.AdminOnly");
        Assert.True(adminOnlyCount >= 2,
            $"Expected AdminOnly on both CRUD group and preview group; found {adminOnlyCount}");
    }

    [Fact]
    public void TemplateCrudEndpoints_MapsAllSixRoutes()
    {
        var source = LoadSource("TemplateCrudEndpoints.cs");

        // Minimal-API mappings — the router group is under /api/admin/templates.
        Assert.Contains("/api/admin/templates\"", source);        // group
        Assert.Contains("crud.MapGet(\"\"", source);              // list
        Assert.Contains("crud.MapGet(\"{id}\"", source);          // detail
        Assert.Contains("crud.MapPost(\"\"", source);             // create
        Assert.Contains("crud.MapPut(\"{id}\"", source);          // update
        Assert.Contains("crud.MapDelete(\"{id}\"", source);       // delete
        Assert.Contains("/api/admin/templates/{id}/preview\"", source); // preview group
        Assert.Contains("preview.MapPost(\"\"", source);          // preview route
    }

    [Fact]
    public void TemplateCrudEndpoints_UsesRateLimitingBuckets()
    {
        var source = LoadSource("TemplateCrudEndpoints.cs");
        // CRUD group: api bucket. Preview group: ai bucket (CAS fan-out).
        Assert.Contains("RequireRateLimiting(\"api\")", source);
        Assert.Contains("RequireRateLimiting(\"ai\")", source);
    }

    // ── Mapper / validator — whitelist grammar (persona-redteam) ──────

    [Fact]
    public void TemplateAuthoringMapper_NoDynamicEvalSurface()
    {
        var source = LoadSource("TemplateAuthoringMapper.cs");
        // Defensive: the mapper must NOT use System.CodeDom, Roslyn scripting,
        // CSharpScript, or the reflection emitter. Those would break the
        // persona-redteam DoD requirement of "whitelist grammar, no dynamic-code
        // execution surface".
        string[] forbidden =
        {
            "CSharpScript", "ScriptOptions", "Assembly.Load",
            "System.CodeDom.Compiler", "CodeCompileUnit",
            "Activator.CreateInstance(Type.GetType",
            "DynamicMethod(", "ILGenerator"
        };
        foreach (var marker in forbidden)
            Assert.False(source.Contains(marker),
                $"TemplateAuthoringMapper.cs must not reference '{marker}' (persona-redteam DoD)");
    }

    [Fact]
    public void TemplateAuthoringMapper_RejectsLatexInjectionMarkers()
    {
        var source = LoadSource("TemplateAuthoringMapper.cs");
        // The injection-marker block must enumerate the classic TeX escape
        // directives. Losing this set would let an author ship \write18 to
        // the CAS sidecar.
        string[] mustList =
        {
            "\\\\write18", "\\\\input", "\\\\directlua", "\\\\catcode"
        };
        foreach (var m in mustList)
            Assert.True(System.Text.RegularExpressions.Regex.IsMatch(source, m),
                $"Expected LaTeX injection marker '{m}' in mapper blacklist");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static string LoadSource(string filename)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "../../../../Cena.Admin.Api/Templates", filename),
            Path.Combine(AppContext.BaseDirectory, "../../../Cena.Admin.Api/Templates", filename),
            Path.Combine(AppContext.BaseDirectory, "../../Cena.Admin.Api/Templates", filename)
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return File.ReadAllText(c);

        // Fallback: walk up the tree. This is slow but robust across different
        // CI layouts (local, GitHub runner, docker test image).
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && cursor is not null; i++, cursor = cursor.Parent)
        {
            var probe = Path.Combine(cursor.FullName, "src", "api", "Cena.Admin.Api", "Templates", filename);
            if (File.Exists(probe)) return File.ReadAllText(probe);
        }

        throw new FileNotFoundException(
            $"Could not locate {filename} from AppContext.BaseDirectory='{AppContext.BaseDirectory}'");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
