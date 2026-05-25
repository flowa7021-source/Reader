namespace Foliant.Domain;

/// <summary>
/// Универсальный wire-формат для append-only event store: discriminator-строка
/// (kind) + JSON-payload, который специфическая команда сама умеет в-/раскодировать.
/// EventStore сам не знает о реальных command-типах — это позволяет добавлять
/// новые команды (`InsertPage`, `RotatePage`, ...) без правки store.
/// </summary>
public sealed record DocumentCommandRecord(string Kind, string PayloadJson);
