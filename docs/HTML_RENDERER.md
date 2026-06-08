# HTML/CSS visual renderer (EPUB · FB2 · MOBI)

> Статус: **Вектор 2 завершён + доработки.** PR-2a (spike) — проект `Foliant.Rendering.Html`; PR-2b
> (EPUB), PR-2c (FB2/MOBI) — разводка по движкам через общий `HtmlPaginator`; PR-2d — author-CSS
> каскад (linked `<link>` + `<style>`, см. §10). Бэклог: зум-reflow, попиксельный текст-слой,
> картинки FB2/MOBI.

## 1. Зачем

EPUB/FB2/MOBI **открываются** уже в Phase 1, но `RenderPageAsync` рисует **белый холст** —
визуального рендера нет (работает только текстовый слой для поиска/FTS). Этот компонент закрывает
крупнейший пользовательский пробел: настоящий рендер этих форматов.

## 2. Выбор технологии — pure-managed, не SkiaSharp

Конвейер: **AngleSharp (HTML5+CSS parse) → вычисление стилей → block+inline layout
(`SixLabors.Fonts.TextMeasurer`) → растеризация (`SixLabors.ImageSharp.Drawing`) → `Image<Bgra32>` →
`byte[]` BGRA32 → `RenderColorMap.ApplyTheme`**.

Почему **SixLabors (ImageSharp.Drawing + Fonts)**, а не SkiaSharp (как тентативно предполагалось в
roadmap-плане): SkiaSharp тянет **нативную** `libSkiaSharp`, что ломает главный инвариант проекта —
кросс-платформенную проверяемость на Linux/CI без нативки. SixLabors — **полностью managed**, без
нативных зависимостей; `SixLabors.ImageSharp` уже в графе зависимостей (Image-движок). Проверено в
sandbox: AngleSharp парсит HTML, ImageSharp.Drawing растеризует текст в `Image<Bgra32>` на headless
Linux. AngleSharp — managed HTML5+CSS парсер, толерантный к «грязному» HTML реальных книг.

## 3. Границы: проект `Foliant.Rendering.Html`

Отдельный кросс-платформенный проект engine-уровня (тот же band, что `Foliant.Engines.*`: может
ссылаться на `Foliant.Domain` + сторонние). Три движка (EPUB/FB2/MOBI) ссылаются **на него**; он —
ни на один из них (однонаправленная граница engine → renderer). Layout/paint на 100 % общий для трёх
форматов — выносим один раз, тестируем в полной изоляции от zip/MOBI-контейнерных забот.

### Публичный API (граница движок ↔ рендерер)

Маленькая, value-in/value-out поверхность — движки владеют всем I/O (zip, контейнеры), рендерер —
чистая функция от (html, ресурсы, опции):

```csharp
public interface IHtmlRenderer
{
    HtmlRenderResult RenderPage(HtmlRenderRequest request); // layout + paint одной страницы-слайса
    HtmlLayout Layout(HtmlRenderRequest request);           // только layout (для пагинации/PageCount)
}
```

- `HtmlRenderRequest(string Html, IResourceResolver Resources, HtmlViewport Viewport, RenderTheme Theme)`.
- `HtmlViewport(int ContentWidthPx, int PageHeightPx, HtmlMargins Margins, double BaseFontSizePx, double ScalePx, int PageIndexInChapter)` — фиксированный вьюпорт для пагинации; `ScalePx` = px-на-CSS-px после zoom.
- `HtmlRenderResult(int WidthPx, int HeightPx, int Stride, byte[] Bgra32, int PageCountInChapter)` — движок оборачивает это в свой `IPageRender` (рендерер **не** зависит от `IPageRender`).
- `HtmlLayout(IReadOnlyList<DrawCommand> Commands, int TotalContentHeightPx, int PageCount)` — артефакт, из которого считается пагинация (и в будущем — текстовый слой).
- `IResourceResolver { bool TryResolveImage(string src, out ReadOnlyMemory<byte> bytes); bool TryResolveCss(string href, out string css); }` — движок реализует поверх своего контейнера (EPUB: `EpubBook.Content.Images` / `Content.Css`); `TryResolveCss` — **default-метод** (по умолчанию `false`), так что только container-backed резолверы (EPUB) его переопределяют; `NullResourceResolver` — заглушка.
- `FontStore` — загружает встроенные шрифты, `Font Resolve(GenericFontFamily family, bool bold, bool italic, float sizePx)`.

