using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Чистая логика триала: принимает три параллельных снимка состояния
/// (primary file, secondary registry, marker hash) и возвращает
/// <see cref="TrialEvaluation"/>. Никакого I/O — store-абстракция отдельно
/// (S13/C). Тестируется детерминированно.
/// </summary>
public sealed class TrialAntiTamperService
{
    /// <summary>Длительность бесплатного триала.</summary>
    public const int TrialDays = 30;

    /// <summary>Создаёт свежий <see cref="TrialState"/> для первого запуска.</summary>
    public static TrialState NewTrial(DateTimeOffset now) =>
        new(now, now, Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Записывает «максимальное виденное время». Если <paramref name="now"/>
    /// раньше <see cref="TrialState.MaxObservedAt"/> — возвращает state без
    /// изменений (это caller отделит как Tampered).
    /// </summary>
    public static TrialState UpdateMaxObserved(TrialState current, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        return now > current.MaxObservedAt ? current with { MaxObservedAt = now } : current;
    }

    /// <summary>
    /// Хэш-маркер для third-store (хранится в <c>Autosave/.trial-marker</c>).
    /// Зависит от StartedAt + Nonce — НЕ от MaxObservedAt, чтобы маркер не
    /// требовал обновления на каждом запуске.
    /// </summary>
    public static string ComputeMarker(TrialState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var raw = string.Create(
            CultureInfo.InvariantCulture,
            $"{state.StartedAt:O}|{state.Nonce}");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Сводит три источника в единый вердикт. Если один из них отсутствует
    /// или контент расходится — Tampered. Если now &lt; MaxObservedAt — Tampered
    /// (откат часов). Иначе — Active или Expired по разнице (now - StartedAt).
    /// </summary>
    public static TrialEvaluation Evaluate(
        TrialState? primary,
        TrialState? secondary,
        string? markerHash,
        DateTimeOffset now)
    {
        bool primaryEmpty = primary is null;
        bool secondaryEmpty = secondary is null;
        bool markerEmpty = string.IsNullOrEmpty(markerHash);

        // Полностью чистая система → триал ещё не запускался.
        if (primaryEmpty && secondaryEmpty && markerEmpty)
        {
            return new TrialEvaluation(TrialStatus.NotStarted, TrialDays, null);
        }

        // Что-то есть, но не всё — кто-то стёр один из stores.
        if (primaryEmpty || secondaryEmpty || markerEmpty)
        {
            return new TrialEvaluation(TrialStatus.Tampered, 0,
                "One or more trial stores missing while others remain");
        }

        // primary != null && secondary != null && markerHash != null после проверок выше.
        if (primary!.StartedAt != secondary!.StartedAt || primary.Nonce != secondary.Nonce)
        {
            return new TrialEvaluation(TrialStatus.Tampered, 0,
                "Primary and secondary trial stores diverge");
        }

        if (!string.Equals(markerHash, ComputeMarker(primary), StringComparison.Ordinal))
        {
            return new TrialEvaluation(TrialStatus.Tampered, 0,
                "Marker hash does not match recomputed value");
        }

        // Откат часов — most-recent-seen использует max обоих stores чтобы tamper
        // не пролезал через занижение MaxObservedAt в одном из них.
        var maxSeen = primary.MaxObservedAt > secondary.MaxObservedAt
            ? primary.MaxObservedAt
            : secondary.MaxObservedAt;
        if (now < maxSeen)
        {
            return new TrialEvaluation(TrialStatus.Tampered, 0,
                $"System clock moved backwards (max observed {maxSeen:O}, now {now:O})");
        }

        var elapsedDays = (int)(now - primary.StartedAt).TotalDays;
        var remaining = TrialDays - elapsedDays;
        return remaining <= 0
            ? new TrialEvaluation(TrialStatus.Expired, 0, null)
            : new TrialEvaluation(TrialStatus.Active, remaining, null);
    }
}
