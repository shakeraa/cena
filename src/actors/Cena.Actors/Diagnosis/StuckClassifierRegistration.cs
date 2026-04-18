// =============================================================================
// Cena Platform — StuckClassifierRegistration (RDY-063 Phase 1)
//
// DI wiring. The LLM backend selection is derived from config:
//   Cena:Llm:ApiKey unset   → NullStuckClassifierLlm (heuristic-only hybrid)
//   Cena:Llm:ApiKey present → ClaudeStuckClassifierLlm
//
// Feature flag (Cena:StuckClassifier:Enabled) is checked at call time
// by HybridStuckClassifier — no conditional registration. This lets
// operators flip the flag at runtime via IOptionsMonitor without a
// restart.
// =============================================================================

using Anthropic;
using Cena.Actors.RateLimit;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Diagnosis;

public static class StuckClassifierRegistration
{
    public static IServiceCollection AddStuckClassifier(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ── Options ─────────────────────────────────────────────────────
        services.AddOptions<StuckClassifierOptions>()
            .Bind(configuration.GetSection(StuckClassifierOptions.SectionName))
            .PostConfigure(opts =>
            {
                // Non-dev environments MUST set AnonSalt via config/secret.
                // We detect "non-dev" via ASPNETCORE_ENVIRONMENT — if it's
                // Production/Staging/Test and the salt is blank, throw.
                if (string.IsNullOrEmpty(opts.AnonSalt))
                {
                    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
                    var isDev = string.IsNullOrEmpty(env)
                             || env.Equals("Development", StringComparison.OrdinalIgnoreCase);
                    if (!isDev)
                    {
                        throw new InvalidOperationException(
                            "Cena:StuckClassifier:AnonSalt must be set in non-Development environments. " +
                            "Generate with: openssl rand -hex 32");
                    }
                    // Dev default — fixed so tests are deterministic.
                    opts.AnonSalt = "cena-stuck-classifier-dev-salt-v1";
                }
            });

        // Provide the raw options value as a singleton for components
        // that don't want IOptionsMonitor. Kept in sync via IOptions.
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<StuckClassifierOptions>>().Value);

        // ── Metrics ────────────────────────────────────────────────────
        services.AddSingleton<StuckClassifierMetrics>();

        // ── Anonymizer ─────────────────────────────────────────────────
        services.AddSingleton<IStuckAnonymizer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<StuckClassifierOptions>>().Value;
            return new StuckAnonymizer(opts.AnonSalt);
        });
        services.AddSingleton<IStuckContextBuilder, StuckContextBuilder>();

        // ── Classifier components ──────────────────────────────────────
        services.AddSingleton<HeuristicStuckClassifier>();

        // LLM client selection. If no API key is set, wire a null backend
        // so the hybrid composer degrades gracefully to heuristic-only.
        services.AddSingleton<IStuckClassifierLlm>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var apiKey = cfg["Cena:Llm:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                return new NullStuckClassifierLlm();
            }

            var logger = sp.GetRequiredService<ILogger<ClaudeStuckClassifierLlm>>();
            var opts = sp.GetRequiredService<IOptions<StuckClassifierOptions>>();
            var client = new AnthropicClient { ApiKey = apiKey };
            return new ClaudeStuckClassifierLlm(client, opts, logger);
        });

        // The LlmStuckClassifier adapter depends on IStuckClassifierLlm +
        // the per-call cost breaker (optional).
        services.AddSingleton(sp =>
        {
            var llm = sp.GetRequiredService<IStuckClassifierLlm>();
            var opts = sp.GetRequiredService<StuckClassifierOptions>();
            var logger = sp.GetRequiredService<ILogger<LlmStuckClassifier>>();
            var breaker = sp.GetService<ICostCircuitBreaker>();
            return new LlmStuckClassifier(llm, opts, logger, breaker);
        });

        // Finally, the hybrid composer — this is what the hint ladder
        // (and future RDY-062 v2) will depend on.
        services.AddSingleton<IStuckTypeClassifier>(sp =>
        {
            var heuristic = sp.GetRequiredService<HeuristicStuckClassifier>();
            var llm = sp.GetRequiredService<LlmStuckClassifier>();
            var optsMonitor = sp.GetRequiredService<IOptionsMonitor<StuckClassifierOptions>>();
            var logger = sp.GetRequiredService<ILogger<HybridStuckClassifier>>();
            var metrics = sp.GetRequiredService<StuckClassifierMetrics>();
            return new HybridStuckClassifier(heuristic, llm, optsMonitor, logger, metrics);
        });

        // ── Repository ─────────────────────────────────────────────────
        services.AddSingleton<IStuckDiagnosisRepository, MartenStuckDiagnosisRepository>();

        return services;
    }
}