## 4. Конвейер

| Стадия | MVP | Отложено |
|---|---|---|
| **Parse** | AngleSharp `HtmlParser.ParseDocument`, обход DOM | — |
| **Style** | UA-default таблица по тегам (h1–h6, p, div, blockquote, ul/ol/li, b/strong, i/em, code/pre, br) + **author-CSS каскад** (PR-2d: `<style>`-блоки + `<link>`-таблицы через `AngleSharp.Css`; селекторы тег/класс/id/атрибут/комбинаторы + специфичность + `!important`) + inline `style=""`; свойства: font-weight/style/size/family, color (вкл. `rgb()`/`rgba()`/`hsl()`/`hsla()`), text-align, `display` (none/block/inline), margin/margin-top/bottom; наследование font/color | `@media`, `@font-face`/web-fonts, `text-indent` и прочие box-свойства; **никогда:** floats, tables, flex/grid, positioning, JS |
| **Layout** | один block formatting context, блоки сверху вниз; inline → line-boxes с greedy word-wrap через `TextMeasurer.MeasureSize`; списки с маркером; `<img>` по ширине контента | вложенные BFC, floats, widow/orphan, «не рвать строку» |
| **Paint** | `ImageSharp.Drawing` `DrawText`/`DrawImage` на `Image<Bgra32>` → `CopyPixelDataTo` → `ApplyTheme` | — |

**Координаты:** render-пиксели, origin top-left, y вниз (как `PageGeometry`/ImageSharp). Все CSS-длины
умножаются на `Viewport.ScalePx` при резолве — layout уже в device-px.

## 5. Пагинация — фиксированный вьюпорт (выбранный вариант)

Глава HTML — один длинный reflowable-поток, но `IDocument` отдаёт фиксированный `PageCount` и
`RenderPageAsync(pageIndex)`. **Выбрано: фиксированные страницы по высоте** (а не одна высокая
картинка на главу и не гибрид), потому что весь стек завязан на фикс-размер bitmap'а и осмысленный
`PageCount`: `MemoryPageCache` считает размер записи как `Stride*HeightPx` и держит sticky-окно ±5
**страниц**; `ThumbnailStrip`/`MultiPage` листают индексы; `WpfPrintService` печатает один
`RenderPageAsync(index)` на физический лист. Одна гигантская картинка на главу ломает учёт кэша,
миниатюры и печать; у `WriteableBitmap` есть практический лимит размера.

Механика:
- Layout считается **один раз** на (глава, ширина-контента, scale) → `TotalContentHeightPx` + список
  `DrawCommand` в непагинированном пространстве. `pagesInChapter = max(1, ceil(total / PageHeightPx))`.
- Слайс страницы — `[pageIndex*PageHeightPx, +PageHeightPx)`; paint эмитит только команды,
  пересекающие слайс, со сдвигом `-sliceTop`.
- **Глобальный индекс ↔ (глава, страница):** движок хранит `pagesInChapter[]` + prefix-sum;
  `PageCount = Σ pagesInChapter`; `GlobalToLocal` — бинпоиск по prefix-sum.
- **PageCount без полного рендера:** layout дёшев (только измерение текста, без растеризации). Главы
  лэйаутятся **лениво** (на первом заходе) с мемоизацией `(глава, widthBucket, scaleBucket) → HtmlLayout`;
  до этого — провизорная оценка (1 страница/глава или по символам), уточняется фоном. VM уже терпит
  async-уточнение (`_renderGeneration`-supersede).
- **Zoom/ширина → reflow:** меняют `ScalePx`/ширину → меняют переносы → `PageCount`. Бакетируем как
  render-кэш (`ZoomBucket`, шаг 25 %); позицию чтения ре-якорим по **смещению в контенте** (глава +
  доля прокрутки), а не по номеру страницы.

## 6. Шрифты — детерминизм и CI-safe

Хост-шрифты различаются между машинами → для воспроизводимого, кросс-платформенно одинакового вывода
**встраиваем шрифты** (embedded resources), `SystemFonts` — только как fallback.

