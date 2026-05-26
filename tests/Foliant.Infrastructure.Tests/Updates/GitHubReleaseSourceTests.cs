using System.Net;
using FluentAssertions;
using Foliant.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Infrastructure.Tests.Updates;

public sealed class GitHubReleaseSourceTests
{
    [Fact]
    public async Task ReturnsTagName_OnSuccess()
    {
        using var http = CreateClient(_ => Json("""{"tag_name":"v0.2.0"}"""));
        var sut = new GitHubReleaseSource(http, NullLogger<GitHubReleaseSource>.Instance);

        string? tag = await sut.GetLatestTagAsync(CancellationToken.None);

        tag.Should().Be("v0.2.0");
    }

    [Fact]
    public async Task NonSuccessStatus_ReturnsNull_DoesNotThrow()
    {
        using var http = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = new GitHubReleaseSource(http, NullLogger<GitHubReleaseSource>.Instance);

        var act = async () => await sut.GetLatestTagAsync(CancellationToken.None);

        (await act.Should().NotThrowAsync()).Subject.Should().BeNull();
    }

    [Fact]
    public async Task NetworkException_ReturnsNull_DoesNotThrow()
    {
        using var http = CreateClient(_ => throw new HttpRequestException("offline"));
        var sut = new GitHubReleaseSource(http, NullLogger<GitHubReleaseSource>.Instance);

        var act = async () => await sut.GetLatestTagAsync(CancellationToken.None);

        (await act.Should().NotThrowAsync()).Subject.Should().BeNull();
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new StubHandler(responder)) { BaseAddress = new Uri("https://api.github.com/") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
