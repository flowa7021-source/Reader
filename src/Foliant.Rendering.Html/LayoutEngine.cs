using System.Diagnostics.CodeAnalysis;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Foliant.Rendering.Html;

/// <summary>
/// Walks an AngleSharp DOM and produces an ordered list of <see cref="DrawCommand"/> in content
/// coordinates using a single top-to-bottom block formatting context with greedy word-wrap.
/// Stateless across calls (all state is per-<see cref="Run"/> local); safe for concurrent use given
/// a thread-safe <see cref="FontStore"/>.
/// </summary>
internal sealed class LayoutEngine
{
    private const float LineHeightFactor = 1.3f;
    private const float MeasureDpi = 72f; // Font size is in px; 72 DPI keeps px == px.

    private readonly FontStore _fonts;
    private readonly ILogger _log;

    public LayoutEngine(FontStore fonts, ILogger log)
    {
        _fonts = fonts;
        _log = log;
    }

    /// <summary>Parses <paramref name="html"/> and lays it out for the given request/viewport.</summary>
    public HtmlLayout Run(string html, HtmlRenderRequest request)
    {
        HtmlViewport vp = request.Viewport;
        double scale = vp.ScalePx <= 0 ? 1.0 : vp.ScalePx;
        int contentWidth = Math.Max(1, vp.ContentWidthPx);

        int leftMargin = Math.Max(0, vp.Margins.Left);
        int rightMargin = Math.Max(0, vp.Margins.Right);
        int topMargin = Math.Max(0, vp.Margins.Top);
        int bottomMargin = Math.Max(0, vp.Margins.Bottom);

        float contentLeft = leftMargin;
        float contentRight = Math.Max(contentLeft + 1, contentWidth - rightMargin);

        var commands = new List<DrawCommand>();
        var ctx = new BlockContext(commands, request.Resources, scale, contentRight)
        {
            CursorY = topMargin,
            PrevMarginBottom = 0,
        };

        IDocument document = ParseSafely(html);
        IElement? body = document.Body;

        ComputedStyle root = new()
        {
            FontSizePx = vp.BaseFontSizePx <= 0 ? 16.0 : vp.BaseFontSizePx,
            Color = Color.Black,
            Family = GenericFontFamily.Serif,
        };

        if (body is not null)
        {
            LayoutChildren(body, root, contentLeft, vp.BaseFontSizePx <= 0 ? 16.0 : vp.BaseFontSizePx, ctx);
        }

        int totalHeight = (int)Math.Ceiling(ctx.CursorY) + bottomMargin;
        totalHeight = Math.Max(totalHeight, topMargin + bottomMargin);

        int pageHeight = Math.Max(1, vp.PageHeightPx);
        int pageCount = Math.Max(1, (int)Math.Ceiling(totalHeight / (double)pageHeight));

        return new HtmlLayout(commands, totalHeight, pageCount);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Robustness contract: the renderer must never throw on content. AngleSharp does not document a closed exception set, so any parse failure degrades to an empty document.")]
    private IDocument ParseSafely(string html)
    {
        try
        {
            return new HtmlParser().ParseDocument(html);
        }
        catch (Exception ex)
        {
            // AngleSharp is extremely tolerant; this is defence-in-depth only.
            _log.LogWarning(ex, "HTML parse failed; rendering empty document.");
            return new HtmlParser().ParseDocument(string.Empty);
        }
    }

    /// <summary>
    /// Lays out the children of <paramref name="parent"/>. Consecutive inline children accumulate into
    /// an inline run that is wrapped when the next block child (or the end) is reached.
    /// </summary>
    private void LayoutChildren(IElement parent, ComputedStyle parentStyle, float inset, double basePx, BlockContext ctx)
    {
        var inline = new List<InlineItem>();

        foreach (INode child in parent.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    AppendText(text.Data, parentStyle, inline);
                    break;

                case IElement element:
                    ComputedStyle style = StyleResolver.Resolve(element.LocalName, element.GetAttribute("style"), parentStyle.InheritTo(), basePx);

                    if (element.LocalName == "br")
                    {
                        inline.Add(InlineItem.Break);
                    }
                    else if (element.LocalName == "img")
                    {
                        FlushInline(inline, parentStyle, inset, ctx);
                        LayoutImage(element, inset, ctx);
                    }
                    else if (style.IsBlock)
                    {
                        FlushInline(inline, parentStyle, inset, ctx);
                        LayoutBlock(element, style, inset, basePx, ctx);
                    }
                    else
                    {
                        // Inline element: descend, accumulating into the same inline run.
                        GatherInline(element, style, basePx, inline, ctx, inset);
                    }

                    break;

                default:
                    // Comments / processing instructions: ignore.
                    break;
            }
        }

        FlushInline(inline, parentStyle, inset, ctx);
    }

