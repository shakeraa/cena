// =============================================================================
// Cena Platform — DbAdmin CLI (RDY-036)
//
// Pre-pilot maintenance tool. Currently supports:
//   wipe-questions --confirm "I UNDERSTAND"
//
// Safety rails:
//   - Requires env CENA_ALLOW_PREPILOT_WIPE=true
//   - Requires exact confirm phrase
//   - Logs every action
// =============================================================================

using Cena.Tools.DbAdmin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("DbAdmin");

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Cena.Tools.DbAdmin <command> [options]");
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  wipe-questions --confirm \"I UNDERSTAND\"");
    Console.Error.WriteLine("  syllabus-ingest --manifest <path.yaml> [--author <id>] [--prune]");
    return 2;
}

var command = args[0];
var rest = args.Skip(1).ToArray();

try
{
    return command switch
    {
        "wipe-questions" => await WipeQuestionsCommand.RunAsync(rest, config, logger),
        "syllabus-ingest" => await SyllabusIngestCommand.RunAsync(rest, config, logger),
        _ => Unknown(command)
    };
}
catch (Exception ex)
{
    logger.LogCritical(ex, "[DBADMIN_FATAL] command={Command}", command);
    return 1;
}

static int Unknown(string c)
{
    Console.Error.WriteLine($"Unknown command: {c}");
    return 2;
}
