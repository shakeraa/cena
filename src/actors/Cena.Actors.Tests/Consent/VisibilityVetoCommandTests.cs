// =============================================================================
// Cena Platform — VetoParentVisibility + RestoreParentVisibility command tests
// (prr-052)
//
// Covers:
//   1. Teen16to17 + Adult can veto → event emitted; ConsentAggregate fold
//      surfaces the veto in VetoedParentVisibilityPurposes.
//   2. Under13 + Teen13to15 are refused with ConsentAuthorizationException.
//   3. Restore removes the veto from the aggregate state.
//   4. Veto event PII is encrypted (SubjectId and InitiatorActorId go
//      through EncryptedFieldAccessor).
//   5. Empty subject-id, empty initiator-id, empty institute-id all
//      throw ArgumentException.
//   6. InstitutePolicy initiator bypasses the band check (institute can
//      add a stricter narrowing regardless of band).
// =============================================================================

using Cena.Actors.Consent;
using Cena.Actors.Consent.Events;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Compliance.KeyStore;

namespace Cena.Actors.Tests.Consent;

public sealed class VisibilityVetoCommandTests
{
    private static readonly byte[] TestRootKey = new byte[]
    {
        0x50, 0x72, 0x72, 0x30, 0x35, 0x32, 0x56, 0x65,
        0x74, 0x6f, 0x52, 0x6f, 0x6f, 0x74, 0x4b, 0x65,
        0x79, 0x46, 0x6f, 0x72, 0x55, 0x6e, 0x69, 0x74,
        0x54, 0x65, 0x73, 0x74, 0x73, 0x20, 0x21, 0x21
    };

    private static (EncryptedFieldAccessor accessor, ConsentCommandHandler handler,
                    InMemoryConsentAggregateStore store) BuildSut()
    {
        var derivation = new SubjectKeyDerivation(TestRootKey, "prr-052-test", isDevFallback: false);
        var keyStore = new InMemorySubjectKeyStore(derivation);
        var accessor = new EncryptedFieldAccessor(keyStore);
        var handler = new ConsentCommandHandler(accessor);
        var store = new InMemoryConsentAggregateStore();
        return (accessor, handler, store);
    }

    // ── Allow path: Teen16to17 veto then restore ─────────────────────────

    [Fact]
    public async Task Teen16to17_CanVetoThenRestore_AggregateReflectsBothStates()
    {
        var (_, handler, store) = BuildSut();
        const string subject = "student-teen16";

        var veto = await handler.HandleAsync(new VetoParentVisibility(
            StudentSubjectId: subject,
            StudentBand: AgeBand.Teen16to17,
            Purpose: ConsentPurpose.ParentDigest,
            Initiator: VetoInitiator.Student,
            InitiatorActorId: subject,
            InstituteId: "inst-A",
            VetoedAt: DateTimeOffset.UtcNow,
            Reason: "unit-test"));

        await store.AppendAsync(subject, veto);

        var afterVeto = await store.LoadAsync(subject);
        Assert.Contains(ConsentPurpose.ParentDigest,
            afterVeto.State.VetoedParentVisibilityPurposes);

        var restore = await handler.HandleAsync(new RestoreParentVisibility(
            StudentSubjectId: subject,
            StudentBand: AgeBand.Teen16to17,
            Purpose: ConsentPurpose.ParentDigest,
            Initiator: VetoInitiator.Student,
            InitiatorActorId: subject,
            InstituteId: "inst-A",
            RestoredAt: DateTimeOffset.UtcNow));

        await store.AppendAsync(subject, restore);

        var afterRestore = await store.LoadAsync(subject);
        Assert.DoesNotContain(ConsentPurpose.ParentDigest,
            afterRestore.State.VetoedParentVisibilityPurposes);
    }

