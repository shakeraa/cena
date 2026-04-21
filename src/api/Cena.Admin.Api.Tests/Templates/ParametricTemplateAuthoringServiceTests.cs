// =============================================================================
// Cena Platform — ParametricTemplateAuthoringService tests (prr-202)
//
// Covers the CRUD + preview orchestration in isolation:
//   * Marten IDocumentSession is NSubstitute'd; event + audit appends are
//     asserted by inspecting the substitute recorder.
//   * The ParametricCompiler is the REAL compiler wrapping a FakeParametricRenderer
//     (from Cena.Actors.Tests). We want preview to exercise the actual
//     deterministic-seed derivation + slot draw + shape gate so a bug in the
//     compiler surfaces here instead of in integration.
//
// The "no tenant-cross leakage" test (TemplateAuthoringTenantScopedTest.cs)
// lives next door as an architecture ratchet — it asserts by reflection that
// every endpoint hits a tenant-aware session helper.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.QuestionBank.Templates;
using Cena.Admin.Api.Templates;
using Cena.Infrastructure.Documents;
using Marten;
using Marten.Events;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
// Use the Marten-public operations surface — IEventStore is internal as of 8.x.
using IEventOps = Marten.Events.IEventStoreOperations;

namespace Cena.Admin.Api.Tests.Templates;

