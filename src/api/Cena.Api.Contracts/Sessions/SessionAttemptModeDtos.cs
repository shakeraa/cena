// =============================================================================
// Cena Platform — Session attempt-mode DTOs (EPIC-PRR-F PRR-260)
//
// Wire shape for the hide-then-reveal toggle endpoints:
//   GET   /api/sessions/{id}/attempt-mode
//   PATCH /api/sessions/{id}/attempt-mode
//
// String wire values are `"visible"` or `"hidden_reveal"` — same wire
// constants as the SessionAttemptModeWire helper in Cena.Actors.Sessions.
// Keeping strings (rather than a JSON enum) keeps the wire stable if the
// enum ever gains new members; unknown values on read surface as the
// Visible default per the forward-compat rule documented in the
// endpoint's CanonicaliseWire helper.
// =============================================================================

namespace Cena.Api.Contracts.Sessions;

/// <summary>
/// Request body for PATCH /api/sessions/{id}/attempt-mode. The server
/// rejects unknown modes with a 400 + the allowed-values list so the
/// client can self-correct.
/// </summary>
/// <param name="Mode">One of <c>"visible"</c> or <c>"hidden_reveal"</c>.</param>
public sealed record SessionAttemptModeUpdateRequestDto(string Mode);

/// <summary>
/// Response from GET and PATCH /api/sessions/{id}/attempt-mode. Mode is
/// always the canonical wire form (lower_snake_case); client reads it
/// verbatim without additional normalisation.
/// </summary>
public sealed record SessionAttemptModeResponseDto(
    string SessionId,
    string Mode);
