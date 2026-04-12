#!/usr/bin/env dotnet-script
// =============================================================================
// Cena Platform — Backfill Question Explanations to Locale-Aware Format
// FIND-pedagogy-013 Migration Script
//
// Backfills legacy single-string Explanation and DistractorRationales into the
// new locale-aware dictionaries (ExplanationByLocale, DistractorRationalesByLocale).
//
// Usage:
//   dotnet script scripts/backfill-question-explanations.csx
//   dotnet script scripts/backfill-question-explanations.csx -- --dry-run
//
// Environment:
//   CENA_POSTGRES_CONNECTION - PostgreSQL connection string (required)
//   LOG_LEVEL - Log level: Debug|Info|Warning|Error (default: Info)
//   BATCH_SIZE - Documents per batch (default: 100)
// =============================================================================

#r "nuget: Marten, 8.0.0"
#r "nuget: Npgsql, 8.0.0"
#r "nuget: Serilog, 3.1.1"
#r "nuget: Serilog.Sinks.Console, 5.0.1"

using Marten;
using Npgsql;
using Serilog;
using Serilog.Events;
using System.Diagnostics;

// =============================================================================
// Document Model (minimal inline definition for script portability)
// =============================================================================

public class QuestionDocument
{
    public string Id { get; set; } = "";
    public string? Explanation { get; set; }
    public Dictionary<string, string>? ExplanationByLocale { get; set; }
    public Dictionary<string, string>? DistractorRationales { get; set; }
    public Dictionary<string, Dictionary<string, string>>? DistractorRationalesByLocale { get; set; }
}

// =============================================================================
// Configuration
// =============================================================================

public class BackfillConfig
{
    public string ConnectionString { get; set; } = "";
    public bool DryRun { get; set; }
    public int BatchSize { get; set; } = 100;
    public int ProgressInterval { get; set; } = 100;
    public LogEventLevel LogLevel { get; set; } = LogEventLevel.Information;
}

// =============================================================================
// Statistics
// =============================================================================

public class BackfillStats
{
    public int TotalDocuments { get; set; }
    public int ProcessedDocuments { get; set; }
    public int UpdatedDocuments { get; set; }
    public int SkippedDocuments { get; set; }
    public int ErrorCount { get; set; }
    public int ExplanationMigrations { get; set; }
    public int DistractorRationaleMigrations { get; set; }
    public Stopwatch Stopwatch { get; set; } = new();

    public void PrintSummary(ILogger logger)
    {
        logger.Information("═══════════════════════════════════════════════════════════════");
        logger.Information("  Backfill Complete");
        logger.Information("═══════════════════════════════════════════════════════════════");
        logger.Information("  Total documents scanned:    {TotalDocuments}", TotalDocuments);
        logger.Information("  Documents processed:        {ProcessedDocuments}", ProcessedDocuments);
        logger.Information("  Documents updated:          {UpdatedDocuments}", UpdatedDocuments);
        logger.Information("  Documents skipped:          {SkippedDocuments}", SkippedDocuments);
        logger.Information("  Errors encountered:         {ErrorCount}", ErrorCount);
        logger.Information("  Explanations migrated:      {ExplanationMigrations}", ExplanationMigrations);
        logger.Information("  Distractor sets migrated:   {DistractorRationaleMigrations}", DistractorRationaleMigrations);
        logger.Information("  Duration:                   {Duration:hh\:mm\:ss\.fff}", Stopwatch.Elapsed);
        logger.Information("  Throughput:                 {Throughput:F1} docs/sec", 
            ProcessedDocuments / Math.Max(Stopwatch.Elapsed.TotalSeconds, 0.001));
        logger.Information("═══════════════════════════════════════════════════════════════");
    }
}

// =============================================================================
// Argument Parser
// =============================================================================

