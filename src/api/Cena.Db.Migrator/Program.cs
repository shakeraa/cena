// =============================================================================
// Cena Platform — Database Migrator (DB-02)
// DbUp-based console application for PostgreSQL schema migrations
// =============================================================================

using System.Reflection;
using DbUp;
using DbUp.Engine;
using Npgsql;

namespace Cena.Db.Migrator;

public static class Program
{
    public static int Main(string[] args)
    {
        var logger = new ConsoleLogger();
        
        logger.WriteInfo("═══════════════════════════════════════════════════════════════");
        logger.WriteInfo("  Cena Database Migrator — DbUp PostgreSQL Migration Runner");
        logger.WriteInfo("═══════════════════════════════════════════════════════════════");
        logger.WriteInfo("");

        // Determine connection string
        var connectionString = GetConnectionString(logger, args);
        if (string.IsNullOrEmpty(connectionString))
        {
            logger.WriteError("ERROR: No connection string provided.");
            logger.WriteInfo("");
            logger.WriteInfo("Usage: Cena.Db.Migrator <connection_string>");
            logger.WriteInfo("   or: Set CENA_MIGRATOR_CONNECTION_STRING environment variable");
            logger.WriteInfo("   or: Set ConnectionStrings__cena_migrator environment variable");
            return 1;
        }

        // Mask password for logging
        var displayConnectionString = MaskConnectionString(connectionString);
        logger.WriteInfo($"Connection: {displayConnectionString}");
        logger.WriteInfo("");

        try
        {
            // Ensure search path includes cena schema for vector extension
            connectionString = AppendSearchPath(connectionString, "cena,public");

            // Test connection first
            if (!TestConnection(connectionString, logger))
            {
                return 1;
            }

            // Configure DbUp
            var assembly = Assembly.GetExecutingAssembly();
            
            var upgrader = DeployChanges.To
                .PostgresqlDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(assembly, script => 
                {
                    // Only include scripts from db/migrations folder
                    // Resource names are like: Cena.Db.Migrator.V0001__....sql
                    return script.Contains(".V") && script.EndsWith(".sql");
                })
                .JournalToPostgresqlTable("cena", "schemaversions")
                // Note: Without transaction - some DDL like CREATE EXTENSION cannot run in a transaction
                .LogToConsole()
                .Build();

            // Check if upgrade is needed
            var scriptsToExecute = upgrader.GetScriptsToExecute();
            if (scriptsToExecute.Count == 0)
            {
                logger.WriteInfo("✓ Database is up to date. No migrations to apply.");
                logger.WriteInfo("");
                return 0;
            }

            logger.WriteInfo($"Found {scriptsToExecute.Count} migration(s) to apply:");
            foreach (var script in scriptsToExecute)
            {
                logger.WriteInfo($"  - {script.Name}");
            }
            logger.WriteInfo("");

            // Execute migrations
            var result = upgrader.PerformUpgrade();

            if (result.Successful)
            {
                logger.WriteInfo("═══════════════════════════════════════════════════════════════");
                logger.WriteInfo("  ✓ Migrations completed successfully");
                logger.WriteInfo("═══════════════════════════════════════════════════════════════");
                logger.WriteInfo("");
                logger.WriteInfo($"Applied {scriptsToExecute.Count} migration(s).");
                logger.WriteInfo("");
                return 0;
            }
            else
            {
                logger.WriteError("═══════════════════════════════════════════════════════════════");
                logger.WriteError("  ✗ Migration failed");
                logger.WriteError("═══════════════════════════════════════════════════════════════");
                logger.WriteError("");
                logger.WriteError($"Error: {result.Error.Message}");
                logger.WriteError("");
                return 1;
            }
        }
        catch (Exception ex)
        {
            logger.WriteError("═══════════════════════════════════════════════════════════════");
            logger.WriteError("  ✗ Unexpected error");
            logger.WriteError("═══════════════════════════════════════════════════════════════");
            logger.WriteError("");
            logger.WriteError($"Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                logger.WriteError($"Inner: {ex.InnerException.Message}");
            }
            logger.WriteError("");
            return 1;
        }
    }

    private static string? GetConnectionString(ConsoleLogger logger, string[] args)
    {
        // 1. Command line argument (highest priority)
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            logger.WriteInfo("Connection string source: command line argument");
            return args[0];
        }

        // 2. CENA_MIGRATOR_CONNECTION_STRING environment variable
        var envVar = Environment.GetEnvironmentVariable("CENA_MIGRATOR_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(envVar))
        {
            logger.WriteInfo("Connection string source: CENA_MIGRATOR_CONNECTION_STRING environment variable");
            return envVar;
        }

        // 3. ConnectionStrings__cena_migrator (ASP.NET Core style)
        var connStrEnv = Environment.GetEnvironmentVariable("ConnectionStrings__cena_migrator");
        if (!string.IsNullOrWhiteSpace(connStrEnv))
        {
            logger.WriteInfo("Connection string source: ConnectionStrings__cena_migrator environment variable");
            return connStrEnv;
        }

        return null;
    }

    private static bool TestConnection(string connectionString, ConsoleLogger logger)
    {
        try
        {
            logger.WriteInfo("Testing database connection...");
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            
            using var cmd = new NpgsqlCommand("SELECT version();", connection);
            var version = cmd.ExecuteScalar()?.ToString();
            
            logger.WriteInfo($"✓ Connected to PostgreSQL: {version?.Substring(0, Math.Min(50, version?.Length ?? 0))}...");
            logger.WriteInfo("");
            return true;
        }
        catch (Exception ex)
        {
            logger.WriteError($"✗ Failed to connect to database: {ex.Message}");
            logger.WriteInfo("");
            return false;
        }
    }

    private static string MaskConnectionString(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(builder.Password))
            {
                builder.Password = "***";
            }
            return builder.ToString();
        }
        catch
        {
            // If parsing fails, do a simple regex replace
            return System.Text.RegularExpressions.Regex.Replace(
                connectionString, 
                @"(Password|Pwd)=[^;]*", 
                "$1=***", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }

    private static string AppendSearchPath(string connectionString, string searchPath)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            // Set or append to existing search path
            if (string.IsNullOrEmpty(builder.SearchPath))
            {
                builder.SearchPath = searchPath;
            }
            else if (!builder.SearchPath.Contains(searchPath))
            {
                builder.SearchPath = $"{searchPath},{builder.SearchPath}";
            }
            return builder.ToString();
        }
        catch
        {
            // If parsing fails, append to the connection string
            if (connectionString.Contains("SearchPath="))
            {
                return connectionString;
            }
            return $"{connectionString};SearchPath={searchPath}";
        }
    }
}

/// <summary>
/// Simple console logger for DbUp
/// </summary>
public class ConsoleLogger : DbUp.Engine.Output.IUpgradeLog
{
    public void WriteInformation(string format, params object[] args)
    {
        Console.WriteLine(format, args);
    }

    public void WriteError(string format, params object[] args)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(format, args);
        Console.ForegroundColor = originalColor;
    }

    public void WriteWarning(string format, params object[] args)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(format, args);
        Console.ForegroundColor = originalColor;
    }

    // Convenience methods for internal use
    public void WriteInfo(string message)
    {
        Console.WriteLine(message);
    }
}