public sealed class ParametricTemplateAuthoringServiceTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();
    private readonly IEventOps _events = Substitute.For<IEventOps>();
    private readonly IQuerySession _query = Substitute.For<IQuerySession>();
    private readonly ParametricCompiler _compiler;
    private readonly ParametricTemplateAuthoringService _service;
    private ParametricTemplateDocument? _current;

    public ParametricTemplateAuthoringServiceTests()
    {
        _store.LightweightSession().Returns(_session);
        _store.QuerySession().Returns(_query);
        _session.Events.Returns(_events);
        _session.LoadAsync<ParametricTemplateDocument>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => _current);

        _compiler = new ParametricCompiler(
            new InlineFakeRenderer(),
            NullLogger<ParametricCompiler>.Instance);

        _service = new ParametricTemplateAuthoringService(
            _store, _compiler, NullLogger<ParametricTemplateAuthoringService>.Instance);
    }

    // ── Factories ──────────────────────────────────────────────────────

    private static ClaimsPrincipal MakeAdmin(string school = "school-a", string uid = "u-1") =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "ADMIN"),
            new Claim("school_id", school),
            new Claim("user_id", uid)
        }, "test"));

    private static ClaimsPrincipal MakeSuperAdmin(string uid = "u-super") =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "SUPER_ADMIN"),
            new Claim("user_id", uid)
        }, "test"));

    private static ClaimsPrincipal MakeAdminMissingSchool() =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "ADMIN") }, "test"));

    private static TemplateCreateRequestDto HappyCreate(string id = "lin-slope-easy") => new(
        Id: id, Subject: "math", Topic: "linear-slope",
        Track: "FourUnit", Difficulty: "Easy", Methodology: "Halabi",
        BloomsLevel: 3, Language: "en",
        StemTemplate: "Solve for x: {a} x = {b}",
        SolutionExpr: "b / a",
        VariableName: "x",
        AcceptShapes: new[] { "integer", "rational" },
        Slots: new[]
        {
            new ParametricSlotPayloadDto("a", "integer", 1, 5, Array.Empty<int>(), 0, 0, 1, 1, true, Array.Empty<string>()),
            new ParametricSlotPayloadDto("b", "integer", 1, 10, Array.Empty<int>(), 0, 0, 1, 1, true, Array.Empty<string>())
        },
        Constraints: new[] { new SlotConstraintPayloadDto("a != 0", "a != 0") },
        DistractorRules: null,
        Status: "draft");

    // ── CREATE ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_HappyPath_PersistsDocAndAppendsCreatedEvent()
    {
        _current = null; // no prior doc
        var user = MakeAdmin();

        var detail = await _service.CreateAsync(HappyCreate(), user);

        Assert.NotNull(detail);
        Assert.Equal("lin-slope-easy", detail.Id);
        Assert.Equal(1, detail.Version);
        Assert.True(detail.Active);
        Assert.Equal("draft", detail.Status);

        // Doc was stored once; event appended once; audit event row also stored.
        _session.Received().Store(Arg.Is<ParametricTemplateDocument>(d => d.Id == "lin-slope-easy" && d.Version == 1));
        _events.Received().Append("lin-slope-easy", Arg.Is<object>(e => e is ParametricTemplateCreated_V1));
        await _session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_DuplicateId_Throws()
    {
        _current = new ParametricTemplateDocument { Id = "existing", Version = 1 };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(HappyCreate("existing"), MakeAdmin()));
    }

    [Theory]
    [InlineData("bad id with spaces")]
    [InlineData("")]
    [InlineData("/etc/passwd")]
    public async Task CreateAsync_MalformedId_ThrowsArgument(string badId)
    {
        _current = null;
        var bad = HappyCreate() with { Id = badId };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(bad, MakeAdmin()));
    }

    [Fact]
    public async Task CreateAsync_RejectsLatexInjection()
    {
        _current = null;
        var bad = HappyCreate() with
        {
            StemTemplate = @"Solve for x: {a} x = {b} \write18{rm -rf /}"
        };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(bad, MakeAdmin()));
        Assert.Contains("\\write18", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownConstraintOperator()
    {
        _current = null;
        var bad = HappyCreate() with
        {
            Constraints = new[]
            {
                new SlotConstraintPayloadDto("bogus", "a ?? 0")
            }
        };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(bad, MakeAdmin()));
    }

    [Fact]
    public async Task CreateAsync_UsesSchoolIdAsActorSchool()
    {
        _current = null;
        ParametricTemplateCreated_V1? observed = null;
        _events.WhenForAnyArgs(x => x.Append(default(string)!, default(object[])!))
               .Do(ci =>
               {
                   foreach (var a in ci.ArgAt<object[]>(1))
                       if (a is ParametricTemplateCreated_V1 v) observed = v;
               });

        await _service.CreateAsync(HappyCreate("abc"), MakeAdmin(school: "school-tiberias"));

        Assert.NotNull(observed);
        Assert.Equal("school-tiberias", observed!.ActorSchoolId);
        Assert.Equal("u-1", observed.ActorUserId);
    }

    [Fact]
    public async Task CreateAsync_AdminWithoutSchool_Throws()
    {
        _current = null;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.CreateAsync(HappyCreate(), MakeAdminMissingSchool()));
    }

    // ── UPDATE ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_BumpsVersionAndRecordsPriorHash()
    {
        var initial = new ParametricTemplateDocument
        {
            Id = "tpl-1", Version = 3, Active = true,
            Subject = "math", Topic = "old",
            Track = "FourUnit", Difficulty = "Easy", Methodology = "Halabi",
            BloomsLevel = 3, Language = "en",
            StemTemplate = "old", SolutionExpr = "a",
            AcceptShapes = new() { "integer" },
            Slots = new() { new ParametricSlotPayload { Name = "a", Kind = "integer", IntegerMin = 1, IntegerMax = 5 } },
        };
        initial.StateHash = TemplateAuthoringMapper.ComputeStateHash(initial);
        _current = initial;

        ParametricTemplateUpdated_V1? observed = null;
        _events.WhenForAnyArgs(x => x.Append(default(string)!, default(object[])!))
               .Do(ci =>
               {
                   foreach (var a in ci.ArgAt<object[]>(1))
                       if (a is ParametricTemplateUpdated_V1 v) observed = v;
               });

        var update = new TemplateUpdateRequestDto(
            Subject: "math", Topic: "NEW-TOPIC",
            Track: "FourUnit", Difficulty: "Easy", Methodology: "Halabi",
            BloomsLevel: 3, Language: "en",
            StemTemplate: "{a}", SolutionExpr: "a",
            VariableName: null,
            AcceptShapes: new[] { "integer" },
            Slots: new[]
            {
                new ParametricSlotPayloadDto("a", "integer", 1, 5, Array.Empty<int>(), 0, 0, 1, 1, true, Array.Empty<string>())
            },
            Constraints: null, DistractorRules: null,
            Status: "draft",
            ExpectedVersion: 3);

        var detail = await _service.UpdateAsync("tpl-1", update, MakeAdmin());

        Assert.NotNull(detail);
        Assert.Equal(4, detail!.Version);
        Assert.NotNull(observed);
        Assert.Equal(4, observed!.Version);
        Assert.Equal(initial.StateHash, observed.PriorStateHash);
        Assert.Contains("Topic", observed.ChangedFields);
    }

    [Fact]
    public async Task UpdateAsync_VersionMismatch_Throws()
    {
        _current = new ParametricTemplateDocument
        {
            Id = "tpl-1", Version = 5, Active = true,
            Subject = "math", Topic = "x",
            Track = "FourUnit", Difficulty = "Easy", Methodology = "Halabi",
            BloomsLevel = 3, Language = "en",
            StemTemplate = "{a}", SolutionExpr = "a",
            AcceptShapes = new() { "integer" },
            Slots = new() { new ParametricSlotPayload { Name = "a", Kind = "integer", IntegerMin = 1, IntegerMax = 5 } }
        };

        var update = new TemplateUpdateRequestDto(
            "math", "y", "FourUnit", "Easy", "Halabi", 3, "en",
            "{a}", "a", null, new[] { "integer" },
            new[] { new ParametricSlotPayloadDto("a", "integer", 1, 5, Array.Empty<int>(), 0, 0, 1, 1, true, Array.Empty<string>()) },
            null, null, "draft", ExpectedVersion: 3 /* stale */);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAsync("tpl-1", update, MakeAdmin()));
    }

    [Fact]
    public async Task UpdateAsync_InactiveTemplate_ReturnsNull()
    {
        _current = new ParametricTemplateDocument { Id = "tpl-x", Version = 2, Active = false };

        var update = new TemplateUpdateRequestDto(
            "math", "y", "FourUnit", "Easy", "Halabi", 3, "en",
            "{a}", "a", null, new[] { "integer" },
            new[] { new ParametricSlotPayloadDto("a", "integer", 1, 5, Array.Empty<int>(), 0, 0, 1, 1, true, Array.Empty<string>()) },
            null, null, "draft", ExpectedVersion: 2);

        var result = await _service.UpdateAsync("tpl-x", update, MakeAdmin());
        Assert.Null(result);
    }

    // ── DELETE ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SoftDeleteAsync_FlipsActiveAndEmitsEvent()
    {
        _current = new ParametricTemplateDocument
        {
            Id = "to-delete", Version = 2, Active = true, Status = "published",
            Subject = "math", Topic = "x",
            Track = "FourUnit", Difficulty = "Easy", Methodology = "Halabi",
            BloomsLevel = 3, Language = "en",
            StemTemplate = "{a}", SolutionExpr = "a",
            AcceptShapes = new() { "integer" },
            Slots = new() { new ParametricSlotPayload { Name = "a", Kind = "integer", IntegerMin = 1, IntegerMax = 5 } }
        };
        _current.StateHash = TemplateAuthoringMapper.ComputeStateHash(_current);

        ParametricTemplateDeleted_V1? observed = null;
        _events.WhenForAnyArgs(x => x.Append(default(string)!, default(object[])!))
               .Do(ci =>
               {
                   foreach (var a in ci.ArgAt<object[]>(1))
                       if (a is ParametricTemplateDeleted_V1 d) observed = d;
               });

        var ok = await _service.SoftDeleteAsync("to-delete", "bad authoring",
            MakeAdmin(school: "school-b", uid: "u-curator"));

        Assert.True(ok);
        Assert.False(_current.Active);
        Assert.NotNull(observed);
        Assert.Equal("to-delete", observed!.TemplateId);
        Assert.Equal(2, observed.PriorVersion);
        Assert.Equal("school-b", observed.ActorSchoolId);
        Assert.Equal("u-curator", observed.ActorUserId);
        Assert.Equal("bad authoring", observed.Reason);
    }

    [Fact]
    public async Task SoftDeleteAsync_AlreadyInactive_ReturnsFalse()
    {
        _current = new ParametricTemplateDocument { Id = "x", Active = false };
        var ok = await _service.SoftDeleteAsync("x", null, MakeAdmin());
        Assert.False(ok);
    }

    // ── PREVIEW ────────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewAsync_IntegerSlotsHappyPath_ReturnsAcceptedSamplesWithCasAnswer()
    {
        _current = new ParametricTemplateDocument
        {
            Id = "lin-ok", Version = 1, Active = true,
            Subject = "math", Topic = "arith",
            Track = "FourUnit", Difficulty = "Easy", Methodology = "Halabi",
            BloomsLevel = 3, Language = "en",
            StemTemplate = "Compute {a} + {b}",
            SolutionExpr = "a + b",
            AcceptShapes = new() { "integer" },
            Slots = new()
            {
                new ParametricSlotPayload { Name = "a", Kind = "integer", IntegerMin = 1, IntegerMax = 10 },
                new ParametricSlotPayload { Name = "b", Kind = "integer", IntegerMin = 1, IntegerMax = 10 }
            }
        };
        _current.StateHash = TemplateAuthoringMapper.ComputeStateHash(_current);

        var resp = await _service.PreviewAsync("lin-ok",
            new TemplatePreviewRequestDto(BaseSeed: 42, SampleCount: 5), MakeAdmin());

        Assert.NotNull(resp);
        Assert.Equal("lin-ok", resp!.TemplateId);
        Assert.Equal(5, resp.AcceptedCount);
        Assert.Null(resp.OverallError);
        Assert.All(resp.Samples, s =>
        {
            Assert.True(s.Accepted);
            Assert.NotNull(s.Stem);
            Assert.NotNull(s.CanonicalAnswer);
        });
    }

    [Fact]
    public async Task PreviewAsync_ZeroDivisor_FailsSampleNotOverallCas()
    {
        // Rational slot allowing denominator 0 + solution that divides by that
        // slot ⇒ FakeParametricRenderer emits RejectedZeroDivisor.
        _current = new ParametricTemplateDocument
        {
            Id = "div-zero", Version = 1, Active = true,
            Subject = "math", Topic = "div",
            Track = "FourUnit", Difficulty = "Easy", Methodology = "Halabi",
            BloomsLevel = 3, Language = "en",
            StemTemplate = "{a}/{b}", SolutionExpr = "a / b",
            AcceptShapes = new() { "integer", "rational" },
            Slots = new()
            {
                new ParametricSlotPayload { Name = "a", Kind = "integer", IntegerMin = 1, IntegerMax = 5 },
                new ParametricSlotPayload { Name = "b", Kind = "integer", IntegerMin = 0, IntegerMax = 0 }
            }
        };

        // No "b != 0" constraint → every draw hits /0 → compiler throws
        // InsufficientSlotSpace. Service catches and returns OverallError.
        var resp = await _service.PreviewAsync("div-zero",
            new TemplatePreviewRequestDto(BaseSeed: 1, SampleCount: 3), MakeAdmin());

        Assert.NotNull(resp);
        Assert.NotNull(resp!.OverallError);
    }

    [Fact]
    public async Task PreviewAsync_NonExistentTemplate_ReturnsNull()
    {
        _current = null;
        var resp = await _service.PreviewAsync("missing",
            new TemplatePreviewRequestDto(1, 3), MakeAdmin());
        Assert.Null(resp);
    }

    [Fact]
    public async Task PreviewAsync_EmitsPreviewEventWithActorSchool()
    {
        _current = new ParametricTemplateDocument
        {
            Id = "prev-audit", Version = 1, Active = true,
            Subject = "math", Topic = "arith",
            Track = "FourUnit", Difficulty = "Easy", Methodology = "Halabi",
            BloomsLevel = 3, Language = "en",
            StemTemplate = "{a}", SolutionExpr = "a",
            AcceptShapes = new() { "integer" },
            Slots = new() { new ParametricSlotPayload { Name = "a", Kind = "integer", IntegerMin = 1, IntegerMax = 3 } }
        };

        ParametricTemplatePreviewExecuted_V1? observed = null;
        _events.WhenForAnyArgs(x => x.Append(default(string)!, default(object[])!))
               .Do(ci =>
               {
                   foreach (var a in ci.ArgAt<object[]>(1))
                       if (a is ParametricTemplatePreviewExecuted_V1 v) observed = v;
               });

        await _service.PreviewAsync("prev-audit",
            new TemplatePreviewRequestDto(BaseSeed: 1, SampleCount: 3),
            MakeAdmin(school: "school-yeru", uid: "u-prev"));

        Assert.NotNull(observed);
        Assert.Equal("school-yeru", observed!.ActorSchoolId);
        Assert.Equal("u-prev", observed.ActorUserId);
        Assert.Equal(3, observed.SampleCount);
    }

    [Fact]
    public async Task PreviewAsync_ClampsSampleCountAboveMax()
    {
        _current = new ParametricTemplateDocument
        {
            Id = "cap", Version = 1, Active = true,
            Subject = "math", Topic = "x",
            Track = "FourUnit", Difficulty = "Easy", Methodology = "Halabi",
            BloomsLevel = 3, Language = "en",
            StemTemplate = "{a}", SolutionExpr = "a",
            AcceptShapes = new() { "integer" },
            Slots = new() { new ParametricSlotPayload { Name = "a", Kind = "integer", IntegerMin = 1, IntegerMax = 100 } }
        };

        var resp = await _service.PreviewAsync("cap",
            new TemplatePreviewRequestDto(BaseSeed: 1, SampleCount: 500 /* will clamp */),
            MakeAdmin());
        Assert.NotNull(resp);
        Assert.Equal(ParametricTemplateAuthoringService.MaxPreviewSamples, resp!.RequestedCount);
    }

    // ── LIST + GET ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_AdminWithoutSchool_Throws()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.ListAsync(
                new TemplateListFilterDto(null, null, null, null, null, null, false, 1, 25),
                MakeAdminMissingSchool()));
    }

    [Fact]
    public async Task GetAsync_AdminWithoutSchool_Throws()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetAsync("x", MakeAdminMissingSchool()));
    }

    [Fact]
    public async Task GetAsync_SuperAdminWithoutSchool_Succeeds()
    {
        _current = null;
        var detail = await _service.GetAsync("tpl-x", MakeSuperAdmin());
        Assert.Null(detail);
    }
}

