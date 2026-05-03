// =============================================================================
// Cena Platform — PRR-252 DI smoke test
//
// Verifies that the student-api DI container can resolve
// ICasGatedQuestionPersister + its full transitive chain. Pinned because
// PRR-245's variant-generation endpoint will fail at runtime if any link
// in the chain (CasVerificationGate / CasGateModeProvider / MathContentDetector
// / StemSolutionExtractor / ICasRouterService / IDocumentStore) is missing
// or has an incompatible lifetime.
//
// Chain mirrors CenaAdminServiceRegistration.cs:78-88 (the canonical admin
// path); a test here is the regression net for the student path.
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.Content;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Cena.Student.Api.Host.Tests.Persistence;

public sealed class CasGatedQuestionPersisterDiTests
{
    [Fact]
    public void StudentApi_ResolvesICasGatedQuestionPersister_WithoutThrowing()
    {
        // Mirror the registration block introduced in
        // Cena.Student.Api.Host/Program.cs by PRR-252. Substitute the two
        // boundary services (IDocumentStore + ICasRouterService) since they
        // require live infrastructure (Postgres / NATS) the unit-test
        // process doesn't have. Everything in between is real.
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSingleton(new Mock<IDocumentStore>().Object);
        services.AddSingleton(new Mock<ICasRouterService>().Object);

        services.AddSingleton<IMathContentDetector, MathContentDetector>();
        services.AddSingleton<IStemSolutionExtractor, StemSolutionExtractor>();
        services.AddSingleton<ICasGateModeProvider, CasGateModeProvider>();
        services.AddScoped<ICasVerificationGate, CasVerificationGate>();
        services.AddScoped<ICasGatedQuestionPersister, CasGatedQuestionPersister>();

        var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var persister = scope.ServiceProvider.GetRequiredService<ICasGatedQuestionPersister>();
        Assert.NotNull(persister);
        Assert.IsType<CasGatedQuestionPersister>(persister);
    }

    [Fact]
    public void StudentCasPersistContext_ParametricVariantWithSeed_HasIdempotencyKey()
    {
        // Lineage + parametric seed → stable dedup key per ADR-0059 §5.
        var ctx = new StudentCasPersistContext(
            StudentId: "stu-001",
            SourceProvenance: new Provenance(
                ProvenanceKind.MinistryBagrut, DateTimeOffset.UtcNow, "035582"),
            SourceShailonCode: "035582",
            SourceQuestionIndex: 3,
            VariationKind: VariationKind.Parametric,
            ParametricSeed: 4242);

        Assert.Equal("variant|035582|3|parametric|4242", ctx.IdempotencyKey);
    }

    [Fact]
    public void StudentCasPersistContext_StructuralVariant_HasNullIdempotencyKey()
    {
        // Structural variants are non-deterministic LLM outputs — every
        // request is a fresh generation, so idempotency must be null
        // (otherwise we'd dedup distinct outputs to the first one cached).
        var ctx = new StudentCasPersistContext(
            StudentId: "stu-002",
            SourceProvenance: new Provenance(
                ProvenanceKind.MinistryBagrut, DateTimeOffset.UtcNow, "035582"),
            SourceShailonCode: "035582",
            SourceQuestionIndex: 5,
            VariationKind: VariationKind.Structural,
            ParametricSeed: 999); // even with seed, structural bypasses dedup

        Assert.Null(ctx.IdempotencyKey);
    }

    [Fact]
    public void StudentCasPersistContext_MissingLineage_HasNullIdempotencyKey()
    {
        // Without source-shailon lineage, the key cannot be stable across
        // students — so disable dedup defensively.
        var ctx = new StudentCasPersistContext(
            StudentId: "stu-003",
            SourceProvenance: null,
            SourceShailonCode: null,
            SourceQuestionIndex: null,
            VariationKind: VariationKind.Parametric,
            ParametricSeed: 1234);

        Assert.Null(ctx.IdempotencyKey);
    }

    [Fact]
    public void StudentCasPersistContext_ParametricWithoutSeed_HasNullIdempotencyKey()
    {
        // Parametric without an explicit seed is also non-deterministic
        // (the persister would need to generate a fresh seed itself, which
        // can't be replayed). Dedup disabled.
        var ctx = new StudentCasPersistContext(
            StudentId: "stu-004",
            SourceProvenance: new Provenance(
                ProvenanceKind.MinistryBagrut, DateTimeOffset.UtcNow, "035582"),
            SourceShailonCode: "035582",
            SourceQuestionIndex: 3,
            VariationKind: VariationKind.Parametric,
            ParametricSeed: null);

        Assert.Null(ctx.IdempotencyKey);
    }
}
