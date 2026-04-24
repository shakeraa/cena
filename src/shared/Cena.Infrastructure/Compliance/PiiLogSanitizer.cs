// =============================================================================
// Cena Platform -- PII Log Sanitizer
// SEC-003: Serilog destructuring policy that redacts [Pii(ExcludeFromLogs=true)]
//          properties before they reach any log sink.
//
// Registration (both host Program.cs):
//
//   builder.Host.UseSerilog((context, services, config) =>
//   {
//       config
//           .ReadFrom.Configuration(context.Configuration)
//           .Destructure.With<PiiDestructuringPolicy>();
//   });
//
// The policy intercepts Serilog's object destructuring and replaces any
// property whose declaring type has [Pii(ExcludeFromLogs=true)] with the
// literal string "[REDACTED]".
// =============================================================================

using System.Collections.Concurrent;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Serilog <see cref="IDestructuringPolicy"/> that replaces PII-annotated properties
/// with <c>"[REDACTED]"</c> in log events.
///
/// Properties are redacted when their <see cref="PiiAttribute.ExcludeFromLogs"/> flag
/// is true, which is set automatically for <see cref="PiiLevel.Medium"/> and above,
/// or when the attribute is constructed with <c>excludeFromLogs: true</c>.
/// </summary>
public sealed class PiiDestructuringPolicy : IDestructuringPolicy
{
    private const string RedactedPlaceholder = "[REDACTED]";

    // Cache of redacted property names per type, to avoid repeated reflection.
    private static readonly ConcurrentDictionary<Type, HashSet<string>> _redactedPropertiesCache = new();

    /// <inheritdoc />
    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue result)
    {
        if (value is null)
        {
            result = null!;
            return false;
        }

        var type = value.GetType();
        var redactedNames = GetRedactedPropertyNames(type);

        // If this type has no PII-annotated properties, skip — let Serilog handle it normally.
        if (redactedNames.Count == 0)
        {
            result = null!;
            return false;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var logProperties = new List<LogEventProperty>(properties.Length);

        foreach (var prop in properties)
        {
            if (redactedNames.Contains(prop.Name))
            {
                // Replace PII with placeholder — value is never read.
                logProperties.Add(new LogEventProperty(
                    prop.Name,
                    new ScalarValue(RedactedPlaceholder)));
            }
            else
            {
                // Recursively destructure safe properties via the factory.
                object? rawValue = null;
                try
                {
                    rawValue = prop.GetValue(value);
                }
                catch
                {
                    // If the getter throws, log a safe placeholder rather than crashing.
                    rawValue = "[UNREADABLE]";
                }

                logProperties.Add(new LogEventProperty(
                    prop.Name,
                    propertyValueFactory.CreatePropertyValue(rawValue, destructureObjects: true)));
            }
        }

        result = new StructureValue(logProperties);
        return true;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static HashSet<string> GetRedactedPropertyNames(Type type)
    {
        return _redactedPropertiesCache.GetOrAdd(type, t =>
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<PiiAttribute>();
                if (attr?.ExcludeFromLogs == true)
                    names.Add(prop.Name);
            }

            return names;
        });
    }
}
