// =============================================================================
// FIND-sec-014: Security Metrics for Observability
//
// Registers a Meter("cena.security") with counters and histograms for
// security-critical events. Wired into all 3 hosts (Admin, Student, Actors).
//
// Metrics:
// - cena.security.tenant_rejection.count (counter)
// - cena.security.auth_rejection.count (counter)
// - cena.security.rate_limit_rejection.count (counter)
// - cena.security.privileged_action.count (counter)
// - cena.security.firebase_token_validation.duration_ms (histogram)
// - cena.security.signalr_connect_rejection.count (counter)
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Infrastructure.Observability;

/// <summary>
/// Security metrics for monitoring security-critical code paths.
/// </summary>
public sealed class SecurityMetrics : IDisposable
{
    private readonly Meter _meter;
    
    // Counters
    private readonly Counter<long> _tenantRejectionCount;
    private readonly Counter<long> _authRejectionCount;
    private readonly Counter<long> _rateLimitRejectionCount;
    private readonly Counter<long> _privilegedActionCount;
    private readonly Counter<long> _signalrConnectRejectionCount;
    
    // Histograms
    private readonly Histogram<double> _firebaseTokenValidationDuration;

    public SecurityMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create("cena.security", "1.0.0");

        _tenantRejectionCount = _meter.CreateCounter<long>(
            "cena.security.tenant_rejection.count",
            description: "Tenant isolation rejections (cross-school access attempts)");

        _authRejectionCount = _meter.CreateCounter<long>(
            "cena.security.auth_rejection.count",
            description: "Authentication rejections (invalid/expired/missing token)");

        _rateLimitRejectionCount = _meter.CreateCounter<long>(
            "cena.security.rate_limit_rejection.count",
            description: "Rate limit rejections by policy");

        _privilegedActionCount = _meter.CreateCounter<long>(
            "cena.security.privileged_action.count",
            description: "Privileged admin actions executed");

        _signalrConnectRejectionCount = _meter.CreateCounter<long>(
            "cena.security.signalr_connect_rejection.count",
            description: "SignalR connection auth rejections");

        _firebaseTokenValidationDuration = _meter.CreateHistogram<double>(
            "cena.security.firebase_token_validation.duration_ms",
            unit: "ms",
            description: "Firebase token validation duration");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Tenant Isolation Metrics
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record a tenant isolation rejection (cross-school access attempt).
    /// </summary>
    public void RecordTenantRejection(string service, string endpoint, string callerRole)
    {
        _tenantRejectionCount.Add(1,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("caller_role", callerRole));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authentication Metrics
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record an authentication rejection.
    /// </summary>
    public void RecordAuthRejection(string service, string reason)
    {
        _authRejectionCount.Add(1,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("reason", reason));
    }

    /// <summary>
    /// Record a SignalR connection auth rejection.
    /// </summary>
    public void RecordSignalrConnectRejection(string service, string reason)
    {
        _signalrConnectRejectionCount.Add(1,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("reason", reason));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Rate Limiting Metrics
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record a rate limit rejection.
    /// </summary>
    public void RecordRateLimitRejection(string policyName)
    {
        _rateLimitRejectionCount.Add(1,
            new KeyValuePair<string, object?>("policy_name", policyName));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Privileged Action Metrics
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record a privileged admin action.
    /// </summary>
    public void RecordPrivilegedAction(string action, string performedBy)
    {
        _privilegedActionCount.Add(1,
            new KeyValuePair<string, object?>("action", action),
            new KeyValuePair<string, object?>("performed_by", performedBy));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Firebase Token Validation Metrics
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record Firebase token validation duration.
    /// </summary>
    public void RecordFirebaseTokenValidationDuration(double durationMs, string service)
    {
        _firebaseTokenValidationDuration.Record(durationMs,
            new KeyValuePair<string, object?>("service", service));
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}

/// <summary>
/// DI registration extension for SecurityMetrics.
/// </summary>
public static class SecurityMetricsRegistration
{
    /// <summary>
    /// Registers SecurityMetrics as a singleton in the DI container.
    /// </summary>
    public static IServiceCollection AddSecurityMetrics(this IServiceCollection services)
    {
        services.AddSingleton<SecurityMetrics>();
        return services;
    }
}
