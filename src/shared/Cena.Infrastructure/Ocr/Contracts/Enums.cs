// =============================================================================
// Cena Platform — OCR Cascade Contract Enums (ADR-0033)
//
// String-valued enums so JSON serialisation round-trips with the Python
// reference implementation. Every string here MUST match the lowercase value
// in scripts/ocr-spike/runners/base.py — dev-fixtures JSON is the contract.
// =============================================================================

using System.Text.Json.Serialization;

namespace Cena.Infrastructure.Ocr.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<Language>))]
public enum Language
{
    [JsonStringEnumMemberName("he")] Hebrew,
    [JsonStringEnumMemberName("en")] English,
    [JsonStringEnumMemberName("ar")] Arabic,
    [JsonStringEnumMemberName("unknown")] Unknown,
}

[JsonConverter(typeof(JsonStringEnumConverter<SourceType>))]
public enum SourceType
{
    [JsonStringEnumMemberName("student_photo")] StudentPhoto,
    [JsonStringEnumMemberName("student_pdf")] StudentPdf,
    [JsonStringEnumMemberName("bagrut_reference")] BagrutReference,
    [JsonStringEnumMemberName("admin_upload")] AdminUpload,
    [JsonStringEnumMemberName("cloud_dir")] CloudDir,
}

[JsonConverter(typeof(JsonStringEnumConverter<Track>))]
public enum Track
{
    [JsonStringEnumMemberName("3u")] Units3,
    [JsonStringEnumMemberName("4u")] Units4,
    [JsonStringEnumMemberName("5u")] Units5,
    [JsonStringEnumMemberName("unknown")] Unknown,
}

[JsonConverter(typeof(JsonStringEnumConverter<PdfTriageVerdict>))]
public enum PdfTriageVerdict
{
    [JsonStringEnumMemberName("text")] Text,
    [JsonStringEnumMemberName("image_only")] ImageOnly,
    [JsonStringEnumMemberName("mixed")] Mixed,
    [JsonStringEnumMemberName("scanned_bad_ocr")] ScannedBadOcr,
    [JsonStringEnumMemberName("encrypted")] Encrypted,
    [JsonStringEnumMemberName("unreadable")] Unreadable,
}