    /// <summary>Recursively gathers an inline subtree into the current inline run.</summary>
    private void GatherInline(IElement element, ComputedStyle style, double basePx, List<InlineItem> inline, BlockContext ctx, float inset)
    {
        foreach (INode child in element.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    AppendText(text.Data, style, inline);
                    break;

                case IElement el:
                    ComputedStyle childStyle = StyleResolver.Resolve(el.LocalName, el.GetAttribute("style"), style.InheritTo(), basePx);

                    if (el.LocalName == "br")
                    {
                        inline.Add(InlineItem.Break);
                    }
                    else if (el.LocalName == "img")
                    {
                        // An image inside inline flow: flush, place as its own block-ish row.
                        FlushInline(inline, style, inset, ctx);
                        LayoutImage(el, inset, ctx);
                    }
                    else if (childStyle.IsBlock)
                    {
                        // Block inside inline (unusual): flush and lay out as a block.
                        FlushInline(inline, style, inset, ctx);
                        LayoutBlock(el, childStyle, inset, basePx, ctx);
                    }
                    else
                    {
                        GatherInline(el, childStyle, basePx, inline, ctx, inset);
                    }

                    break;

                default:
                    break;
            }
        }
    }

    private static void AppendText(string? data, ComputedStyle style, List<InlineItem> inline)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        // Collapse whitespace to single spaces by splitting on any run of whitespace.
        foreach (string word in data.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            inline.Add(InlineItem.ForWord(word, style));
        }
    }

    /// <summary>Lays out one block element: top-margin collapse, marker, indent, children, bottom margin.</summary>
    private void LayoutBlock(IElement element, ComputedStyle style, float parentInset, double basePx, BlockContext ctx)
    {
        double scaledTop = style.MarginTopPx * ctx.Scale;
        // Adjacent-margin collapse: the gap is max(prevBottom, thisTop), not the sum.
        ctx.CursorY += Math.Max(0, Math.Max(scaledTop, ctx.PrevMarginBottom) - ctx.PrevMarginBottom);
        ctx.PrevMarginBottom = 0;

        float inset = parentInset + (float)(style.IndentPx * ctx.Scale);

        // List marker for li.
        if (element.LocalName == "li")
        {
            EmitListMarker(element, style, inset, basePx, ctx);
        }

        LayoutChildren(element, style, inset, basePx, ctx);

        double scaledBottom = style.MarginBottomPx * ctx.Scale;
        ctx.CursorY += scaledBottom;
        ctx.PrevMarginBottom = scaledBottom;
    }

    private void EmitListMarker(IElement li, ComputedStyle style, float inset, double basePx, BlockContext ctx)
    {
        ListKind kind = FindListKind(li);
        string marker = kind == ListKind.Ordered ? $"{OrdinalOf(li)}." : "•";
        Font font = ResolveFont(style, ctx.Scale);
        float lineHeight = LineHeight(font);
        float gap = (float)(basePx * ctx.Scale * 0.4);
        float markerWidth = MeasureWidth(marker, font);
        float markerX = Math.Max(0, inset - gap - markerWidth);
        ctx.Commands.Add(new TextDrawCommand(marker, markerX, (float)ctx.CursorY, font, style.Color, lineHeight));
    }

    private static ListKind FindListKind(IElement li)
    {
        IElement? parent = li.ParentElement;
        while (parent is not null)
        {
            if (parent.LocalName == "ol")
            {
                return ListKind.Ordered;
            }

            if (parent.LocalName == "ul")
            {
                return ListKind.Unordered;
            }

            parent = parent.ParentElement;
        }

        return ListKind.Unordered;
    }

    private static int OrdinalOf(IElement li)
    {
        int index = 1;
        IElement? sibling = li.PreviousElementSibling;
        while (sibling is not null)
        {
            if (sibling.LocalName == "li")
            {
                index++;
            }

            sibling = sibling.PreviousElementSibling;
        }

        return index;
    }

    /// <summary>Greedy word-wrap: flush the accumulated inline items into one or more line rows.</summary>
    private void FlushInline(List<InlineItem> inline, ComputedStyle blockStyle, float inset, BlockContext ctx)
    {
        if (inline.Count == 0)
        {
            return;
        }

        float lineWidth = Math.Max(1, ctx.ContentRight - inset);
        var line = new List<Fragment>();
        float lineX = inset;
        float currentWidth = 0;
        float spaceWidth = 0;
        float lineHeight = 0;

        void FlushLine()
        {
            if (line.Count > 0)
            {
                EmitLine(line, inset, lineWidth, currentWidth, blockStyle.Align, (float)ctx.CursorY, lineHeight, ctx);
                ctx.CursorY += lineHeight;
            }

            line.Clear();
            lineX = inset;
            currentWidth = 0;
            lineHeight = 0;
        }

        foreach (InlineItem item in inline)
        {
            if (item.IsBreak)
            {
                if (line.Count == 0)
                {
                    // Empty line from a lone <br>: advance by a default line height.
                    Font brFont = ResolveFont(blockStyle, ctx.Scale);
                    ctx.CursorY += LineHeight(brFont);
                }
                else
                {
                    FlushLine();
                }

                continue;
            }

            ComputedStyle style = item.Style!;
            string word = item.Word!;
            Font font = ResolveFont(style, ctx.Scale);
            float wordWidth = MeasureWidth(word, font);
            spaceWidth = MeasureWidth(" ", font);
            float fontLineHeight = LineHeight(font);

            float advance = line.Count == 0 ? wordWidth : currentWidth + spaceWidth + wordWidth;

            if (line.Count > 0 && advance > lineWidth)
            {
                FlushLine();
            }

            float fragX = line.Count == 0 ? inset : lineX + currentWidth + spaceWidth;
            float gap = line.Count == 0 ? 0 : spaceWidth;
            line.Add(new Fragment(word, fragX, font, style.Color, fontLineHeight));
            currentWidth = line.Count == 1 ? wordWidth : currentWidth + gap + wordWidth;
            lineHeight = Math.Max(lineHeight, fontLineHeight);
        }

        FlushLine();
        inline.Clear();
    }

    private static void EmitLine(List<Fragment> line, float inset, float lineWidth, float usedWidth, TextAlign align, float y, float lineHeight, BlockContext ctx)
    {
        float offset = align switch
        {
            TextAlign.Center => Math.Max(0, (lineWidth - usedWidth) / 2f),
            TextAlign.Right => Math.Max(0, lineWidth - usedWidth),
            _ => 0f,
        };

        foreach (Fragment f in line)
        {
            ctx.Commands.Add(new TextDrawCommand(f.Text, f.X + offset, y, f.Font, f.Color, lineHeight));
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Robustness contract: a malformed/unsupported image must be skipped, not throw. ImageSharp surfaces several unrelated exception types on bad input, so we degrade for any of them.")]
    private void LayoutImage(IElement img, float inset, BlockContext ctx)
    {
        string? src = img.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(src) || !ctx.Resources.TryResolveImage(src, out ReadOnlyMemory<byte> bytes) || bytes.IsEmpty)
        {
            return;
        }

        Image<Bgra32>? decoded = null;
        try
        {
            decoded = Image.Load<Bgra32>(bytes.Span);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to decode image '{Src}'; skipping.", src);
            decoded?.Dispose();
            return;
        }

        float availableWidth = Math.Max(1, ctx.ContentRight - inset);
        float scale = Math.Min(1f, availableWidth / Math.Max(1, decoded.Width));
        float destWidth = decoded.Width * scale;
        float destHeight = decoded.Height * scale;

        // A sub-pixel destination (e.g. a 1px source scaled down in a wide column) would emit a
        // degenerate draw and advance the cursor by a fraction — skip it entirely.
        if (destWidth < 1 || destHeight < 1)
        {
            decoded.Dispose();
            return;
        }

        if (decoded.Width != (int)destWidth || decoded.Height != (int)destHeight)
        {
            int w = Math.Max(1, (int)Math.Round(destWidth));
            int h = Math.Max(1, (int)Math.Round(destHeight));
            decoded.Mutate(c => c.Resize(w, h));
            destWidth = decoded.Width;
            destHeight = decoded.Height;
        }

        ctx.Commands.Add(new ImageDrawCommand(decoded, inset, (float)ctx.CursorY, destWidth, destHeight));
        ctx.CursorY += destHeight;
        ctx.PrevMarginBottom = 0;
    }

    private Font ResolveFont(ComputedStyle style, double scale)
    {
        float sizePx = (float)Math.Max(1.0, style.FontSizePx * scale);
        return _fonts.Resolve(style.Family, style.Bold, style.Italic, sizePx);
    }

    private static float LineHeight(Font font) => font.Size * LineHeightFactor;

    private static float MeasureWidth(string text, Font font)
    {
        var options = new TextOptions(font) { Dpi = MeasureDpi };
        return TextMeasurer.MeasureSize(text, options).Width;
    }

    /// <summary>One positioned word on a line (pre-alignment-offset X).</summary>
    private readonly record struct Fragment(string Text, float X, Font Font, Color Color, float LineHeight);

    /// <summary>Mutable per-run block-formatting state passed by reference through the walk.</summary>
    private sealed class BlockContext
    {
        public BlockContext(List<DrawCommand> commands, IResourceResolver resources, double scale, float contentRight)
        {
            Commands = commands;
            Resources = resources;
            Scale = scale;
            ContentRight = contentRight;
        }

        public List<DrawCommand> Commands { get; }

        public IResourceResolver Resources { get; }

        public double Scale { get; }

        public float ContentRight { get; }

        public double CursorY { get; set; }

        public double PrevMarginBottom { get; set; }
    }
}
