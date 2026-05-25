namespace Foliant.Domain;

/// <summary>
/// Снимок состояния триала. Записывается одновременно в три места
/// (file, registry, marker), любая разсинхронизация трактуется как tamper.
/// <see cref="MaxObservedAt"/> используется для детекции «откатили часы назад».
/// </summary>
public sealed record TrialState(
    DateTimeOffset StartedAt,
    DateTimeOffset MaxObservedAt,
    string Nonce);

public enum TrialStatus
{
    /// <summary>Триал ещё не начинался — первое открытие приложения.</summary>
    NotStarted,

    /// <summary>Триал активен, осталось <see cref="TrialEvaluation.DaysRemaining"/> дней.</summary>
    Active,

    /// <summary>Прошло ≥ <c>TrialAntiTamperService.TrialDays</c> дней с момента старта.</summary>
    Expired,

    /// <summary>Зафиксировано вмешательство: разсинхронизация stores или откат системного времени.</summary>
    Tampered,
}

public sealed record TrialEvaluation(
    TrialStatus Status,
    int DaysRemaining,
    string? TamperReason);
