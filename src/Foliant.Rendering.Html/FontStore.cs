using System.Globalization;
using System.Reflection;
using SixLabors.Fonts;

namespace Foliant.Rendering.Html;

/// <summary>
/// Loads the twelve bundled Liberation faces (Serif / Sans / Mono &#215; Regular / Bold / Italic /
/// Bold-Italic) once from embedded resources and resolves a concrete <see cref="Font"/> for a
/// generic family + bold/italic + size. All four styles of every family ship as real faces, so the
/// store never synthesizes a style. Thread-safe and immutable after construction; share one instance.
/// </summary>
public sealed class FontStore
{
    private const string ResourcePrefix = "Foliant.Rendering.Html.Fonts.";

    // family -> [Regular, Bold, Italic, BoldItalic]; index via StyleIndex(bold, italic).
    private readonly Dictionary<GenericFontFamily, FontFamily[]> _families;

    /// <summary>
    /// Loads the twelve embedded Liberation faces. The default <see cref="GenericFontFamily.Serif"/>
    /// body face must be present, or construction fails fast.
    /// </summary>
    /// <exception cref="InvalidOperationException">A required embedded font face is missing.</exception>
    public FontStore()
    {
        var collection = new FontCollection();
        Assembly assembly = typeof(FontStore).Assembly;

        _families = new Dictionary<GenericFontFamily, FontFamily[]>
        {
            [GenericFontFamily.Serif] = LoadFamily(collection, assembly, "LiberationSerif"),
            [GenericFontFamily.SansSerif] = LoadFamily(collection, assembly, "LiberationSans"),
            [GenericFontFamily.Monospace] = LoadFamily(collection, assembly, "LiberationMono"),
        };
    }

    /// <summary>
    /// Resolves a concrete font for the given generic family, weight and slant at the requested
    /// pixel size. Size is clamped to a small positive minimum to keep ImageSharp/Fonts happy.
    /// </summary>
    /// <param name="family">The generic family to resolve.</param>
    /// <param name="bold">Whether a bold face is wanted.</param>
    /// <param name="italic">Whether an italic face is wanted.</param>
    /// <param name="sizePx">Em size in pixels (treated as points at 72 DPI by the layout/paint path).</param>
    /// <returns>A non-<see langword="null"/> <see cref="Font"/>.</returns>
    public Font Resolve(GenericFontFamily family, bool bold, bool italic, float sizePx)
    {
        if (!_families.TryGetValue(family, out FontFamily[]? faces))
        {
            faces = _families[GenericFontFamily.Serif];
        }

        FontFamily face = faces[StyleIndex(bold, italic)];
        float size = Math.Max(1f, sizePx);
        return face.CreateFont(size, FontStyle.Regular);
    }

    private static int StyleIndex(bool bold, bool italic) => (bold ? 1 : 0) + (italic ? 2 : 0);

    private static FontFamily[] LoadFamily(FontCollection collection, Assembly assembly, string baseName)
    {
        // Order matches StyleIndex: 0=Regular, 1=Bold, 2=Italic, 3=BoldItalic.
        return
        [
            Add(collection, assembly, baseName + "-Regular.ttf"),
            Add(collection, assembly, baseName + "-Bold.ttf"),
            Add(collection, assembly, baseName + "-Italic.ttf"),
            Add(collection, assembly, baseName + "-BoldItalic.ttf"),
        ];
    }

    private static FontFamily Add(FontCollection collection, Assembly assembly, string fileName)
    {
        string resourceName = ResourcePrefix + fileName;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded font resource '{resourceName}' was not found. Ensure '{fileName}' is bundled as an EmbeddedResource.");
        return collection.Add(stream, CultureInfo.InvariantCulture);
    }
}
