# Verso

App desktop de transcrição de áudio/vídeo com [whisper.net](https://github.com/sandrohanea/whisper.net), em português. Windows, Linux e macOS (Apple Silicon).

**Stack:** .NET 10 · Photino.Blazor (WebView2 / WebKitGTK / WKWebView) · EF Core 10 + SQLite · whisper.net 1.9.1 · NAudio

## O que faz

- Transcreve áudio e vídeo (fila com progresso por etapa)
- Biblioteca de transcrições com cards, status e busca
- Organiza transcrições em **pesquisas** e **tags**
- Editor com player, waveform e atribuição de **locutores** por segmento
- Detecção automática de locutores (speaker diarization)
- Gravação direto do microfone
- Exporta em **TXT, SRT e VTT**
- Tema claro/escuro
- Baixa modelos de reconhecimento de fala sob demanda
- Recomendação de precisão com base no hardware

## Precisão da transcrição

O Verso oferece **três perfis** pensados para pesquisa acadêmica (sem nomes técnicos de modelo):

| Perfil | Tamanho | Tempo | Quando usar |
|--------|:-------:|:-----:|-------------|
| **Rápido** | ~142 MB | Mais rápido | Rascunho, checagem de áudio, primeira passagem |
| **Equilibrado** | ~466 MB | Tempo médio | Maioria das entrevistas e aulas (padrão) |
| **Preciso** | ~1,2 GB | Mais lento · maior qualidade | Citação na tese, áudio difícil, análise fina |

> A escolha na interface é por **cartões**. O app recomenda um perfil em Configurações com base na memória. Hardware (CPU/GPU) fica em **Avançado**.

### Dispositivos de execução (Avançado)

| Modo | Descrição |
|------|-----------|
| **Automático** | Tenta GPU e cai para CPU. Padrão recomendado. |
| **CPU** | Processa apenas com CPU. Qualquer computador. |
| **CUDA** | Aceleração por GPU NVIDIA. |
| **Vulkan** | Aceleração por GPU via Vulkan (AMD, Intel, NVIDIA). |

## Requisitos

- **Windows 10/11**, **Linux x64** ou **macOS Apple Silicon**
- Runtime de WebView da plataforma:
  - Windows: Edge **WebView2** (padrão no Windows 11)
  - Linux: **WebKitGTK** (ex.: `libwebkit2gtk-4.1-0` no Ubuntu/Debian)
  - macOS: **WKWebView** (nativo)
- **FFmpeg** no `PATH` (no Windows o app pode oferecer instalação via winget)
- Para build: [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Rodar em desenvolvimento

```bash
dotnet restore
dotnet run --project src/Verso.App
# ou:
dotnet publish src/Verso.App -c Release -r win-x64 --self-contained true -o ./publish
# Linux: -r linux-x64 · macOS Apple Silicon: -r osx-arm64
dotnet test
```

O banco SQLite, as migrations e a pasta `data/` são criados automaticamente no primeiro boot.

## Estrutura

```
src/Verso.App     UI Photino.Blazor, ViewModels, serviços de UI
src/Verso.Worker  Processo isolado de transcrição (spawn automático pelo App)
src/Verso.Core    Engine (whisper.net), serviços, dados (EF Core + SQLite), export
tests/Verso.Tests testes xUnit
```

## Releases

Pipeline em `.github/workflows/release.yml`. Tag `v*.*.*` ou push na `main` gera um GitHub Release com **6 zips** (cpu/gpu × win-x64 / linux-x64 / osx-arm64):

| Zip | Aceleração |
|-----|------------|
| `*-cpu-win-x64.zip` / `*-gpu-win-x64.zip` | CPU · CUDA/Vulkan |
| `*-cpu-linux-x64.zip` / `*-gpu-linux-x64.zip` | CPU · CUDA/Vulkan |
| `*-cpu-osx-arm64.zip` / `*-gpu-osx-arm64.zip` | CPU · Core ML |

Cada zip tem o app + Worker (iniciado na transcrição) + `wwwroot/` + `runtimes/`. Self-contained (sem .NET instalado). **Dados portáteis** em `data/` ao lado do executável.

```bash
git tag v0.1.0
git push origin v0.1.0
```

> **Aviso do SmartScreen:** por padrão os executáveis **não são assinados**. Ao abrir pela primeira vez, clique em **Mais informações → Executar mesmo assim**. Para eliminar o alerta, configure a assinatura via Azure Trusted Signing (instruções abaixo).

### Assinatura do código (opcional, Azure Trusted Signing)

A etapa de assinatura está no pipeline e ativa **automaticamente** quando as variáveis do Azure estão configuradas.

**Setup (uma vez):**

1. No Azure: crie um **Trusted Signing Account** + **Certificate Profile**
2. No Entra ID: registre um App, gere um **client secret**, conceda a role **Trusted Signing Certificate Profile Signer**
3. No GitHub (Settings → Secrets and variables → Actions):
   - **Secrets:** `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`
   - **Variables:** `AZURE_SIGNING_ENDPOINT`, `AZURE_SIGNING_ACCOUNT`, `AZURE_CERTIFICATE_PROFILE`

A próxima tag assina os `.exe` da raiz (`Verso.App.exe` e `Verso.Worker.exe`) automaticamente.

## Roadmap

### Pendente de validação

- **Confirmar estabilidade do crash nativo (`0x80131506`)** — mitigações aplicadas no engine (fábrica fresca por job, sem `WithStringPool`, cancelamento entre chunks, dispose explícito); falta UAT com transcrição longa real (AD-006).
- **UAT ponta a ponta** — upload → transcrição → editor → exportação, nunca validado manualmente.

### Planejado

- **Drag & drop funcional no upload** — hoje só seleção por clique
- **Progresso determinado no download de modelos** — barra atual é indeterminada

### Futuro (fora de escopo atual)

- **macOS Intel (osx-x64)** — release atual foca em Apple Silicon (`osx-arm64`).
