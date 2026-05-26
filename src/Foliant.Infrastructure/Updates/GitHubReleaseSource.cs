using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Updates;

/// <summary>
/// Тянет последний релиз из GitHub Releases API
/// (<c>GET /repos/{owner}/{repo}/releases/latest</c>) и возвращает <c>tag_name</c>.
/// Любая ошибка (сеть, не-2xx, кривой JSON, отмена) поглощается → <c>null</c>, чтобы
/// проверка обновлений никогда не валила запуск приложения.
/// </summary>
public sealed class GitHubReleaseSource : IReleaseSource
{
    /// <summary>Имя именованного <see cref="HttpClient"/> в DI (см. AppHostBuilder).</summary>
    public const string HttpClientName = "github-releases";

    private const string LatestReleaseUri = "repos/foliant-reader/foliant/releases/latest";

    private readonly HttpClient _http;
    private readonly ILogger<GitHubReleaseSource> _log;

    public GitHubReleaseSource(HttpClient http, ILogger<GitHubReleaseSource> log)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(log);
        _http = http;
        _log = log;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Update check must never throw on transient network/parse failures; caller treats null as 'no update'.")]
    public async Task<string?> GetLatestTagAsync(CancellationToken ct)
    {
        try
        {
            GitHubRelease? release = await _http
                .GetFromJsonAsync(LatestReleaseUri, GitHubReleaseJsonContext.Default.GitHubRelease, ct)
                .ConfigureAwait(false);
            return release?.TagName;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogInformation(ex, "Update check could not reach GitHub Releases; skipping.");
            return null;
        }
    }
}

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string? TagName);

[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class GitHubReleaseJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
