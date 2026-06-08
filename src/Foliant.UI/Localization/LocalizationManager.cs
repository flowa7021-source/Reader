using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Threading;
using Foliant.Application.Services;

namespace Foliant.UI.Localization;

/// <summary>
/// Singleton-обёртка над <see cref="ResourceManager"/>. Умеет hot-switch
/// культуры — рейзит <c>PropertyChanged("Item[]")</c>, что в WPF означает
/// «переколбасить все индексные биндинги», поэтому весь UI перерисовывается
/// без перезапуска.
///
/// Singleton нужен для XAML-биндинга вида:
/// <code>{Binding Source={x:Static loc:LocalizationManager.Instance}, Path=[Key]}</code>.
/// Тот же экземпляр регистрируется в DI как <see cref="ILocalizationService"/>.
/// </summary>
public sealed class LocalizationManager : ILocalizationService
{
    private static readonly ResourceManager Resources = new(
        baseName: "Foliant.UI.Resources.Strings",
        assembly: typeof(LocalizationManager).Assembly);

    private CultureInfo _currentCulture = CultureInfo.GetCultureInfo("en");

    public static LocalizationManager Instance { get; } = new();

    private LocalizationManager()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentCulture => _currentCulture.Name;

    public string this[string key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            return Resources.GetString(key, _currentCulture) ?? key;
        }
    }

    /// <summary>Resolves <paramref name="key"/> and formats the resulting template with
    /// <paramref name="args"/> in the current culture. Centralising
    /// <see cref="string.Format(IFormatProvider, string, object?[])"/> here — where the format string
    /// is the <em>resolved resource</em> (an opaque value), not a string-literal resource key —
    /// keeps localized formatting in one place and avoids the literal-key-as-format-string pattern at
    /// call sites.</summary>
    /// <param name="key">Resource key whose value is a composite format string (e.g. <c>"Pages: {0}"</c>).</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The formatted, localized string.</returns>
    public string Format(string key, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return string.Format(_currentCulture, this[key], args);
    }

    public void SetCulture(string culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);

        var ci = CultureInfo.GetCultureInfo(culture);
        if (Equals(ci, _currentCulture))
        {
            return;
        }

        _currentCulture = ci;
        Thread.CurrentThread.CurrentUICulture = ci;
        CultureInfo.DefaultThreadCurrentUICulture = ci;

        // "Item[]" — конвенция WPF: «обновить все индексные биндинги».
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
    }
}
