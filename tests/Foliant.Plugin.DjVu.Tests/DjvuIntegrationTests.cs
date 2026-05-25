using FluentAssertions;
using Xunit;

namespace Foliant.Plugin.DjVu.Tests;

/// <summary>
/// Integration coverage that requires the real DjVuLibre CLI. Each test is a no-op when
/// <c>ddjvu</c>/<c>djvused</c> cannot be resolved (not installed / not on PATH), so the
/// suite stays green on machines without the GPL binaries.
/// </summary>
[Trait("Category", "Slow")]
public sealed class DjvuIntegrationTests
{
    private static bool ToolsAvailable => DjvuToolset.TryResolve(out _);

    [Fact]
    public void TryResolve_WhenToolsPresent_ReturnsExistingExecutables()
    {
        if (!ToolsAvailable)
        {
            return; // skipped: DjVuLibre not installed on this host.
        }

        DjvuToolset.TryResolve(out var tools).Should().BeTrue();
        tools.Should().NotBeNull();
        File.Exists(tools!.DdjvuPath).Should().BeTrue();
        File.Exists(tools.DjvusedPath).Should().BeTrue();
    }
}
