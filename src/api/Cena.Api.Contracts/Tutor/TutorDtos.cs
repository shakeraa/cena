// =============================================================================
// Cena Platform -- Tutor API DTOs (STB-04 Phase 1)
// AI tutor thread and message contracts
// =============================================================================

namespace Cena.Api.Contracts.Tutor;

/// <summary>Tutor thread list response (STB-04)</summary>
public record TutorThreadListDto(
    TutorThreadDto[] Items,
    int TotalCount);

/// <summary>Tutor thread DTO (STB-04)</summary>
public record TutorThreadDto(
    string ThreadId,
    string Title,
    string? Subject,
    string? Topic,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int MessageCount,
    bool IsArchived);

/// <summary>Tutor message list response (STB-04)</summary>
public record TutorMessageListDto(
    string ThreadId,
    TutorMessageDto[] Messages,
    bool HasMore);

/// <summary>Tutor message DTO (STB-04)</summary>
public record TutorMessageDto(
    string MessageId,
    string Role,        // 'user' | 'assistant' | 'system'
    string Content,
    DateTime CreatedAt,
    string? Model);     // For assistant messages

/// <summary>Send message request (STB-04)</summary>
public record SendMessageRequest(
    string Content);

/// <summary>Send message response (STB-04)</summary>
public record SendMessageResponse(
    string MessageId,
    string Role,
    string Content,
    DateTime CreatedAt,
    string Status);     // 'complete' | 'streaming' (Phase 1: always 'complete')

/// <summary>Create thread request (STB-04)</summary>
public record CreateThreadRequest(
    string? Title,
    string? Subject,
    string? Topic,
    string? InitialMessage);

/// <summary>Create thread response (STB-04)</summary>
public record CreateThreadResponse(
    string ThreadId,
    string Title,
    DateTime CreatedAt);
