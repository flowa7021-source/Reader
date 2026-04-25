using PDFiumCore;

namespace Foliant.Engines.Pdf;

internal static class PdfLibrary
{
    private static volatile bool _initialized;
    private static readonly Lock _gate = new();

    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }

            fpdfview.FPDF_InitLibrary();
            _initialized = true;
        }
    }
}
