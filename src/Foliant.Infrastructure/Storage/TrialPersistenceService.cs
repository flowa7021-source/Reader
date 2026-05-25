using System.Runtime.Versioning;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Storage;

/// <summary>
/// Production <see cref="ITrialService"/>: оркеструет тройной
/// <see cref="TrialStores"/> поверх чистой логики
/// <see cref="TrialAntiTamperService"/>. Каждый <see cref="TouchAsync"/>
/// продвигает <see cref="TrialState.MaxObservedAt"/> (anti-rollback heartbeat).
/// Windows-only во время выполнения (DPAPI + registry внутри stores).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrialPersistenceService(
    TrialStores stores,
    TimeProvider clock,
    ILogger<TrialPersistenceService> log) : ITrialService
{
    public Task<TrialEvaluation> StartAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var existing = ReadEvaluation();
        if (existing.Status != TrialStatus.NotStarted)
        {
            return Task.FromResult(existing);
        }

        var state = TrialAntiTamperService.NewTrial(clock.GetUtcNow());
        stores.WriteAll(state, TrialAntiTamperService.ComputeMarker(state));
        log.LogInformation("Trial started; {Days} days granted", TrialAntiTamperService.TrialDays);
        return Task.FromResult(ReadEvaluation());
    }

    public Task<TrialEvaluation> EvaluateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ReadEvaluation());
    }

    public Task<TrialEvaluation> TouchAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var primary = stores.ReadPrimary();
        var verdict = TrialAntiTamperService.Evaluate(
            primary, stores.ReadSecondary(), stores.ReadMarker(), clock.GetUtcNow());

        // Продвигаем MaxObservedAt только на «здоровом» состоянии — иначе перезапись
        // могла бы стереть улики tamper-а или зафиксировать откат часов.
        if (primary is not null && verdict.Status is TrialStatus.Active or TrialStatus.Expired)
        {
            var advanced = TrialAntiTamperService.UpdateMaxObserved(primary, clock.GetUtcNow());
            if (!ReferenceEquals(advanced, primary))
            {
                stores.WriteAll(advanced, TrialAntiTamperService.ComputeMarker(advanced));
            }
        }

        return Task.FromResult(verdict);
    }

    private TrialEvaluation ReadEvaluation() =>
        TrialAntiTamperService.Evaluate(
            stores.ReadPrimary(), stores.ReadSecondary(), stores.ReadMarker(), clock.GetUtcNow());
}
