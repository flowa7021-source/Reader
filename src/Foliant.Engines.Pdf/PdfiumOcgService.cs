using Foliant.Application.Services;
using Foliant.Domain;
using UglyToad.PdfPig.Core;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Реализация <see cref="IPdfOcgService"/> (Q-F8 MVP). PDFiumCore 146.x не экспонирует
/// OCG-bindings (проверено: <c>strings PDFiumCore.dll | grep -i ocg</c> возвращает
/// пусто), поэтому работа идёт целиком managed-кодом: чтение — через PdfPig
/// (<see cref="PdfOcgCosReader"/>), запись — инкрементальным апдейтом
/// (<see cref="PdfIncrementalWriter"/> + <see cref="PdfOcgCosWriter"/>) поверх
/// исходных байт. Pdfium при рендере подхватывает обновлённое состояние слоёв
/// из <c>/OCProperties → /D</c> сам.
///
/// <para>Класс не держит native-ресурсов и не использует общий PDFium-lock
/// сервисов (<c>NativeGate</c>): все операции pure-managed и потокобезопасны
/// на уровне отдельного вызова. Для MVP это допустимо (никакая UI-цепочка не
/// вызывает Read/Set параллельно над одним и тем же файлом).</para>
///
/// <para>Запись использует инкрементальный апдейт: оригинальные байты PDF не
/// меняются, новый <c>/OCProperties</c>-объект дописывается в конец + xref-секция
/// с <c>/Prev</c> на старую xref. Это сохраняет подписи (PAdES B-LT/B-LTA) и
/// валидность любого reader'а (Acrobat / pdfium / poppler).</para>
/// </summary>
public sealed class PdfiumOcgService : IPdfOcgService
{
    public async Task<IReadOnlyList<PdfLayer>> ReadLayersAsync(string sourcePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        return await Task.Run<IReadOnlyList<PdfLayer>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = PdfOcgCosReader.Read(source);
            return snapshot.Layers;
        }, ct).ConfigureAwait(false);
    }

    public async Task SetLayerVisibilityAsync(
        string sourcePath,
        string targetPath,
        IReadOnlyDictionary<int, bool> visibilityByIndex,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(visibilityByIndex);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => RewriteCore(source, visibilityByIndex, ct), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static byte[] RewriteCore(
        byte[] source, IReadOnlyDictionary<int, bool> desired, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = PdfOcgCosReader.Read(source);
        if (snapshot.OCPropertiesDict is null || snapshot.OcgRefs.Count == 0)
        {
            // PDF без OCG — копируем как есть (паттерн «no-op = identity»).
            return source;
        }

        var (off, on) = ComputeTargetSets(snapshot, desired);
        string ocPropertiesBody =
            PdfOcgCosWriter.WriteOcPropertiesWithVisibility(snapshot.OCPropertiesDict, off, on);

        return AppendIncrementalUpdate(source, snapshot, ocPropertiesBody);
    }

    private static (IReadOnlyList<IndirectReference> Off, IReadOnlyList<IndirectReference> On)
        ComputeTargetSets(
            PdfOcgCosReader.OcgSnapshot snapshot,
            IReadOnlyDictionary<int, bool> desired)
    {
        // Берём фактическое состояние каждого слоя — желаемое из словаря (если есть),
        // иначе текущее. Затем выписываем explicit ON / OFF массивы (BaseState = /ON,
        // поэтому OFF — явно скрытые, ON — оставляем пустым в типичном случае).
        var offList = new List<IndirectReference>();
        var onList = new List<IndirectReference>();
        for (int i = 0; i < snapshot.Layers.Count; i++)
        {
            bool target = desired.TryGetValue(i, out bool wanted)
                ? wanted
                : snapshot.Layers[i].IsVisible;

            var iref = snapshot.OcgRefs[i];
            if (target)
            {
                // BaseState=/ON → слой видим по дефолту, явно перечислять не нужно.
                continue;
            }

            offList.Add(iref);
        }

        return (offList, onList);
    }

    private static byte[] AppendIncrementalUpdate(
        byte[] source, PdfOcgCosReader.OcgSnapshot snapshot, string ocPropertiesBody)
    {
        long prevXrefOffset = PdfIncrementalWriter.FindLastStartXref(source);
        long rootObj = snapshot.RootObjectNumber;
        long trailerSize = snapshot.TrailerSize;

        var updated = new List<PdfIncrementalWriter.RawObject>();
        var added = new List<PdfIncrementalWriter.RawObject>();

        if (snapshot.OCPropertiesRef is { } ocpRef)
        {
            // Перезаписываем индирект /OCProperties.
            updated.Add(new PdfIncrementalWriter.RawObject(ocpRef, ocPropertiesBody));
            return PdfIncrementalWriter.Append(
                source, added, updated, prevXrefOffset, rootObj, trailerSize);
        }

        // /OCProperties inline в catalog'е → переписываем catalog (/Root в трейлере).
        string catalogBody = PdfCatalogCosWriter.WriteCatalogWithInlineOCProperties(
            snapshot, ocPropertiesBody);
        updated.Add(new PdfIncrementalWriter.RawObject(
            new IndirectReference(rootObj, 0), catalogBody));
        return PdfIncrementalWriter.Append(
            source, added, updated, prevXrefOffset, rootObj, trailerSize);
    }

    private static async Task WriteAtomicAsync(string targetPath, byte[] bytes, CancellationToken ct)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, targetPath, overwrite: true);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best-effort
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort
        }
    }
}
