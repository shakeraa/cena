// =============================================================================
// Cena Platform — DI extension for the cloud-directory provider stack.
// ADR-0058. Registers Local + S3 providers unconditionally; each
// provider's IsEnabled property reflects runtime config (Ingestion:
// CloudWatchDirs for local, Ingestion:S3:Enabled + AllowedBuckets for
// S3). Dispatch to a disabled provider throws a curator-readable error.
//
// IAmazonS3 is registered once; the AWS SDK's default credential chain
// resolves credentials at first call — IRSA in EKS prod, static keys
// from config elsewhere (BasicAWSCredentials), or the shared-profile
// / IMDS fallbacks for local ad-hoc runs.
// =============================================================================

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cena.Admin.Api.Ingestion;

public static class CloudDirectoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the cloud-directory provider stack (ADR-0058).
    /// Call from <c>CenaAdminServiceRegistration.AddCenaAdminServices</c>
    /// so both Admin.Api.Host and Actor.Host get the registration.
    /// Safe to call when no S3 configuration is present — the S3 provider
    /// simply reports <c>IsEnabled=false</c>.
    /// </summary>
    public static IServiceCollection AddCloudDirectoryProviders(this IServiceCollection services)
    {
        // Bind IngestionOptions from IConfiguration (auto-resolved from
        // DI). Using AddOptions+BindConfiguration avoids needing callers
        // to pass IConfiguration explicitly — the binding picks up the
        // host's IConfiguration at options-resolution time.
        services.AddOptions<IngestionOptions>().BindConfiguration("Ingestion");

        // Local provider: filesystem-backed, path-traversal-guarded.
        services.AddSingleton<ICloudDirectoryProvider, LocalDirectoryProvider>();

        // S3 provider: uses IAmazonS3. The factory below reads IOptions at
        // resolve time, so if S3 is disabled in config the client is never
        // actually constructed (the S3DirectoryProvider.IsEnabled check
        // short-circuits dispatch before IAmazonS3 is resolved).
        services.AddSingleton<IAmazonS3>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<IngestionOptions>>().Value.S3
                ?? throw new InvalidOperationException(
                    "Ingestion:S3 configuration section is missing, but IAmazonS3 was resolved. " +
                    "Check dispatch flow — S3DirectoryProvider.IsEnabled should have short-circuited.");

            var config = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(opts.Region),
                ForcePathStyle = opts.ForcePathStyle,
            };

            if (!string.IsNullOrEmpty(opts.ServiceUrl))
            {
                // LocalStack / MinIO dev endpoint. ServiceURL takes
                // precedence over RegionEndpoint in the SDK.
                config.ServiceURL = opts.ServiceUrl;
                config.AuthenticationRegion = opts.Region;
            }

            // Credentials: static keys when configured (non-EKS dev and
            // LocalStack), otherwise fall through to the SDK's default
            // chain: IRSA → env → shared-credentials → IMDS.
            AWSCredentials credentials =
                (!string.IsNullOrEmpty(opts.AccessKey) && !string.IsNullOrEmpty(opts.SecretKey))
                    ? new BasicAWSCredentials(opts.AccessKey, opts.SecretKey)
                    : FallbackCredentialsFactory.GetCredentials();

            return new AmazonS3Client(credentials, config);
        });
        services.AddSingleton<ICloudDirectoryProvider, S3DirectoryProvider>();

        services.AddSingleton<ICloudDirectoryProviderRegistry, CloudDirectoryProviderRegistry>();

        return services;
    }

}
