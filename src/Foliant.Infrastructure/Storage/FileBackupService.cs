using System.Diagnostics.CodeAnalysis;
using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Storage;

/// <summary>
/// Файловый бэкап пользовательских данных (§7.4): копирует заданные файлы и каталоги в
/// <c>backupRoot\{label}\</c>. Отсутствующие источники пропускаются; ошибка одного источника
/// логируется и не прерывает остальные.
/// </summary>
public sealed class FileBackupService : IBackupService
{
    private readonly string _backupRoot;
    private readonly IReadOnlyList<string> _sourceFiles;
    private readonly IReadOnlyList<string> _sourceDirectories;
    private readonly ILogger<FileBackupService> _log;

    /// <summary>Создаёт сервис, копирующий <paramref name="sourceFiles"/> и
    /// <paramref name="sourceDirectories"/> в подкаталоги <paramref name="backupRoot"/>.</summary>
    public FileBackupService(
        string backupRoot,
        IReadOnlyList<string> sourceFiles,
        IReadOnlyList<string> sourceDirectories,
        ILogger<FileBackupService> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(sourceDirectories);
        ArgumentNullException.ThrowIfNull(log);
        _backupRoot = backupRoot;
        _sourceFiles = sourceFiles;
        _sourceDirectories = sourceDirectories;
        _log = log;
    }

    public Task<string?> BackupUserDataAsync(string label, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var target = Path.Combine(_backupRoot, label);
        var copiedAnything = false;

        foreach (var file in _sourceFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(file))
            {
                continue;
            }

            if (TryCopyFile(file, Path.Combine(target, Path.GetFileName(file))))
            {
                copiedAnything = true;
            }
        }

        foreach (var dir in _sourceDirectories)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(dir))
            {
                continue;
            }

            if (TryCopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))), ct))
            {
                copiedAnything = true;
            }
        }

        return Task.FromResult(copiedAnything ? target : null);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Backup is best-effort before an upgrade; one unreadable source must not abort the rest.")]
    private bool TryCopyFile(string source, string destination)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Backup: failed to copy file {Source}.", source);
            return false;
        }
    }

    private bool TryCopyDirectory(string source, string destination, CancellationToken ct)
    {
        var any = false;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            if (TryCopyFile(file, Path.Combine(destination, relative)))
            {
                any = true;
            }
        }

        return any;
    }
}
