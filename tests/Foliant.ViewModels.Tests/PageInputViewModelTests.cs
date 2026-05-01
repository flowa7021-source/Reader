using FluentAssertions;
using Foliant.ViewModels;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class PageInputViewModelTests
{
    [Fact]
    public void Empty_IsNotValid_NoErrorMessage()
    {
        var vm = new PageInputViewModel(pageCount: 10, _ => { });

        vm.IsValid.Should().BeFalse();
        vm.ErrorMessage.Should().BeNull();   // пусто → ещё не печатали, не подсвечиваем ошибку
    }

    [Fact]
    public void OutOfRange_IsInvalid_ShowsError()
    {
        var vm = new PageInputViewModel(pageCount: 10, _ => { });

        vm.Input = "999";

        vm.IsValid.Should().BeFalse();
        vm.ErrorMessage.Should().Contain("1 to 10");
    }

    [Fact]
    public void Zero_IsInvalid()
    {
        var vm = new PageInputViewModel(pageCount: 10, _ => { });

        vm.Input = "0";

        vm.IsValid.Should().BeFalse();
    }

    [Fact]
    public void NonNumeric_IsInvalid()
    {
        var vm = new PageInputViewModel(pageCount: 10, _ => { });

        vm.Input = "abc";

        vm.IsValid.Should().BeFalse();
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidInput_AcceptCommand_InvokesCallback_With0BasedIndex()
    {
        int? received = null;
        var vm = new PageInputViewModel(pageCount: 100, idx => received = idx);
        vm.Input = "42";

        vm.AcceptCommand.Execute(null);

        received.Should().Be(41);   // 1-based 42 → 0-based 41
    }

    [Fact]
    public void InvalidInput_AcceptCommand_DoesNotInvokeCallback()
    {
        int? received = null;
        var vm = new PageInputViewModel(pageCount: 100, idx => received = idx);
        vm.Input = "abc";

        vm.AcceptCommand.Execute(null);

        received.Should().BeNull();
    }

    [Fact]
    public void CancelCommand_InvokesCancelCallback()
    {
        bool cancelled = false;
        var vm = new PageInputViewModel(pageCount: 1, _ => { }, () => cancelled = true);

        vm.CancelCommand.Execute(null);

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void Input_FiresPropertyChanged_For_IsValid_And_ErrorMessage()
    {
        var vm = new PageInputViewModel(pageCount: 10, _ => { });
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.Input = "5";

        fired.Should().Contain(nameof(PageInputViewModel.IsValid));
        fired.Should().Contain(nameof(PageInputViewModel.ErrorMessage));
    }

    [Fact]
    public void AcceptCommand_CanExecute_FollowsValidity()
    {
        var vm = new PageInputViewModel(pageCount: 10, _ => { });
        vm.AcceptCommand.CanExecute(null).Should().BeFalse();

        vm.Input = "3";
        vm.AcceptCommand.CanExecute(null).Should().BeTrue();

        vm.Input = "999";
        vm.AcceptCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Constructor_NegativePageCount_Throws()
    {
        var act = () => new PageInputViewModel(pageCount: -1, _ => { });
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
