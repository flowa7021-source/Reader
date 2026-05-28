using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Foliant.Application.Services;
using Foliant.Domain;

namespace Foliant.UI.Imaging;

/// <summary>
/// WPF-реализация <see cref="IPageImageExporter"/>: рендерит страницу через
/// <see cref="IDocument.RenderPageAsync"/>, оборачивает BGRA32-буфер в
/// <see cref="BitmapSource"/> без копии, и записывает PNG/JPEG через стандартные WPF-энкодеры.
/// Атомарная запись: temp + Move в той же папке, чтобы кросс-том Move не зашёл в дегенерацию
/// в copy+delete (теряем atomicity при сбое посреди).
///
/// JPEG quality захардкожен в 90 — sweet spot для документов: маленький артефакт-фон у текста,
/// разумный размер файла. Конфигурируемо через будущий ExportOptions, если понадобится.
/// </summary>
public sealed class WpfPageImageExporter : IPageImageExporter
{
    private const int JpegQuality = 90;

    public IReadOnlyList<string> SupportedFormats { get; } = ["png", "jpg", "jpeg"];

    public async Task ExportAsync(IDocument document, int pageIndex, double zoom, string targetPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, document.PageCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoom);

        string ext = ResolveExtension(targetPath);
        BitmapEncoder encoder = CreateEncoder(ext);

        using IPageRender render = await document.RenderPageAsync(pageIndex, new Foliant.Domain.RenderOptions(Zoom: zoom), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        // DPI у выходного файла — реальный масштаб рендера: 96 × zoom (PageGeometry-совместимо).
        // Это позволяет внешним инструментам корректно восстановить физический размер страницы.
        double dpi = 96.0 * zoom;
        BitmapSource source = BuildBitmapSource(render, dpi);
        encoder.Frames.Add(BitmapFrame.Create(source));

        await WriteAtomicAsync(targetPath, encoder, ct).ConfigureAwait(false);
    }

    private static BitmapSource BuildBitmapSource(IPageRender render, double dpi)
    {
        // BGRA32 совпадает с PixelFormats.Bgra32 — zero-copy через ReadOnlyMemory.ToArray()
        // (BitmapSource.Create требует byte[]; ToArray даёт одну копию, неизбежно для безопасности
        // — IPageRender может быть переиспользован caller'ом).
        byte[] pixels = render.Bgra32.ToArray();
        var bitmap = BitmapSource.Create(
            render.WidthPx,
            render.HeightPx,
            dpi,
            dpi,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            render.Stride);

        // Freeze — кросс-потоковый доступ, иначе WPF может ругаться при записи на не-UI-потоке.
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapEncoder CreateEncoder(string ext) => ext switch
    {
        "PNG" => new PngBitmapEncoder(),
        "JPG" or "JPEG" => new JpegBitmapEncoder { QualityLevel = JpegQuality },
        _ => throw new NotSupportedException($"Unsupported image format '{ext}'. Use one of: png, jpg, jpeg."),
    };

    private static string ResolveExtension(string path)
    {
        // CA1308 ругается на ToLowerInvariant; для расширений сравниваем по
        // upper-invariant и matchим по верхним литералам.
        return Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
    }

    private static async Task WriteAtomicAsync(string targetPath, BitmapEncoder encoder, CancellationToken ct)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = File.Create(tmp))
            {
                encoder.Save(stream);
            }

            ct.ThrowIfCancellationRequested();
            File.Move(tmp, targetPath, overwrite: true);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }
}
