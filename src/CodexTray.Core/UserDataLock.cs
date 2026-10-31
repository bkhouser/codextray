namespace CodexTray.Core;

/// <summary>Serializes per-user persistent state across all Windows sessions.</summary>
public sealed class UserDataLock : IDisposable
{
    private readonly FileStream _stream;
    private UserDataLock(FileStream stream) => _stream = stream;

    public static bool TryAcquire(string directory, out UserDataLock? guard)
    {
        guard = null;
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, ".instance.lock");
            guard = new(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose() => _stream.Dispose();
}
