# Verso — Documentação

## Índice

1. [O que é o Verso](#1-o-que-é-o-verso)
2. [Precisão da transcrição](#2-precisão-da-transcrição)
   - [Perfis na interface](#21-perfis-na-interface)
   - [Recomendação automática](#22-recomendação-automática)
   - [Modelos Whisper (referência técnica)](#23-modelos-whisper-referência-técnica)
3. [Dispositivos de Execução](#3-dispositivos-de-execução)
4. [Como usar o projeto](#4-como-usar-o-projeto)
   - [Requisitos](#41-requisitos)
   - [Build e execução em desenvolvimento](#42-build-e-execução-em-desenvolvimento)
   - [Estrutura do projeto](#43-estrutura-do-projeto)
5. [Como baixar as releases](#5-como-baixar-as-releases)
   - [Variantes CPU e GPU](#51-variantes-cpu-e-gpu)
   - [Instalação e execução](#52-instalação-e-execução)
6. [Funcionalidades](#6-funcionalidades)

---

## 1. O que é o Verso

**Verso** é um aplicativo desktop para transcrição de áudio e vídeo usando o modelo [Whisper](https://github.com/openai/whisper) da OpenAI, via a biblioteca [whisper.net](https://github.com/sandrohanea/whisper.net). É desenvolvido em .NET 10 com interface Photino.Blazor e tem suporte nativo ao português brasileiro.

O aplicativo permite transcrever arquivos de áudio e vídeo, organizar transcrições em **pesquisas** e **tags**, editar segmentos com atribuição de **locutores**, gravar áudio direto do microfone e exportar nos formatos **TXT**, **SRT** e **VTT**.

**Plataformas:** Windows 10/11 (WebView2), Linux x64 (WebKitGTK) e macOS Apple Silicon (WKWebView).

---

## 2. Precisão da transcrição

### 2.1 Perfis na interface

Para o público acadêmico, o Verso expõe **três perfis** (sem nomes técnicos de modelo Whisper):

| Perfil na UI | Quando usar | Tempo | Modelo interno | Tamanho |
|--------|-------------|------|:---:|:---:|
| **Rápido** | Rascunho, checagem de áudio | Mais rápido | Base | ~142 MB |
| **Equilibrado** | Maioria das entrevistas (padrão) | Tempo médio | Small (`Standard`) | ~466 MB |
| **Preciso** | Citação, áudio difícil | Mais lento · maior qualidade | Large v3-turbo | ~1,2 GB |

A escolha na UI é por **cartões** (não dropdown), com badge “Recomendado” quando o perfil coincide com a sugestão do sistema.

### 2.2 Recomendação automática

O recomendador sugere um dos três perfis com base no dispositivo e na RAM. Motivos usam os nomes Rápido / Equilibrado / Preciso.

| Contexto | Perfil sugerido |
|----------|-----------------|
| CPU, &lt; 6 GB RAM | Rápido |
| CPU, 6–24 GB RAM | Equilibrado |
| CPU, ≥ 24 GB RAM | Preciso |
| GPU (CUDA / Vulkan / Core ML) | Preciso |

Hardware (CPU/CUDA/Vulkan) e logs ficam em **Configurações → Avançado**.

### 2.3 Modelos Whisper (referência técnica)

O Verso usa [Whisper](https://github.com/openai/whisper) via [whisper.net](https://github.com/sandrohanea/whisper.net) em formato **GGML**. A tabela abaixo é referência interna — não aparece na UI do usuário comum.

| Modelo GGML | Tamanho | Uso no Verso |
|--------|:---:|------|
| Base | ~142 MB | Perfil Rápido |
| Small | ~466 MB | Perfil Equilibrado (padrão) |
| Large v3-turbo | ~1,2 GB | Perfil Preciso |
| Tiny / Medium / Large v1–v3 / EN | vários | Legado / engine apenas |

---

## 3. Dispositivos de Execução

O Verso suporta quatro modos de execução:

| Modo | Descrição | Quando usar |
|------|-----------|-------------|
| **Automático** | Tenta CUDA → Vulkan → CPU, nesta ordem | Padrão recomendado. Usa GPU se disponível, fallback para CPU. |
| **CPU** | Processa apenas com CPU | Qualquer computador funciona. Mais lento que GPU. |
| **CUDA** | Aceleração por GPU NVIDIA (CUDA) | Placa NVIDIA com VRAM ≥ 4 GB recomendada. |
| **Vulkan** | Aceleração por GPU via Vulkan (AMD, Intel, NVIDIA) | Alternativa ao CUDA para GPUs não-NVIDIA. |

> **Nota:** a variante **GPU** inclui CUDA/Vulkan no Windows e Linux, e Core ML no macOS. A variante **CPU** inclui apenas o runtime CPU.

---

## 4. Como usar o projeto

### 4.1 Requisitos

- **Windows 10/11**, **Linux x64** ou **macOS Apple Silicon**
- Runtime de WebView: WebView2 (Windows), WebKitGTK (Linux), WKWebView (macOS)
- **FFmpeg** no `PATH` (no Windows o app pode oferecer instalação via winget)
- Para build: **[.NET 10 SDK](https://dotnet.microsoft.com/download)** (apenas para desenvolvimento)

### 4.2 Build e execução em desenvolvimento

```bash
# Restaurar dependências
dotnet restore

# Executar em desenvolvimento
dotnet run --project src/Verso.App

# Publicar (self-contained, single-file; Worker sai no mesmo -o)
dotnet publish src/Verso.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false `
  -o ./publish

# Executar testes
dotnet test
```

O banco SQLite, as migrations e a pasta de dados (`data/`) são criados automaticamente no primeiro boot.

### 4.3 Estrutura do projeto

```
Verso.sln                    — Solução principal
├── src/
│   ├── Verso.App/           — UI (Photino.Blazor + WebView2)
│   │   ├── Pages/           — Páginas Razor (Dashboard, Editor, Upload, etc.)
│   │   ├── ViewModels/      — ViewModels (Settings, Upload, Editor, etc.)
│   │   ├── Services/        — Serviços de UI
│   │   ├── wwwroot/         — Assets estáticos (CSS, JS, fontes)
│   │   └── Program.cs       — Entry point
│   ├── Verso.Worker/        — Processo isolado de transcrição (spawn automático)
│   └── Verso.Core/          — Motor e dados
│       ├── Engine/           — Transcrição (whisper.net), fila, download de modelos
│       ├── Services/         — Serviços de domínio (Library, Speaker, Export, etc.)
│       ├── Data/             — EF Core + SQLite, migrations, entidades
│       ├── Media/            — Playback de áudio (NAudio)
│       └── Logging/          — File logger
├── tests/Verso.Tests/       — Testes xUnit
└── models/                  — Modelos baixados (criado pelo app)
```

---

## 5. Como baixar as releases

As releases são geradas automaticamente pelo GitHub Actions ao publicar uma tag `v*.*.*` no repositório, ou ao fazer push na branch `main` (que auto-incrementa o patch). Cada release contém **seis zips** (cpu/gpu × três plataformas).

### 5.1 Variantes por plataforma

| Arquivo | Contém | Para quem |
|---------|--------|-----------|
| `Verso-X.Y.Z-cpu-win-x64.zip` | CPU | Windows sem GPU NVIDIA |
| `Verso-X.Y.Z-gpu-win-x64.zip` | CUDA + CUDA 12 + Vulkan | Windows com NVIDIA/Vulkan |
| `Verso-X.Y.Z-cpu-linux-x64.zip` | CPU | Linux (WebKitGTK) |
| `Verso-X.Y.Z-gpu-linux-x64.zip` | CUDA + Vulkan | Linux com NVIDIA/Vulkan |
| `Verso-X.Y.Z-cpu-osx-arm64.zip` | CPU | macOS Apple Silicon |
| `Verso-X.Y.Z-gpu-osx-arm64.zip` | Core ML | macOS Apple Silicon com aceleração |

Builds **self-contained** / **single-file**. O zip contém o app, o Worker (iniciado automaticamente na transcrição), `wwwroot/` e `runtimes/`.

### 5.2 Instalação e execução

1. Baixe o `.zip` da [página de Releases](https://github.com/gnios/Verso/releases) para a sua plataforma
2. Escolha **cpu** ou **gpu** conforme o hardware
3. Extraia e execute `Verso.App.exe` (Windows) ou `./Verso.App` (Linux/macOS)

**Pré-requisitos:** WebView2 (Windows), WebKitGTK (Linux), WKWebView (macOS). No Linux: `sudo apt install libwebkit2gtk-4.1-0` (nome pode variar).

**Dados portáteis:** modelos, áudios, banco de dados e logs ficam numa pasta `data/` ao lado do executável. Você pode mover a pasta inteira para outro local que tudo funciona — sem depender de `%AppData%`.

> **Aviso de segurança do Windows:** por padrão os executáveis não são assinados digitalmente. Ao abrir pela primeira vez, o Windows pode mostrar o alerta do SmartScreen ("O Windows protegeu seu computador"). Clique em **Mais informações → Executar mesmo assim**. Para eliminar o alerta permanentemente, configure a assinatura via Azure Trusted Signing (opcional, veja o README).

---

## 6. Funcionalidades

- **Transcrição de áudio e vídeo** com fila e progresso por etapa
- **Biblioteca de transcrições** com cards, status e busca
- **Organização em pesquisas e tags** — agrupe transcrições tematicamente
- **Editor completo** com player, waveform e atribuição de locutores por segmento
- **Detecção automática de locutores** (speaker diarization)
- **Gravação direto do microfone**
- **Exportação** nos formatos **TXT**, **SRT** e **VTT**
- **Download sob demanda de modelos** diretamente da interface
- **Tema claro/escuro**
- **Configuração de dispositivo** (CPU, CUDA, Vulkan, Automático)
- **Recomendação inteligente de modelo** baseada no hardware

---

> Documentação gerada a partir do código-fonte. Para mais detalhes técnicos, consulte o repositório e os arquivos `.specs/STATE.md`.
