namespace Foliant.Application.Services;

/// <summary>
/// Опции OCR-распознавания.
/// <list type="bullet">
/// <item><see cref="Languages"/> — Tesseract-style набор кодов через <c>+</c>.</item>
/// <item><see cref="MinConfidence"/> — порог в [0..1]: регионы с
/// <c>TextRun.Confidence &lt; MinConfidence</c> отбрасываются движком до возврата. По умолчанию 0
/// (всё пропускаем); типичная зашумлённость скана сглаживается 0.5–0.6.</item>
/// <item><see cref="RenderZoom"/> — масштаб рендера страницы для подачи в OCR. 1.0 = 96 DPI
/// (слишком низко для большинства движков); 2.0 = 192 DPI — sweet spot для PaddleOCR.
/// По умолчанию <c>2.0</c>: распознавание заметно точнее ценой 4× пикселей и времени.
/// <c>OcrPageUseCase</c> кодирует это значение в cache key, поэтому смена zoom инвалидирует
/// старые кешированные результаты автоматически.</item>
/// </list>
/// </summary>
public sealed record OcrOptions(
    string Languages = "eng+rus",
    double MinConfidence = 0.0,
    double RenderZoom = 2.0);
