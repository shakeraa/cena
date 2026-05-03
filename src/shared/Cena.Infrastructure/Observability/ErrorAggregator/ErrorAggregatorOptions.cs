// =============================================================================
// Cena Platform — Error Aggregator Options (RDY-064)
// Bound from appsettings.json → "ErrorAggregator" section.
// =============================================================================

namespace Cena.Infrastructure.Observability.ErrorAggregator;

public sealed class ErrorAggregatorOptions
{
    public const string SectionName = "ErrorAggregator";

    /// <summary>
    /// Master switch. When false, <see cref="NullErrorAggregator"/> is
    /// registered regardless of any other settings.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Backend identifier. One of: <c>null</c>, <c>sentry</c>,
    /// <c>appinsights</c>. The concrete implementation is selected by the
    /// hosting AddCenaErrorAggregator extension; unknown values fall back
    /// to null.
    /// </summary>
    public string Backend { get; set; } = "null";

    /// <summary>
    /// DSN / connection string for the backend. Empty forces null backend.
    /// Must be supplied via environment / secret, never committed.
    /// </summary>
    public string? Dsn { get; set; }

    /// <summary>
    /// Service environment (prod / staging / dev). Flows through as a tag.
    /// </summary>
    public string Environment { get; set; } = "dev";

    /// <summary>
    /// Release version string. Typically set from CENA_GIT_SHA at container
    /// build time. Defaults to "unknown" so unreleased builds still tag.
    /// </summary>
    public string Release { get; set; } = "unknown";

    /// <summary>
    /// Sampling rate for non-exception events (0.0 .. 1.0). Exceptions are
    /// always sent; this applies to breadcrumbs and messages.
    /// </summary>
    public double SampleRate { get; set; } = 1.0;

    /// <summary>
    /// When true, emulator / test events are tagged with
    /// <c>environment=emulator</c> so they can be filtered out of the
    /// production incident view.
    /// </summary>
    public bool TagEmulatorSeparately { get; set; } = true;
}
