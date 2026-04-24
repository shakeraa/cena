using Cena.Infrastructure.Compliance;

namespace Cena.Actors.Events;

/// <summary>
/// RDY-004a: emitted when a question is served in a locale different from the
/// student's requested locale because that translation is unavailable.
/// </summary>
[MlExcluded("RDY-004a: translation-gap analytics only")]
public sealed record QuestionFallbackLanguage_V1(
    string StudentId,
    string SessionId,
    string QuestionId,
    string RequestedLocale,
    string ServedLocale,
    DateTimeOffset Timestamp
) : IDelegatedEvent;
