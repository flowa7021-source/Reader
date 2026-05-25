using FluentAssertions;
using Foliant.ViewModels;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class LabelInputViewModelTests
{
    [Fact]
    public void EmptyInitial_IsInvalid()
    {
        var vm = new LabelInputViewModel("Rename", "New name:", initialText: "", _ => { });

        vm.IsValid.Should().BeFalse();
        vm.AcceptCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void NonEmptyInitial_IsValid()
    {
        var vm = new LabelInputViewModel("Rename", "New name:", initialText: "Chapter 3", _ => { });

        vm.IsValid.Should().BeTrue();
        vm.AcceptCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Whitespace_IsInvalid()
    {
        var vm = new LabelInputViewModel("Rename", "x", initialText: "ok", _ => { });
        vm.Input = "   ";

        vm.IsValid.Should().BeFalse();
    }

    [Fact]
    public void AcceptCommand_InvokesCallback_WithTrimmedText()
    {
        string? received = null;
        var vm = new LabelInputViewModel("Rename", "x", "ok", text => received = text);
        vm.Input = "   trimmed  ";

        vm.AcceptCommand.Execute(null);

        received.Should().Be("trimmed");
    }

    [Fact]
    public void CancelCommand_InvokesCancelCallback()
    {
        bool cancelled = false;
        var vm = new LabelInputViewModel("Rename", "x", "ok", _ => { }, () => cancelled = true);

        vm.CancelCommand.Execute(null);

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void MaxLength_Honored()
    {
        var vm = new LabelInputViewModel("Rename", "x", "ok", _ => { }, maxLength: 5);
        vm.Input = "12345";
        vm.IsValid.Should().BeTrue();

        vm.Input = "123456";
        vm.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NegativeMaxLength_Throws()
    {
        var act = () => new LabelInputViewModel("x", "x", "x", _ => { }, maxLength: -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DialogTitleAndPrompt_AreSurfaced_Verbatim()
    {
        var vm = new LabelInputViewModel("Переименовать", "Новое имя:", "first", _ => { });

        vm.DialogTitle.Should().Be("Переименовать");
        vm.Prompt.Should().Be("Новое имя:");
        vm.Input.Should().Be("first");
    }
}
