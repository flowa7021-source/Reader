namespace Foliant.Rendering.Html;

/// <summary>
/// The laid-out (but not yet painted) representation of a chapter: the ordered draw commands in
/// content coordinates, the total content height, and the resulting page count for the viewport.
/// Owns any <see cref="ImageDrawCommand"/> decoded images; <see cref="Dispose"/> releases them.
/// </summary>
public sealed class HtmlLayout : IDisposable
{
    private bool _disposed;

    /// <summary>Creates a layout result.</summary>
    /// <param name="commands">Ordered draw commands in content (pre-pagination) coordinates.</param>
    /// <param name="totalContentHeightPx">Total laid-out content height in pixels (incl. bottom margin).</param>
    /// <param name="pageCount">Number of page slices the content spans (always &#8805; 1).</param>
    public HtmlLayout(IReadOnlyList<DrawCommand> commands, int totalContentHeightPx, int pageCount)
    {
        Commands = commands;
        TotalContentHeightPx = totalContentHeightPx;
        PageCount = pageCount;
    }

    /// <summary>The ordered draw commands, typed as the base <see cref="DrawCommand"/>.</summary>
    public IReadOnlyList<DrawCommand> Commands { get; }

    /// <summary>Total laid-out content height in pixels (top of chapter to end + bottom margin).</summary>
    public int TotalContentHeightPx { get; }

    /// <summary>Number of fixed-height page slices the content spans (always at least 1).</summary>
    public int PageCount { get; }

    /// <summary>Disposes any decoded images owned by <see cref="ImageDrawCommand"/> entries.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (DrawCommand command in Commands)
        {
            if (command is ImageDrawCommand image)
            {
                image.Dispose();
            }
        }
    }
}
