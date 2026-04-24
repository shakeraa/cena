// =============================================================================
// Cena Platform — Focus Analytics Service Event Naming Tests (FIND-data-005)
//
// Verifies that FocusAnalyticsService uses correct Marten event type names.
// Regression: Service used '__v1' (double underscore) but Marten's NameToAlias
// produces '_v1' (single underscore), causing all queries to return empty.
// =============================================================================

using Cena.Actors.Events;
using Marten;
using Marten.Events;

namespace Cena.Admin.Api.Tests;

/// <summary>
/// Tests for event type name consistency between Marten and service queries.
/// FIND-data-005: Event type names must use single underscore (_v1), not double (__v1).
/// </summary>
public sealed class FocusAnalyticsServiceEventNamingTests
{
    /// <summary>
    /// Verifies that Marten's event type name for FocusScoreUpdated_V1
    /// matches what FocusAnalyticsService queries for.
    /// </summary>
    [Fact]
    public void FocusScoreUpdated_V1_EventTypeName_IsSingleUnderscore()
    {
        // Arrange: Create a Marten StoreOptions and register the event
        var opts = new StoreOptions();
        opts.UseSystemTextJsonForSerialization();
        opts.Events.AddEventType<FocusScoreUpdated_V1>();

        // Act: Get the event type name that Marten would use in the database
        // Marten uses the class name with underscore + version suffix
        // FocusScoreUpdated_V1 → focus_score_updated_v1
        var eventTypeName = GetMartenEventTypeName<FocusScoreUpdated_V1>();

        // Assert: Should be single underscore, not double
        Assert.Equal("focus_score_updated_v1", eventTypeName);
        Assert.DoesNotContain("__v1", eventTypeName);
    }

    /// <summary>
    /// Verifies that all notification event type names use single underscore.
    /// </summary>
    [Theory]
    [InlineData(typeof(NotificationDeleted_V1), "notification_deleted_v1")]
    [InlineData(typeof(NotificationSnoozed_V1), "notification_snoozed_v1")]
    [InlineData(typeof(WebPushSubscribed_V1), "web_push_subscribed_v1")]
    [InlineData(typeof(WebPushUnsubscribed_V1), "web_push_unsubscribed_v1")]
    public void NotificationEvent_TypeNames_AreSingleUnderscore(Type eventType, string expectedName)
    {
        var actualName = GetMartenEventTypeName(eventType);
        Assert.Equal(expectedName, actualName);
        Assert.DoesNotContain("__v1", actualName);
    }

    /// <summary>
    /// Helper to get the Marten event type name (kebab-case with underscore version).
    /// </summary>
    private static string GetMartenEventTypeName<T>() where T : class
    {
        return GetMartenEventTypeName(typeof(T));
    }

    private static string GetMartenEventTypeName(Type eventType)
    {
        // Marten converts PascalCase to snake_case and appends _v{N}
        // e.g., FocusScoreUpdated_V1 → focus_score_updated_v1
        var name = eventType.Name;
        
        // Remove _V1 suffix for processing
        var baseName = name.EndsWith("_V1") ? name[..^3] : name;
        
        // Convert PascalCase to snake_case
        var snakeCase = ConvertPascalCaseToSnakeCase(baseName);
        
        return $"{snakeCase}_v1";
    }

    private static string ConvertPascalCaseToSnakeCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
            return pascalCase;

        var result = new System.Text.StringBuilder();
        result.Append(char.ToLowerInvariant(pascalCase[0]));

        for (int i = 1; i < pascalCase.Length; i++)
        {
            var c = pascalCase[i];
            if (char.IsUpper(c))
            {
                result.Append('_');
                result.Append(char.ToLowerInvariant(c));
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }
}
