namespace Foliant.ViewModels;

/// <summary>
/// Какие аннотации показывать в <c>DocumentTabViewModel.CurrentPageAnnotations</c>.
/// Это чисто view-уровневый фильтр — оригинальные данные в <c>_allAnnotations</c>
/// и в sidecar-файле остаются без изменений.
/// </summary>
public enum AnnotationFilterMode
{
    All,
    Highlights,
    Notes,
    Freehand,
}
