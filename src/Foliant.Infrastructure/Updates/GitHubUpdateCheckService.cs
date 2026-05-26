using System.Reflection;
using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Updates;

/// <summary>
/// Проверяет GitHub Releases на наличие более новой версии (PROJECT_BOARD §7.4).
/// Throttle — не чаще раза в сутки (по <see cref="Application.Settings.AppSettings.LastUpdateCheckTime"/>);
/// opt-out через <see cref="Application.Settings.AppSettings.CheckForUpdates"/>. Сетевые сбои
/// поглощаются <see cref="IReleaseSource"/> (возвращает <c>null</c>) — сервис никогда не бросает.
/// </summary>
public sealed class GitHubUpdateCheckService : IUpdateCheckService
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromHours(24);

    private readonly IReleaseSource _releases;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<GitHubUpdateCheckService> _log;
    private readonly Version _currentVersion;

    public GitHubUpdateCheckService(
        IReleaseSource releases,
        ISettingsService settings,
        TimeProvider clock,
        ILogger<GitHubUpdateCheckService> log)
    {
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        _releases = releases;
        _settings = settings;
        _clock = clock;
        _log = log;
        _currentVersion = ResolveCurrentVersion();
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        if (!_settings.Current.CheckForUpdates)
        {
            return UpdateCheckResult.NotAvailable(_currentVersion);
        }

        DateTimeOffset now = _clock.GetUtcNow();
        DateTimeOffset? last = _settings.Current.LastUpdateCheckTime;
        if (last is not null && now - last.Value < MinInterval)
        {
            return UpdateCheckResult.NotAvailable(_currentVersion);
        }

        string? tag = await _releases.GetLatestTagAsync(ct).ConfigureAwait(false);

        await _settings.UpdateAsync(s => s with { LastUpdateCheckTime = now }, ct).ConfigureAwait(false);

        if (!TryParseTag(tag, out Version? latest))
        {
            if (tag is not null)
            {
                _log.LogWarning("Could not parse latest release tag '{Tag}' as a version.", tag);
            }
            return UpdateCheckResult.NotAvailable(_currentVersion);
        }

        bool available = latest > _currentVersion;
        return new UpdateCheckResult(_currentVersion, latest, available);
    }

    private static bool TryParseTag(string? tag, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        // Теги вида "v0.1.0" / "0.1.0"; отбрасываем prerelease/build-метку после '-' или '+'.
        ReadOnlySpan<char> span = tag.AsSpan().Trim();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V'))
        {
            span = span[1..];
        }

        int cut = span.IndexOfAny('-', '+');
        if (cut >= 0)
        {
            span = span[..cut];
        }

        return Version.TryParse(span, out version);
    }

    private static Version ResolveCurrentVersion()
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        string? informational = asm
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (TryParseTag(StripMetadata(informational), out Version? parsed) && parsed is not null)
        {
            return parsed;
        }

        return asm.GetName().Version ?? new Version(0, 0, 0, 0);
    }

    private static string? StripMetadata(string? informational)
    {
        if (string.IsNullOrEmpty(informational))
        {
            return informational;
        }

        // SourceLink дописывает "+<commit-sha>" к informational version — режем до '+'.
        int plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? informational[..plus] : informational;
    }
}
