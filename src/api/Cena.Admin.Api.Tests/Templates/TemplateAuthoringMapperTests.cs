// =============================================================================
// Cena Platform — TemplateAuthoringMapper tests (prr-202)
//
// Unit tests for the pure validator + mapper functions. Covers:
//   * Id / body validation (edge cases, required fields, enum shapes)
//   * Constraint-predicate grammar whitelist
//   * LaTeX injection-marker blocklist
//   * State hash stability (same content → same hash; different content →
//     different hash)
//   * Document ↔ domain round-trip
// =============================================================================

using Cena.Actors.QuestionBank.Templates;
using Cena.Admin.Api.Templates;
using Cena.Infrastructure.Documents;

namespace Cena.Admin.Api.Tests.Templates;

public sealed class TemplateAuthoringMapperTests
{
    private static ParametricSlotPayloadDto Int(string n, int lo, int hi) =>
        new(n, "integer", lo, hi, Array.Empty<int>(), 0, 0, 1, 1, true, Array.Empty<string>());

    private static TemplateCreateRequestDto HappyRequest(string id = "tpl-happy") => new(
        Id: id, Subject: "math", Topic: "lin",
        Track: "FourUnit", Difficulty: "Easy", Methodology: "Halabi",
        BloomsLevel: 3, Language: "en",
        StemTemplate: "Solve {a} x = {b}",
        SolutionExpr: "b / a",
        VariableName: "x",
        AcceptShapes: new[] { "integer", "rational" },
        Slots: new[] { Int("a", 1, 5), Int("b", 0, 20) },
        Constraints: new[] { new SlotConstraintPayloadDto("non-zero a", "a != 0") },
        DistractorRules: null,
        Status: "draft");

    // ── ValidateId ──

