// =============================================================================
// Cena Platform — Seed Context (RDY-037)
// Carrier passed to each optional seed delegate in DatabaseSeeder. Replaces
// the old (IDocumentStore, ILogger) tuple so seeds that need additional
// services (e.g. the CAS-gated question persister) can resolve them without
// being migrated to full DI classes.
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Seed;

/// <summary>
/// RDY-037: context carrier for <see cref="DatabaseSeeder"/> optional seeds.
/// Seeds that require richer dependencies resolve them via
/// <see cref="Services"/>; legacy seeds that only need the store + logger
/// can ignore the other fields.
/// </summary>
public sealed record SeedContext(
    IDocumentStore Store,
    IServiceProvider Services,
    ILogger Logger);
