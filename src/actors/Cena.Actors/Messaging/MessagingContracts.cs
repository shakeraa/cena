// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Messaging Context Contracts
// Layer: Domain Contracts | Runtime: .NET 9
// Enums, value objects, and DTOs for bidirectional human communication.
// ═══════════════════════════════════════════════════════════════════════

using System.ComponentModel.DataAnnotations;

namespace Cena.Actors.Messaging;

/// <summary>
/// Role of the message sender. Controls authorization and throttle limits.
/// </summary>
public enum MessageRole
{
    Teacher,
    Parent,
    Student,
    System
}

/// <summary>
/// Channel through which a message was sent or received.
/// </summary>
public enum MessageChannel
{
    InApp,
    WhatsApp,
    Telegram,
    Push
}

/// <summary>
/// Type of conversation thread.
/// </summary>
public enum ThreadType
{
    DirectMessage,
    ClassBroadcast,
    ParentThread
}

/// <summary>
/// Content payload for a message. Immutable value object.
/// Max 2000 characters enforced via DataAnnotations.
/// </summary>
public sealed record MessageContent(
    [property: MaxLength(2000)] string Text,
    string ContentType, // "text" | "resource-link" | "encouragement"
    string? ResourceUrl,
    Dictionary<string, string>? Metadata
);

/// <summary>
/// Result of content moderation check.
/// </summary>
public sealed record ModerationResult(bool Safe, string? Reason, string? Flag = null);

/// <summary>
/// Result of rate limit / throttle check.
/// </summary>
public sealed record ThrottleResult(bool Allowed, int RetryAfterSeconds = 0);

/// <summary>
/// Result of message classification (learning signal vs communication).
/// </summary>
public sealed record ClassificationResult(
    bool IsLearningSignal,
    string Intent, // "quiz-answer", "confirmation", "concept-question", "greeting", "resource-share", "general"
    double Confidence
);
