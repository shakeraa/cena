// =============================================================================
// Cena Platform -- PII Scanner
// SEC-003: Reflection-based discovery of [Pii]-annotated fields on any object.
//
// Used by:
//   - StudentDataExporter  — marks PII fields in GDPR portability exports
//   - PiiLogSanitizer      — drives which properties to redact in Serilog
//   - Compliance endpoints — generates pii_fields_report for audit
// =============================================================================

using System.Collections.Concurrent;
using System.Reflection;

namespace Cena.Infrastructure.Compliance;

/// <summary>Describes a single PII-annotated property discovered on a type.</summary>
public sealed record PiiFieldDescriptor(
    string TypeName,
    string PropertyName,
    PiiLevel Level,
    string Category,
    bool RequiresEncryption,
    bool ExcludeFromLogs);

/// <summary>Aggregated PII scan result for a single object instance.</summary>
public sealed record PiiScanReport(
    string TypeName,
    IReadOnlyList<PiiFieldDescriptor> Fields,
    bool HasCritical,
    bool HasHigh,
    bool HasMedium,
    bool HasLow)
{
    /// <summary>True if any PII fields were found.</summary>
    public bool HasAnyPii => Fields.Count > 0;
}

/// <summary>
/// Scans types and object instances for properties decorated with <see cref="PiiAttribute"/>.
/// Results are cached per type so repeated scans are allocation-free after the first call.
/// </summary>
public static class PiiScanner
{
    // Type-level cache: Type -> list of (PropertyInfo, PiiAttribute) pairs
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<(PropertyInfo Property, PiiAttribute Attr)>>
        _cache = new();

    /// <summary>
    /// Scans all declared properties on <typeparamref name="T"/> for <see cref="PiiAttribute"/>
    /// annotations and returns a report.  Does NOT inspect object values — purely structural.
    /// </summary>
    public static PiiScanReport ScanType<T>() => ScanType(typeof(T));

    /// <summary>
    /// Scans all declared properties on <paramref name="type"/> for <see cref="PiiAttribute"/>
    /// annotations and returns a report.
    /// </summary>
    public static PiiScanReport ScanType(Type type)
    {
        var annotated = GetAnnotatedProperties(type);

        var descriptors = annotated
            .Select(p => new PiiFieldDescriptor(
                TypeName: type.Name,
                PropertyName: p.Property.Name,
                Level: p.Attr.Level,
                Category: p.Attr.Category,
                RequiresEncryption: p.Attr.RequiresEncryption,
                ExcludeFromLogs: p.Attr.ExcludeFromLogs))
            .ToList();

        return BuildReport(type.Name, descriptors);
    }

    /// <summary>
    /// Scans the runtime type of <paramref name="instance"/> and returns the report.
    /// The instance itself is only used to resolve the concrete type; no values are read.
    /// </summary>
    public static PiiScanReport ScanInstance(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return ScanType(instance.GetType());
    }

    /// <summary>
    /// Returns the names of all properties that must be excluded from logs on this type.
    /// Cached after first call.
    /// </summary>
    public static IReadOnlyList<string> GetLogExcludedPropertyNames(Type type)
    {
        return GetAnnotatedProperties(type)
            .Where(p => p.Attr.ExcludeFromLogs)
            .Select(p => p.Property.Name)
            .ToList();
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static IReadOnlyList<(PropertyInfo Property, PiiAttribute Attr)> GetAnnotatedProperties(Type type)
    {
        return _cache.GetOrAdd(type, t =>
        {
            return t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => (Property: p, Attr: p.GetCustomAttribute<PiiAttribute>()))
                .Where(pair => pair.Attr is not null)
                .Select(pair => (pair.Property, pair.Attr!))
                .ToList();
        });
    }

    private static PiiScanReport BuildReport(string typeName, List<PiiFieldDescriptor> descriptors)
    {
        return new PiiScanReport(
            TypeName: typeName,
            Fields: descriptors,
            HasCritical: descriptors.Any(f => f.Level == PiiLevel.Critical),
            HasHigh: descriptors.Any(f => f.Level == PiiLevel.High),
            HasMedium: descriptors.Any(f => f.Level == PiiLevel.Medium),
            HasLow: descriptors.Any(f => f.Level == PiiLevel.Low));
    }
}
