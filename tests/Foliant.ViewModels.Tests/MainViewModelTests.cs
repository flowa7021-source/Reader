using FluentAssertions;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void Title_DefaultsToFoliant()
    {
        var vm = new MainViewModel();

        vm.Title.Should().Be("Foliant");
    }

    [Fact]
    public void StatusMessage_RaisesPropertyChanged_WhenSet()
    {
        var vm = new MainViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StatusMessage))
            {
                raised = true;
            }
        };

        vm.StatusMessage = "Loading...";

        raised.Should().BeTrue();
        vm.StatusMessage.Should().Be("Loading...");
    }
}
