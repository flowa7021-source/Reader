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

    /// <summary>
    /// Битовая раскладка <see cref="Flags"/>:
    ///   bit 0      — RenderAnnotations
    ///   bits 1..4  — RenderTheme (4 бита, до 16 значений)
    ///   bits 5..6  — ViewRotation (2 бита: None/Cw90/Cw180/Cw270)
    /// Изменение любого из этих параметров делает запись новой ключом —
    /// разные render-варианты не пересекаются в кэше.
    /// </summary>
    public static CacheKey For(
        string docFingerprint,
        int pageIndex,
        int engineVersion,
        RenderOptions opts)
    {
        ArgumentNullException.ThrowIfNull(docFingerprint);
        ArgumentNullException.ThrowIfNull(opts);

        var flags = 0;
        if (opts.RenderAnnotations)
        {
            flags |= 1;
        }
        flags |= ((int)opts.Theme & 0xF) << 1;
        flags |= ((int)opts.Rotation & 0x3) << 5;
        return new CacheKey(docFingerprint, pageIndex, engineVersion, opts.ZoomBucket(), flags);
    }
}
