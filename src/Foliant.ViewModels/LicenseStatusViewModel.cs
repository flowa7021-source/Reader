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
    public LicenseStatusViewModel(LicenseValidationResult? result, DateTimeOffset now)
    {
        Result = result;
        Now = now;
    }

    public LicenseValidationResult? Result { get; }

    public DateTimeOffset Now { get; }

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
