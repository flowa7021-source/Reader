namespace Foliant.Domain;

/// <summary>
/// Описание желаемой защиты PDF: user/owner пароли + набор разрешённых действий
/// (<see cref="PdfPermissions"/>). Pure-data; immutable; без I/O. Криптоалгоритм
/// (AES-256, PDF 2.0) фиксирован в реализации <c>IPdfEncryptionService</c> —
/// домен не моделирует параметры шифра (R/V/length), чтобы не утечь cos-уровень
/// в публичную поверхность.
///
/// <para>Парольная модель (ISO 32000-2 §7.6.4.3): открытие требует user или owner
/// пароля; permissions enforce-ятся пока документ открыт user-паролем. Передача
/// одного и того же значения в оба поля — допустимая практика «однопарольной»
/// защиты (Acrobat это поддерживает): тогда любой, кто знает пароль, получает
/// полный owner-доступ.</para>
///
/// <para>Инварианты валидируются в ctor: оба пароля — не null; owner-пароль —
/// не пустой (owner без пароля = документ без защиты от снятия ограничений).
/// User-пароль допускается пустым: спецификация трактует это как «открывается
/// без пароля, но с permission-ограничениями» — типовой сценарий для публикации.</para>
/// </summary>
/// <param name="UserPassword">Пароль user'а (обычное открытие документа). Может быть пустой
/// строкой; null недопустим — используйте <see cref="string.Empty"/>.</param>
/// <param name="OwnerPassword">Owner-пароль (полный доступ, снимает permission-ограничения).
/// Не должен быть null/пустым.</param>
/// <param name="AllowedPermissions">Какие действия разрешены user'у. <see cref="PdfPermissions.None"/>
/// = полный read-only; <see cref="PdfPermissions.All"/> = ограничений нет (но документ всё
/// равно зашифрован).</param>
public sealed record PdfEncryptionSpec(
    string UserPassword,
    string OwnerPassword,
    PdfPermissions AllowedPermissions)
{
    /// <summary>Проверяет инварианты <see cref="PdfEncryptionSpec"/> и возвращает экземпляр.
    /// Вынесено в фабрику, потому что primary-constructor record'а не позволяет throw'нуть
    /// до присваивания (а <c>with</c>-копии всё равно идут через тот же ctor — если они
    /// нарушат инвариант, повторный вызов <c>Validate</c> в point-of-use поможет).</summary>
    public static PdfEncryptionSpec Create(string userPassword, string ownerPassword, PdfPermissions allowedPermissions)
    {
        System.ArgumentNullException.ThrowIfNull(userPassword);
        System.ArgumentException.ThrowIfNullOrEmpty(ownerPassword);
        return new PdfEncryptionSpec(userPassword, ownerPassword, allowedPermissions);
    }
}
