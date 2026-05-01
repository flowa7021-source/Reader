using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class CrashRecoveryViewModelTests
{
    private readonly IEventStore _store = Substitute.For<IEventStore>();

    public CrashRecoveryViewModelTests()
    {
        _store.ListPendingFingerprintsAsync(Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<string>>([]));
        _store.GetEventCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(0);
        _store.ClearAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Task.CompletedTask);
    }

    // ───── S11/Z ─────

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new CrashRecoveryViewModel(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Fresh_HasNoPendingDocuments()
    {
        var vm = new CrashRecoveryViewModel(_store);

        vm.HasPendingDocuments.Should().BeFalse();
        vm.PendingDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_NoPending_LeavesCollectionEmpty()
    {
        var vm = new CrashRecoveryViewModel(_store);

        await vm.LoadAsync(default);

        vm.PendingDocuments.Should().BeEmpty();
        vm.HasPendingDocuments.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_WithFingerprints_PopulatesItems()
    {
        _store.ListPendingFingerprintsAsync(Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<string>>(["fp-A", "fp-B"]));
        _store.GetEventCountAsync("fp-A", Arg.Any<CancellationToken>()).Returns(5);
        _store.GetEventCountAsync("fp-B", Arg.Any<CancellationToken>()).Returns(12);
        var vm = new CrashRecoveryViewModel(_store);

        await vm.LoadAsync(default);

        vm.PendingDocuments.Should().HaveCount(2);
        vm.PendingDocuments[0].Fingerprint.Should().Be("fp-A");
        vm.PendingDocuments[0].EventCount.Should().Be(5);
        vm.PendingDocuments[1].Fingerprint.Should().Be("fp-B");
        vm.PendingDocuments[1].EventCount.Should().Be(12);
        vm.HasPendingDocuments.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_SecondCall_ClearsAndReloads()
    {
        _store.ListPendingFingerprintsAsync(Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<string>>(["fp-X"]));
        _store.GetEventCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(3);
        var vm = new CrashRecoveryViewModel(_store);
        await vm.LoadAsync(default);
        vm.PendingDocuments.Should().ContainSingle();

        _store.ListPendingFingerprintsAsync(Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<string>>([]));
        await vm.LoadAsync(default);

        vm.PendingDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task DismissCommand_ClearsStoreAndRemovesItem()
    {
        _store.ListPendingFingerprintsAsync(Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<string>>(["fp-1", "fp-2"]));
        _store.GetEventCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(1);
        var vm = new CrashRecoveryViewModel(_store);
        await vm.LoadAsync(default);
        var item = vm.PendingDocuments[0];

        await vm.DismissCommand.ExecuteAsync(item);

        await _store.Received(1).ClearAsync("fp-1", Arg.Any<CancellationToken>());
        vm.PendingDocuments.Should().NotContain(i => i.Fingerprint == "fp-1");
        vm.PendingDocuments.Should().HaveCount(1);
    }

    [Fact]
    public async Task DismissCommand_NullArg_IsNoOp()
    {
        var vm = new CrashRecoveryViewModel(_store);

        await vm.DismissCommand.ExecuteAsync(null);

        await _store.DidNotReceive().ClearAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DismissAllCommand_ClearsAllAndEmptiesCollection()
    {
        _store.ListPendingFingerprintsAsync(Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<string>>(["fp-A", "fp-B", "fp-C"]));
        _store.GetEventCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(2);
        var vm = new CrashRecoveryViewModel(_store);
        await vm.LoadAsync(default);

        await vm.DismissAllCommand.ExecuteAsync(null);

        await _store.Received(1).ClearAsync("fp-A", Arg.Any<CancellationToken>());
        await _store.Received(1).ClearAsync("fp-B", Arg.Any<CancellationToken>());
        await _store.Received(1).ClearAsync("fp-C", Arg.Any<CancellationToken>());
        vm.PendingDocuments.Should().BeEmpty();
        vm.HasPendingDocuments.Should().BeFalse();
    }

    [Fact]
    public async Task HasPendingDocuments_FiresPropertyChanged_WhenCollectionChanges()
    {
        _store.ListPendingFingerprintsAsync(Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<string>>(["fp-1"]));
        _store.GetEventCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(1);
        var vm = new CrashRecoveryViewModel(_store);
        await vm.LoadAsync(default);

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        await vm.DismissAllCommand.ExecuteAsync(null);

        fired.Should().Contain(nameof(CrashRecoveryViewModel.HasPendingDocuments));
    }
}
