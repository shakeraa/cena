// =============================================================================
// Cena Platform -- Bulk Roster Import Hardening Tests (prr-021)
//
// Integration-style tests for AdminUserService.BulkInviteAsync. They verify
// the end-to-end sanitizer wiring:
//   * file-level rejections (oversize, malformed UTF-8) short-circuit before
//     Firebase is touched, and surface a synthetic "<file>" failure row
//   * row-level rejections (injection, bidi, homoglyph, empty, wrong columns)
//     are counted and do not trigger InviteUser
//   * tenant binding: caller's school_id dictates the target; the CSV cannot
//     carry a school hint
//   * good rows hit Firebase exactly once per row
//
// Firebase, Redis, NATS, and Marten are stubbed via NSubstitute so the tests
// are deterministic and do not spin up a container.
// =============================================================================

using System.Security.Claims;
using System.Text;
using Cena.Actors.Bus;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Firebase;
using Cena.Infrastructure.Security;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Admin.Api.Tests;

public class BulkRosterImportHardeningTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();
    private readonly IFirebaseAdminService _firebase = Substitute.For<IFirebaseAdminService>();
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly INatsConnection _nats = Substitute.For<INatsConnection>();

    public BulkRosterImportHardeningTests()
    {
        _store.LightweightSession().Returns(_session);
        _store.QuerySession().Returns(Substitute.For<IQuerySession>());
        _firebase.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
                 .Returns(ci => $"uid-{Guid.NewGuid():N}");
        _firebase.SetCustomClaimsAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object>>())
                 .Returns(Task.CompletedTask);
        _firebase.GenerateSignInLinkAsync(Arg.Any<string>())
                 .Returns("https://example.com/invite");
    }

    private AdminUserService CreateService(RosterImportOptions? options = null) => new(
        _store,
        _firebase,
        _redis,
        _nats,
        NullLogger<AdminUserService>.Instance,
        Options.Create(options ?? new RosterImportOptions()));

    private static ClaimsPrincipal Admin(string? schoolId, string role = "ADMIN")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, role),
            new("role", role),
            new("user_id", "admin-1"),
        };
        if (schoolId != null)
            claims.Add(new Claim("school_id", schoolId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static Stream Csv(string body) =>
        new MemoryStream(Encoding.UTF8.GetBytes(body));

    // =========================================================================
    // File-level rejection
    // =========================================================================

    [Fact]
    public async Task BulkInvite_FileExceedsSizeCap_ReturnsFileSyntheticFailure_NoFirebase()
    {
        var options = new RosterImportOptions { DefaultMaxBytes = 100, DefaultMaxRows = 5000 };
        var service = CreateService(options);

        var pad = new string('x', 200);
        var csv = $"name,email,role\nAlice,alice@x.com,{pad}\n";

        var result = await service.BulkInviteAsync(Csv(csv), Admin("school-A"));

        Assert.Equal(0, result.Created);
        Assert.Single(result.Failed);
        Assert.Equal("<file>", result.Failed[0].Email);
        Assert.Contains("FileTooLarge", result.Failed[0].Error);
        await _firebase.DidNotReceive().CreateUserAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task BulkInvite_MalformedUtf8_RejectsFile_NoFirebase()
    {
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("name,email,role\n"));
        ms.WriteByte(0xC3); // leading byte of 2-byte seq
        ms.WriteByte(0x28); // invalid continuation
        ms.Write(Encoding.ASCII.GetBytes(",alice@x.com,STUDENT\n"));
        ms.Position = 0;

        var service = CreateService();
        var result = await service.BulkInviteAsync(ms, Admin("school-A"));

        Assert.Equal(0, result.Created);
        Assert.Single(result.Failed);
        Assert.Contains("MalformedUtf8", result.Failed[0].Error);
        await _firebase.DidNotReceive().CreateUserAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task BulkInvite_HeaderMismatch_RejectsFile()
    {
        var csv = "name,email,password\nAlice,alice@x.com,hunter2\n";
        var service = CreateService();
        var result = await service.BulkInviteAsync(Csv(csv), Admin("school-A"));

        Assert.Equal(0, result.Created);
        Assert.Single(result.Failed);
        Assert.Contains("HeaderMismatch", result.Failed[0].Error);
    }

    // =========================================================================
    // Row-level rejections
    // =========================================================================

    [Fact]
    public async Task BulkInvite_InjectionRows_StrippedButAccepted()
    {
        // Leading = on the name column gets stripped; rest of row is valid.
        var csv = "name,email,role\n\"=cmd\",alice@x.com,STUDENT\n";
        var service = CreateService();

        var result = await service.BulkInviteAsync(Csv(csv), Admin("school-A"));

        Assert.Equal(1, result.Created);
        Assert.Empty(result.Failed);
        // The Firebase display name must NOT contain the leading =.
        await _firebase.Received(1).CreateUserAsync("alice@x.com", "alice@x.com", null);
    }

    [Fact]
    public async Task BulkInvite_HomoglyphRow_Rejected_NoFirebase()
    {
        var csv = "name,email,role\n\uFF21dmin,ad@x.com,STUDENT\n";
        var service = CreateService();

        var result = await service.BulkInviteAsync(Csv(csv), Admin("school-A"));

        Assert.Equal(0, result.Created);
        // Homoglyph row is silently counted in rejections_by_kind (audit log);
        // the caller sees a zero-created, empty-failed response. No Firebase.
        Assert.Empty(result.Failed);
        await _firebase.DidNotReceive().CreateUserAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task BulkInvite_BidiOverride_Stripped_InviteSucceeds()
    {
        var csv = "name,email,role\n\u202EEvil,alice@x.com,STUDENT\n";
        var service = CreateService();

        var result = await service.BulkInviteAsync(Csv(csv), Admin("school-A"));

        Assert.Equal(1, result.Created);
        await _firebase.Received(1).CreateUserAsync("alice@x.com", "alice@x.com", null);
    }

    [Fact]
    public async Task BulkInvite_WrongColumnCount_Rejected_NoFirebase()
    {
        var csv = "name,email,role\nAlice,alice@x.com\nBob,bob@x.com,STUDENT\n";
        var service = CreateService();

        var result = await service.BulkInviteAsync(Csv(csv), Admin("school-A"));

        Assert.Equal(1, result.Created); // only Bob
        await _firebase.Received(1).CreateUserAsync("bob@x.com", Arg.Any<string>(), Arg.Any<string?>());
    }

    // =========================================================================
    // Tenant binding
    // =========================================================================

    [Fact]
    public async Task BulkInvite_NonSuperAdmin_TargetsOwnSchool_Always()
    {
        var csv = "name,email,role\nAlice,alice@x.com,STUDENT\n";
        var service = CreateService();

        // Admin in school-A imports; the invite must be scoped to school-A,
        // regardless of what Firebase custom claims previously existed.
        await service.BulkInviteAsync(Csv(csv), Admin("school-A"));

        await _firebase.Received().SetCustomClaimsAsync(
            Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>>(d =>
                d.ContainsKey("school_id") && (string)d["school_id"] == "school-A"));
    }

    [Fact]
    public async Task BulkInvite_SuperAdmin_NoSchoolScope_ReceivesEmpty()
    {
        var csv = "name,email,role\nAlice,alice@x.com,STUDENT\n";
        var service = CreateService();

        await service.BulkInviteAsync(Csv(csv), Admin(null, role: "SUPER_ADMIN"));

        await _firebase.Received().SetCustomClaimsAsync(
            Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>>(d =>
                d.ContainsKey("school_id") && (string)d["school_id"] == string.Empty));
    }

    // =========================================================================
    // Audit persistence
    // =========================================================================

    [Fact]
    public async Task BulkInvite_SuccessfulImport_PersistsAuditRow()
    {
        var csv = "name,email,role\nAlice,alice@x.com,STUDENT\n";
        var service = CreateService();

        await service.BulkInviteAsync(Csv(csv), Admin("school-A"));

        // Two LightweightSession() calls: one for audit, one embedded in
        // InviteUserAsync for the AdminUser document. Each must Store + Save.
        _session.Received().Store(Arg.Any<AuditEventDocument>());
    }

    [Fact]
    public async Task BulkInvite_FileRejection_StillPersistsAudit()
    {
        var options = new RosterImportOptions { DefaultMaxBytes = 20 };
        var service = CreateService(options);

        var csv = new string('x', 100);
        await service.BulkInviteAsync(Csv(csv), Admin("school-A"));

        _session.Received().Store(Arg.Is<AuditEventDocument>(
            a => a.Action == "users.bulk_invite" && !a.Success));
    }

    // =========================================================================
    // Row cap boundary
    // =========================================================================

    [Fact]
    public async Task BulkInvite_AtRowCap_AcceptsAll()
    {
        var options = new RosterImportOptions { DefaultMaxRows = 3 };
        var service = CreateService(options);

        var sb = new StringBuilder("name,email,role\n");
        sb.AppendLine("A,a@x.com,STUDENT");
        sb.AppendLine("B,b@x.com,STUDENT");
        sb.AppendLine("C,c@x.com,STUDENT");

        var result = await service.BulkInviteAsync(Csv(sb.ToString()), Admin("school-A"));
        Assert.Equal(3, result.Created);
    }

    [Fact]
    public async Task BulkInvite_OverRowCap_RejectsExcess()
    {
        var options = new RosterImportOptions { DefaultMaxRows = 2 };
        var service = CreateService(options);

        var sb = new StringBuilder("name,email,role\n");
        sb.AppendLine("A,a@x.com,STUDENT");
        sb.AppendLine("B,b@x.com,STUDENT");
        sb.AppendLine("C,c@x.com,STUDENT");
        sb.AppendLine("D,d@x.com,STUDENT");

        var result = await service.BulkInviteAsync(Csv(sb.ToString()), Admin("school-A"));

        Assert.Equal(2, result.Created);
        await _firebase.Received(2).CreateUserAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
    }
}