    [Theory]
    [InlineData("good-id.v1")]
    [InlineData("a")]
    [InlineData("a_b_c")]
    public void ValidateId_Valid(string id) => TemplateAuthoringMapper.ValidateId(id);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("has$symbol")]
    public void ValidateId_Invalid(string id) =>
        Assert.Throws<ArgumentException>(() => TemplateAuthoringMapper.ValidateId(id));

    // ── ValidateBody happy path ──

    [Fact]
    public void ApplyCreate_HappyPath_PopulatesDocument()
    {
        var req = HappyRequest();
        var now = DateTimeOffset.Parse("2026-04-20T10:00:00Z");

        var doc = TemplateAuthoringMapper.ApplyCreate(req, "u-1", "school-a", now);

        Assert.Equal("tpl-happy", doc.Id);
        Assert.Equal(1, doc.Version);
        Assert.Equal("math", doc.Subject);
        Assert.Equal("FourUnit", doc.Track);
        Assert.Equal(2, doc.Slots.Count);
        Assert.True(doc.Active);
        Assert.Equal("u-1", doc.CreatedBy);
        Assert.Equal("school-a", doc.CreatedBySchool);
    }

    [Fact]
    public void ApplyUpdate_BumpsVersion_PreservesCreatedBy()
    {
        var req = HappyRequest();
        var now = DateTimeOffset.Parse("2026-04-20T10:00:00Z");
        var doc = TemplateAuthoringMapper.ApplyCreate(req, "u-1", "school-a", now);
        doc.Version = 3; // simulate prior update
        doc.CreatedBy = "orig-author";
        doc.CreatedBySchool = "school-z";

        var update = new TemplateUpdateRequestDto(
            Subject: "math", Topic: "NEW",
            Track: "FiveUnit", Difficulty: "Hard", Methodology: "Rabinovitch",
            BloomsLevel: 4, Language: "he",
            StemTemplate: "{a}", SolutionExpr: "a",
            VariableName: null,
            AcceptShapes: new[] { "integer" },
            Slots: new[] { Int("a", 1, 5) },
            Constraints: null, DistractorRules: null,
            Status: "published",
            ExpectedVersion: 3);

        var updated = TemplateAuthoringMapper.ApplyUpdate(
            doc, update, "u-2", "school-b", now.AddMinutes(5));

        Assert.Equal(4, updated.Version);
        Assert.Equal("orig-author", updated.CreatedBy);
        Assert.Equal("school-z", updated.CreatedBySchool);
        Assert.Equal("u-2", updated.LastMutatedBy);
        Assert.Equal("school-b", updated.LastMutatedBySchool);
        Assert.Equal("NEW", updated.Topic);
        Assert.Equal("FiveUnit", updated.Track);
        Assert.Equal("published", updated.Status);
    }

    [Fact]
    public void ApplyUpdate_VersionMismatch_Throws()
    {
        var doc = TemplateAuthoringMapper.ApplyCreate(HappyRequest(), "u-1", "school-a",
            DateTimeOffset.UtcNow);
        doc.Version = 7;

        var update = new TemplateUpdateRequestDto(
            "math", "z", "FourUnit", "Easy", "Halabi", 3, "en",
            "{a}", "a", null, new[] { "integer" },
            new[] { Int("a", 1, 5) }, null, null, "draft",
            ExpectedVersion: 3 /* stale */);

        Assert.Throws<ArgumentException>(() =>
            TemplateAuthoringMapper.ApplyUpdate(doc, update, "u-2", "school-a", DateTimeOffset.UtcNow));
    }

    // ── Constraint grammar ──

    [Theory]
    [InlineData("a != 0")]      // numeric rhs
    [InlineData("a == b")]      // slot rhs
    [InlineData("a > -5")]      // signed literal
    [InlineData("a <= 10")]
    public void ApplyCreate_AllowsSupportedConstraints(string predicate)
    {
        var req = HappyRequest() with
        {
            Constraints = new[] { new SlotConstraintPayloadDto("c", predicate) }
        };
        var doc = TemplateAuthoringMapper.ApplyCreate(req, "u", "s", DateTimeOffset.UtcNow);
        Assert.Single(doc.Constraints);
    }

    [Theory]
    [InlineData("a +++ 0")]     // bad shape (not 3 tokens)
    [InlineData("a @@ 0")]      // unknown operator
    [InlineData("unknown != 0")] // unknown slot
    [InlineData("a != notASlot")]// rhs neither slot nor number
    public void ApplyCreate_RejectsMalformedConstraint(string predicate)
    {
        var req = HappyRequest() with
        {
            Constraints = new[] { new SlotConstraintPayloadDto("c", predicate) }
        };
        Assert.Throws<ArgumentException>(() =>
            TemplateAuthoringMapper.ApplyCreate(req, "u", "s", DateTimeOffset.UtcNow));
    }

    // ── LaTeX injection ──

    [Theory]
    [InlineData(@"Solve \write18{rm -rf /} x = {b}")]
    [InlineData(@"\input{malicious.tex} = {b}")]
    [InlineData(@"\directlua{os.execute('x')}")]
    public void ApplyCreate_RejectsLatexInjection(string stem)
    {
        var req = HappyRequest() with { StemTemplate = stem };
        Assert.Throws<ArgumentException>(() =>
            TemplateAuthoringMapper.ApplyCreate(req, "u", "s", DateTimeOffset.UtcNow));
    }

    // ── State hash stability ──

    [Fact]
    public void ComputeStateHash_Stable_AcrossActorChanges()
    {
        var req = HappyRequest();
        var a = TemplateAuthoringMapper.ApplyCreate(req, "u-A", "school-1", DateTimeOffset.UtcNow);
        var b = TemplateAuthoringMapper.ApplyCreate(req, "u-B", "school-2", DateTimeOffset.UtcNow.AddDays(10));

        Assert.Equal(
            TemplateAuthoringMapper.ComputeStateHash(a),
            TemplateAuthoringMapper.ComputeStateHash(b));
    }

    [Fact]
    public void ComputeStateHash_Differs_OnContentChange()
    {
        var a = TemplateAuthoringMapper.ApplyCreate(HappyRequest(), "u-A", "school-1", DateTimeOffset.UtcNow);
        var bReq = HappyRequest() with { Topic = "DIFFERENT" };
        var b = TemplateAuthoringMapper.ApplyCreate(bReq, "u-A", "school-1", DateTimeOffset.UtcNow);

        Assert.NotEqual(
            TemplateAuthoringMapper.ComputeStateHash(a),
            TemplateAuthoringMapper.ComputeStateHash(b));
    }

    // ── Round-trip to domain ──

    [Fact]
    public void ToDomain_Validates_ViaParametricTemplate()
    {
        var doc = TemplateAuthoringMapper.ApplyCreate(HappyRequest(), "u", "s", DateTimeOffset.UtcNow);

        var domain = TemplateAuthoringMapper.ToDomain(doc);
        // Should not throw — ParametricTemplate.Validate has its own invariants.
        domain.Validate();

        Assert.Equal("tpl-happy", domain.Id);
        Assert.Equal(TemplateTrack.FourUnit, domain.Track);
        Assert.Equal(TemplateMethodology.Halabi, domain.Methodology);
        Assert.Equal(2, domain.Slots.Count);
    }

    [Fact]
    public void ToDetailDto_PreservesAllFields()
    {
        var doc = TemplateAuthoringMapper.ApplyCreate(HappyRequest(), "u", "s", DateTimeOffset.UtcNow);
        doc.Version = 5;
        doc.Status = "published";
        var dto = TemplateAuthoringMapper.ToDetailDto(doc);

        Assert.Equal(doc.Id, dto.Id);
        Assert.Equal(5, dto.Version);
        Assert.Equal("published", dto.Status);
        Assert.Equal(doc.Slots.Count, dto.Slots.Count);
        Assert.Equal(doc.Constraints.Count, dto.Constraints.Count);
    }

    // ── Enum parsers ──

    [Theory]
    [InlineData("FourUnit")]
    [InlineData("4-unit")]
    [InlineData("5unit")]
    [InlineData("FiveUnit")]
    public void ParseTrack_Accepts(string v) => TemplateAuthoringMapper.ParseTrack(v);

    [Theory]
    [InlineData("bogus")]
    [InlineData("")]
    public void ParseTrack_Rejects(string v) =>
        Assert.Throws<ArgumentException>(() => TemplateAuthoringMapper.ParseTrack(v));
}
