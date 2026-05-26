using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foliant.Application.Services;

namespace Foliant.Infrastructure.Diagnostics;

/// <summary>Сериализуемый снимок сбоя.</summary>
internal sealed record CrashReport(
    DateTimeOffset TimestampUtc,
    string Context,
    string ExceptionType,
    string Message,
    string? StackTrace);

[JsonSerializable(typeof(CrashReport))]
internal sealed partial class CrashReportJsonContext : JsonSerializerContext;

/// <summary>
/// Пишет crash-отчёт (JSON) в указанный каталог при включённом opt-in
/// (<c>AppSettings.CrashReportingEnabled</c>). Best-effort: никогда не бросает — вызывается,
/// когда приложение уже в состоянии сбоя.
/// </summary>
public sealed class FileCrashReporter : ICrashReporter
{
    private readonly ISettingsService _settings;
    private readonly string _crashDirectory;
    private readonly TimeProvider _time;

    /// <summary>Создаёт репортер, пишущий в <paramref name="crashDirectory"/>.</summary>
    /// <param name="settings">Источник opt-in флага.</param>
    /// <param name="crashDirectory">Каталог для файлов отчётов.</param>
    /// <param name="timeProvider">Источник времени (для метки и имени файла); по умолчанию системный.</param>
    public FileCrashReporter(ISettingsService settings, string crashDirectory, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(crashDirectory);
        _settings = settings;
        _crashDirectory = crashDirectory;
        _time = timeProvider ?? TimeProvider.System;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Crash reporting is best-effort during a failing app — it must never throw.")]
    public string? Report(Exception exception, string context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!_settings.Current.CrashReportingEnabled)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_crashDirectory);
            DateTimeOffset now = _time.GetUtcNow();
            var report = new CrashReport(
                now,
                context,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message,
                exception.StackTrace);
            string name = "crash-" + now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".json";
            string path = Path.Combine(_crashDirectory, name);
            File.WriteAllText(path, JsonSerializer.Serialize(report, CrashReportJsonContext.Default.CrashReport));
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
