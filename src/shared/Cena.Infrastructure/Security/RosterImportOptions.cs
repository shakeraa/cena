// =============================================================================
// Cena Platform -- Roster Import Options (prr-021)
//
// Configuration for the bulk-invite CSV import. Ships with sane production
// defaults; allows per-tenant override via configuration section
// "Cena:RosterImport:TenantOverrides:<school_id>:{MaxBytes|MaxRows}".
//
// Bound via Microsoft.Extensions.Options from appsettings.json; the admin
// host's Program.cs calls `services.AddRosterImportOptions(configuration)`
// which in turn wires the IOptions<RosterImportOptions> singleton.
// =============================================================================

namespace Cena.Infrastructure.Security;

public sealed class RosterImportOptions
{
    public const string SectionName = "Cena:RosterImport";

    /// <summary>Default byte cap applied when no per-tenant override exists. Default 10 MiB.</summary>
    public int DefaultMaxBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Default row cap. Default 5000.</summary>
    public int DefaultMaxRows { get; set; } = 5000;

    /// <summary>Default per-cell char cap after sanitization. Default 1024.</summary>
    public int DefaultMaxCellLength { get; set; } = 1024;

    /// <summary>
    /// Rate-limit window for admin-initiated roster imports. Default: 5 imports per hour per tenant.
    /// These are consumed by the rate-limiter policy registered in the admin host.
    /// </summary>
    public int ImportsPerHourPerTenant { get; set; } = 5;

    /// <summary>
    /// Per-school overrides for size/row caps. Key = school_id (tenant id).
    /// Missing keys inherit the defaults. Nulls inside the override record
    /// also inherit (allow partial overrides).
    /// </summary>
    public Dictionary<string, TenantOverride> TenantOverrides { get; set; } = new();

    /// <summary>
    /// Produce the effective sanitizer config for the given tenant. Falls
    /// back to defaults when no override exists or the override field is null.
    /// </summary>
    public CsvRosterSanitizerConfig ForTenant(string? tenantId)
    {
        if (!string.IsNullOrEmpty(tenantId)
            && TenantOverrides.TryGetValue(tenantId, out var ovr))
        {
            return new CsvRosterSanitizerConfig
            {
                MaxBytes = ovr.MaxBytes ?? DefaultMaxBytes,
                MaxRows = ovr.MaxRows ?? DefaultMaxRows,
                MaxCellLength = ovr.MaxCellLength ?? DefaultMaxCellLength,
            };
        }

        return new CsvRosterSanitizerConfig
        {
            MaxBytes = DefaultMaxBytes,
            MaxRows = DefaultMaxRows,
            MaxCellLength = DefaultMaxCellLength,
        };
    }

    public sealed class TenantOverride
    {
        public int? MaxBytes { get; set; }
        public int? MaxRows { get; set; }
        public int? MaxCellLength { get; set; }
    }
}
