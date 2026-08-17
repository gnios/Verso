using Verso.Core.Data.Entities;

namespace Verso.App.ViewModels;

/// <summary>
/// Perfil de precisão exibido em Settings e Upload.
/// Value é o <see cref="ModelQuality"/> persistido; demais campos são copy amigável.
/// </summary>
public sealed record ModelOptionViewModel(
    ModelQuality Value,
    string Label,
    string SizeLabel,
    string Description,
    string SpeedHint,
    string WhenToUse)
{
    public override string ToString() => $"{Label} · {SizeLabel}";
}
