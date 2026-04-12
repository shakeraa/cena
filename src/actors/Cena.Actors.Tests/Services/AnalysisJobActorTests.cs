// =============================================================================
// Cena Platform -- AnalysisJobActor Tests
// FIND-data-022: PascalCase event name regression tests
// =============================================================================

using Cena.Actors.Services;
using Cena.Actors.Events;
using Marten;

namespace Cena.Actors.Tests.Services;

public class AnalysisJobActorTests
{
    /// <summary>
    /// Regression test for FIND-data-022: Verifies that LoadAttempts uses the 
    /// correct snake_case event type alias (concept_attempted_v1) not PascalCase.
    /// </summary>
    [Fact]
    public void EventTypeName_UsesSnakeCaseAlias_NotPascalCase()
    {
        // This test verifies the bug fix: the event type name in the query
        // must be "concept_attempted_v1" (snake_case) not "ConceptAttempted_V1" (PascalCase)
        // because Marten stores events with snake_case aliases.
        
        var actorSource = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "../../../../../Cena.Actors/Services/AnalysisJobActor.cs"));
        
        // Should contain the snake_case version
        Assert.Contains("\"concept_attempted_v1\"", actorSource);
        
        // Should NOT contain the PascalCase version that caused the dead query
        Assert.DoesNotContain("\"ConceptAttempted_V1\"", actorSource);
    }

    /// <summary>
    /// Verifies that the ConceptAttempted_V1 event type alias is snake_case.
    /// This is a guard test to catch if the event naming convention changes.
    /// </summary>
    [Fact]
    public void ConceptAttemptedV1_EventTypeAlias_IsSnakeCase()
    {
        var eventType = typeof(ConceptAttempted_V1);
        var expectedAlias = "concept_attempted_v1";
        
        // Marten's default naming converts PascalCase to snake_case
        // This test documents the expected alias
        Assert.Equal("ConceptAttempted_V1", eventType.Name);
        
        // The alias used in event store queries should be snake_case
        // This is the convention Marten uses with default configuration
        var computedAlias = string.Concat(eventType.Name.Select((c, i) => 
            i > 0 && char.IsUpper(c) ? "_" + char.ToLower(c) : char.ToLower(c).ToString()));
        
        Assert.Equal(expectedAlias, computedAlias);
    }
}
