namespace CodexTray.Core;

public interface ISanitizedLogger
{
    void Info(string eventCode, string? safeDetail = null);

    void Warning(string eventCode, string? safeDetail = null);

    void Error(string eventCode, Exception? exception = null);
}

public sealed class NullSanitizedLogger : ISanitizedLogger
{
    public static NullSanitizedLogger Instance { get; } = new();

    public void Info(string eventCode, string? safeDetail = null) { }

    public void Warning(string eventCode, string? safeDetail = null) { }

    public void Error(string eventCode, Exception? exception = null) { }
}

public sealed class SanitizedFileLogger : ISanitizedLogger
{
    private const long MaximumLogBytes = 1_000_000;
    private readonly string _path;
    private readonly object _gate = new();

    public SanitizedFileLogger(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "codextray.log");
    }

    public void Info(string eventCode, string? safeDetail = null) => Write("INFO", eventCode, safeDetail);

    public void Warning(string eventCode, string? safeDetail = null) => Write("WARN", eventCode, safeDetail);

    public void Error(string eventCode, Exception? exception = null) =>
        Write("ERROR", eventCode, exception?.GetType().Name);

    private void Write(string level, string eventCode, string? safeDetail)
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > MaximumLogBytes)
                {
                    File.Move(_path, _path + ".previous", true);
                }

                var safeCode = Sanitize(eventCode, 64);
                var safeText = Sanitize(safeDetail, 256);
                File.AppendAllText(
                    _path,
                    $"{DateTimeOffset.Now:O} {level} {safeCode}{(safeText.Length > 0 ? " " + safeText : string.Empty)}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Logging must never make the tray application fail.
            }
        }
    }

    private static string Sanitize(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var chars = value
            .Take(maximumLength)
            .Select(character => char.IsControl(character) ? ' ' : character)
            .ToArray();
        return new string(chars).Replace("Bearer ", "[redacted] ", StringComparison.OrdinalIgnoreCase);
    }
}
