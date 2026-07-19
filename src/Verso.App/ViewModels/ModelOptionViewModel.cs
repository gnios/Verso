using Verso.Core.Data.Entities;

namespace Verso.App.ViewModels;

/// <summary>
/// Perfil de precisão exibido em Settings e Upload.
/// Value é o <see cref="ModelQuality"/> persistido; Label/Description são amigáveis ao usuário acadêmico.
/// </summary>
public sealed record ModelOptionViewModel(
    ModelQuality Value,
    string Label,
    string SizeLabel,
    string Description)
{
    public override string ToString() => $"{Label} · {SizeLabel}";
}
