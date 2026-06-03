namespace Foliant.Domain;

/// <summary>
/// Снимок одного OCG (Optional Content Group, «PDF layer») в документе. Phase 2 MVP
/// (Q-F8): только плоский список верхнеуровневых слоёв + текущая видимость по
/// умолчанию. Иерархия (parent/child), OCMD (Optional Content Membership Dictionary)
/// и сложные visibility-правила (VE) — Phase 2+/3.
///
/// <para><see cref="Index"/> — 0-based индекс в массиве <c>/OCProperties → /OCGs</c>
/// каталога. Используется как стабильный идентификатор для toggle'а: между чтениями
/// одного и того же PDF индекс не меняется (порядок в массиве сохраняется).</para>
///
/// <para><see cref="Name"/> — UTF-16 строка из <c>/Name</c> словаря OCG; читается как
/// есть, без обрезки/нормализации. Пустая строка допустима.</para>
///
/// <para><see cref="IsVisible"/> — текущая видимость по умолчанию (<c>/D</c>-словарь
/// в <c>/OCProperties</c>): слой видим, если он отсутствует в <c>/OFF</c> массиве
/// (а если массив <c>/ON</c> задан — то слой должен в нём быть). Соответствует PDF
/// spec §8.11.4.4 «Default visibility».</para>
/// </summary>
public sealed record PdfLayer(int Index, string Name, bool IsVisible);
