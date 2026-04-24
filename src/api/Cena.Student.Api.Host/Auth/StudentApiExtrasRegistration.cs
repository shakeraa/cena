// =============================================================================
// Cena Platform — Student API extras DI bundle
//
// Bundles prr-001 ExifStripper + prr-011 SessionRevocationList + cleanup service +
// SessionExchangeEndpoints + CookieAuthMiddleware into single-line host registration
// and pipeline calls so Program.cs stays under its FileSize500LocBaseline.yml
// grandfather baseline (per ADR-0012 Schedule Lock — baselines only ratchet down).
// =============================================================================

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Media;

namespace Cena.Student.Api.Host.Auth;

public static class StudentApiExtrasRegistration
{
    /// <summary>prr-001 + prr-011 service registrations (bundled to minimise Program.cs LOC).</summary>
    public static IServiceCollection AddStudentApiExtras(this IServiceCollection services)
    {
        services.AddSingleton<ExifStripper>();
        services.AddSingleton<SessionRevocationList>();
        services.AddHostedService<SessionRevocationListCleanupService>();
        return services;
    }

    /// <summary>prr-011 middleware + endpoint registration (bundled to minimise Program.cs LOC).</summary>
    public static WebApplication UseStudentApiAuthPipeline(this WebApplication app)
    {
        app.UseMiddleware<CookieAuthMiddleware>();
        app.MapSessionExchangeEndpoints();
        return app;
    }
}
