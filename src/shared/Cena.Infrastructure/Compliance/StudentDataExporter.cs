// =============================================================================
// Cena Platform -- Student Data Exporter
// SEC-003 / GDPR Article 20: Data portability export for student records.
//
// Produces a structured JSON document containing all student data held by
// the platform. PII fields are clearly labelled with their classification
// level and category so the export consumer (student / parent / DPO) can
// identify and act on each piece of data.
//
// The exporter is generic: it accepts any object, reflects its properties,
// and annotates fields decorated with [Pii]. The caller is responsible for
// loading the snapshot (e.g. StudentProfileSnapshot) and passing it in.
//
// Usage (from a compliance endpoint or admin service):
//
//   var snapshot = await session.LoadAsync<StudentProfileSnapshot>(studentId);
//   var export   = StudentDataExporter.Export(studentId, snapshot);
//   var json     = JsonSerializer.Serialize(export, JsonOptions.Web);
// =============================================================================

using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// A single field entry in the GDPR portability export, carrying the value alongside
/// its PII classification metadata.
/// </summary>
public sealed record ExportedField(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] object? Value,
    [property: JsonPropertyName("piiLevel")] string PiiLevel,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("requiresEncryption")] bool RequiresEncryption);

/// <summary>
/// Full portability export document produced for a single student.
/// </summary>
public sealed record StudentDataExport(
    [property: JsonPropertyName("exportedAt")] DateTimeOffset ExportedAt,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("profile")] IReadOnlyList<ExportedField> Profile,
    [property: JsonPropertyName("notice")] string Notice);

/// <summary>
/// Exports any persisted student data object for GDPR Article 20 portability requests.
/// PII fields are annotated in the output with their classification level and category.
/// Non-PII properties are included with PiiLevel = "None".
/// </summary>
public static class StudentDataExporter
{
    private const string SchemaVersion = "1.0";
    private const string PortabilityNotice =
        "This export was generated in response to a GDPR Article 20 data portability request. " +
        "Fields marked with a piiLevel indicate personal data held by Cena. " +
        "Retain securely and share only with the data subject or their authorised representative.";

    /// <summary>
    /// Builds a portability export from any student data object.
    /// </summary>
    /// <param name="studentId">The student's identifier (used as the document key).</param>
    /// <param name="dataObject">
    /// The student data instance to export. Must not be null.
    /// Typically a <c>StudentProfileSnapshot</c> loaded from Marten.
    /// </param>
    /// <param name="logger">Optional logger — logs field counts at Information level.</param>
    public static StudentDataExport Export(
        string studentId,
        object dataObject,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        ArgumentNullException.ThrowIfNull(dataObject);

        var profileFields = ExtractFields(dataObject);

        logger?.LogInformation(
            "SEC-003: GDPR portability export generated for student {StudentId}. " +
            "Fields={FieldCount}, PiiFields={PiiFieldCount}",
            studentId,
            profileFields.Count,
            profileFields.Count(f => f.PiiLevel != nameof(Compliance.PiiLevel.None)));

        return new StudentDataExport(
            ExportedAt: DateTimeOffset.UtcNow,
            StudentId: studentId,
            SchemaVersion: SchemaVersion,
            Profile: profileFields,
            Notice: PortabilityNotice);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Iterates all public properties of the data object and builds export records.
    /// Properties with a <see cref="PiiAttribute"/> carry full classification metadata;
    /// all others are included with PiiLevel = "None".
    /// </summary>
    private static List<ExportedField> ExtractFields(object dataObject)
    {
        var properties = dataObject.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var fields = new List<ExportedField>(properties.Length);

        foreach (var prop in properties)
        {
            object? rawValue;
            try
            {
                rawValue = prop.GetValue(dataObject);
            }
            catch
            {
                rawValue = null;
            }

            var piiAttr = prop.GetCustomAttribute<PiiAttribute>();

            if (piiAttr is not null)
            {
                fields.Add(new ExportedField(
                    Name: prop.Name,
                    Value: rawValue,
                    PiiLevel: piiAttr.Level.ToString(),
                    Category: piiAttr.Category,
                    RequiresEncryption: piiAttr.RequiresEncryption));
            }
            else
            {
                fields.Add(new ExportedField(
                    Name: prop.Name,
                    Value: rawValue,
                    PiiLevel: nameof(Compliance.PiiLevel.None),
                    Category: string.Empty,
                    RequiresEncryption: false));
            }
        }

        return fields;
    }
}
