using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Foliant.Domain;

namespace Foliant.Engines.Pdf.Editing;

/// <summary>
/// Чистая функция «применить запись журнала к байтам PDF». Сопоставляет
/// <see cref="DocumentCommandRecord.Kind"/> с соответствующей трансформацией
/// <see cref="PdfPageOps"/>. Вынесено из <see cref="PdfDocumentEditor"/>, чтобы
/// редактор оставался ≤300 строк, а dispatch можно было подменить в unit-тестах.
/// </summary>
public static class PdfCommandDispatcher
{
    public static byte[] Dispatch(byte[] input, DocumentCommandRecord record)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(record);

        // FRAGILE: replay/PDF-mutation boundary — Kind должен совпадать с тем, что
        // записала команда; неизвестный Kind означает повреждённый/чужой журнал.
        return record.Kind switch
        {
            RotatePageCommand.KindValue => Rotate(input, record.PayloadJson),
            DeletePageCommand.KindValue => Delete(input, record.PayloadJson),
            ReorderPagesCommand.KindValue => Reorder(input, record.PayloadJson),
            InsertPagesCommand.KindValue => Insert(input, record.PayloadJson),
            _ => throw new InvalidOperationException($"Unknown command kind '{record.Kind}'."),
        };
    }

    private static byte[] Rotate(byte[] input, string payload)
    {
        var cmd = Deserialize(payload, PdfCommandJsonContext.Default.RotatePageCommand);
        return PdfPageOps.RotatePage(input, cmd.Index, cmd.Rotation);
    }

    private static byte[] Delete(byte[] input, string payload)
    {
        var cmd = Deserialize(payload, PdfCommandJsonContext.Default.DeletePageCommand);
        return PdfPageOps.DeletePage(input, cmd.Index);
    }

    private static byte[] Reorder(byte[] input, string payload)
    {
        var cmd = Deserialize(payload, PdfCommandJsonContext.Default.ReorderPagesCommand);
        return PdfPageOps.ReorderPages(input, [.. cmd.Order]);
    }

    private static byte[] Insert(byte[] input, string payload)
    {
        var cmd = Deserialize(payload, PdfCommandJsonContext.Default.InsertPagesCommand);
        // FRAGILE: IO during replay — the inserted file must still exist at OtherPath.
        byte[] other = File.ReadAllBytes(cmd.OtherPath);
        return PdfPageOps.InsertPages(input, other, cmd.AtIndex);
    }

    private static T Deserialize<T>(string payload, JsonTypeInfo<T> info)
        where T : class =>
        JsonSerializer.Deserialize(payload, info)
            ?? throw new InvalidOperationException($"Null payload for {typeof(T).Name}.");
}