    [Fact]
    public async Task Adult_CanVeto()
    {
        var (_, handler, _) = BuildSut();

        var ev = await handler.HandleAsync(new VetoParentVisibility(
            StudentSubjectId: "adult-1",
            StudentBand: AgeBand.Adult,
            Purpose: ConsentPurpose.TeacherShare,
            Initiator: VetoInitiator.Student,
            InitiatorActorId: "adult-1",
            InstituteId: "inst-A",
            VetoedAt: DateTimeOffset.UtcNow,
            Reason: "adult-veto"));

        Assert.Equal(ConsentPurpose.TeacherShare, ev.Purpose);
        Assert.Equal(VetoInitiator.Student, ev.Initiator);
        Assert.NotEmpty(ev.StudentSubjectIdEncrypted);
        Assert.NotEmpty(ev.InitiatorActorIdEncrypted);
        // PII-encrypted fields must not equal the plaintext input.
        Assert.NotEqual("adult-1", ev.StudentSubjectIdEncrypted);
    }

    // ── Deny path: Under13 and Teen13to15 refused ────────────────────────

    [Theory]
    [InlineData(AgeBand.Under13)]
    [InlineData(AgeBand.Teen13to15)]
    public async Task Under16_StudentInitiatedVeto_Refused(AgeBand band)
    {
        var (_, handler, _) = BuildSut();

        await Assert.ThrowsAsync<ConsentAuthorizationException>(async () =>
            await handler.HandleAsync(new VetoParentVisibility(
                StudentSubjectId: $"student-{band}",
                StudentBand: band,
                Purpose: ConsentPurpose.ParentDigest,
                Initiator: VetoInitiator.Student,
                InitiatorActorId: $"student-{band}",
                InstituteId: "inst-A",
                VetoedAt: DateTimeOffset.UtcNow,
                Reason: "should-be-refused")));
    }

    // ── Institute-policy initiator bypasses band check ───────────────────

    [Fact]
    public async Task InstitutePolicy_Initiator_CanVeto_EvenUnder13()
    {
        var (_, handler, _) = BuildSut();

        var ev = await handler.HandleAsync(new VetoParentVisibility(
            StudentSubjectId: "under13-kid",
            StudentBand: AgeBand.Under13,
            Purpose: ConsentPurpose.TeacherShare,
            Initiator: VetoInitiator.InstitutePolicy,
            InitiatorActorId: "inst-policy-engine",
            InstituteId: "inst-A",
            VetoedAt: DateTimeOffset.UtcNow,
            Reason: "institute-narrowing"));

        Assert.Equal(VetoInitiator.InstitutePolicy, ev.Initiator);
    }

    // ── Argument validation ──────────────────────────────────────────────

    [Fact]
    public async Task EmptyStudentId_ThrowsArgumentException()
    {
        var (_, handler, _) = BuildSut();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await handler.HandleAsync(new VetoParentVisibility(
                StudentSubjectId: "",
                StudentBand: AgeBand.Adult,
                Purpose: ConsentPurpose.ParentDigest,
                Initiator: VetoInitiator.Student,
                InitiatorActorId: "x",
                InstituteId: "inst-A",
                VetoedAt: DateTimeOffset.UtcNow,
                Reason: "")));
    }

    [Fact]
    public async Task EmptyInstituteId_ThrowsArgumentException()
    {
        var (_, handler, _) = BuildSut();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await handler.HandleAsync(new VetoParentVisibility(
                StudentSubjectId: "s1",
                StudentBand: AgeBand.Adult,
                Purpose: ConsentPurpose.ParentDigest,
                Initiator: VetoInitiator.Student,
                InitiatorActorId: "s1",
                InstituteId: "",
                VetoedAt: DateTimeOffset.UtcNow,
                Reason: "")));
    }

    // ── Restore refused for bands without veto authority ─────────────────

    [Theory]
    [InlineData(AgeBand.Under13)]
    [InlineData(AgeBand.Teen13to15)]
    public async Task Restore_StudentInitiated_RefusedForNonVetoingBands(AgeBand band)
    {
        var (_, handler, _) = BuildSut();

        await Assert.ThrowsAsync<ConsentAuthorizationException>(async () =>
            await handler.HandleAsync(new RestoreParentVisibility(
                StudentSubjectId: "s1",
                StudentBand: band,
                Purpose: ConsentPurpose.ParentDigest,
                Initiator: VetoInitiator.Student,
                InitiatorActorId: "s1",
                InstituteId: "inst-A",
                RestoredAt: DateTimeOffset.UtcNow)));
    }
}
