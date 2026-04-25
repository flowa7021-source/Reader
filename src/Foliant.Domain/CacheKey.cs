namespace Foliant.Domain;

/// <summary>
/// Ключ кэша рендера. Стабильный между процессами и версиями приложения,
/// меняется только при смене версии engine. См. IMPLEMENTATION_PLAN.md, раздел 5.1.
/// </summary>
public sealed record CacheKey(
    string DocFingerprint,
    int PageIndex,
    int EngineVersion,
    int ZoomBucket,
    int Flags)
{
    public string ToFileName() =>
        $"{DocFingerprint}_{PageIndex}_{EngineVersion}_{ZoomBucket}_{Flags}.bin";

    public static CacheKey For(
        string docFingerprint,
        int pageIndex,
        int engineVersion,
        RenderOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);

        var flags = 0;
        if (opts.RenderAnnotations)
        {
            flags |= 1;
        }
        flags |= (int)opts.Theme << 1;
        return new CacheKey(docFingerprint, pageIndex, engineVersion, opts.ZoomBucket(), flags);
    }
}
