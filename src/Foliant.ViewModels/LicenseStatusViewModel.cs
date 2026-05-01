using System.Globalization;
using Foliant.Domain;

namespace Foliant.ViewModels;

/// <summary>
/// UI-friendly read-only обёртка над <see cref="LicenseValidationResult"/>:
/// разворачивает domain-record в плоский набор флагов и строк, удобных для
/// data-binding в статус-баре, диалоге About и менеджере лицензии.
/// Снимок: создаётся вокруг конкретной пары (result, now) и больше не меняется —
/// при обновлении LicenseStatus в MainViewModel создаётся новый snapshot.
/// </summary>
public sealed class LicenseStatusViewModel
{
    /// <summary>Порог по умолчанию для <see cref="IsExpiringSoon"/>: 30 дней до истечения.
    /// Соответствует UX-ожиданию подсветки лицензии оранжевым в статус-баре.</summary>
    public const int DefaultExpiringSoonDays = 30;

    public LicenseStatusViewModel(
        LicenseValidationResult? result,
        DateTimeOffset now,
        int expiringSoonDays = DefaultExpiringSoonDays)
    {
        if (expiringSoonDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expiringSoonDays), expiringSoonDays, "Must be non-negative.");
        }
        Result = result;
        Now = now;
        ExpiringSoonDays = expiringSoonDays;
    }

    public LicenseValidationResult? Result { get; }

    public DateTimeOffset Now { get; }

    /// <summary>Сколько дней до истечения считается «скоро истекает». Default: 30.</summary>
    public int ExpiringSoonDays { get; }

    public bool HasResult => Result is not null;

    public LicenseStatus? Status => Result?.Status;

    public bool IsValid => Status == LicenseStatus.Valid;

    public bool IsExpired => Status == LicenseStatus.Expired;

    public bool IsInvalid => Status == LicenseStatus.Invalid;

    /// <summary>True если результат отсутствует (null) или domain-status = Missing.</summary>
    public bool IsMissing => Result is null || Status == LicenseStatus.Missing;

    /// <summary>Действующая лицензия — синоним <see cref="IsValid"/> для xaml-bindings,
    /// чтобы UI мог писать <c>Visibility="{Binding IsLicensed,...}"</c>.</summary>
    public bool IsLicensed => IsValid;

    public string User => Result?.License?.User ?? string.Empty;

    public string Sku => Result?.License?.Sku ?? string.Empty;

    public DateTimeOffset? ExpiresAt => Result?.License?.ExpiresAt;

    public string Reason => Result?.Reason ?? string.Empty;

    /// <summary>Дни до истечения. Положительное число — действует ещё столько; отрицательное —
    /// просрочена; <c>null</c> — лицензии нет вообще.</summary>
    public int? DaysUntilExpiry => ExpiresAt is { } at
        ? (int)Math.Floor((at - Now).TotalDays)
        : null;

    /// <summary>True если лицензия валидна, но истекает в пределах <see cref="ExpiringSoonDays"/>
    /// (включительно). Просрочка / Invalid / Missing → false (для них есть отдельные флаги).</summary>
    public bool IsExpiringSoon =>
        IsValid && DaysUntilExpiry is { } d && d >= 0 && d <= ExpiringSoonDays;

    /// <summary>True если лицензия валидна и в её фичах есть <paramref name="featureCode"/>
    /// (case-insensitive, проброс в <see cref="License.HasFeature"/>). Истёкшие /
    /// невалидные лицензии всегда возвращают false — UI должен прятать Pro-функции
    /// до явного продления / активации.</summary>
    public bool HasFeature(string featureCode)
    {
        ArgumentNullException.ThrowIfNull(featureCode);
        return IsValid && Result?.License is { } lic && lic.HasFeature(featureCode);
    }

    /// <summary>Готовая к показу в статус-баре строка. Специально не локализуется
    /// (Sku, User уже идут как есть), форматирование числа — invariant для
    /// предсказуемого CI-сравнения.</summary>
    public string DisplayText
    {
        get
        {
            if (IsMissing)
            {
                return "No license";
            }
            return Status switch
            {
                LicenseStatus.Valid =>
                    DaysUntilExpiry is { } d
                        ? string.Create(CultureInfo.InvariantCulture, $"{Sku} — {User} ({d} d left)")
                        : $"{Sku} — {User}",
                LicenseStatus.Expired => $"{Sku} expired",
                LicenseStatus.Invalid => string.IsNullOrEmpty(Reason) ? "Invalid license" : $"Invalid: {Reason}",
                _ => "No license",
            };
        }
    }
}
