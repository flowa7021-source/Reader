using FluentAssertions;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class MainViewModelTests
{
    private static MainViewModel CreateVm()
    {
        var useCase = new OpenDocumentUseCase([], NullLogger<OpenDocumentUseCase>.Instance);
        Func<IDocument, string, DocumentTabViewModel> factory = (_, _) => throw new NotSupportedException();
        return new MainViewModel(useCase, factory, NullLogger<MainViewModel>.Instance);
    }

    [Fact]
    public void Title_DefaultsToFoliant()
    {
        var vm = CreateVm();

        vm.Title.Should().Be("Foliant");
    }

    [Fact]
    public void StatusMessage_RaisesPropertyChanged_WhenSet()
    {
        var vm = CreateVm();
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
