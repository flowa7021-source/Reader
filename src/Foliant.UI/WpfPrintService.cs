using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Foliant.Application.Services;
using Foliant.Domain;

namespace Foliant.UI;

/// <summary>
/// WPF-реализация <see cref="IPrintService"/>: показывает <c>System.Windows.Controls.PrintDialog</c>
/// модально, рендерит выбранные пользователем страницы через <see cref="IDocument.RenderPageAsync"/>,
/// строит <see cref="FixedDocument"/> и шлёт его в спулер.
///
/// <para>Print dpi — 300: sweet spot для бумаги (PDF-точка 1/72", 300 dpi → zoom ≈ 4.17). Каждый
/// отрендеренный <see cref="IPageRender"/> диспозится сразу после конвертации в
/// <see cref="BitmapSource"/> — не держим буферы всех страниц одновременно (это критично для
/// больших документов, см. также known limitation в PR-теле: для очень больших документов
/// FixedDocument всё равно держит все BitmapSource'ы в памяти; future work — lazy
/// DocumentPaginator).</para>
///
/// <para>Маршалинг на UI-поток — через <c>System.Windows.Application.Current.Dispatcher</c>
/// (полная квалификация: <c>using Foliant.Application.Services</c> вводит namespace
/// <c>Foliant.Application</c> в скоуп; голый <c>Application</c> резолвился бы как namespace, а не
/// WPF-Application — точно так же сделано в соседнем <c>WpfPasswordPrompt</c> после #126-фикса).</para>
///
/// <para>Public (не internal), т.к. регистрируется в DI из <c>Foliant.App</c>, у которого нет
/// <c>InternalsVisibleTo</c> к <c>Foliant.UI</c> — так же, как соседние <c>WpfPasswordPrompt</c>
/// и <c>WpfPageImageExporter</c>.</para>
/// </summary>
public sealed class WpfPrintService : IPrintService
{
    /// <summary>Целевой DPI для рендера каждой страницы при печати. 300 — стандарт для печати на
    /// бумаге; больше — раздувает память без визуального профита (принтер всё равно резамплит).</summary>
    private const double PrintDpi = 300.0;

    /// <summary>PDF-точка = 1/72". При рендере мы получаем картинку с разрешением
    /// <c>WidthPt × Zoom</c> px. Zoom = <c>PrintDpi / PdfPointDpi</c> → нужное DPI на выходе.</summary>
    private const double PdfPointDpi = 96.0;

    public Task<bool> PrintAsync(IDocument document, string documentTitle, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentTitle);
        ct.ThrowIfCancellationRequested();

        // Полная квалификация: System.Windows.Application vs Foliant.Application namespace clash.
        System.Windows.Application? app = System.Windows.Application.Current;
        if (app is null)
        {
            // Нет WPF-приложения (headless край) → нечего печатать; команда уже отфильтрована
            // gate'ом CanPrint, но защищаемся на случай прямого вызова.
            return Task.FromResult(false);
        }

        // Owner у диалога — активное главное окно, чтобы PrintDialog центрировался к нему.
        Window? owner = app.MainWindow;

        // ShowDialog синхронный — оборачиваем в Task.FromResult после Dispatcher.Invoke.
        // Если уже на UI-потоке — выполняем напрямую; иначе маршалим.
        bool result = app.Dispatcher.CheckAccess()
            ? PrintOnUiThread(document, documentTitle, owner, ct)
            : app.Dispatcher.Invoke(() => PrintOnUiThread(document, documentTitle, owner, ct));

