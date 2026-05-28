namespace Foliant.Application.Services;

/// <summary>
/// Опции OCR-распознавания. <see cref="MinConfidence"/> — пороговое значение в [0..1]: регионы с
/// <c>TextRun.Confidence &lt; MinConfidence</c> отбрасываются движком до возврата. По умолчанию 0
/// (всё пропускаем); типичная зашумлённость скана хорошо сглаживается 0.5–0.6.
/// </summary>
public sealed record OcrOptions(string Languages = "eng+rus", double MinConfidence = 0.0);
