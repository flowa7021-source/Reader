using System.Text.Json;
using System.Text.Json.Serialization;
using Foliant.Domain;

namespace Foliant.Engines.Pdf.Editing;

/// <summary>
/// Конкретные команды редактора PDF — сериализуемые намерения (<see cref="IDocumentCommand"/>).
/// Каждая знает свой <c>Kind</c> (дискриминатор для event store) и кодирует payload через
/// source-gen контекст <see cref="PdfCommandJsonContext"/>. Обратной операции нет: отмена —
/// это replay журнала от исходного снимка (см. <see cref="PdfDocumentEditor"/>).
/// </summary>
public sealed record RotatePageCommand(int Index, ViewRotation Rotation) : IDocumentCommand
{
    public const string KindValue = "rotate-page";

    public string Kind => KindValue;

    public DocumentCommandRecord ToRecord() =>
        new(KindValue, JsonSerializer.Serialize(this, PdfCommandJsonContext.Default.RotatePageCommand));
}

public sealed record DeletePageCommand(int Index) : IDocumentCommand
{
    public const string KindValue = "delete-page";

    public string Kind => KindValue;

    public DocumentCommandRecord ToRecord() =>
        new(KindValue, JsonSerializer.Serialize(this, PdfCommandJsonContext.Default.DeletePageCommand));
}

public sealed record ReorderPagesCommand(IReadOnlyList<int> Order) : IDocumentCommand
{
    public const string KindValue = "reorder-pages";

    public string Kind => KindValue;

    public DocumentCommandRecord ToRecord() =>
        new(KindValue, JsonSerializer.Serialize(this, PdfCommandJsonContext.Default.ReorderPagesCommand));
}

public sealed record InsertPagesCommand(string OtherPath, int AtIndex) : IDocumentCommand
{
    public const string KindValue = "insert-pages";

    public string Kind => KindValue;

    public DocumentCommandRecord ToRecord() =>
        new(KindValue, JsonSerializer.Serialize(this, PdfCommandJsonContext.Default.InsertPagesCommand));
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(RotatePageCommand))]
[JsonSerializable(typeof(DeletePageCommand))]
[JsonSerializable(typeof(ReorderPagesCommand))]
[JsonSerializable(typeof(InsertPagesCommand))]
internal sealed partial class PdfCommandJsonContext : JsonSerializerContext;