public static BackfillConfig ParseArguments(string[] args)
{
    var config = new BackfillConfig();
    
    foreach (var arg in args)
    {
        if (arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
        {
            config.DryRun = true;
        }
        else if (arg.StartsWith("--batch-size=", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(arg.Substring("--batch-size=".Length), out var batchSize))
            {
                config.BatchSize = batchSize;
            }
        }
        else if (arg.StartsWith("--log-level=", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<LogEventLevel>(arg.Substring("--log-level=".Length), true, out var level))
            {
                config.LogLevel = level;
            }
        }
    }
    
    return config;
}

// =============================================================================
// Connection String Resolver
// =============================================================================

public static string? GetConnectionString(ILogger logger)
{
    // 1. CENA_POSTGRES_CONNECTION (highest priority - Cena convention)
    var envVar = Environment.GetEnvironmentVariable("CENA_POSTGRES_CONNECTION");
    if (!string.IsNullOrWhiteSpace(envVar))
    {
        logger.Debug("Connection string source: CENA_POSTGRES_CONNECTION environment variable");
        return envVar;
    }
    
    // 2. ConnectionStrings__cena (ASP.NET Core style)
    var connStrEnv = Environment.GetEnvironmentVariable("ConnectionStrings__cena");
    if (!string.IsNullOrWhiteSpace(connStrEnv))
    {
        logger.Debug("Connection string source: ConnectionStrings__cena environment variable");
        return connStrEnv;
    }
    
    // 3. DATABASE_URL (common container/DB convention)
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrWhiteSpace(databaseUrl))
    {
        logger.Debug("Connection string source: DATABASE_URL environment variable");
        return databaseUrl;
    }
    
    return null;
}

// =============================================================================
// Marten Configuration
// =============================================================================

public static void ConfigureMarten(StoreOptions opts, string connectionString)
{
    opts.Connection(connectionString);
    opts.AutoCreateSchemaObjects = AutoCreate.None; // Never modify schema in scripts
    opts.DatabaseSchemaName = "cena";
    
    // Use System.Text.Json with camelCase for consistency
    opts.UseSystemTextJsonForSerialization(
        enumStorage: EnumStorage.AsString,
        casing: Casing.CamelCase
    );
    
    // Register QuestionDocument with same configuration as the main app
    opts.Schema.For<QuestionDocument>()
        .Identity(x => x.Id);
}

// =============================================================================
// Main Backfill Logic
// =============================================================================

