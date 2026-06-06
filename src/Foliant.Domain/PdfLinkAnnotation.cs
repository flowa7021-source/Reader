using System.Diagnostics.CodeAnalysis;

namespace Foliant.Domain;

/// <summary>
/// Одна link-аннотация PDF (ISO 32000-1 §12.5.6.5): элемент массива <c>/Annots</c> страницы с
/// <c>/Subtype /Link</c>, делающий прямоугольную область кликабельной. Цель ссылки задаётся либо
/// action'ом <c>/A</c> (<c>&lt;&lt; /S /URI /URI (…) &gt;&gt;</c> — внешний URI; <c>&lt;&lt; /S /GoTo
/// /D dest &gt;&gt;</c> — переход внутри документа), либо напрямую <c>/Dest dest</c> на самой
/// аннотации. Тут — read-only-проекция: страница-источник, внешний URI и/или целевая страница
/// внутреннего перехода.
///
/// <para>Pure-data, immutable. <c>with</c>-копии идут мимо какой-либо валидации — это допустимо, как и
/// у других domain-record'ов.</para>
/// </summary>
/// <param name="PageIndex">0-based индекс страницы, которая <b>содержит</b> ссылку (где лежит
/// <c>/Annots</c>-элемент).</param>
/// <param name="Uri">Внешний URI (из <c>/A &lt;&lt; /S /URI /URI (…) &gt;&gt;</c>) либо
/// <see langword="null"/>, если ссылка не внешняя.</param>
/// <param name="TargetPageIndex">0-based индекс целевой страницы внутреннего перехода (GoTo / прямой
/// <c>/Dest</c> с destination-массивом <c>[pageRef …]</c>) либо <see langword="null"/>. Для ссылок на
/// именованный пункт назначения (name/string-dest) цель в MVP не разрешается и остаётся
/// <see langword="null"/>.</param>
[SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
    Justification = "Read-only projection of the PDF /URI string token verbatim; modelling it as System.Uri would force parsing that may throw on malformed URIs — out of scope for a best-effort listing.")]
[SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
    Justification = "Read-only projection of the PDF /URI string token verbatim; modelling it as System.Uri would force parsing that may throw on malformed URIs — out of scope for a best-effort listing.")]
public sealed record PdfLinkAnnotation(int PageIndex, string? Uri, int? TargetPageIndex);
