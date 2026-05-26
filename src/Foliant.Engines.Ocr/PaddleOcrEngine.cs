using System.IO;
using System.Runtime.InteropServices;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;

namespace Foliant.Engines.Ocr;

/// <summary>
/// OCR-движок на PaddleOCR (in-process через Sdcb.PaddleOCR).
/// Принимает растровый рендер страницы (BGRA32), возвращает <see cref="TextLayer"/>
/// с одним <see cref="TextRun"/> на распознанную строку (с её bounding-box в пикселях
/// рендера). Это богаче, чем у Tesseract-варианта, и даёт координаты строк «из коробки».
///
/// Модели грузятся оффлайн из <c>native/paddleocr/</c> рядом с приложением
/// (детектор + классификатор — общие, распознаватель — по набору языков). Их кладёт
/// <c>tools/fetch-natives.ps1</c> / инсталлятор.
/// </summary>
/// <remarks>
/// ВАЖНО: <see cref="PaddleOcrAll"/> не потокобезопасен — все вызовы сериализуются через
/// <see cref="SemaphoreSlim"/>. Движок регистрируется синглтоном и переиспользует
/// загруженные модели между вызовами.
///
/// Точные сигнатуры Sdcb.PaddleOCR (FromDirectory / ModelVersion / PaddleDevice) и версии
/// пакетов нужно сверить при сборке на Windows-стенде — см. план, раздел «Риски».
/// </remarks>
public sealed class PaddleOcrEngine : IOcrEngine, IDisposable
{
    // Версия для CacheKey.EngineVersion: меняем при апгрейде набора моделей PaddleOCR,
    // чтобы автоматически инвалидировать старый OCR-кэш.
    private const int EngineVersion = 1;

    private readonly ILogger<PaddleOcrEngine> _log;
    private readonly string _modelsRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<OcrModelKind, PaddleOcrAll> _engines = [];
    private bool _disposed;

    public PaddleOcrEngine(ILogger<PaddleOcrEngine> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _modelsRoot = Path.Combine(AppContext.BaseDirectory, "native", "paddleocr");
    }

    public int Version => EngineVersion;

    public async Task<TextLayer> RecognizeAsync(
        IPageRender render,
        int pageIndex,
        OcrOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);

        OcrModelKind kind = OcrLanguageMap.Resolve(options.Languages);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => RecognizeCore(render, pageIndex, kind), ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private TextLayer RecognizeCore(IPageRender render, int pageIndex, OcrModelKind kind)
    {
        using Mat bgr = ToBgrMat(render);
        PaddleOcrAll engine = GetEngine(kind);
        PaddleOcrResult result = engine.Run(bgr);

        var runs = result.Regions
            .Select(r =>
            {
                Rect box = r.Rect.BoundingRect();
                return new TextRun(r.Text, box.X, box.Y, box.Width, box.Height);
            })
            .OrderBy(r => r.Y)
            .ThenBy(r => r.X)
            .ToList();

        _log.LogDebug("PaddleOCR page {Page}: {Count} regions ({Kind})", pageIndex, runs.Count, kind);
        return runs.Count == 0 ? TextLayer.Empty(pageIndex) : new TextLayer(pageIndex, runs);
    }

    private PaddleOcrAll GetEngine(OcrModelKind kind)
    {
        if (_engines.TryGetValue(kind, out PaddleOcrAll? existing))
        {
            return existing;
        }

        FullOcrModel model = LoadModel(kind);
        var all = new PaddleOcrAll(model, PaddleDevice.Mkldnn())
        {
            AllowRotateDetection = true,
            Enable180Classification = false,
        };
        _engines[kind] = all;
        return all;
    }

    private FullOcrModel LoadModel(OcrModelKind kind)
    {
        string detDir = Path.Combine(_modelsRoot, "det");
        string clsDir = Path.Combine(_modelsRoot, "cls");
        string recName = kind == OcrModelKind.Cyrillic ? "cyrillic" : "latin";
        string recDir = Path.Combine(_modelsRoot, "rec", recName);
        string labelFile = Path.Combine(recDir, "label.txt");

        if (!Directory.Exists(detDir) || !Directory.Exists(recDir))
        {
            throw new DirectoryNotFoundException(
                $"PaddleOCR models not found under '{_modelsRoot}' (need det/ and rec/{recName}/). " +
                "Run tools/fetch-natives.ps1 or install the OCR model tier.");
        }

        DetectionModel det = DetectionModel.FromDirectory(detDir, ModelVersion.V4);
        ClassificationModel cls = ClassificationModel.FromDirectory(clsDir);
        RecognizationModel rec = RecognizationModel.FromDirectory(recDir, labelFile, ModelVersion.V4);
        return new FullOcrModel(det, cls, rec);
    }

    private static Mat ToBgrMat(IPageRender render)
    {
        int w = render.WidthPx;
        int h = render.HeightPx;
        int stride = render.Stride;
        byte[] src = render.Bgra32.ToArray();

        using var bgra = new Mat(h, w, MatType.CV_8UC4);
        int rowBytes = w * 4;
        for (int y = 0; y < h; y++)
        {
            Marshal.Copy(src, y * stride, bgra.Ptr(y), rowBytes);
        }

        var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
        return bgr;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (PaddleOcrAll engine in _engines.Values)
        {
            engine.Dispose();
        }

        _engines.Clear();
        _gate.Dispose();
    }
}
