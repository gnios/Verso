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

Pipeline em `.github/workflows/release.yml` (runner `windows-latest`). Ao publicar uma tag `v*.*.*`, o CI builda **duas variantes self-contained** win-x64 e cria um GitHub Release com ambas anexadas:

- **`Verso-x.y.z-cpu-win-x64.zip`** (~100 MB) — só runtime CPU. Recomendado para quem não tem GPU NVIDIA.
- **`Verso-x.y.z-gpu-win-x64.zip`** (~500 MB) — inclui runtimes CUDA/Vulkan para aceleração por GPU.

Nenhuma das duas exige instalação do .NET — só o WebView2 Runtime no Windows do usuário.

```bash
git tag v0.1.0
git push origin v0.1.0
```

> **Aviso de segurança do Windows:** por padrão os executáveis **não são assinados**. Ao abrir pela primeira vez, o Windows pode mostrar o alerta do SmartScreen ("O Windows protegeu seu computador") — clique em **Mais informações → Executar mesmo assim**. Para eliminar o alerta, ative a assinatura via Azure Trusted Signing (abaixo).

### Assinatura do código (opcional, via Azure Trusted Signing)

A etapa de assinatura já está no pipeline e ativa **automaticamente** quando as variáveis do Azure estão configuradas. Enquanto não configurar, as releases saem sem assinar (o alerta do SmartScreen permanece).

**Setup (uma vez):**

1. No Azure: crie um **Trusted Signing Account** + **Certificate Profile** (a Microsoft valida a identidade — pode levar alguns dias).
2. No Entra ID: registre um App, gere um **client secret** e conceda a role **Trusted Signing Certificate Profile Signer** ao app no Certificate Profile.
3. No GitHub (Settings → Secrets and variables → Actions):
   - **Secrets:** `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`
   - **Variables:** `AZURE_SIGNING_ENDPOINT` (ex.: `https://eus.codesigning.azure.net/` — conforme a região da conta), `AZURE_SIGNING_ACCOUNT`, `AZURE_CERTIFICATE_PROFILE`

A próxima `git tag vX.Y.Z && git push origin vX.Y.Z` assina o `Verso.App.exe` automaticamente. Para publicações estreantes, o SmartScreen pode ainda exibir o aviso uma vez até ganhar reputação — acelere enviando o arquivo ao [portal de submissão do Microsoft SmartScreen](https://www.microsoft.com/wdsi/filesubmission).

## Roadmap

Itens pendentes, extraídos do estado atual do código e do log de decisões.

### Pendente de validação

- **Confirmar estabilidade do crash nativo (`0x80131506`)** — mitigações aplicadas no engine (fábrica fresca por job, sem `WithStringPool`, cancelamento entre chunks, dispose explícito); falta UAT com uma transcrição longa real para confirmar ausência do crash (AD-006).
- **UAT ponta a ponta** — upload → transcrição → editor → exportação, nunca validado de ponta a ponta manualmente.

### Planejado

- **Drag & drop funcional no upload** — hoje só seleção por clique; o drop encerra só o feedback visual (`Upload.razor`, TODO no código).
- **Progresso determinado no download de modelos** — a barra atual é indeterminada.

### Futuro (fora de escopo atual)

- **Suporte Linux/macOS** — o app é Windows-only (WPF + WebView2); exigiria reescrever a UI fora do WPF.