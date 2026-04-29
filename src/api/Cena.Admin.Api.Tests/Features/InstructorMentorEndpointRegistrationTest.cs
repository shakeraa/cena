// =============================================================================
// BG-05: architecture test — instructor + mentor endpoints must stay wired
//
// Closes the regression class where these endpoints were silently 404 in
// main: BG-05 surfaced 4 production 404s (instructor classrooms + 3
// mentor surfaces) that the admin SPA called on every dashboard load.
//
// This test fails the build if:
//   1. The InstructorClassroomsEndpoint or MentorInstitutesEndpoint
//      classes disappear, OR
//   2. Their public Map* extension method signatures drift, OR
//   3. The route constants change without a corresponding allowlist /
//      e2e-flow spec update, OR
//   4. The admin Program.cs source no longer calls the Map extension
//      methods (silently un-wiring the endpoints).
//
// Static-source check — no runtime DI, no Marten connection.
// =============================================================================

using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace Cena.Admin.Api.Tests.Features;

public sealed class InstructorMentorEndpointRegistrationTest
{
    /// <summary>
    /// Walk up from the test assembly bin folder until we find the repo
    /// root (folder containing src/, docs/, tasks/). Same lenient scheme
    /// as BagrutCorpusSeedRegistrationTest.
    /// </summary>
    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            if (d.GetDirectories("src").Length > 0
                && d.GetDirectories("docs").Length > 0
                && d.GetDirectories("tasks").Length > 0)
            {
                return d.FullName;
            }
            d = d.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate Cena repo root from {AppContext.BaseDirectory}");
    }

    [Fact]
    public void InstructorClassroomsEndpoint_class_must_exist()
    {
        var asm = typeof(Cena.Admin.Api.Features.InstructorConsole.InstructorClassroomsEndpoint).Assembly;
        var t = asm.GetType("Cena.Admin.Api.Features.InstructorConsole.InstructorClassroomsEndpoint");
        Assert.NotNull(t);

        var route = (string?)t!.GetField("Route", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        Assert.Equal("/api/instructor/classrooms", route);

        var map = t.GetMethod("MapInstructorClassroomsEndpoint",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(map);
    }

    [Fact]
    public void MentorInstitutesEndpoint_class_must_expose_3_routes()
    {
        var t = typeof(Cena.Admin.Api.Features.MentorConsole.MentorInstitutesEndpoint);

        var listRoute = (string?)t.GetField("ListRoute", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        var detailRoute = (string?)t.GetField("DetailRoute", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        var classroomsRoute = (string?)t.GetField("ClassroomsRoute", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

        Assert.Equal("/api/mentor/institutes", listRoute);
        Assert.Equal("/api/mentor/institutes/{instituteId}", detailRoute);
        Assert.Equal("/api/mentor/institutes/{instituteId}/classrooms", classroomsRoute);

        var map = t.GetMethod("MapMentorInstitutesEndpoint",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(map);
    }

    [Fact]
    public void Program_cs_must_wire_both_endpoint_groups()
    {
        var repoRoot = FindRepoRoot();
        var programPath = Path.Combine(repoRoot, "src", "api", "Cena.Admin.Api.Host", "Program.cs");
        Assert.True(File.Exists(programPath), $"admin-api Program.cs must be at {programPath}");

        var content = File.ReadAllText(programPath);
        Assert.Contains("MapInstructorClassroomsEndpoint", content);
        Assert.Contains("MapMentorInstitutesEndpoint", content);
    }

    [Fact]
    public void DTOs_match_SPA_TypeScript_contract_field_names()
    {
        // Match the SPA's TS interface ClassroomOverview at
        // src/admin/full-version/src/pages/instructor/index.vue:8-16
        var dto = typeof(Cena.Admin.Api.Features.InstructorConsole.InstructorClassroomDto);
        var props = dto.GetProperties();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in props) names.Add(p.Name);

        // ASP.NET will camelCase these on the wire — the SPA expects
        // {id, name, mode, status, studentCount, joinCode}.
        Assert.Contains("Id", names);
        Assert.Contains("Name", names);
        Assert.Contains("Mode", names);
        Assert.Contains("Status", names);
        Assert.Contains("StudentCount", names);
        Assert.Contains("JoinCode", names);

        // Match the SPA's TS interface InstituteOverview at
        // src/admin/full-version/src/pages/mentor/index.vue:7-13
        var instDto = typeof(Cena.Admin.Api.Features.MentorConsole.MentorInstituteDto);
        var instNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in instDto.GetProperties()) instNames.Add(p.Name);
        Assert.Contains("Id", instNames);
        Assert.Contains("Name", instNames);
        Assert.Contains("Type", instNames);
        Assert.Contains("ClassroomCount", instNames);
        Assert.Contains("StudentCount", instNames);
    }
}
