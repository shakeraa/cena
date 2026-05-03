// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Archival Manifest
// Layer: Domain Model | Runtime: .NET 9
// Tracks all archived threads per month with checksums.
// Stored at s3://cena-messages/{year}/{month}/manifest.json
// ═══════════════════════════════════════════════════════════════════════

using System.Text.Json.Serialization;

namespace Cena.Actors.Messaging.Archival;

public sealed class ArchivalManifest
{
    [JsonPropertyName("month")]
    public string Month { get; set; } = "";

    [JsonPropertyName("archivedAt")]
    public DateTimeOffset ArchivedAt { get; set; }

    [JsonPropertyName("threads")]
    public List<ArchivedThreadEntry> Threads { get; set; } = new();
}

public sealed class ArchivedThreadEntry
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = "";

    [JsonPropertyName("s3Key")]
    public string S3Key { get; set; } = "";

    [JsonPropertyName("messageCount")]
    public int MessageCount { get; set; }

    [JsonPropertyName("firstMessageAt")]
    public DateTimeOffset FirstMessageAt { get; set; }

    [JsonPropertyName("lastMessageAt")]
    public DateTimeOffset LastMessageAt { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";
}

/// <summary>
/// Key naming helpers for archived messages in blob storage.
/// </summary>
public static class ArchivalKeys
{
    /// <summary>
    /// Archive key: {year}/{month}/thread-{threadId}-{startDate}-{endDate}.jsonl.gz
    /// </summary>
    public static string ArchiveKey(string threadId, DateTimeOffset start, DateTimeOffset end) =>
        $"{start:yyyy}/{start:MM}/thread-{threadId}-{start:yyyyMMdd}-{end:yyyyMMdd}.jsonl.gz";

    /// <summary>
    /// Manifest key: {year}/{month}/manifest.json
    /// </summary>
    public static string ManifestKey(int year, int month) =>
        $"{year:D4}/{month:D2}/manifest.json";

    /// <summary>
    /// Manifest key for a specific date.
    /// </summary>
    public static string ManifestKey(DateTimeOffset date) =>
        ManifestKey(date.Year, date.Month);
}
