namespace Foliant.Domain;

/// <summary>
/// Описание текстового водяного знака: текст + визуальные параметры. Pure-data, без I/O —
/// потребляется сервисом наложения, который работает поверх PDFium (см. IWatermarkService).
/// </summary>
/// <param name="Text">Сам водяной знак. Пустой/whitespace недопустим (см. validators).</param>
/// <param name="FontSize">Размер шрифта в PDF points (1/72 inch). Типично 48–96 для A4.</param>
/// <param name="Opacity">Прозрачность 0..1 (0 — невидимо, 1 — непрозрачно). Типично 0.2–0.4.</param>
/// <param name="AngleDegrees">Угол поворота в градусах CCW относительно центра страницы.
/// 45° — классический «диагональный» watermark; 0° — горизонтальный.</param>
/// <param name="R">Red-канал (0..255). Серый <c>128,128,128</c> — частый дефолт.</param>
/// <param name="G">Green-канал (0..255).</param>
/// <param name="B">Blue-канал (0..255).</param>
/// <param name="Range">К каким страницам применить watermark; <c>null</c> — ко всем
/// (Q-F13 «по диапазону»). Парсится из строки через <see cref="PageRange.Parse"/>.</param>
/// <param name="ImagePath">Если задан — image-watermark вместо текстового (Q-F13 «текст +
/// изображение»). Картинка центруется на странице, скейлится до ~40 % ширины с сохранением
/// aspect ratio, поворачивается на <see cref="AngleDegrees"/>, к alpha-каналу применяется
/// <see cref="Opacity"/>. Поддерживаются PNG/JPEG/BMP/GIF/TIFF (декодируется ImageSharp'ом).
/// Когда <c>ImagePath</c> задан, <see cref="Text"/> и color-каналы игнорируются.</param>
public sealed record WatermarkSpec(
    string Text,
    double FontSize,
    double Opacity,
    double AngleDegrees,
    byte R,
    byte G,
    byte B,
    PageRange? Range = null,
    string? ImagePath = null);