Встроено: **Liberation** (Serif/Sans/Mono × Regular/Bold/Italic/BoldItalic = 12 начертаний, ~4.4 МБ),
лицензия **SIL OFL 1.1** (разрешает встраивание/распространение с ПО). Покрытие Latin + Cyrillic +
Greek — достаточно для EPUB/FB2 на EN/RU; metric-compatible с Times/Arial/Courier. (DejaVu
рассматривался ради широкого Unicode, но в базовой поставке нет Serif-Italic/Sans-Oblique начертаний —
неполное покрытие стилей; Liberation даёт полный набор из 12 faces.)

`FontStore` строит `FontCollection` из встроенных faces один раз; `Resolve(generic, bold, italic, size)`
маппит CSS-generic → семейство: `serif`/body → Liberation Serif, `sans-serif` → Sans, `monospace`/
`code`/`pre` → Mono; bold/italic выбирают соответствующий face (синтез не нужен — все 12 есть).
Произвольные `font-family` из CSS книги → ближайший generic (MVP). Атрибуция — в `NOTICE.md` +
`Fonts/LICENSE-Liberation.txt`.

## 7. Интеграция (PR-2b/2c)

- **EPUB (2b):** `EpubDocument` хранит весь `EpubBook` (ресурсы); `RenderPageAsync(global)` →
  `Task.Run` → map global→(глава,страница) → `HtmlRenderRequest{ Html=spine[chapter].Content,
  Resources=EpubResourceResolver(book, spinePath), Viewport from opts, Theme }` →
  `_renderer.RenderPage` → обернуть в `EpubPageRender`. `EpubResourceResolver` нормализует `<img src>`
  относительно пути spine-итема → ключ в `book.Content.Images`. Bump engine-версии (инвалидация
  blank-canvas кэша); снять disclaimer; заменить тест `RenderPageAsync_ReturnsBlankWhiteBitmap`.
- **FB2 (2c):** `Fb2ToHtml`-трансформ (`<section>`→`<div>`, `<title>`→`<h*>`, `<emphasis>`→`<em>`,
  `<epigraph>`→`<blockquote>`, `<empty-line>`→`<br>`…) → тот же `IHtmlRenderer`. Хранить per-page
  HTML (сейчас — только flatten-текст). Картинки (`<binary>` base64) — отложены.
- **MOBI (2c):** уже даёт HTML (`MobiHtml`) — хранить raw HTML на запись (плюс stripped-текст для
  поиска) → тот же рендерер. Картинки (image-records) — отложены.
- **Текстовый слой (будущее, 2d):** из позиционированных `TextDrawCommand` можно вывести точные
  `TextRun(text,x,y,w,h)` per-page — точные прямоугольники подсветки поиска. Затрагивает допущения
  координат текст-слоя → отдельный PR.
- **Кэш:** новый тип кэша не нужен. `MemoryPageCache` ключит по `(fingerprint, global page, engineVersion,
  zoomBucket, flags)`. Рендерер добавляет свой **layout-мемо** внутри документа per `(глава,
  widthBucket, scaleBucket)` (re-slice страниц одной главы — только paint). Декодированные `<img>`
  кэшируются на время layout главы.

## 8. Риски и стратегия тестов

- **Детерминизм между платформами** — главный риск: anti-aliasing/хинтинг/суб-пиксель различаются.
  Митигация: (1) встроенные шрифты (нет дрейфа системных); (2) тесты проверяют **грубые,
  пороговые свойства, не точные пиксели**: «непусто» (число не-белых пикселей > 0), «пусто» (0 для
  empty HTML), «чернил в ожидаемом коридоре», монотонность (больше текста ⇒ ≥ чернил; bold ⇒ ≥
  чернил; крупнее шрифт ⇒ ≥ чернил), инверсия в Dark-теме. Никаких pixel-exact checksum / image-snapshot
  для bitmap'а (флейки на матрице ОС). `Verify` можно для **списка команд** layout (детерминирован
  метриками шрифта), не для картинки.
- **Производительность:** рендер тяжелее `Array.Fill` → off-UI (`Task.Run`); переиспользовать
  `MemoryPageCache` (sticky ±5); мемоизация `HtmlLayout` (листание внутри главы — только paint); кэш
  декодированных `<img>`; ленивый/фоновый подсчёт `PageCount`; бакетированный zoom. BenchmarkDotNet-кейс
  «рендер репрезентативной главы».
- **Память:** высокие главы не материализуются одной картинкой (фикс-страницы); cap upscaling до
  вьюпорта; layout-мемо хранит лёгкие команды, не bitmap'ы; эвикт при закрытии документа.
