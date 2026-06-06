namespace Foliant.Domain;

/// <summary>
/// Один именованный пункт назначения PDF (named destination, ISO 32000-1 §12.3.2): отображение
/// «имя → пункт назначения» (целевая страница + способ её показа). Хранится в catalog name-tree
/// <c>/Names → /Dests</c> (modern-форма) либо в legacy-словаре catalog <c>/Dests</c>; и там, и там
/// значение — destination-массив <c>[pageRef /Fit]</c> (или dict <c>&lt;&lt; /D [pageRef …] &gt;&gt;</c>).
/// По <see cref="Name"/> на такой пункт ссылаются GoTo-действия и outline-элементы.
///
/// <para>Pure-data, immutable. <c>with</c>-копии идут мимо какой-либо валидации — это допустимо, как и
/// у других domain-record'ов.</para>
/// </summary>
/// <param name="Name">Имя пункта назначения (ключ name-tree / legacy-словаря). Уникально в пределах
/// документа; именно по нему пункт добавляется / удаляется.</param>
/// <param name="PageIndex">0-based индекс целевой страницы, на которую указывает пункт назначения.</param>
public sealed record PdfNamedDestination(string Name, int PageIndex);
