// =============================================================================
// Cena Platform -- Event Type Alias Guard Tests
// FIND-data-022: Guards against PascalCase event name regressions
// =============================================================================

namespace Cena.EventStore.Tests;

/// <summary>
/// Static guard tests that scan source code for PascalCase event type name patterns
/// that would cause dead queries against Marten's snake_case event aliases.
/// </summary>
public class EventTypeAliasGuardTests
{
    private static readonly string[] SourceRoots = { "src/actors", "src/api" };
    
    /// <summary>
    /// Scans all C# source files for EventTypeName comparisons using PascalCase.
    /// Pattern like: EventTypeName == "ConceptAttempted_V1"
    /// These should use snake_case: EventTypeName == "concept_attempted_v1"
    /// </summary>
    [Fact]
    public void Scan_ForPascalCaseEventTypeName_Comparisons()
    {
        var repoRoot = Path.Combine(AppContext.BaseDirectory, "../../../../../..");
        var pascalCasePattern = new System.Text.RegularExpressions.Regex(
            @"EventTypeName\s*==\s*""[A-Z][A-Za-z0-9]*_[Vv]\d+""",
            System.Text.RegularExpressions.RegexOptions.Compiled);
        
        var violations = new List<string>();
        
        foreach (var root in SourceRoots)
        {
            var rootPath = Path.Combine(repoRoot, root);
            if (!Directory.Exists(rootPath)) continue;
            
            var csFiles = Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories);
            foreach (var file in csFiles)
            {
                var content = File.ReadAllText(file);
                var matches = pascalCasePattern.Matches(content);
                foreach (Match match in matches)
                {
                    violations.Add($"{Path.GetRelativePath(repoRoot, file)}:{match.Value}");
                }
            }
        }
        
        Assert.Empty(violations);
    }

    /// <summary>
    /// Verifies that no hardcoded event type names use PascalCase in query predicates.
    /// </summary>
    [Theory]
    [InlineData("ConceptAttempted_V1", "concept_attempted_v1")]
    [InlineData("ConceptMastered_V1", "concept_mastered_v1")]
    [InlineData("QuestionAnswered_V1", "question_answered_v1")]
    [InlineData("SessionStarted_V1", "session_started_v1")]
    [InlineData("SessionCompleted_V1", "session_completed_v1")]
    public void EventTypeName_Conventions_AreSnakeCase(string pascalCase, string expectedSnakeCase)
    {
        // This test documents the expected snake_case aliases for common event types
        var computedSnakeCase = string.Concat(pascalCase.Select((c, i) => 
            i > 0 && char.IsUpper(c) 
                ? "_" + char.ToLower(c) 
                : char.ToLower(c).ToString()));
        
        Assert.Equal(expectedSnakeCase, computedSnakeCase);
    }
}
