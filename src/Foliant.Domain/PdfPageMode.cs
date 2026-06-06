namespace Foliant.Domain;

/// <summary>
/// Режим показа вспомогательных панелей при открытии документа (PDF catalog <c>/PageMode</c>,
/// ISO 32000-1 §7.7.2, «Initial View» в Acrobat). Определяет, какая навигационная панель
/// (закладки, миниатюры, слои, вложения) или полноэкранный режим активны при первом показе.
/// </summary>
public enum PdfPageMode
{
    /// <summary>Ключ <c>/PageMode</c> отсутствует — viewer использует своё значение по умолчанию
    /// (как правило <see cref="UseNone"/>).</summary>
    Default,

    /// <summary>Ни одна панель не раскрыта (<c>/UseNone</c>).</summary>
    UseNone,

    /// <summary>Открыта панель закладок / outline'а (<c>/UseOutlines</c>).</summary>
    UseOutlines,

    /// <summary>Открыта панель миниатюр страниц (<c>/UseThumbs</c>).</summary>
    UseThumbs,

    /// <summary>Полноэкранный режим без меню и панелей (<c>/FullScreen</c>).</summary>
    FullScreen,

    /// <summary>Открыта панель Optional Content (слоёв) (<c>/UseOC</c>).</summary>
    UseOC,

    /// <summary>Открыта панель вложений (<c>/UseAttachments</c>).</summary>
    UseAttachments,
}
