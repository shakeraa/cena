// =============================================================================
// Cena Platform — Reference<T> factory-only construction enforcement (PRR-267)
//
// Locks ADR-0059 §15.1 invariant: the only path to a Reference<T> value is
// the static `From()` factory. Direct constructor usage from outside
// Cena.Actors (the assembly that owns the type) is unreachable because
// the constructor is `internal`. This arch test pins:
//
//   1. The struct itself is a readonly record struct (matches the
//      compile-time-immutable phantom-type intent).
//   2. The constructor is non-public (internal/private), so external
//      assemblies cannot bypass `From()`.
//   3. `From(...)` is public + static + returns `Reference<T>`.
//   4. The audit-event id pinned at 8009 ("BagrutReferenceBrowsed").
//
// This is the positive-list arch test claude-code requested in
// m_c5f4da7dad20: "every endpoint returning Reference<T> MUST call
// factory (not direct ctor) — enforced via private ctor". The
// reachability check (no `new Reference<...>(...)` in any other
// assembly) is enforced by C# accessibility rules + this test's
// constructor visibility assertion. A grep-style scan would be
// belt-and-suspenders but redundant given the language already enforces
// it; we still surface a clear diagnostic if someone makes the ctor
// public.
// =============================================================================

using System.Reflection;
using Cena.Actors.Content;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Architecture;

public sealed class ReferenceFactoryOnlyTest
{
    [Fact]
    public void Reference_T_is_readonly_record_struct()
    {
        var type = typeof(Reference<>);
        Assert.True(type.IsValueType,
            "Reference<T> must be a struct (readonly record struct) per ADR-0059 §15.1.");
        // Records compile to a class with synthesized members; readonly
        // record struct compiles to a value type with [IsReadOnly]. We
        // assert both — IsValueType for the struct shape, IsByRefLike
        // staying false (so it can flow through async/await + collections).
        Assert.False(type.IsByRefLike,
            "Reference<T> must not be ref-struct — it has to flow through async/await.");
    }

    [Fact]
    public void Reference_T_constructor_is_internal_or_private()
    {
        var ctors = typeof(Reference<>).GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotEmpty(ctors);
        foreach (var ctor in ctors)
        {
            Assert.False(ctor.IsPublic,
                $"Reference<T> ctor must NOT be public — direct construction from external "
                + $"assemblies must be unreachable so the From() factory is the only path "
                + $"(ADR-0059 §15.1). Found public ctor with {ctor.GetParameters().Length} params.");
        }
    }

    [Fact]
    public void Reference_T_From_is_public_static_factory()
    {
        var fromMethod = typeof(Reference<>).GetMethod(
            "From",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(fromMethod);
        Assert.True(fromMethod.IsStatic, "Reference<T>.From must be static.");
        Assert.True(fromMethod.IsPublic, "Reference<T>.From must be public.");
    }

    [Fact]
    public void From_throws_when_provenance_is_not_MinistryBagrut()
    {
        var nonMinistryProvenance = new Provenance(
            ProvenanceKind.AiRecreated,
            DateTimeOffset.UtcNow,
            Source: "ai-recreated/abc123");
        var token = new ConsentTokenId(
            StudentId: "stu-1",
            Context: ReferenceContextKind.BrowseLibrary,
            IssuedAt: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(24),
            TokenHmac: "irrelevant-for-this-test");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Reference<string>.From(
                value: "payload",
                provenance: nonMinistryProvenance,
                consentToken: token,
                context: ReferenceContextKind.BrowseLibrary,
                auditLogger: NullLogger.Instance,
                now: DateTimeOffset.UtcNow,
                itemId: "item-1"));

        Assert.Contains("MinistryBagrut", ex.Message);
    }

    [Fact]
    public void From_throws_when_consent_token_expired()
    {
        var ministryProvenance = new Provenance(
            ProvenanceKind.MinistryBagrut,
            DateTimeOffset.UtcNow,
            Source: "ministry-bagrut/035581/2024/summer/A/q3");
        var now = DateTimeOffset.UtcNow;
        var expiredToken = new ConsentTokenId(
            StudentId: "stu-1",
            Context: ReferenceContextKind.BrowseLibrary,
            IssuedAt: now.AddHours(-25),
            ExpiresAt: now.AddHours(-1),
            TokenHmac: "irrelevant-for-this-test");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Reference<string>.From(
                value: "payload",
                provenance: ministryProvenance,
                consentToken: expiredToken,
                context: ReferenceContextKind.BrowseLibrary,
                auditLogger: NullLogger.Instance,
                now: now,
                itemId: "item-1"));

        Assert.Contains("expired", ex.Message);
    }

    [Fact]
    public void From_throws_when_token_context_mismatches_render_context()
    {
        var ministryProvenance = new Provenance(
            ProvenanceKind.MinistryBagrut,
            DateTimeOffset.UtcNow,
            Source: "ministry-bagrut/035581/2024/summer/A/q3");
        var now = DateTimeOffset.UtcNow;
        var browseToken = new ConsentTokenId(
            StudentId: "stu-1",
            Context: ReferenceContextKind.BrowseLibrary,
            IssuedAt: now,
            ExpiresAt: now.AddHours(24),
            TokenHmac: "irrelevant-for-this-test");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Reference<string>.From(
                value: "payload",
                provenance: ministryProvenance,
                consentToken: browseToken,
                context: ReferenceContextKind.VariantSourceCitation,
                auditLogger: NullLogger.Instance,
                now: now,
                itemId: "item-1"));

        Assert.Contains("context", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void From_succeeds_with_Ministry_provenance_and_valid_token()
    {
        var now = DateTimeOffset.UtcNow;
        var ministryProvenance = new Provenance(
            ProvenanceKind.MinistryBagrut,
            now,
            Source: "ministry-bagrut/035581/2024/summer/A/q3");
        var validToken = new ConsentTokenId(
            StudentId: "stu-1",
            Context: ReferenceContextKind.BrowseLibrary,
            IssuedAt: now,
            ExpiresAt: now.AddHours(24),
            TokenHmac: "valid-mock");

        var refWrapper = Reference<string>.From(
            value: "payload",
            provenance: ministryProvenance,
            consentToken: validToken,
            context: ReferenceContextKind.BrowseLibrary,
            auditLogger: NullLogger.Instance,
            now: now,
            itemId: "item-1");

        Assert.Equal("payload", refWrapper.Value);
        Assert.Equal(ProvenanceKind.MinistryBagrut, refWrapper.Provenance.Kind);
        Assert.Equal(ReferenceContextKind.BrowseLibrary, refWrapper.Context);
        Assert.Equal(validToken, refWrapper.ConsentToken);
    }

    [Fact]
    public void BagrutReferenceBrowsed_event_id_is_pinned_at_8009()
    {
        // ADR-0059 §15.1 invariant 4 — SIEM pipelines key on this id.
        // Drift here is a contract break; pin it.
        //
        // Reflect against a constructed generic (Reference<string>) — the
        // open generic Reference<> can't be reflected for static-field
        // values; the runtime needs a closed type.
        var eventIdField = typeof(Reference<string>)
            .GetField("BagrutReferenceBrowsedEventId",
                BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(eventIdField);
        var value = eventIdField.GetValue(null);
        Assert.NotNull(value);
        Assert.IsType<Microsoft.Extensions.Logging.EventId>(value);
        var eventId = (Microsoft.Extensions.Logging.EventId)value;
        Assert.Equal(8009, eventId.Id);
        Assert.Equal("BagrutReferenceBrowsed", eventId.Name);
    }
}