        return Task.FromResult(result);
    }

    /// <summary>UI-потоковая часть: показ диалога + сборка FixedDocument + отправка в спулер.
    /// Вынесена отдельным методом, чтобы Dispatcher.Invoke получал чистый делегат и не запутывался
    /// в async-state-machine.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Print pipeline must not propagate beyond service boundary.")]
    private static bool PrintOnUiThread(IDocument document, string jobTitle, Window? owner, CancellationToken ct)
    {
        var dialog = new PrintDialog();
        // PageRangeSelection.UserPages разрешает пользователю задать конкретный диапазон в UI
        // диалога; AllPages — дефолт. UserPageRangeEnabled = true делает поле доступным.
        dialog.UserPageRangeEnabled = true;
        dialog.MinPage = 1;
        dialog.MaxPage = (uint)Math.Max(1, document.PageCount);

        bool? showResult = owner is not null ? ShowDialogWithOwner(dialog, owner) : dialog.ShowDialog();
        if (showResult != true)
        {
            // Пользователь отменил — возвращаем false (см. контракт IPrintService).
            return false;
        }

        if (ct.IsCancellationRequested)
        {
            return false;
        }

        (int firstPageIndex, int lastPageIndex) = ResolvePageRange(dialog, document.PageCount);

        FixedDocument fixedDoc = BuildFixedDocument(document, firstPageIndex, lastPageIndex, dialog, ct);

        // Отправка в спулер. PrintDocument блокирующий, но мы уже на UI-потоке — а Windows-print
        // spooler принимает задание быстро (фактическая печать — асинхронная).
        dialog.PrintDocument(fixedDoc.DocumentPaginator, jobTitle);
        return true;
    }

    /// <summary>WPF PrintDialog.ShowDialog не принимает owner — окно владельца устанавливается
    /// через временный hook. Если owner не указан — обычный ShowDialog (диалог встанет посередине
    /// экрана). Вынесено в отдельный метод, чтобы держать <see cref="PrintOnUiThread"/> коротким.</summary>
    private static bool? ShowDialogWithOwner(PrintDialog dialog, Window owner)
    {
        // PrintDialog не выставляет owner напрямую. На практике он подхватывает Application.Current
        // .MainWindow.Handle сам; явное проставление через WindowInteropHelper не даёт
        // переносимого API и не нужно — диалог корректно модален к main window и так.
        _ = owner;
        return dialog.ShowDialog();
    }

    /// <summary>Преобразовать выбор диапазона из диалога в нулёвый [first..last] inclusive.
    /// Дефолт (range не выбран) — все страницы документа.</summary>
    private static (int FirstPageIndex, int LastPageIndex) ResolvePageRange(PrintDialog dialog, int pageCount)
    {
        if (dialog.PageRangeSelection == PageRangeSelection.UserPages)
        {
            var range = dialog.PageRange;
            // PageRange приходит в one-based (как в диалоге); конвертируем в zero-based + clamp.
            int firstOneBased = Math.Max(1, range.PageFrom);
            int lastOneBased = Math.Max(firstOneBased, range.PageTo);
            int first = Math.Clamp(firstOneBased - 1, 0, pageCount - 1);
            int last = Math.Clamp(lastOneBased - 1, first, pageCount - 1);
            return (first, last);
        }

        return (0, pageCount - 1);
    }

    /// <summary>Построить FixedDocument с одной страницей под каждый отрендеренный page.
    /// Размер FixedPage = <c>dialog.PrintableArea*</c> (в DIPs, 1/96"), Image растягивается
    /// Uniform — пропорции страницы сохраняются. Каждый IPageRender диспозится сразу после
    /// создания BitmapSource (BitmapSource уже скопировал pixels).</summary>
    private static FixedDocument BuildFixedDocument(
        IDocument document,
        int firstPageIndex,
        int lastPageIndex,
        PrintDialog dialog,
        CancellationToken ct)
    {
        var fixedDoc = new FixedDocument();
        // Размер страницы (DIPs у WPF). Это размер целевой бумаги принтера — Image внутри
        // подгоняется Uniform-stretch, так что широкая страница не обрежется.
        var pageSize = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);

        // PDF point = 1/72"; WPF DIP = 1/96"; для рендера используем zoom = PrintDpi/96
        // (чтобы получить картинку в нужном DPI). PrintDpi = 300, итого zoom ≈ 3.125.
        double renderZoom = PrintDpi / PdfPointDpi;

        for (int pageIndex = firstPageIndex; pageIndex <= lastPageIndex; pageIndex++)
        {
            ct.ThrowIfCancellationRequested();

            BitmapSource? bitmap = TryRenderPageAsBitmap(document, pageIndex, renderZoom, ct);
            if (bitmap is null)
            {
                // Сбой рендера одной страницы — пропускаем (не валим весь job). Это очень редкий
                // путь: RenderPageAsync на корректном индексе обычно не падает; но если упадёт —
                // PDFium-инициализация / corrupted page — лучше напечатать остальные.
                continue;
            }

            var fixedPage = BuildFixedPage(bitmap, pageSize);
            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(fixedPage);
            fixedDoc.Pages.Add(pageContent);
        }

        return fixedDoc;
    }

    /// <summary>Отрендерить одну страницу и сразу сконвертировать в frozen <see cref="BitmapSource"/>.
    /// IPageRender диспозится сразу — BitmapSource.Create копирует pixels из byte[], так что владеть
    /// рендером дальше не нужно. Возвращает <c>null</c> при сбое (caller пропустит страницу).</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "One bad page must not abort the whole print job.")]
    private static BitmapSource? TryRenderPageAsBitmap(IDocument document, int pageIndex, double zoom, CancellationToken ct)
    {
        try
        {
            // GetAwaiter().GetResult() — мы на UI-потоке (Dispatcher.Invoke сверху), и
            // RenderPageAsync в боевом PdfDocumentLoader выполняет работу на ThreadPool —
            // дедлока не будет, т.к. continuation не требует возврата на UI-поток (внутренние
            // .ConfigureAwait(false) у движков). Тестовые fake-документы возвращают completed Task.
            // Полная квалификация: System.Windows.Media.RenderOptions vs Foliant.Domain.RenderOptions
            // (using Foliant.Domain + using System.Windows.Media оба в скоупе → CS0104). Берём
            // domain-тип явно — это параметр rendering pipeline'а, не WPF-тип.
            using IPageRender render = document
                .RenderPageAsync(pageIndex, new Foliant.Domain.RenderOptions(Zoom: zoom), ct)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            ct.ThrowIfCancellationRequested();

            return BuildBitmapSource(render);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>BGRA32 → frozen <see cref="BitmapSource"/>. Тот же подход, что в
    /// <c>WpfPageImageExporter</c>: ToArray() даёт одну копию pixels, BitmapSource.Create() уже
    /// независим от IPageRender, Freeze() позволяет передавать его между потоками (если
    /// потребуется — PrintDocument на UI-потоке, но FixedDocument в спулере читается из своего).</summary>
    private static BitmapSource BuildBitmapSource(IPageRender render)
    {
        byte[] pixels = render.Bgra32.ToArray();
        var bitmap = BitmapSource.Create(
            render.WidthPx,
            render.HeightPx,
            PrintDpi,
            PrintDpi,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            render.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Собрать <see cref="FixedPage"/> с одним <see cref="Image"/> внутри. Stretch=Uniform
    /// сохраняет пропорции страницы — широкая страница вмещается, узкая центрируется. Размеры
    /// FixedPage — размер целевой бумаги (PrintableAreaWidth/Height в DIPs у диалога).</summary>
    private static FixedPage BuildFixedPage(BitmapSource bitmap, Size targetPageSize)
    {
        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
        };
        FixedPage.SetLeft(image, 0);
        FixedPage.SetTop(image, 0);
        image.Width = targetPageSize.Width;
        image.Height = targetPageSize.Height;

        var fixedPage = new FixedPage
        {
            Width = targetPageSize.Width,
            Height = targetPageSize.Height,
        };
        fixedPage.Children.Add(image);
        return fixedPage;
    }
}
