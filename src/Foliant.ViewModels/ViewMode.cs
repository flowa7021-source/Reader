namespace Foliant.ViewModels;

/// <summary>Раскладка страниц в области просмотра документа.</summary>
public enum ViewMode
{
    /// <summary>Одна страница за раз.</summary>
    SinglePage,

    /// <summary>Непрерывная вертикальная лента всех страниц.</summary>
    Continuous,

    /// <summary>Разворот: текущая и следующая страница рядом.</summary>
    TwoPage,
}

/// <summary>Режим подгонки масштаба под область просмотра.</summary>
public enum FitMode
{
    /// <summary>Масштаб задаётся вручную; под размер окна не пересчитывается.</summary>
    ActualSize,

    /// <summary>Подогнать по ширине страницы.</summary>
    FitWidth,

    /// <summary>Подогнать страницу целиком (по ширине и высоте).</summary>
    FitPage,
}
