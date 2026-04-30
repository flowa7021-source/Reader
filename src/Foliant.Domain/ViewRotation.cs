namespace Foliant.Domain;

/// <summary>
/// Поворот страницы при рендере (по часовой стрелке). Не модифицирует PDF
/// — это viewer-only опция, чтобы пользователь мог быстро повернуть
/// landscape-страницу для удобного чтения.
/// </summary>
public enum ViewRotation
{
    None = 0,
    Cw90 = 1,
    Cw180 = 2,
    Cw270 = 3,
}

public static class ViewRotationExtensions
{
    /// <summary>Возвращает поворот в градусах (0/90/180/270).</summary>
    public static int Degrees(this ViewRotation r) => (int)r * 90;
}
