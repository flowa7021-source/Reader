using System.Diagnostics;

namespace Foliant.Plugin.DjVu;

/// <summary>
/// Secure child-process launcher for the DjVuLibre CLI. Always uses
/// <see cref="ProcessStartInfo.ArgumentList"/> (never a joined command string) so callers
/// cannot inject shell metacharacters or extra arguments. No shell execute; stdout is
/// redirected; the process is killed on cancellation or timeout and always disposed.
/// </summary>
internal static class DjvuProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Runs the tool and returns raw stdout bytes (for binary PPM output).</summary>
    public static async Task<byte[]> RunForBytesAsync(
        string exePath,
        IReadOnlyList<string> args,
        CancellationToken ct)
    {
        // FRAGILE: process boundary — ArgumentList only; ProcessStartInfo.Arguments left unset.
        using var process = CreateProcess(exePath, args);
        process.Start();

        using var ms = new MemoryStream();
        Task copy = process.StandardOutput.BaseStream.CopyToAsync(ms, ct);
        Task<string> stderr = process.StandardError.ReadToEndAsync(ct);

        string err = await WaitAndCollectAsync(process, copy, stderr, ct).ConfigureAwait(false);
        ThrowIfFailed(process, exePath, err);

        return ms.ToArray();
    }

    /// <summary>Runs the tool and returns decoded stdout text (for djvused output).</summary>
    public static async Task<string> RunForTextAsync(
        string exePath,
        IReadOnlyList<string> args,
        CancellationToken ct)
    {
        // FRAGILE: process boundary — ArgumentList only; ProcessStartInfo.Arguments left unset.
        using var process = CreateProcess(exePath, args);
        process.Start();

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderr = process.StandardError.ReadToEndAsync(ct);

        string err = await WaitAndCollectAsync(process, stdout, stderr, ct).ConfigureAwait(false);
        ThrowIfFailed(process, exePath, err);

        return await stdout.ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for exit (or kills on cancel/timeout) and returns stderr text. On failure the
    /// stdout/stderr reads are always drained so no read task is left unobserved.
    /// </summary>
    private static async Task<string> WaitAndCollectAsync(Process process, Task stdout, Task<string> stderr, CancellationToken ct)
    {
        try
        {
            await WaitOrKillAsync(process, ct).ConfigureAwait(false);
            await stdout.ConfigureAwait(false);
            return await stderr.ConfigureAwait(false);
        }
        catch
        {
            await DrainAsync(stdout).ConfigureAwait(false);
            await DrainAsync(stderr).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task DrainAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Read cancelled alongside the original fault — already accounted for.
        }
        catch (IOException)
        {
            // Pipe closed because the process was killed — irrelevant to the original fault.
        }
    }

    private static Process CreateProcess(string exePath, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        return new Process { StartInfo = psi };
    }

    private static async Task WaitOrKillAsync(Process process, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(DefaultTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillSafely(process);
            if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"DjVu tool '{process.StartInfo.FileName}' exceeded {DefaultTimeout.TotalSeconds:0}s timeout.");
            }

            throw;
        }
    }

    private static void KillSafely(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between the check and the kill — nothing to do.
        }
    }

    private static void ThrowIfFailed(Process process, string exePath, string stderr)
    {
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"DjVu tool '{exePath}' exited with code {process.ExitCode}. {stderr}".Trim());
        }
    }
}
