using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    /// <summary>Минимальный масштаб (10 %).</summary>
    public const double MinZoom = 0.10;

    /// <summary>Максимальный масштаб (800 %).</summary>
    public const double MaxZoom = 8.00;

    /// <summary>Шаг для команд ZoomIn/ZoomOut (25 п.п. — соответствует ZoomBucket-сетке в Domain).</summary>
    public const double ZoomStep = 0.25;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomPercent))]
    private double _zoom = 1.0;

    /// <summary>Текущий масштаб в процентах для статус-бара. Округлён до целого.</summary>
    public int ZoomPercent => (int)Math.Round(Zoom * 100);

    partial void OnZoomChanged(double value)
    {
        double clamped = Math.Clamp(value, MinZoom, MaxZoom);
        if (Math.Abs(clamped - value) > 1e-9)
        {
            Zoom = clamped;
            return;
        }
        _ = RenderCurrentPageAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void ZoomIn()
    {
        FitMode = FitMode.ActualSize;
        Zoom = Math.Min(MaxZoom, Math.Round(Zoom + ZoomStep, 2));
    }

    [RelayCommand]
    private void ZoomOut()
    {
        FitMode = FitMode.ActualSize;
        Zoom = Math.Max(MinZoom, Math.Round(Zoom - ZoomStep, 2));
    }

    [RelayCommand]
    private void ResetZoom()
    {
        FitMode = FitMode.ActualSize;
        Zoom = 1.0;
    }
}
