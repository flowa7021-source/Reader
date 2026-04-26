using System.ComponentModel;

namespace Foliant.Application.Services;

/// <summary>
/// Сервис локализации. Хранит текущую культуру UI и резолвит строки по ключу.
/// Реализует <see cref="INotifyPropertyChanged"/> — рейзит «Item[]» при смене культуры,
/// чтобы все XAML-биндинги вида <c>Path=[Key]</c> автоматически обновились
/// (полное переключение UI без перезапуска — требование S5).
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>Текущий BCP-47 тег культуры (например, "ru" или "en").</summary>
    string CurrentCulture { get; }

    /// <summary>Резолвит строку по ключу для <see cref="CurrentCulture"/>. Если ключ не найден — возвращает сам ключ.</summary>
    string this[string key] { get; }

    /// <summary>Меняет текущую культуру и нотифицирует подписчиков об изменении всех индексных биндингов.</summary>
    void SetCulture(string culture);
}
