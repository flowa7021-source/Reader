namespace Foliant.Domain;

public interface IFormController
{
    IReadOnlyList<FormField> Fields { get; }

    Task SetValueAsync(string fieldName, string value, CancellationToken ct);

    Task<IReadOnlyDictionary<string, string>> ExportValuesAsync(CancellationToken ct);

    Task ImportValuesAsync(IReadOnlyDictionary<string, string> values, CancellationToken ct);
}

public sealed record FormField(
    string Name,
    FormFieldKind Kind,
    string? Value,
    bool IsRequired,
    bool IsReadOnly);

public enum FormFieldKind
{
    Text,
    Checkbox,
    Radio,
    Choice,
    Signature,
    Button,
}
