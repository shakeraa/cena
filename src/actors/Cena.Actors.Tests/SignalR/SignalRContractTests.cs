// =============================================================================
// Cena Platform -- SignalR Event Contract Tests (FIND-arch-009)
// Verifies alignment between NatsSignalRBridge events and student web UI subscriptions.
// This test fails if the two sides drift.
// =============================================================================

using System.Reflection;
using Cena.Actors.Bus;
using Cena.Api.Contracts.Hub;
using Xunit;

namespace Cena.Actors.Tests.SignalR;

public class SignalRContractTests
{
    /// <summary>
    /// Events the student web UI subscribes to (src/student/full-version/src/api/signalr.ts:78-90).
    /// If this list changes, update the UI subscription array AND this test.
    /// </summary>
    public static readonly string[] UiSubscribedEvents =
    [
        NatsSubjects.StudentSessionStarted,
        NatsSubjects.StudentSessionEnded,
        NatsSubjects.StudentAnswerEvaluated,
        NatsSubjects.StudentMasteryUpdated,
        NatsSubjects.StudentHintDelivered,
        NatsSubjects.StudentXpAwarded,
        NatsSubjects.StudentStreakUpdated,
        NatsSubjects.StudentBadgeEarned,
        NatsSubjects.StudentTutorMessage,
    ];

    /// <summary>
    /// Events the bridge emits that are hub-caller-only (not subscribed in UI v1).
    /// Used by: Admin dashboard, analytics, future mobile app.
    /// </summary>
    public static readonly string[] HubCallerOnlyEvents =
    [
        NatsSubjects.StudentMethodologySwitched,
        NatsSubjects.StudentStagnationDetected,
        NatsSubjects.StudentTutoringStarted,
        NatsSubjects.StudentTutoringEnded,
    ];

    [Fact]
    public void AllUiSubscribedEvents_HaveServerSideContracts()
    {
        // Every UI-subscribed event must have:
        // 1. A NatsSubjects constant
        // 2. An ICenaClient method
        // 3. An Event DTO in HubContracts.cs

        var clientMethods = typeof(ICenaClient).GetMethods()
            .Select(m => m.Name)
            .ToHashSet();

        var eventTypes = typeof(ICenaClient).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Event") && t.IsClass && !t.IsAbstract)
            .Select(t => t.Name)
            .ToHashSet();

        foreach (var eventName in UiSubscribedEvents)
        {
            // Map snake_case event name to PascalCase method name
            var pascalName = ToPascalCase(eventName);
            
            Assert.True(clientMethods.Contains(pascalName),
                $"UI subscribes to '{eventName}' but ICenaClient has no '{pascalName}' method. " +
                "Add the method to ICenaClient or remove from UI subscription list.");

            Assert.True(eventTypes.Contains(pascalName + "Event"),
                $"UI subscribes to '{eventName}' but no '{pascalName}Event' DTO found. " +
                "Add the DTO to HubContracts.cs or remove from UI subscription list.");
        }
    }

    [Fact]
    public void AllHubCallerOnlyEvents_HaveDocumentation()
    {
        // Hub-caller-only events must have explicit comments documenting their usage
        // This is a soft check - we verify the constants exist and are categorized
        
        var allCategorizedEvents = UiSubscribedEvents
            .Concat(HubCallerOnlyEvents)
            .ToHashSet();

        var allStudentEvents = typeof(NatsSubjects).GetFields()
            .Where(f => f.IsPublic && f.IsStatic && f.Name.StartsWith("Student") && f.FieldType == typeof(string))
            .Select(f => f.GetValue(null)?.ToString())
            .Where(v => v != null)
            .ToHashSet();

        // Every Student* constant should be categorized
        foreach (var eventName in allStudentEvents)
        {
            Assert.True(allCategorizedEvents.Contains(eventName!),
                $"NatsSubjects event '{eventName}' is not categorized. " +
                "Add to UiSubscribedEvents or HubCallerOnlyEvents in this test.");
        }
    }

    [Fact]
    public void BadgeEarned_IsFullyWired()
    {
        // Specific regression test for FIND-arch-009
        // BadgeEarned was missing from server-side contracts
        
        Assert.Contains(NatsSubjects.StudentBadgeEarned, UiSubscribedEvents);
        
        var clientMethods = typeof(ICenaClient).GetMethods().Select(m => m.Name).ToHashSet();
        Assert.Contains("BadgeEarned", clientMethods);
        
        var eventType = typeof(BadgeEarnedEvent);
        Assert.NotNull(eventType);
        Assert.Equal("BadgeEarnedEvent", eventType.Name);
    }

    private static string ToPascalCase(string snakeCase)
    {
        return string.Concat(
            snakeCase.Split('_')
                .Select(part => part.Length > 0 
                    ? char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant() 
                    : ""));
    }
}
