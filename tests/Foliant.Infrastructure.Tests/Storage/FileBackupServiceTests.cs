using FluentAssertions;
using Foliant.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Infrastructure.Tests.Storage;

public sealed class FileBackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"foliant-bk-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task BackupUserData_CopiesExistingFilesAndDirectories()
    {
        var src = Path.Combine(_root, "src");
        var settings = Path.Combine(src, "settings.json");
        var autosave = Path.Combine(src, "Autosave");
        Directory.CreateDirectory(autosave);
        File.WriteAllText(settings, "{}");
        File.WriteAllText(Path.Combine(autosave, "doc1.jsonl"), "evt");
        var backupRoot = Path.Combine(_root, "Backup");

        var sut = new FileBackupService(backupRoot, [settings], [autosave], NullLogger<FileBackupService>.Instance);
        var path = await sut.BackupUserDataAsync("v0.1.0_20260526", default);

        path.Should().Be(Path.Combine(backupRoot, "v0.1.0_20260526"));
        File.Exists(Path.Combine(path!, "settings.json")).Should().BeTrue();
        File.Exists(Path.Combine(path!, "Autosave", "doc1.jsonl")).Should().BeTrue();
    }

    [Fact]
    public async Task BackupUserData_SkipsMissingSources_ReturnsNullWhenNothingToCopy()
    {
        var backupRoot = Path.Combine(_root, "Backup");
        var sut = new FileBackupService(
            backupRoot,
            [Path.Combine(_root, "nope.json")],
            [Path.Combine(_root, "no-dir")],
            NullLogger<FileBackupService>.Instance);

        var path = await sut.BackupUserDataAsync("v0.1.0", default);

        path.Should().BeNull();
        Directory.Exists(backupRoot).Should().BeFalse();
    }

    [Fact]
    public async Task BackupUserData_CopiesPresentSources_EvenWhenOthersMissing()
    {
        Directory.CreateDirectory(_root);
        var present = Path.Combine(_root, "settings.json");
        File.WriteAllText(present, "{}");
        var backupRoot = Path.Combine(_root, "Backup");

        var sut = new FileBackupService(
            backupRoot,
            [present, Path.Combine(_root, "missing.dat")],
            [],
            NullLogger<FileBackupService>.Instance);
        var path = await sut.BackupUserDataAsync("v1", default);

        path.Should().NotBeNull();
        File.Exists(Path.Combine(path!, "settings.json")).Should().BeTrue();
        File.Exists(Path.Combine(path!, "missing.dat")).Should().BeFalse();
    }
}
