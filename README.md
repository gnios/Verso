# Verso

App desktop de transcrição de áudio/vídeo com [whisper.net](https://github.com/sandrohanea/whisper.net), em português. Windows-only.

**Stack:** .NET 10 · Photino.Blazor (WebView2) · EF Core 10 + SQLite · whisper.net 1.9.1 · NAudio

## O que faz

- Transcreve áudio e vídeo (fila com progresso por etapa)
- Biblioteca de transcrições com cards, status e busca
- Organiza transcrições em **pesquisas** e **tags**
- Editor com player, waveform e atribuição de **locutores** por segmento
- Detecção automática de locutores (speaker diarization)
- Gravação direto do microfone
- Exporta em **TXT, SRT e VTT**
- Tema claro/escuro
- Baixa modelos do Whisper sob demanda (incl. fine-tune pt-BR)
- Download e recomendação inteligente de modelo baseada no hardware

## Modelos de Transcrição

Whisper é um modelo de rede neural da OpenAI para transcrição de fala, treinado com 680 mil horas de áudio multilíngue (99 idiomas). O Verso utiliza os modelos no formato **GGML** (quantizados), que são versões comprimidas para rodar eficientemente em hardware de consumo.

### Tabela comparativa

| Modelo | Tamanho | RAM/VRAM | Velocidade | Precisão |
|--------|:-------:|:--------:|:----------:|:--------:|
| **Tiny** | ~75 MB | ~1 GB | Muito rápida | Baixa |
| **Base** | ~142 MB | ~1,5 GB | Rápida | Baixa–Média |
| **Padrão (Small)** | ~466 MB | ~2,5 GB | Moderada | Média–Alta |
| **Medium** | ~1,5 GB | ~5 GB | Lenta | Alta |
| **Large v3-turbo** | ~1,2 GB | ~4 GB | Rápida (GPU) | Muito alta |
| **Large v3 (Alta)** | ~3 GB | ~6–8 GB | Muito lenta | Máxima |
| **Pt-BR Turbo (distil)** | ~538 MB | ~2 GB | Rápida | Muito alta (pt-BR) |
| **Large v2** | ~3 GB | ~6–8 GB | Muito lenta | Alta |
| **Large v1** | ~3 GB | ~6–8 GB | Muito lenta | Alta (obsoleto) |
| **Tiny/Base/Small/Medium (EN)** | — | — | Mais rápidos | Inglês apenas |

### Qual modelo usar

**CPU:**

| RAM | Recomendado | Motivo |
|:---:|:-----------:|--------|
| < 6 GB | **Tiny** | Roda em máquinas com pouca memória |
| 6–12 GB | **Base** | Equilíbrio para hardware modesto |
| 12–24 GB | **Padrão (Small)** | Boa precisão sem ser lento |
| ≥ 24 GB | **Medium** | Máxima precisão em CPU |

**GPU (CUDA NVIDIA ou Vulkan):**

| RAM | Recomendado | Motivo |
|:---:|:-----------:|--------|
| < 8 GB | **Large v3-turbo** | Equilíbrio para GPUs de entrada |
| 8–32 GB | **Large v3-turbo** | Melhor relação velocidade/qualidade |
| ≥ 32 GB | **Alta (Large v3)** | Máxima qualidade (VRAM 12 GB+) |

**Orientações rápidas:**

- **Primeiro uso:** comece com **Padrão (Small)**
- **Português brasileiro:** use **Pt-BR Turbo (distil)** — fine-tuned para pt-BR, qualidade superior com metade do tamanho
- **Máxima qualidade (GPU):** **Alta (Large v3)**
- **Máxima velocidade (GPU):** **Large v3-turbo**
- **Inglês apenas:** prefira variante **EN** (Tiny, Base, Small ou Medium)
- **Hardware limitado (≤ 4 GB):** **Tiny** ou **Base**

> O app tem um **recomendador automático** que sugere o modelo ideal na tela de Configurações com base no dispositivo e na RAM do computador.

### Dispositivos de execução

| Modo | Descrição |
|------|-----------|
| **Automático** | Tenta CUDA → Vulkan → CPU. Padrão recomendado. |
| **CPU** | Processa apenas com CPU. Qualquer computador. |
| **CUDA** | Aceleração por GPU NVIDIA. |
| **Vulkan** | Aceleração por GPU via Vulkan (AMD, Intel, NVIDIA). |

## Requisitos

- **Windows 10/11** (Photino + WebView2 — Windows-only)
- Microsoft Edge **WebView2 Runtime** (presente por padrão no Windows 11)
- **FFmpeg** — o app tenta localizar no `PATH`; se não achar, oferece instalar automaticamente
- Para build: [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Rodar em desenvolvimento

```bash
dotnet restore
dotnet run --project src/Verso.App
# ou:
dotnet publish src/Verso.App -c Release -r win-x64 --self-contained true -o ./publish
dotnet test
```

O banco SQLite, as migrations e a pasta `data/` são criados automaticamente no primeiro boot.

## Estrutura

```
src/Verso.App     UI Photino.Blazor (WebView2), ViewModels, serviços de UI
src/Verso.Worker  Processo isolado de transcrição (spawn automático pelo App)
src/Verso.Core    Engine (whisper.net), serviços, dados (EF Core + SQLite), export
tests/Verso.Tests testes xUnit
```

## Releases

Pipeline em `.github/workflows/release.yml`. Ao publicar uma tag `v*.*.*`, ou fazer push na `main`, o CI builda **duas variantes self-contained** win-x64 e cria um GitHub Release:

- **`Verso-x.y.z-cpu-win-x64.zip`** (~200 MB) — só runtime CPU. Recomendado para quem não tem GPU NVIDIA.
- **`Verso-x.y.z-gpu-win-x64.zip`** (~960 MB) — inclui runtimes CUDA + CUDA 12 + Vulkan para aceleração por GPU.

Cada zip contém `Verso.App.exe` + `Verso.Worker.exe` (iniciado automaticamente na transcrição; sem console) + `wwwroot/` + `runtimes/`. Nenhuma exige .NET Runtime instalado — só o WebView2 Runtime. **Dados portáteis:** modelos, áudios, banco e logs ficam em `data/` ao lado do exe — mova a pasta inteira que tudo funciona.

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

- **Suporte Linux/macOS** — app é Windows-only (Photino + WebView2); exigiria reescrever a UI.
