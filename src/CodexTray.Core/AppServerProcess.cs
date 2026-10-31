using System.Diagnostics;

namespace CodexTray.Core;

public interface IAppServerProcess : IAsyncDisposable
{
    TextReader StandardOutput { get; }

    TextWriter StandardInput { get; }

    TextReader StandardError { get; }

    bool HasExited { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public interface IAppServerProcessFactory
{
    IAppServerProcess Start(string executablePath);
}

public sealed class AppServerProcessFactory : IAppServerProcessFactory
{
    public IAppServerProcess Start(string executablePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("app-server");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Codex App Server did not start.");
        }

        return new AppServerProcess(process);
    }

    private sealed class AppServerProcess(Process process) : IAppServerProcess
    {
        public TextReader StandardOutput => process.StandardOutput;

        public TextWriter StandardInput => process.StandardInput;

        public TextReader StandardError => process.StandardError;

        public bool HasExited
        {
            get
            {
                try
                {
                    return process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (HasExited)
            {
                return;
            }

            try
            {
                process.StandardInput.Close();
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                if (!HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await StopAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
