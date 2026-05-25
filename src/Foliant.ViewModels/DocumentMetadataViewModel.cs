using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Foliant.Domain;

namespace Foliant.ViewModels;

/// <summary>
/// Read-only обёртка над <see cref="DocumentMetadata"/> для info-диалога.
/// Все свойства возвращают строку, готовую к отображению (placeholder
/// «—» для отсутствующих полей, ISO-формат для дат).
/// </summary>
public sealed partial class DocumentMetadataViewModel : ObservableObject
{
    /// <summary>Placeholder для отсутствующих полей. Видим в UI без локали-зависимого «(none)».</summary>
    public const string MissingPlaceholder = "—";

    private readonly DocumentMetadata _meta;

    public DocumentMetadataViewModel(DocumentMetadata meta, string? filePath, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(meta);
        _meta = meta;
        FilePath = filePath ?? MissingPlaceholder;
        PageCount = pageCount;

        Custom = [];
        foreach (var kv in meta.Custom)
        {
            Custom.Add(new MetadataEntry(kv.Key, kv.Value));
        }
    }

    public string Title => Display(_meta.Title);

    public string Author => Display(_meta.Author);

    public string Subject => Display(_meta.Subject);

    public string Created => DisplayDate(_meta.Created);

    public string Modified => DisplayDate(_meta.Modified);

    public string FilePath { get; }

    public int PageCount { get; }

    /// <summary>Доп. метаданные документа в виде «ключ=значение» — для info-диалога.</summary>
    public ObservableCollection<MetadataEntry> Custom { get; }

    /// <summary>True если хотя бы одно «человеческое» поле заполнено — UI может показать «No metadata available».</summary>
    public bool HasAnyKnownField =>
        !string.IsNullOrWhiteSpace(_meta.Title)
        || !string.IsNullOrWhiteSpace(_meta.Author)
        || !string.IsNullOrWhiteSpace(_meta.Subject)
        || _meta.Created.HasValue
        || _meta.Modified.HasValue;

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? MissingPlaceholder : value;

    private static string DisplayDate(DateTimeOffset? value) =>
        value.HasValue
            ? value.Value.ToString("yyyy-MM-dd HH:mm:ssK", CultureInfo.InvariantCulture)
            : MissingPlaceholder;
}

public sealed record MetadataEntry(string Key, string Value);
