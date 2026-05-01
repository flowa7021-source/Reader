using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Foliant.ViewModels;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class LicenseImportViewModelTests
{
    private readonly ILicenseManager _manager = Substitute.For<ILicenseManager>();

    [Fact]
    public void Empty_Inputs_ImportCannotExecute()
    {
        var vm = new LicenseImportViewModel(_manager);

        vm.ImportCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void OnlyOneFieldFilled_ImportStillCannotExecute()
    {
        var vm = new LicenseImportViewModel(_manager);
        vm.LicenseJson = "{\"User\":\"alice\"}";

        vm.ImportCommand.CanExecute(null).Should().BeFalse();

        vm.LicenseJson = "";
        vm.SignatureBase64 = "abc==";

        vm.ImportCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void BothFieldsFilled_ImportCanExecute()
    {
        var vm = new LicenseImportViewModel(_manager);
        vm.LicenseJson = "{\"User\":\"alice\"}";
        vm.SignatureBase64 = "abc==";

        vm.ImportCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Import_DelegatesToManager_TrimsInputs()
    {
        var lic = new License("alice", "Pro", DateTimeOffset.UtcNow.AddYears(1), []);
        _manager.ImportAsync("payload", "sig", Arg.Any<CancellationToken>())
                .Returns(LicenseValidationResult.Valid(lic));
        var vm = new LicenseImportViewModel(_manager);
        vm.LicenseJson = "  payload  ";
        vm.SignatureBase64 = "  sig  ";

        await vm.ImportCommand.ExecuteAsync(null);

        await _manager.Received(1).ImportAsync("payload", "sig", Arg.Any<CancellationToken>());
        vm.LastResult!.Status.Should().Be(LicenseStatus.Valid);
        vm.WasImportedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task Import_ManagerRejects_LastResultReflectsAndNoSuccessFlag()
    {
        _manager.ImportAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(LicenseValidationResult.Invalid("Bad signature"));
        var vm = new LicenseImportViewModel(_manager);
        vm.LicenseJson = "x";
        vm.SignatureBase64 = "y";

        await vm.ImportCommand.ExecuteAsync(null);

        vm.LastResult!.Status.Should().Be(LicenseStatus.Invalid);
        vm.WasImportedSuccessfully.Should().BeFalse();
    }
}
