using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Licensing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Xunit;

namespace Foliant.Infrastructure.Tests.Licensing;

/// <summary>
/// DPAPI + HKCU зависят от Windows-runtime → помечены Slow и фильтруются на
/// non-Windows CI (<c>--filter Category!=Slow</c>); вдобавок каждый тест
/// no-op'ом возвращается, если запущен не на Windows. Registry-subkey изолирован
/// per-instance и удаляется в Dispose.
/// </summary>
[Trait("Category", "Slow")]
public sealed class DpapiLicenseStorageTests : IDisposable
{
    private const string TestRoot = @"Software\Foliant-Tests";
    private readonly TempDir _tmp = new();
    private readonly string _subKey = TestRoot + "\\" + Guid.NewGuid().ToString("N");

    private DpapiLicenseStorage CreateSut() =>
        new(_tmp.File("license.key"), _subKey, "License", NullLogger<DpapiLicenseStorage>.Instance);

    [Fact]
    public async Task Load_NoFileNoRegistry_ReturnsNull()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await CreateSut().LoadAsync(default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsEqual()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sut = CreateSut();
        var blob = new LicenseBlob("{\"User\":\"alice\",\"Sku\":\"Pro\"}", "c2lnbmF0dXJl");

        await sut.SaveAsync(blob, default);
        var loaded = await sut.LoadAsync(default);

        loaded.Should().Be(blob);
    }

    [Fact]
    public async Task Save_WritesEncryptedFile_NotPlaintext_AndNoTempLeftover()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = _tmp.File("license.key");
        var sut = CreateSut();
        var blob = new LicenseBlob("PLAINTEXT-MARKER", "sig==");

        await sut.SaveAsync(blob, default);

        File.Exists(path).Should().BeTrue();
        File.Exists(path + ".tmp").Should().BeFalse();
        var bytes = await File.ReadAllBytesAsync(path);
        System.Text.Encoding.UTF8.GetString(bytes).Should().NotContain("PLAINTEXT-MARKER");
    }

    [Fact]
    public async Task Load_FromRegistryMirror_WhenFileDeleted()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = _tmp.File("license.key");
        var sut = CreateSut();
        var blob = new LicenseBlob("{\"User\":\"bob\"}", "sig==");
        await sut.SaveAsync(blob, default);

        File.Delete(path); // file gone — registry mirror must still resolve.

        var loaded = await sut.LoadAsync(default);
        loaded.Should().Be(blob);
    }

    [Fact]
    public async Task Clear_RemovesFileAndRegistry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sut = CreateSut();
        await sut.SaveAsync(new LicenseBlob("{}", "sig"), default);

        await sut.ClearAsync(default);

        (await sut.LoadAsync(default)).Should().BeNull();
        File.Exists(_tmp.File("license.key")).Should().BeFalse();
    }

    [Fact]
    public async Task Save_NullBlob_Throws()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var act = () => CreateSut().SaveAsync(null!, default);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    public void Dispose()
    {
        _tmp.Dispose();
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(TestRoot, throwOnMissingSubKey: false);
        }
        catch
        {
            /* best-effort cleanup */
        }
    }
}
