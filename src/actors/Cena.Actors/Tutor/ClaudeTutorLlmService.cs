// =============================================================================
// Cena Platform — Claude Tutor LLM Service (HARDEN TutorEndpoints)
// Real Anthropic Claude integration with simulated streaming
// =============================================================================

using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace Cena.Actors.Tutor;

/// <summary>
/// Production-grade Anthropic Claude integration for AI tutoring.
/// Uses the official Anthropic SDK (v12.9.0).
/// </summary>
public sealed class ClaudeTutorLlmService : ITutorLlmService
{
    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly string _systemPromptTemplate;
    private readonly ILogger<ClaudeTutorLlmService> _logger;

    public ClaudeTutorLlmService(
        IConfiguration configuration,
        ILogger<ClaudeTutorLlmService> logger)
    {
        _logger = logger;
        
        var apiKey = configuration["Cena:Llm:ApiKey"] 
            ?? throw new InvalidOperationException("Cena:Llm:ApiKey is required for ClaudeTutorLlmService");
        
        _client = new AnthropicClient { ApiKey = apiKey };
        _model = configuration["Cena:Llm:Model"] ?? "claude-sonnet-4-6";
        _systemPromptTemplate = configuration["Cena:Llm:SystemPromptTemplate"] 
            ?? GetDefaultSystemPrompt();
        
        _logger.LogInformation("ClaudeTutorLlmService initialized with model: {Model}", _model);
    }

    public async IAsyncEnumerable<LlmChunk> StreamCompletionAsync(
        TutorContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt(context);
        var messages = BuildMessages(context);
        
        _logger.LogDebug("Streaming completion for student {StudentId}, thread {ThreadId}", 
            context.StudentId, context.ThreadId);

        string fullText;
        int? totalTokens;
        bool hasError = false;
        
        try
        {
            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 1024,
                System = systemPrompt,
                Temperature = 0.7,
                Messages = messages
            }, ct);

            fullText = string.Join("", response.Content
                .Select(b => b.Value)
                .OfType<TextBlock>()
                .Select(b => b.Text));

            var inputTokens = response.Usage?.InputTokens ?? 0;
            var outputTokens = response.Usage?.OutputTokens ?? 0;
            totalTokens = (int)(inputTokens + outputTokens);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Streaming cancelled for thread {ThreadId}", context.ThreadId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming from Claude for thread {ThreadId}", context.ThreadId);
            fullText = "I'm sorry, I encountered an error. Please try again.";
            totalTokens = null;
            hasError = true;
        }

        if (hasError)
        {
            // Return error as a single chunk
            yield return new LlmChunk(
                Delta: fullText,
                Finished: true,
                TokensUsed: null,
                Model: _model
            );
            yield break;
        }

        // Simulate streaming by yielding word by word
        var words = fullText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            yield return new LlmChunk(
                Delta: word + " ",
                Finished: false,
                TokensUsed: null,
                Model: _model
            );
            await Task.Delay(20, ct); // Small delay for natural feel
        }

        // Final chunk with token usage
        yield return new LlmChunk(
            Delta: "",
            Finished: true,
            TokensUsed: totalTokens,
            Model: _model
        );
    }

    private string BuildSystemPrompt(TutorContext context)
    {
        var gradeInfo = context.CurrentGrade.HasValue 
            ? $"grade {context.CurrentGrade.Value}" 
            : "their current level";
        
        var subjectInfo = !string.IsNullOrEmpty(context.Subject) 
            ? context.Subject 
            : "the subject at hand";

        return _systemPromptTemplate
            .Replace("{{Grade}}", gradeInfo)
            .Replace("{{Subject}}", subjectInfo);
    }

    private List<MessageParam> BuildMessages(TutorContext context)
    {
        var messages = new List<MessageParam>();
        
        // Add conversation history (last 10 messages for context window)
        foreach (var msg in context.MessageHistory.TakeLast(10))
        {
            var role = msg.Role switch
            {
                "user" => "user",
                "assistant" => "assistant",
                _ => "user"
            };
            
            messages.Add(new MessageParam { Role = role, Content = msg.Content });
        }
        
        return messages;
    }

    private static string GetDefaultSystemPrompt()
    {
        return """
        You are a patient, encouraging AI tutor helping a student at {{Grade}} level with {{Subject}}.
        
        Guidelines:
        - Use the Socratic method: guide students to answers through questions, don't just give answers
        - Adapt explanations to the student's grade level
        - Be encouraging and supportive, especially when students struggle
        - Break complex problems into smaller, manageable steps
        - Use analogies and examples appropriate to the subject and age
        - If a student is stuck, offer hints rather than solutions
        - Celebrate progress and effort, not just correct answers
        - Keep responses concise but thorough (2-4 paragraphs typically)
        
        Current context: Tutoring session for {{Subject}}.
        """;
    }
}
