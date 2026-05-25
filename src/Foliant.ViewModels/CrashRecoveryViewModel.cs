using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;

namespace Foliant.ViewModels;

/// <summary>
/// Диалог «Crash Recovery»: при старте приложение сканирует <see cref="IEventStore"/>
/// на наличие несохранённых действий (документы, которые были открыты до сбоя).
/// VM загружает список через <see cref="LoadAsync"/>, пользователь может сбросить
/// отдельный документ (<see cref="DismissCommand"/>) или все сразу
/// (<see cref="DismissAllCommand"/>).
/// </summary>
public sealed partial class CrashRecoveryViewModel : ObservableObject
{
    private readonly IEventStore _eventStore;

    public ObservableCollection<CrashRecoveryItem> PendingDocuments { get; } = [];

    /// <summary>True если есть хотя бы один документ с несохранёнными действиями.
    /// Биндится к видимости всего диалога.</summary>
    public bool HasPendingDocuments => PendingDocuments.Count > 0;

    public CrashRecoveryViewModel(IEventStore eventStore)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        _eventStore = eventStore;

        PendingDocuments.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(HasPendingDocuments));
    }

    /// <summary>Заполнить <see cref="PendingDocuments"/> данными из event store.
    /// Вызывать при инициализации диалога — не в конструкторе, чтобы не блокировать UI.</summary>
    public async Task LoadAsync(CancellationToken ct)
    {
        PendingDocuments.Clear();

        var fingerprints = await _eventStore.ListPendingFingerprintsAsync(ct).ConfigureAwait(false);
        foreach (var fp in fingerprints)
        {
            ct.ThrowIfCancellationRequested();
            int count = await _eventStore.GetEventCountAsync(fp, ct).ConfigureAwait(false);
            PendingDocuments.Add(new CrashRecoveryItem(fp, count));
        }
    }

    /// <summary>Сбросить (удалить) event-лог для одного документа. После этого crash
    /// recovery для него больше не предлагается.</summary>
    [RelayCommand]
    private async Task DismissAsync(CrashRecoveryItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _eventStore.ClearAsync(item.Fingerprint, CancellationToken.None);
        PendingDocuments.Remove(item);
    }

    /// <summary>Сбросить event-логи для всех документов одним действием.</summary>
    [RelayCommand]
    private async Task DismissAllAsync()
    {
        var snapshot = PendingDocuments.ToList();
        PendingDocuments.Clear();

        foreach (var item in snapshot)
        {
            await _eventStore.ClearAsync(item.Fingerprint, CancellationToken.None);
        }
    }
}

/// <summary>Строка в списке диалога crash recovery.</summary>
public sealed record CrashRecoveryItem(string Fingerprint, int EventCount);
