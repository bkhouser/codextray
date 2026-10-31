namespace CodexTray.Core;

public sealed record CodexDiscoveryResult(string? ExecutablePath, IReadOnlyList<string> SearchedLocations)
{
    public bool Found => !string.IsNullOrWhiteSpace(ExecutablePath);

    public string ToActionableMessage() => Found
        ? $"Codex found at {ExecutablePath}"
        : "Codex could not be found. Searched: " + string.Join("; ", SearchedLocations);
}

public interface ICodexExecutableLocator
{
    CodexDiscoveryResult Find();
}

public sealed class CodexExecutableLocator : ICodexExecutableLocator
{
    public CodexDiscoveryResult Find()
    {
        var searched = new List<string>();

        var configured = Environment.GetEnvironmentVariable("CODEXTRAY_CODEX_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
            searched.Add("CODEXTRAY_CODEX_PATH (must name an existing absolute .exe path)");
            if (Path.IsPathFullyQualified(expanded) && Path.GetExtension(expanded).Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(expanded))
            {
                return new CodexDiscoveryResult(Path.GetFullPath(expanded), searched);
            }
            return new CodexDiscoveryResult(null, searched);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appBin = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        searched.Add(Path.Combine(appBin, "*", "codex.exe"));
        if (Directory.Exists(appBin))
        {
            try
            {
                var newest = Directory.EnumerateDirectories(appBin)
                    .Select(directory => Path.Combine(directory, "codex.exe"))
                    .Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (newest is not null)
                {
                    return new CodexDiscoveryResult(Path.GetFullPath(newest), searched);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The result below retains the safe searched location for troubleshooting.
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        searched.Add("PATH (codex.exe in absolute directories only)");
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cleanDirectory = directory.Trim('"');
            if (!Path.IsPathFullyQualified(cleanDirectory)) continue;
            foreach (var fileName in new[] { "codex.exe" })
            {
                try
                {
                    var candidate = Path.Combine(cleanDirectory, fileName);
                    if (File.Exists(candidate))
                    {
                        return new CodexDiscoveryResult(Path.GetFullPath(candidate), searched);
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // Ignore malformed PATH entries and continue discovery.
                }
            }
        }

        return new CodexDiscoveryResult(null, searched);
    }
}
