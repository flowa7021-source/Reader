using System.Windows;

namespace Foliant.UI;

internal static class ThemeManager
{
    private static readonly Uri LightUri = new("pack://application:,,,/Foliant.UI;component/Themes/Light.xaml", UriKind.Absolute);
    private static readonly Uri DarkUri = new("pack://application:,,,/Foliant.UI;component/Themes/Dark.xaml", UriKind.Absolute);
    private static readonly Uri HighContrastUri = new("pack://application:,,,/Foliant.UI;component/Themes/HighContrast.xaml", UriKind.Absolute);

    private static ResourceDictionary? _currentTheme;

    // Полностью квалифицированный System.Windows.Application — иначе в WPF temp-проекте
    // (Foliant.UI_*_wpftmp.csproj), который генерирует g.cs для XAML, символ Application
    // разрешается в пространство имён 'Foliant.Application', а не в WPF-тип.
    public static void Apply(string themeName, System.Windows.Application app)
    {
        ArgumentNullException.ThrowIfNull(themeName);
        ArgumentNullException.ThrowIfNull(app);

        Uri uri = themeName switch
        {
            "Dark" => DarkUri,
            "HighContrast" => HighContrastUri,
            _ => LightUri,
        };

        if (_currentTheme is not null)
        {
            app.Resources.MergedDictionaries.Remove(_currentTheme);
        }

        var dict = new ResourceDictionary { Source = uri };
        app.Resources.MergedDictionaries.Add(dict);
        _currentTheme = dict;
    }
}
