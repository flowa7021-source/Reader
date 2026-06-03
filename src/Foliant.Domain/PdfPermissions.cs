namespace Foliant.Domain;

/// <summary>
/// Восемь permission-флагов, разрешающих/запрещающих действия с зашифрованным PDF
/// (ISO 32000-2 §7.6.4.2 «User access permissions», битовое поле P-entry в encryption
/// dictionary). Здесь моделируем уровень намерения («что разрешено user'у») — то есть
/// каждый флаг означает «действие разрешено», отсутствие флага = «запрещено». Это
/// соответствует UX-формулировкам Acrobat: «Allow printing», «Allow copying» и т. п.
///
/// <para>Биты в этом enum НЕ совпадают с битами P-entry: маппинг в спецификацию
/// (1≡reserved, 3≡print, 4≡modify, 5≡copy, 6≡annotate, 9≡fill-forms, 10≡accessibility,
/// 11≡assemble, 12≡print-hq) — обязанность реализации <c>IPdfEncryptionService</c>;
/// домен остаётся чистым описанием намерения, без знания о cos-кодировке.</para>
/// </summary>
[System.Flags]
public enum PdfPermissions
{
    /// <summary>Запретить все восемь действий (полный read-only).</summary>
    None = 0,

    /// <summary>Разрешить печать (низкое качество, если не задан <see cref="HighQualityPrint"/>).</summary>
    Print = 1 << 0,

    /// <summary>Разрешить изменение содержимого документа (кроме форм/аннотаций/сборки —
    /// они закрываются отдельными флагами).</summary>
    Modify = 1 << 1,

    /// <summary>Разрешить копирование текста/изображений (clipboard, text extraction).</summary>
    Copy = 1 << 2,

    /// <summary>Разрешить добавление/изменение аннотаций.</summary>
    Annotate = 1 << 3,

    /// <summary>Разрешить заполнение AcroForm/XFA полей даже когда <see cref="Modify"/> запрещён.</summary>
    FillForms = 1 << 4,

    /// <summary>Разрешить извлечение текста/изображений для accessibility-инструментов
    /// (screen reader'ов). В PDF 2.0 этот бит зарезервирован и должен быть 1, но мы
    /// сохраняем его как явный флаг намерения — реализация может игнорировать или
    /// форсировать согласно версии спеки.</summary>
    Accessibility = 1 << 5,

    /// <summary>Разрешить сборку документа (insert/rotate/delete pages, создание bookmarks).</summary>
    Assemble = 1 << 6,

    /// <summary>Разрешить печать в высоком качестве (faithful representation).
    /// Если запрещён при разрешённом <see cref="Print"/>, печать ограничена low-resolution.</summary>
    HighQualityPrint = 1 << 7,

    /// <summary>Сахар: все восемь действий разрешены (эквивалент «no restrictions», но при
    /// этом документ всё равно зашифрован user/owner-паролями).</summary>
    All = Print | Modify | Copy | Annotate | FillForms | Accessibility | Assemble | HighQualityPrint,
}
