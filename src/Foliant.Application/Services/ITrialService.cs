using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Фасад над <see cref="TrialAntiTamperService"/> + тройным персистентным
/// хранилищем (DPAPI-файл, HKCU-реестр, marker-файл). Чистая логика живёт в
/// <see cref="TrialAntiTamperService"/>; этот порт лишь оркеструет I/O и
/// продвигает <see cref="TrialState.MaxObservedAt"/> на каждом обращении.
/// Production-impl — Windows-only (DPAPI/registry); dev-сборка может не
/// регистрировать его.
/// </summary>
public interface ITrialService
{
    /// <summary>
    /// Инициирует триал, если он ещё не запускался: пишет свежий
    /// <see cref="TrialState"/> во все три backings и возвращает вердикт.
    /// Если триал уже стартовал — эквивалентен <see cref="EvaluateAsync"/>
    /// (повторно не перезаписывает <see cref="TrialState.StartedAt"/>).
    /// </summary>
    Task<TrialEvaluation> StartAsync(CancellationToken ct);

    /// <summary>
    /// Считывает три backings, сводит их через
    /// <see cref="TrialAntiTamperService.Evaluate"/> и возвращает текущий
    /// статус. На «чистой» системе — <see cref="TrialStatus.NotStarted"/>.
    /// </summary>
    Task<TrialEvaluation> EvaluateAsync(CancellationToken ct);

    /// <summary>
    /// Как <see cref="EvaluateAsync"/>, но при активном/истёкшем триале
    /// продвигает <see cref="TrialState.MaxObservedAt"/> до текущего времени
    /// во всех backings (anti-rollback «heartbeat»).
    /// </summary>
    Task<TrialEvaluation> TouchAsync(CancellationToken ct);
}