public static async Task<int> RunBackfill(BackfillConfig config, ILogger logger)
{
    var stats = new BackfillStats();
    stats.Stopwatch.Start();
    
    logger.Information("═══════════════════════════════════════════════════════════════");
    logger.Information("  Cena Question Explanation Backfill");
    logger.Information("═══════════════════════════════════════════════════════════════");
    logger.Information("  Mode: {Mode}", config.DryRun ? "DRY RUN (no changes)" : "LIVE");
    logger.Information("  Batch size: {BatchSize}", config.BatchSize);
    logger.Information("═══════════════════════════════════════════════════════════════");
    
    try
    {
        // Build document store
        using var store = DocumentStore.For(opts => ConfigureMarten(opts, config.ConnectionString));
        
        // Query documents needing migration
        logger.Information("Scanning for documents requiring migration...");
        
        await using (var session = store.QuerySession())
        {
            // Query: Explanation is not null AND (ExplanationByLocale is null OR empty)
            var query = session.Query<QuestionDocument>()
                .Where(q => q.Explanation != null);
            
            // Count total for progress reporting
            stats.TotalDocuments = await query.CountAsync();
            logger.Information("Found {Count} documents with legacy explanations", stats.TotalDocuments);
        }
        
        if (stats.TotalDocuments == 0)
        {
            logger.Information("No documents require migration. Exiting.");
            return 0;
        }
        
        // Process in batches
        var processedInBatch = 0;
        
        await using (var session = store.LightweightSession())
        {
            var batchQuery = session.Query<QuestionDocument>()
                .Where(q => q.Explanation != null);
            
            await foreach (var doc in batchQuery.ToAsyncEnumerable().WithCancellation(CancellationToken.None))
            {
                stats.ProcessedDocuments++;
                
                try
                {
                    var needsUpdate = false;
                    
                    // Check if explanation needs migration
                    // Idempotent: only migrate if ExplanationByLocale is null or doesn't have "en"
                    if (!string.IsNullOrEmpty(doc.Explanation))
                    {
                        doc.ExplanationByLocale ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        
                        if (!doc.ExplanationByLocale.ContainsKey("en"))
                        {
                            doc.ExplanationByLocale["en"] = doc.Explanation;
                            needsUpdate = true;
                            stats.ExplanationMigrations++;
                            logger.Debug("Migrated explanation for document {DocId}", doc.Id);
                        }
                    }
                    
                    // Check if distractor rationales need migration
                    if (doc.DistractorRationales?.Count > 0)
                    {
                        doc.DistractorRationalesByLocale ??= new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                        
                        if (!doc.DistractorRationalesByLocale.ContainsKey("en"))
                        {
                            // Clone the dictionary to avoid reference issues
                            doc.DistractorRationalesByLocale["en"] = new Dictionary<string, string>(doc.DistractorRationales, StringComparer.Ordinal);
                            needsUpdate = true;
                            stats.DistractorRationaleMigrations++;
                            logger.Debug("Migrated distractor rationales for document {DocId}", doc.Id);
                        }
                    }
                    
                    if (needsUpdate)
                    {
                        if (!config.DryRun)
                        {
                            session.Store(doc);
                        }
                        stats.UpdatedDocuments++;
                    }
                    else
                    {
                        stats.SkippedDocuments++;
                    }
                    
                    // Progress logging
                    processedInBatch++;
                    if (processedInBatch >= config.ProgressInterval)
                    {
                        logger.Information("Progress: {Processed}/{Total} documents processed ({Percent:F1}%)",
                            stats.ProcessedDocuments, stats.TotalDocuments,
                            100.0 * stats.ProcessedDocuments / stats.TotalDocuments);
                        
                        // Save batch if not dry run
                        if (!config.DryRun)
                        {
                            await session.SaveChangesAsync();
                            logger.Debug("Saved batch of {Count} changes", processedInBatch);
                        }
                        
                        processedInBatch = 0;
                    }
                }
                catch (Exception ex)
                {
                    stats.ErrorCount++;
                    logger.Error(ex, "Error processing document {DocId}", doc.Id);
                    
                    // Continue with next document, don't fail entire batch
                    if (stats.ErrorCount > 10)
                    {
                        logger.Error("Too many errors ({ErrorCount}), aborting", stats.ErrorCount);
                        break;
                    }
                }
            }
            
            // Save final batch
            if (!config.DryRun && processedInBatch > 0)
            {
                await session.SaveChangesAsync();
                logger.Debug("Saved final batch of {Count} changes", processedInBatch);
            }
        }
        
        stats.Stopwatch.Stop();
        stats.PrintSummary(logger);
        
        if (config.DryRun)
        {
            logger.Warning("DRY RUN completed. No changes were saved to the database.");
            logger.Information("Run without --dry-run to apply changes.");
        }
        
        return stats.ErrorCount > 0 ? 1 : 0;
    }
    catch (Exception ex)
    {
        stats.Stopwatch.Stop();
        logger.Fatal(ex, "Fatal error during backfill");
        return 1;
    }
}

// =============================================================================
// Entry Point
// =============================================================================

public static async Task<int> Main(string[] args)
{
    // Parse configuration
    var config = ParseArguments(args);
    
    // Configure logging
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Is(config.LogLevel)
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();
    
    var logger = Log.Logger;
    
    try
    {
        // Validate connection string
        var connectionString = GetConnectionString(logger);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.Error("ERROR: No PostgreSQL connection string provided.");
            logger.Error("");
            logger.Error("Please set one of the following environment variables:");
            logger.Error("  - CENA_POSTGRES_CONNECTION (recommended)");
            logger.Error("  - ConnectionStrings__cena");
            logger.Error("  - DATABASE_URL");
            logger.Error("");
            logger.Error("Example:");
            logger.Error("  export CENA_POSTGRES_CONNECTION=\"Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password\"");
            return 1;
        }
        
        config.ConnectionString = connectionString;
        
        // Log startup info
        logger.Debug("Arguments: {Args}", string.Join(" ", args));
        logger.Debug("Dry run: {DryRun}", config.DryRun);
        
        // Run the backfill
        return await RunBackfill(config, logger);
    }
    finally
    {
        await Log.CloseAndFlushAsync();
    }
}

// Execute
return await Main(Args.ToArray());
