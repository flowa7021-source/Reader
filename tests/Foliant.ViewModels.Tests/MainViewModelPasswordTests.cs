using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Application.Settings;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

/// <summary>
/// Покрывает retry-loop открытия зашифрованного PDF в <see cref="MainViewModel"/>: промпт пароля,
/// повтор с введённым паролем, отмена пользователем и headless-путь без промпт-сервиса.
/// <para><c>OpenDocumentUseCase</c> — sealed-конкретный класс, поэтому «бросить пароль-исключение,
/// затем отдать документ» мы делаем через сценарный <c>IPasswordAwareDocumentLoader</c>-мок,
/// которым кормим реальный use-case (он проверяет <c>File.Exists</c> → нужен temp-файл).</para>
/// </summary>
public sealed class MainViewModelPasswordTests : IDisposable
{
    private readonly string _path;

    public MainViewModelPasswordTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"foliant-pwd-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(_path, "%PDF-1.4 stub"u8.ToArray());
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
        }
        catch
        {
            /* best-effort */
        }
    }

    [Fact]
    public async Task OpenDocument_WhenPasswordRequired_PromptsAndRetriesWithPassword()
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(1);
        // Первый вызов (password == null) → требует пароль; со «swordfish» отдаёт документ.
        var loader = ScriptedLoader(password => password == "swordfish"
            ? doc
            : throw DocumentPasswordRequiredException.ForPath(_path));
        var prompt = Substitute.For<IPasswordPrompt>();
        prompt.RequestPasswordAsync(_path, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("swordfish");
        var vm = CreateVm(loader, prompt);

        await vm.OpenDocumentFromPathAsync(_path, default);

        // Промпт вызван (attempt 0) и tab создан на успешно открытом документе.
        await prompt.Received(1).RequestPasswordAsync(_path, 0, Arg.Any<CancellationToken>());
        vm.Tabs.Should().HaveCount(1);
        vm.SelectedTab.Should().BeSameAs(vm.Tabs[0]);
        vm.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenDocument_WhenUserCancelsPrompt_NoTabAddedNoError()
    {
        var loader = ScriptedLoader(_ => throw DocumentPasswordRequiredException.ForPath(_path));
        var prompt = Substitute.For<IPasswordPrompt>();
        // null == пользователь нажал Cancel.
        prompt.RequestPasswordAsync(_path, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var vm = CreateVm(loader, prompt);

        await vm.OpenDocumentFromPathAsync(_path, default);

        vm.Tabs.Should().BeEmpty();
        vm.SelectedTab.Should().BeNull();
        // Отмена — это не ошибка: статус-строка не должна заполняться сообщением.
        vm.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenDocument_WrongPasswordThenCorrect_IncrementsAttempt()
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(1);
        // Принимает только верный пароль; «wrong» снова бросает password-required.
        var loader = ScriptedLoader(password => password == "correct-horse"
            ? doc
            : throw DocumentPasswordRequiredException.ForPath(_path));
        var prompt = Substitute.For<IPasswordPrompt>();
        // attempt 0 → неверный, attempt 1 → верный.
        prompt.RequestPasswordAsync(_path, 0, Arg.Any<CancellationToken>()).Returns("wrong");
        prompt.RequestPasswordAsync(_path, 1, Arg.Any<CancellationToken>()).Returns("correct-horse");
        var vm = CreateVm(loader, prompt);

        await vm.OpenDocumentFromPathAsync(_path, default);

        // Промпт показан дважды с растущим attempt; на третьей загрузке — успех.
        await prompt.Received(1).RequestPasswordAsync(_path, 0, Arg.Any<CancellationToken>());
        await prompt.Received(1).RequestPasswordAsync(_path, 1, Arg.Any<CancellationToken>());
        vm.Tabs.Should().HaveCount(1);
    }

    [Fact]
    public async Task OpenDocument_NoPromptService_SurfacesError()
    {
        var loader = ScriptedLoader(_ => throw DocumentPasswordRequiredException.ForPath(_path));
        // Промпт-сервис не зарегистрирован (headless): исключение должно всплыть в
        // catch (InvalidOperationException) и попасть в StatusMessage (старое поведение).
        var vm = CreateVm(loader, passwordPrompt: null);

        await vm.OpenDocumentFromPathAsync(_path, default);

        vm.Tabs.Should().BeEmpty();
        vm.StatusMessage.Should().NotBeEmpty();
        vm.StatusMessage.Should().Contain(_path);
    }

    // ───── helpers ─────

    // Мульти-интерфейсный loader: CanLoad→true, password-aware LoadAsync(path,password,ct)
    // делегирует в сценарную функцию (она решает: бросить «нужен пароль» или вернуть документ).
    private IDocumentLoader ScriptedLoader(Func<string?, IDocument> byPassword)
    {
        var loader = Substitute.For<IDocumentLoader, IPasswordAwareDocumentLoader>();
        loader.CanLoad(Arg.Any<string>()).Returns(true);
        ((IPasswordAwareDocumentLoader)loader)
            .LoadAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(byPassword(ci.ArgAt<string?>(1))));
        return loader;
    }

    private static MainViewModel CreateVm(IDocumentLoader loader, IPasswordPrompt? passwordPrompt)
    {
        var useCase = new OpenDocumentUseCase([loader], NullLogger<OpenDocumentUseCase>.Instance);

        // Фабрика отдаёт рабочий tab на любом документе (аннотации/закладки замоканы пустыми).
        Func<IDocument, string, DocumentTabViewModel> factory = (doc, path) =>
        {
            var search = Substitute.For<ISearchService>();
            search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
            var ann = Substitute.For<IAnnotationService>();
            ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
            var bm = Substitute.For<IBookmarkService>();
            bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
            return new DocumentTabViewModel(doc, path, search, ann, bm, NullLogger<DocumentTabViewModel>.Instance);
        };

        var recents = Substitute.For<IRecentsService>();
        recents.GetAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default);
        var localization = Substitute.For<ILocalizationService>();
        var indexer = Substitute.For<IDocumentIndexer>();

        return new MainViewModel(
            useCase, factory, recents, settings, localization, indexer,
            NullLogger<MainViewModel>.Instance,
            passwordPrompt: passwordPrompt);
    }
}
