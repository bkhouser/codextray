using CodexTray.Core;

namespace CodexTray.Tests;

public sealed class UserDataLockTests
{
    [Fact]
    public void SharedUserDataCannotBeOpenedTwiceAndStaleLockFileDoesNotBlockRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CodexTrayLock-" + Guid.NewGuid());
        try
        {
            Assert.True(UserDataLock.TryAcquire(directory, out var first));
            using (first)
            {
                Assert.False(UserDataLock.TryAcquire(directory, out var duplicate));
                Assert.Null(duplicate);
                Assert.True(UserDataLock.TryAcquire(Path.Combine(directory,"other-user"), out var other));
                other!.Dispose();
            }
            Assert.True(File.Exists(Path.Combine(directory, ".instance.lock")));
            Assert.True(UserDataLock.TryAcquire(directory, out var restarted));
            restarted!.Dispose();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
