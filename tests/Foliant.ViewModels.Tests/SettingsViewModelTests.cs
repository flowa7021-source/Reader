using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Application.Settings;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class SettingsViewModelTests
{
    private static SettingsViewModel CreateVm(
        AppSettings? initial = null,
        ISettingsService? settings = null,
        ILocalizationService? localization = null)
    {
        settings ??= Substitute.For<ISettingsService>();
        settings.Current.Returns(initial ?? AppSettings.Default);

        localization ??= Substitute.For<ILocalizationService>();
        localization.CurrentCulture.Returns("ru");

        return new SettingsViewModel(settings, localization);
    }

    [Fact]
    public void Constructor_LoadsFromCurrentSettings()
    {
        var initial = AppSettings.Default with
        {
            Theme = "Dark",
            Language = "en",
            Cache = new CacheSettings { DiskLimitBytes = 2L * 1024 * 1024 * 1024, ClearOnExit = true },
        };

        var vm = CreateVm(initial);

        vm.SelectedTheme.Should().Be("Dark");
        vm.SelectedLanguage.Should().Be("en");
        vm.DiskCacheLimitGb.Should().BeApproximately(2.0, 0.001);
        vm.ClearCacheOnExit.Should().BeTrue();
    }

    [Fact]
    public async Task SaveCommand_PersistsThroughSettingsService()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default);
        var localization = Substitute.For<ILocalizationService>();
        localization.CurrentCulture.Returns("ru");
        var vm = CreateVm(settings: settings, localization: localization);
        vm.SelectedTheme = "HighContrast";

        await vm.SaveCommand.ExecuteAsync(null);

        await settings.Received(1).UpdateAsync(
            Arg.Is<Func<AppSettings, AppSettings>>(f => f(AppSettings.Default).Theme == "HighContrast"),
            Arg.Any<CancellationToken>());
        vm.IsSaved.Should().BeTrue();
    }

    [Fact]
    public async Task SaveCommand_LanguageChanged_CallsLocalizationSetCulture()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default with { Language = "ru" });
        var localization = Substitute.For<ILocalizationService>();
        localization.CurrentCulture.Returns("ru");
        var vm = CreateVm(settings: settings, localization: localization);
        vm.SelectedLanguage = "en";

        await vm.SaveCommand.ExecuteAsync(null);

        localization.Received(1).SetCulture("en");
    }

    [Fact]
    public async Task SaveCommand_LanguageUnchanged_DoesNotCallSetCulture()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default with { Language = "ru" });
        var localization = Substitute.For<ILocalizationService>();
        localization.CurrentCulture.Returns("ru");
        var vm = CreateVm(settings: settings, localization: localization);
        // SelectedLanguage стартует с "ru"; не меняем

        await vm.SaveCommand.ExecuteAsync(null);

        localization.DidNotReceive().SetCulture(Arg.Any<string>());
    }

    [Fact]
    public void AvailableThemes_ContainsExpectedSet()
    {
        var vm = CreateVm();

        vm.AvailableThemes.Should().BeEquivalentTo(["Auto", "Light", "Dark", "HighContrast"]);
    }

    [Fact]
    public void AvailableLanguages_ContainsRuAndEn()
    {
        var vm = CreateVm();

        vm.AvailableLanguages.Should().BeEquivalentTo(["ru", "en"]);
    }

    // ───── IsSaved auto-reset (S9/D) ─────

    [Fact]
    public async Task IsSaved_ResetsToFalse_WhenThemeChangedAfterSave()
    {
        var vm = CreateVm();
        await vm.SaveCommand.ExecuteAsync(null);
        vm.IsSaved.Should().BeTrue();

        vm.SelectedTheme = "Dark";

        vm.IsSaved.Should().BeFalse();
    }

    [Fact]
    public async Task IsSaved_ResetsToFalse_WhenLanguageChangedAfterSave()
    {
        var vm = CreateVm();
        await vm.SaveCommand.ExecuteAsync(null);

        vm.SelectedLanguage = "en";

        vm.IsSaved.Should().BeFalse();
    }

    [Fact]
    public async Task IsSaved_ResetsToFalse_WhenDiskLimitChangedAfterSave()
    {
        var vm = CreateVm();
        await vm.SaveCommand.ExecuteAsync(null);

        vm.DiskCacheLimitGb = 10.0;

        vm.IsSaved.Should().BeFalse();
    }

    [Fact]
    public async Task IsSaved_ResetsToFalse_WhenClearOnExitChangedAfterSave()
    {
        var vm = CreateVm();
        await vm.SaveCommand.ExecuteAsync(null);

        vm.ClearCacheOnExit = !vm.ClearCacheOnExit;

        vm.IsSaved.Should().BeFalse();
    }

    // ───── ResetToDefaults (S9/D) ─────

    [Fact]
    public void ResetToDefaultsCommand_RestoresDefaultValues()
    {
        var custom = AppSettings.Default with
        {
            Theme = "Dark",
            Language = "en",
            Cache = new CacheSettings { DiskLimitBytes = 10L * 1024 * 1024 * 1024, ClearOnExit = true },
        };
        var vm = CreateVm(custom);

        vm.ResetToDefaultsCommand.Execute(null);

        vm.SelectedTheme.Should().Be(AppSettings.Default.Theme);
        vm.SelectedLanguage.Should().Be(AppSettings.Default.Language);
        vm.DiskCacheLimitGb.Should().BeApproximately(
            AppSettings.Default.Cache.DiskLimitBytes / (1024.0 * 1024 * 1024), 0.001);
        vm.ClearCacheOnExit.Should().Be(AppSettings.Default.Cache.ClearOnExit);
    }

    [Fact]
    public async Task ResetToDefaultsCommand_AfterSave_SetIsSavedFalse()
    {
        var vm = CreateVm(AppSettings.Default with { Theme = "Dark" });
        await vm.SaveCommand.ExecuteAsync(null);
        vm.IsSaved.Should().BeTrue();

        vm.ResetToDefaultsCommand.Execute(null);

        vm.IsSaved.Should().BeFalse();
    }

    [Fact]
    public void ResetToDefaultsCommand_DoesNotCallSaveOnService()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default with { Theme = "Dark" });
        var vm = CreateVm(settings: settings);

        vm.ResetToDefaultsCommand.Execute(null);

        settings.DidNotReceive().UpdateAsync(
            Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>());
    }

    // ───── S9/E: OCR settings ─────

    [Fact]
    public void Constructor_LoadsOcrSettingsFromCurrent()
    {
        var initial = AppSettings.Default with
        {
            Ocr = new OcrSettings
            {
                DefaultLanguage = "deu",
                MaxParallelPages = 2,
                AutoOcrOpenedScans = true,
            },
        };

        var vm = CreateVm(initial);

        vm.OcrLanguage.Should().Be("deu");
        vm.MaxParallelOcrPages.Should().Be(2);
        vm.AutoOcrOpenedScans.Should().BeTrue();
    }

    [Fact]
    public async Task SaveCommand_PersistsOcrSettings()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default);
        var localization = Substitute.For<ILocalizationService>();
        localization.CurrentCulture.Returns("ru");
        var vm = CreateVm(settings: settings, localization: localization);
        vm.OcrLanguage = "fra";
        vm.MaxParallelOcrPages = 8;
        vm.AutoOcrOpenedScans = true;

        await vm.SaveCommand.ExecuteAsync(null);

        await settings.Received(1).UpdateAsync(
            Arg.Is<Func<AppSettings, AppSettings>>(f =>
                f(AppSettings.Default).Ocr.DefaultLanguage == "fra" &&
                f(AppSettings.Default).Ocr.MaxParallelPages == 8 &&
                f(AppSettings.Default).Ocr.AutoOcrOpenedScans),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsSaved_ResetsToFalse_WhenOcrLanguageChangedAfterSave()
    {
        var vm = CreateVm();
        await vm.SaveCommand.ExecuteAsync(null);

        vm.OcrLanguage = "fra";

        vm.IsSaved.Should().BeFalse();
    }

    [Fact]
    public async Task IsSaved_ResetsToFalse_WhenMaxParallelOcrPagesChangedAfterSave()
    {
        var vm = CreateVm();
        await vm.SaveCommand.ExecuteAsync(null);

        vm.MaxParallelOcrPages = 1;

        vm.IsSaved.Should().BeFalse();
    }

    [Fact]
    public async Task IsSaved_ResetsToFalse_WhenAutoOcrOpenedScansChangedAfterSave()
    {
        var vm = CreateVm();
        await vm.SaveCommand.ExecuteAsync(null);

        vm.AutoOcrOpenedScans = !vm.AutoOcrOpenedScans;

        vm.IsSaved.Should().BeFalse();
    }

    [Fact]
    public void ResetToDefaults_RestoresOcrDefaults()
    {
        var custom = AppSettings.Default with
        {
            Ocr = new OcrSettings { DefaultLanguage = "fra", MaxParallelPages = 1, AutoOcrOpenedScans = true },
        };
        var vm = CreateVm(custom);

        vm.ResetToDefaultsCommand.Execute(null);

        vm.OcrLanguage.Should().Be(AppSettings.Default.Ocr.DefaultLanguage);
        vm.MaxParallelOcrPages.Should().Be(AppSettings.Default.Ocr.MaxParallelPages);
        vm.AutoOcrOpenedScans.Should().Be(AppSettings.Default.Ocr.AutoOcrOpenedScans);
    }

    // ───── OCR model tier ─────

    [Fact]
    public void AvailableOcrTiers_ContainsAllThreeTiers()
    {
        var vm = CreateVm();

        vm.AvailableOcrTiers.Should().BeEquivalentTo(
            [OcrModelTier.Basic, OcrModelTier.Standard, OcrModelTier.Full]);
    }

    [Fact]
    public void Constructor_LoadsOcrModelTierFromCurrent()
    {
        var initial = AppSettings.Default with
        {
            Ocr = new OcrSettings { ModelTier = OcrModelTier.Full },
        };

        var vm = CreateVm(initial);

        vm.OcrModelTier.Should().Be(OcrModelTier.Full);
    }

    [Fact]
    public async Task SaveThenLoad_OcrModelTier_RoundTrips()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default);
        AppSettings? persisted = null;
        await settings.UpdateAsync(
            Arg.Do<Func<AppSettings, AppSettings>>(f => persisted = f(AppSettings.Default)),
            Arg.Any<CancellationToken>());
        var vm = CreateVm(settings: settings);
        vm.OcrModelTier = OcrModelTier.Standard;

        await vm.SaveCommand.ExecuteAsync(null);
        persisted.Should().NotBeNull();
        settings.Current.Returns(persisted);
        vm.LoadFromCurrent();

        vm.OcrModelTier.Should().Be(OcrModelTier.Standard);
        persisted!.Ocr.ModelTier.Should().Be(OcrModelTier.Standard);
    }

    [Fact]
    public async Task IsSaved_ResetsToFalse_WhenOcrModelTierChangedAfterSave()
    {
        var vm = CreateVm();
        await vm.SaveCommand.ExecuteAsync(null);
        vm.IsSaved.Should().BeTrue();

        vm.OcrModelTier = OcrModelTier.Full;

        vm.IsSaved.Should().BeFalse();
    }

    [Fact]
    public void ResetToDefaults_RestoresDefaultOcrModelTier()
    {
        var custom = AppSettings.Default with
        {
            Ocr = new OcrSettings { ModelTier = OcrModelTier.Full },
        };
        var vm = CreateVm(custom);

        vm.ResetToDefaultsCommand.Execute(null);

        vm.OcrModelTier.Should().Be(AppSettings.Default.Ocr.ModelTier);
        vm.OcrModelTier.Should().Be(OcrModelTier.Basic);
    }
}
