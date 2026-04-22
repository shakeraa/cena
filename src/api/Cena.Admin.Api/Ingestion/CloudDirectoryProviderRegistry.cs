// =============================================================================
// Cena Platform — Cloud Directory Provider Registry (ADR-0058)
//
// Routes CloudDir list/ingest requests to the correct
// ICloudDirectoryProvider by its identifier string. Populated by DI
// at startup via AddCloudDirectoryProviders().
// =============================================================================

namespace Cena.Admin.Api.Ingestion;

public sealed class CloudDirectoryProviderRegistry : ICloudDirectoryProviderRegistry
{
    private readonly Dictionary<string, ICloudDirectoryProvider> _byId;

    public CloudDirectoryProviderRegistry(IEnumerable<ICloudDirectoryProvider> providers)
    {
        _byId = providers.ToDictionary(
            p => p.ProviderId,
            p => p,
            StringComparer.OrdinalIgnoreCase);
        All = _byId.Values.ToList();
    }

    public IReadOnlyList<ICloudDirectoryProvider> All { get; }

    public ICloudDirectoryProvider Resolve(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new InvalidOperationException("Provider id must be supplied.");

        if (!_byId.TryGetValue(providerId, out var provider))
            throw new InvalidOperationException(
                $"Unknown cloud-directory provider '{providerId}'. " +
                $"Registered: {string.Join(", ", _byId.Keys)}.");

        if (!provider.IsEnabled)
            throw new InvalidOperationException(
                $"Cloud-directory provider '{providerId}' is registered but disabled. " +
                $"Check Ingestion:{providerId}:Enabled (or the provider's allowlist) in configuration.");

        return provider;
    }
}
