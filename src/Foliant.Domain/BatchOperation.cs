namespace Foliant.Domain;

/// <summary>
/// Тип операции в batch-задаче. Discriminated union (base + sealed leaves): каждый leaf-record
/// несёт спецификацию, нужную соответствующему сервису. Расширяется добавлением нового
/// sealed-record + arm'а в dispatcher. <c>IBatchJobRunner</c> (см. <c>Foliant.Application.Services</c>)
/// выбирает сервис на основании конкретного типа.
///
/// Phase 1 (Q-F29 starter slice): три PDF-mutate операции, которые уже доступны как
/// dialog-driven actions (Q-F13/F14/F15). Merge / image-export / range-extract — следующие
/// PR'ы (тривиальное расширение по тому же шаблону).
/// </summary>
public abstract record BatchOperation
{
    /// <summary>Sealed на уровне ассембли: только две leaf-имплементации ниже.</summary>
    private protected BatchOperation() { }
}

/// <summary>Наложить watermark на каждую страницу с переданной спецификацией.</summary>
public sealed record BatchWatermarkOperation(WatermarkSpec Spec) : BatchOperation;

/// <summary>Добавить header / footer ко всем страницам.</summary>
public sealed record BatchHeaderFooterOperation(HeaderFooterSpec Spec) : BatchOperation;

/// <summary>Срез полей через /CropBox (обратимый).</summary>
public sealed record BatchCropOperation(CropSpec Spec) : BatchOperation;

/// <summary>
/// Описание одного job'а в batch-режиме: один input-файл → одна операция → один output-файл.
/// Caller сам решает naming-конвенцию для output'а; runner не угадывает. Pure-data.
/// </summary>
/// <param name="SourcePath">Путь к input-документу.</param>
/// <param name="Operation">Что сделать (см. <see cref="BatchOperation"/>).</param>
/// <param name="TargetPath">Куда писать output-документ.</param>
public sealed record BatchJob(string SourcePath, BatchOperation Operation, string TargetPath);

/// <summary>Исход одного job'а: успех, ошибка или отмена.</summary>
public enum BatchJobOutcome
{
    /// <summary>Job завершился без исключений; output-файл создан.</summary>
    Succeeded,
    /// <summary>Job упал с исключением; output-файла нет или он битый.</summary>
    Failed,
    /// <summary>Job не запустился или прервался по cancellation token'у.</summary>
    Cancelled,
}

/// <summary>Результат одного job'а — что было запрошено + чем кончилось.</summary>
/// <param name="Job">Оригинальный job (для пары request/response).</param>
/// <param name="Outcome">Исход.</param>
/// <param name="Error">Текст ошибки для <see cref="BatchJobOutcome.Failed"/>; иначе <c>null</c>.</param>
public sealed record BatchJobResult(BatchJob Job, BatchJobOutcome Outcome, string? Error = null);

/// <summary>Снимок прогресса batch'а для UI / лога. Шлётся через <c>IProgress&lt;BatchProgress&gt;</c>
/// после КАЖДОГО завершённого job'а; <see cref="Completed"/> + <see cref="Total"/> монотонно растут.</summary>
/// <param name="Completed">Сколько job'ов уже завершено (любой исход).</param>
/// <param name="Total">Общее число job'ов в текущем прогоне.</param>
/// <param name="LastResult">Результат последнего завершённого job'а — для лога.</param>
public sealed record BatchProgress(int Completed, int Total, BatchJobResult LastResult);
