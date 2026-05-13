namespace HSMS.LegacyMigrator.Logging;

internal enum LogLevel
{
    Info,
    Warn,
    Error,
}

internal static class MigrationLog
{
    private static readonly object Sync = new();
    private static StreamWriter? _writer;
    private static string? _logPath;

    public static string? LogPath => _logPath;

    public static void Initialize(string logsDirectory, Guid runId)
    {
        Directory.CreateDirectory(logsDirectory);
        _logPath = Path.Combine(logsDirectory, $"migration_{runId:N}.log");
        _writer = new StreamWriter(_logPath, append: true)
        {
            AutoFlush = true,
        };
        Write(LogLevel.Info, $"=== Migration run {runId} started at {DateTime.UtcNow:O} ===");
    }

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Close(string status)
    {
        Write(LogLevel.Info, $"=== Migration run completed: {status} at {DateTime.UtcNow:O} ===");
        lock (Sync)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static void Write(LogLevel level, string message)
    {
        var line = $"[{DateTime.UtcNow:O}] [{level,-5}] {message}";
        lock (Sync)
        {
            Console.WriteLine(line);
            _writer?.WriteLine(line);
        }
    }
}
