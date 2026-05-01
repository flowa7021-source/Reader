using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Licensing;
using Xunit;

namespace Foliant.Infrastructure.Tests.Licensing;

public sealed class InMemoryLicenseStorageTests
{
    private readonly InMemoryLicenseStorage _sut = new();

    [Fact]
    public async Task Load_NoSave_ReturnsNull()
    {
        var result = await _sut.LoadAsync(default);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveThenLoad_ReturnsSameBlob()
    {
        var blob = new LicenseBlob("{}", "sig==");

        await _sut.SaveAsync(blob, default);
        var result = await _sut.LoadAsync(default);

        result.Should().BeSameAs(blob);
    }

    [Fact]
    public async Task Save_OverwritesPrevious()
    {
        await _sut.SaveAsync(new LicenseBlob("v1", "s1"), default);
        await _sut.SaveAsync(new LicenseBlob("v2", "s2"), default);

        var result = await _sut.LoadAsync(default);

        result!.LicenseJson.Should().Be("v2");
        result.SignatureBase64.Should().Be("s2");
    }

    [Fact]
    public async Task Clear_RemovesBlob()
    {
        await _sut.SaveAsync(new LicenseBlob("{}", "sig"), default);
        await _sut.ClearAsync(default);

        (await _sut.LoadAsync(default)).Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_NullBlob_Throws()
    {
        var act = () => _sut.SaveAsync(null!, default);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ConcurrentSaveAndLoad_NoCorruption()
    {
        var saves = Enumerable.Range(0, 50).Select(i =>
            _sut.SaveAsync(new LicenseBlob($"v{i}", $"s{i}"), default));
        var loads = Enumerable.Range(0, 50).Select(_ =>
            _sut.LoadAsync(default));

        await Task.WhenAll(saves.Concat<Task>(loads));

        var final = await _sut.LoadAsync(default);
        final.Should().NotBeNull();   // any one of the saves wins
    }
}
