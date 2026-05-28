using System.Composition;
using System.Reflection;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Plugins;
using Foliant.Plugins.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Infrastructure.Tests.Plugins;

public sealed class PluginCatalogTests
{
    private readonly PluginCatalog _sut = new(NullLogger<PluginCatalog>.Instance);

    [Fact]
    public void Compose_AssemblyWithExportedPlugin_ReturnsIt()
    {
        var plugins = _sut.Compose([typeof(FakeEnginePlugin).Assembly]);

        var plugin = plugins.Should().ContainSingle(p => p.Name == "Fake").Subject;
        plugin.Kind.Should().Be(DocumentKind.Image);
        plugin.Loader.Should().NotBeNull();
        plugin.Loader.Kind.Should().Be(DocumentKind.Image);
    }

    [Fact]
    public void Compose_EmptyAssemblyList_ReturnsEmpty()
    {
        _sut.Compose([]).Should().BeEmpty();
    }

    [Fact]
    public void Compose_AssemblyWithoutPlugins_ReturnsEmpty()
    {
        // System.Linq's assembly has no IEnginePlugin export.
        _sut.Compose([typeof(Enumerable).Assembly]).Should().BeEmpty();
    }

    [Fact]
    public void Discover_MissingDirectory_ReturnsEmpty()
    {
        string missing = Path.Combine(Path.GetTempPath(), "foliant-plugins-" + Guid.NewGuid().ToString("N"));

        _sut.Discover(missing).Should().BeEmpty();
    }

    [Fact]
    public void Discover_EmptyDirectory_ReturnsEmpty()
    {
        string dir = Path.Combine(Path.GetTempPath(), "foliant-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            _sut.Discover(dir).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Fixture: a minimal MEF2-exported engine plugin living in the test assembly. ──

    [Export(typeof(IEnginePlugin))]
    private sealed class FakeEnginePlugin : IEnginePlugin
    {
        public string Name => "Fake";

        public string Version => "1.0.0";

        public DocumentKind Kind => DocumentKind.Image;

        public IDocumentLoader Loader { get; } = new FakeLoader();
    }

    private sealed class FakeLoader : IDocumentLoader
    {
        public DocumentKind Kind => DocumentKind.Image;

        public bool CanLoad(string path) => false;

        public Task<IDocument> LoadAsync(string path, CancellationToken ct) =>
            throw new NotSupportedException("test loader");
    }
}
