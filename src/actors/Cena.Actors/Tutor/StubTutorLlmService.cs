// =============================================================================
// Cena Platform — Stub Tutor LLM Service (STB-04b)
// Canned response templates with simulated streaming
// =============================================================================

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tutor;

/// <summary>
/// Simulated LLM response for tutoring. Yields tokens with realistic delays.
/// </summary>
public interface ITutorLlmService
{
    IAsyncEnumerable<string> StreamResponseAsync(
        string studentId,
        string threadId,
        string userMessage,
        IReadOnlyList<(string Role, string Content)> conversationHistory,
        CancellationToken ct = default);
}

/// <summary>
/// Stub implementation with canned templates. No external LLM calls.
/// </summary>
public sealed class StubTutorLlmService : ITutorLlmService
{
    // Canned response templates by keyword detection
    private static readonly List<(string[] Keywords, string[] Templates)> ResponseBank = new()
    {
        // Math / Algebra
        (new[] { "algebra", "equation", "solve", "x", "variable" }, new[]
        {
            "Let's work through this algebra problem step by step. First, identify what you're solving for. Then isolate the variable on one side of the equation.",
            "Great algebra question! Remember: whatever you do to one side of the equation, you must do to the other. Let's start by simplifying both sides.",
            "When solving equations, think of it as a balance scale. We want to keep it balanced while getting x all by itself on one side."
        }),

        // Geometry
        (new[] { "geometry", "triangle", "circle", "angle", "area", "volume" }, new[]
        {
            "Visualizing this geometrically will help. Let me guide you through identifying the key shapes and their properties.",
            "For geometry problems, always start by listing what you know: given angles, side lengths, and what you're trying to find.",
            "Great geometry question! Let's draw this out mentally and identify which formulas apply to the shapes involved."
        }),

        // Physics
        (new[] { "physics", "force", "motion", "velocity", "acceleration", "newton" }, new[]
        {
            "Physics is all about understanding the relationships between quantities. Let's identify the given variables and choose the right formula.",
            "Remember Newton's approach: draw a free-body diagram first! This helps visualize all the forces at play.",
            "Excellent physics question! Let's break this down using the fundamental principles. What conservation laws might apply here?"
        }),

        // Chemistry
        (new[] { "chemistry", "molecule", "reaction", "element", "compound", "periodic" }, new[]
        {
            "Chemistry is the study of matter and its changes. Let's identify the reactants and products in this scenario.",
            "For chemical reactions, balancing the equation is always our first step. Let's count atoms on each side.",
            "Great chemistry question! Consider the periodic trends and how they relate to the properties we're discussing."
        }),

        // Biology
        (new[] { "biology", "cell", "organism", "dna", "protein", "ecosystem" }, new[]
        {
            "Biology connects from molecules to ecosystems. Let's zoom into the right level of organization for this question.",
            "Living systems are beautifully complex! Let's trace the pathway from DNA to protein to understand this process.",
            "Great biology question! Think about the structure-function relationship that's central to understanding life."
        }),

        // Study / Learning
        (new[] { "study", "learn", "remember", "memorize", "review", "exam", "test" }, new[]
        {
            "Learning effectively is a skill! Spaced repetition and active recall are your best friends for long-term retention.",
            "For exam prep, I recommend the Feynman technique: explain the concept in simple terms as if teaching a friend.",
            "Great question about learning! Breaking complex topics into chunks and connecting them to what you already know works wonders."
        }),

        // Help / Confused
        (new[] { "help", "confused", "don't understand", "lost", "stuck", "difficult" }, new[]
        {
            "No worries at all! Learning involves confusion before clarity. Let's break this down into smaller, manageable pieces.",
            "I hear you! Sometimes a fresh perspective helps. Let me explain this concept from a different angle.",
            "Being stuck is part of the learning process! Let's identify exactly where the confusion starts and work from there."
        }),

        // Greeting
        (new[] { "hello", "hi", "hey", "good morning", "good afternoon" }, new[]
        {
            "Hello! I'm your AI tutor, ready to help with any subject. What would you like to learn about today?",
            "Hi there! Excited to help you on your learning journey. What topic shall we explore?",
            "Hey! Ready to dive into some learning? I'm here to guide you through any questions you have."
        }),

        // Default / General
        (new[] { "question", "how", "what", "why", "explain" }, new[]
        {
            "That's a thoughtful question! Let me walk you through the key concepts involved here.",
            "Great inquiry! Understanding the 'why' behind concepts makes learning stick. Let me explain.",
            "I love curious questions like this! Let's explore the answer together step by step.",
            "Excellent question! This touches on some fundamental principles. Let me break it down clearly."
        })
    };

    public async IAsyncEnumerable<string> StreamResponseAsync(
        string studentId,
        string threadId,
        string userMessage,
        IReadOnlyList<(string Role, string Content)> conversationHistory,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Select appropriate response template
        var response = SelectResponse(userMessage);

        // Split into words for token-like streaming
        var words = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Stream with simulated typing delay (20-80ms per word for natural feel)
        var random = new Random(threadId.GetHashCode(StringComparison.Ordinal) ^ DateTime.UtcNow.Millisecond);

        foreach (var word in words)
        {
            ct.ThrowIfCancellationRequested();

            // Yield word with trailing space (except last word)
            yield return word + " ";

            // Random delay 20-80ms
            var delay = random.Next(20, 80);
            await Task.Delay(delay, ct);
        }

        // Final punctuation if not present
        if (!char.IsPunctuation(response[^1]))
        {
            yield return ".";
        }
    }

    private static string SelectResponse(string userMessage)
    {
        var lowerMessage = userMessage.ToLowerInvariant();

        // Find matching keyword group
        foreach (var (keywords, templates) in ResponseBank)
        {
            if (keywords.Any(k => lowerMessage.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                // Random template from matching group
                var random = new Random();
                return templates[random.Next(templates.Length)];
            }
        }

        // Fallback to default
        var defaults = ResponseBank[^1].Templates;
        return defaults[new Random().Next(defaults.Length)];
    }
}
