using Verso.Core.Data.Entities;

namespace Verso.App.ViewModels;

/// <summary>
/// Opção de modelo Whisper para os seletores de Settings e Upload.
/// Carrega Value (enum persistido), Label (exibido), SizeLabel (tamanho aprox.
/// do download) e IsEnglish (variantes inglês-only, sinalizadas na UI).
/// </summary>
public sealed record ModelOptionViewModel(ModelQuality Value, string Label, string SizeLabel, bool IsEnglish)
{
    public override string ToString() => $"{Label} · {SizeLabel}";
}