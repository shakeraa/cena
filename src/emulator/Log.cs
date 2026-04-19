namespace Cena.Emulator;

static class Log
{
    public static void Information(string msg, params object[] args)
    {
        var formatted = args.Length > 0 ? FormatMsg(msg, args) : msg;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss} INF] {formatted}");
    }

    public static void Warning(string msg, params object[] args)
    {
        var formatted = args.Length > 0 ? FormatMsg(msg, args) : msg;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss} WRN] {formatted}");
    }

    /// <summary>
    /// Serilog-style overload that prints the full exception + stack trace
    /// alongside the warning. Keep this explicit so the homegrown
    /// formatter doesn't coerce the Exception into a template arg.
    /// </summary>
    public static void Warning(Exception ex, string msg, params object[] args)
    {
        var formatted = args.Length > 0 ? FormatMsg(msg, args) : msg;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss} WRN] {formatted}");
        Console.WriteLine(ex.ToString());
    }

    static string FormatMsg(string template, object[] args)
    {
        var result = template;
        foreach (var arg in args)
        {
            var idx = result.IndexOf('{');
            if (idx < 0) break;
            var end = result.IndexOf('}', idx);
            if (end < 0) break;
            result = result[..idx] + arg?.ToString() + result[(end + 1)..];
        }
        return result;
    }
}
