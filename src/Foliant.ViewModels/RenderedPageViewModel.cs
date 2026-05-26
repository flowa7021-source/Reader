using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Foliant.Domain;

namespace Foliant.ViewModels;

/// <summary>
/// Одна страница в multi-page раскладке (continuous/two-page). Рендерит свой битмап
/// лениво по запросу View (при реализации элемента в виртуализированном списке) и
/// переотрисовывается после <see cref="Invalidate"/> (смена zoom/темы). Владеет своим
/// <see cref="Render"/> и диспозит его. Поколение отбрасывает устаревший результат.
/// </summary>
public sealed partial class RenderedPageViewModel : ObservableObject, IDisposable
{
    private readonly Func<int, RenderOptions, CancellationToken, Task<IPageRender>> _renderPage;
    private readonly Func<RenderOptions> _optionsProvider;
    private int _generation;
    private bool _disposed;

    /// <summary>0-based индекс страницы документа.</summary>
    public int PageIndex { get; }

    /// <summary>1-based номер страницы для отображения.</summary>
    public int DisplayNumber => PageIndex + 1;

    [ObservableProperty]
    private IPageRender? _render;

    /// <summary>Аннотации этой страницы (снимок) — для overlay в multi-page режимах.
    /// Обновляется владельцем при перестройке/мутации списка аннотаций.</summary>
    [ObservableProperty]
    private IReadOnlyList<Annotation> _annotations = [];

    /// <summary>Создаёт слот страницы. <paramref name="renderPage"/> рендерит страницу по
    /// индексу/опциям; <paramref name="optionsProvider"/> отдаёт текущие <see cref="RenderOptions"/>.</summary>
    public RenderedPageViewModel(
        int pageIndex,
        Func<int, RenderOptions, CancellationToken, Task<IPageRender>> renderPage,
        Func<RenderOptions> optionsProvider)
    {
        ArgumentNullException.ThrowIfNull(renderPage);
        ArgumentNullException.ThrowIfNull(optionsProvider);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        PageIndex = pageIndex;
        _renderPage = renderPage;
        _optionsProvider = optionsProvider;
    }

    /// <summary>Отрисовать страницу, если ещё не отрисована (идемпотентно). Вызывается View
    /// при реализации элемента; повторный вызов при наличии <see cref="Render"/> — no-op.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Per-page render failure must leave the slot blank, not crash the view.")]
    public async Task EnsureRenderedAsync(CancellationToken ct)
    {
        if (Render is not null || _disposed)
        {
            return;
        }

        int generation = Interlocked.Increment(ref _generation);
        try
        {
            IPageRender result = await _renderPage(PageIndex, _optionsProvider(), ct);
            if (_disposed || generation != Volatile.Read(ref _generation))
            {
                result.Dispose();
                return;
            }
            Render = result;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error.
        }
        catch (Exception)
        {
            Render = null; // leave the slot blank; the primary render path logs failures
        }
    }

    /// <summary>Сбросить отрисовку (после смены zoom/темы) — следующий
    /// <see cref="EnsureRenderedAsync"/> перерисует с новыми опциями.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        IPageRender? old = Render;
        Render = null;
        old?.Dispose();
    }

    public void Dispose()
    {
        _disposed = true;
        Interlocked.Increment(ref _generation);
        Render?.Dispose();
        Render = null;
    }
}