- **Корректность:** битый HTML (AngleSharp терпит), отсутствующая картинка (skip), нулевые размеры
  (`Math.Max(1, …)` — ImageSharp бросает на 0-размерности), отсутствующие глифы (покрытие Liberation +
  fallback) — деградируют, а не бросают; ошибка рендера не роняет вкладку (VM уже ловит, но рендерер
  предпочитает «пусто-но-валидно»).

## 9. Разбиение на PR

- **PR-2a (этот):** проект `Foliant.Rendering.Html` + встроенный шрифт + тонкий `HtmlRenderer` (block+
  inline+bold/italic+font-size+color+word-wrap на синтетической HTML-строке; `<img>` через
  stub-resolver) + тесты (грубые свойства) + этот документ + строка в `ARCHITECTURE.md`/`NOTICE.md`.
  **Без** разводки по движкам.
- **PR-2b:** EPUB — реальный layout/пагинация/картинки; prefix-sum `PageCount`; `EpubResourceResolver`;
  bump engine-версии; снять disclaimer.
- **PR-2c:** FB2 (`Fb2ToHtml`) + MOBI (raw HTML) переиспользуют тот же `IHtmlRenderer`; снять
  disclaimer'ы. Картинки FB2/MOBI — отложены.

## 10. Author-CSS каскад (PR-2d)

Книги, чья типографика жила во **внешнем CSS**, до PR-2d рендерились только по UA-defaults. Теперь
рендерер применяет настоящий (MVP) каскад над тем же небольшим набором свойств.

**Сбор CSS (внутри рендерера, не движка).** `LayoutEngine.Run` после парса DOM собирает источники в
порядке документа: текст каждого `<style>`-блока + содержимое каждого `<link rel="stylesheet">`,
резолвится out-of-band через `IResourceResolver.TryResolveCss(href)`. `HtmlChapter`/`HtmlRenderRequest`
не меняются — движку (EPUB) достаточно реализовать `TryResolveCss`; `<style>` рендерер достаёт из DOM
сам. Метадата-элементы (`<style>`/`<script>`/`<head>`/`<title>`/`<link>`/`<meta>`/`<base>`/`<noscript>`/
`<template>`) исключаются из обхода — их содержимое никогда не рисуется как текст.

**Матчинг (`AuthorStylesheet`).** Источники парсятся через `AngleSharp.Css` (`CssParser.ParseStyleSheet`);
берутся только top-level `ICssStyleRule` (at-rules — `@media`/`@font-face`/`@import`… — игнорируются в
MVP). Для элемента селекторы матчатся **движком AngleSharp** (`ICssStyleRule.TryMatch(el, null, out
Priority)`), который заодно даёт специфичность. Поддержан весь грамматический набор селекторов
AngleSharp (тег/класс/id/атрибут/комбинаторы/псевдоклассы); патологический/неподдержанный селектор —
skip (не бросает в layout).

**Порядок каскада (`StyleResolver`).** Объявления применяются возрастающе по приоритету (последнее
побеждает):

1. UA-defaults (таблица по тегам),
2. author-normal — по специфичности, затем по исходному порядку правила,
3. inline `style=""` (normal),
4. author-`!important` — по специфичности/порядку,
5. inline `style` `!important`.

Свойства маппятся на `ComputedStyle` через общий `ApplyDeclaration` (тот же путь, что у inline):
`color` (named/hex/`rgb()`/`rgba()`/`hsl()`/`hsla()` — AngleSharp нормализует даже именованные цвета в
`rgba(...)`, поэтому `CssColors` понимает функциональную нотацию), `font-family`/`font-size`/
`font-weight`/`font-style`, `text-align`, `display` (`none` → элемент и поддерево не рендерятся;
`block`/`inline`/`inline-block` → переключение потока), `margin`/`margin-top`/`margin-bottom`.

**Граница.** `TryResolveCss` — default-метод `IResourceResolver` (возвращает `false`), поэтому FB2/MOBI
(на `NullResourceResolver`) и стаб-резолверы не затронуты; реально переопределяет его только
`EpubResourceResolver` (CSS из `EpubBook.Content.Css`, candidate-логика path/Key/filename — как у
картинок). CSS-less глава идёт по fast-path без аллокаций (`AuthorStylesheet.Empty`).
