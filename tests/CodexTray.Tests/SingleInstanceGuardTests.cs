using CodexTray.Core;

namespace CodexTray.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void OnlyOneGuardCanOwnTheNamedMutex()
    {
        var name = $"Local\\CodexTray.Test.{Guid.NewGuid():N}";
        Assert.True(SingleInstanceGuard.TryAcquire(name, out var first));
        Assert.NotNull(first);
        try
        {
            Assert.False(SingleInstanceGuard.TryAcquire(name, out var second));
            Assert.Null(second);
        }
        finally
        {
            first!.Dispose();
        }

        Assert.True(SingleInstanceGuard.TryAcquire(name, out var afterRelease));
        afterRelease!.Dispose();
    }
}
