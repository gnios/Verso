# Verso

App desktop de transcrição de áudio/vídeo com [whisper.net](https://github.com/sandrohanea/whisper.net), em português. Windows-only.

## O que faz

- Transcreve áudio e vídeo (fila com progresso por etapa)
- Biblioteca de transcrições com cards, status e busca
- Organiza transcrições em **pesquisas** e **tags**
- Editor com player, waveform e atribuição de **locutores** por segmento
- Gravação direto do microfone
- Exporta em **TXT, SRT e VTT**
- Tema claro/escuro
- Baixa modelos do Whisper sob demanda (incl. fine-tune pt-BR)

## Requisitos

- **Windows 10/11** (WPF + WebView2 não rodam em Linux/macOS)
- Microsoft Edge **WebView2 Runtime** (presente por padrão no Windows 11)
- **FFmpeg** — o app tenta localizar no `PATH`; se não achar, oferece instalar automaticamente
- Para build: [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Rodar em desenvolvimento

```bash
dotnet restore
dotnet run --project src/Verso.App
```

O banco SQLite e as migrations são criados automaticamente no primeiro boot.

## Estrutura

```
src/Verso.App    UI WPF + Blazor Hybrid (WebView2), ViewModels, serviços de UI
src/Verso.Core   Engine (whisper.net), serviços, dados (EF Core + SQLite), export
tests/Verso.Tests  testes xUnit
```

**Stack:** .NET 10 · WPF + Blazor Hybrid (WebView2) · EF Core 10 + SQLite · whisper.net 1.9.1 · NAudio

## Releases

Pipeline em `.github/workflows/release.yml` (runner `windows-latest`). Ao publicar uma tag `v*.*.*`, o CI builda um **self-contained** win-x64, empacota num zip e cria um GitHub Release com o executável anexado.

```bash
git tag v0.1.0
git push origin v0.1.0
```

O release não exige instalação do .NET — só o WebView2 Runtime no Windows do usuário.