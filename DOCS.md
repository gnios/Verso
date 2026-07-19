# Verso — Documentação

## Índice

1. [O que é o Verso](#1-o-que-é-o-verso)
2. [Modelos de Transcrição](#2-modelos-de-transcrição)
   - [O que são os modelos Whisper](#21-o-que-são-os-modelos-whisper)
   - [Tabela comparativa de modelos](#22-tabela-comparativa-de-modelos)
   - [Indicação para cada modelo](#23-indicação-para-cada-modelo)
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

**Verso** é um aplicativo desktop para transcrição de áudio e vídeo usando o modelo [Whisper](https://github.com/openai/whisper) da OpenAI, via a biblioteca [whisper.net](https://github.com/sandrohanea/whisper.net). É desenvolvido em .NET 10 com interface Photino.Blazor (WebView2) e tem suporte nativo ao português brasileiro.

O aplicativo permite transcrever arquivos de áudio e vídeo, organizar transcrições em **pesquisas** e **tags**, editar segmentos com atribuição de **locutores**, gravar áudio direto do microfone e exportar nos formatos **TXT**, **SRT** e **VTT**.

**Plataforma:** Windows 10/11 apenas (Photino + WebView2 — Windows-only).

---

## 2. Modelos de Transcrição

### 2.1 O que são os modelos Whisper

Whisper é um modelo de rede neural desenvolvido pela OpenAI para transcrição de fala e tradução. Ele foi treinado com 680 mil horas de áudio multilíngue e suporta 99 idiomas.

O Verso utiliza os modelos no formato **GGML** (quantizados), que são versões comprimidas dos modelos originais para rodar eficientemente em hardware de consumo. A quantização reduz o tamanho do arquivo e o uso de memória, com perda mínima de precisão.

Os modelos se diferenciam por:
- **Tamanho e arquitetura**: quanto maior o modelo, mais parâmetros e camadas, maior a precisão — e maior o custo computacional.
- **Suporte a idiomas**: a maioria dos modelos é multilíngue; existem variantes "EN" que processam apenas inglês (mais rápidas e menores para este idioma).
- **Versão da arquitetura**: Large v1, v2, v3 são versões progressivamente mais precisas. Large-v3 é o estado da arte entre os modelos grandes padrão.
- **Versões "turbo"**: arquitetura otimizada com attention pooling e decodificador mais raso — qualidade próxima ao Large completo com **2–3× mais velocidade**.
- **Fine-tuning**: o modelo **Pt-BR Turbo** é uma versão fine-tuned (distil-whisper-large-v3) especificamente para português brasileiro, oferecendo qualidade superior para este idioma com tamanho reduzido (~538 MB).

### 2.2 Tabela comparativa de modelos

| Modelo | Tamanho no disco | RAM/VRAM estimada | Idiomas | Velocidade relativa | Precisão relativa |
|--------|:-:|:-:|:-:|:-:|:-:|
| **Tiny** | ~75 MB | ~1 GB | Multilíngue | Muito rápida | Baixa |
| **Base** | ~142 MB | ~1,5 GB | Multilíngue | Rápida | Baixa–Média |
| **Padrão (Small)** | ~466 MB | ~2,5 GB | Multilíngue | Moderada | Média–Alta |
| **Medium** | ~1,5 GB | ~5 GB | Multilíngue | Lenta | Alta |
| **Large v3-turbo** | ~1,2 GB | ~4 GB | Multilíngue | Rápida (GPU) | Muito alta |
| **Large v2** | ~3 GB | ~6–8 GB | Multilíngue | Muito lenta | Alta |
| **Large v3 (Alta)** | ~3 GB | ~6–8 GB | Multilíngue | Muito lenta | Máxima |
| **Large v1** | ~3 GB | ~6–8 GB | Multilíngue | Muito lenta | Alta (obsoleto) |
| **Pt-BR Turbo (distil)** | ~538 MB | ~2 GB | Português (forçado) | Rápida | Muito alta (pt-BR) |
| **Tiny (inglês)** | ~75 MB | ~1 GB | Inglês apenas | Muito rápida | Baixa |
| **Base (inglês)** | ~142 MB | ~1,5 GB | Inglês apenas | Rápida | Baixa–Média |
| **Small (inglês)** | ~466 MB | ~2,5 GB | Inglês apenas | Moderada | Média–Alta |
| **Medium (inglês)** | ~1,5 GB | ~5 GB | Inglês apenas | Lenta | Alta |

> **Sobre a RAM/VRAM:** os valores são estimativas para uso em GPU. Em CPU pura, o consumo de RAM é maior e o desempenho significativamente mais lento.

### 2.3 Indicação para cada modelo

#### Recomendação dinâmica

O Verso possui um **recomendador automático** que sugere o modelo ideal com base no dispositivo (CPU ou GPU) e na memória RAM total do computador. A recomendação aparece na tela de Configurações.

#### CPU

| RAM | Modelo recomendado | Motivo |
|:---:|:---:|------|
| < 6 GB | **Tiny** (~75 MB) | Único que roda confortavelmente em máquinas com pouca memória. Ideal para testes rápidos. |
| 6–12 GB | **Base** (~142 MB) | Equilíbrio entre velocidade e precisão para hardware modesto. |
| 12–24 GB | **Padrão/Small** (~466 MB) | Boa precisão sem ser lento demais. Escolha segura para a maioria dos notebooks. |
| ≥ 24 GB | **Medium** (~1,5 GB) | Máxima precisão em CPU, mas a transcrição será lenta (considere usar GPU). |

#### GPU (CUDA NVIDIA ou Vulkan)

| RAM/VRAM | Modelo recomendado | Motivo |
|:---:|:---:|------|
| < 8 GB | **Large v3-turbo** (~1,2 GB) | Equilíbrio entre velocidade e qualidade para GPUs de entrada. |
| 8–32 GB | **Large v3-turbo** (~1,2 GB) | Melhor relação velocidade/qualidade. Processa áudio longo rapidamente. |
| ≥ 32 GB | **Large v3** / Alta (~3 GB) | Máxima qualidade disponível. Para GPUs com bastante VRAM (12 GB+). |

#### Orientações gerais

- **Primeiro uso / testes:** comece com **Padrão (Small)**. É o modelo padrão do aplicativo, bom equilíbrio entre velocidade e qualidade.
- **Português brasileiro:** use **Pt-BR Turbo (distil)**. É um modelo fine-tuned especificamente para o português do Brasil, com qualidade superior ao Large-v3-turbo geral, mas com metade do tamanho (~538 MB vs ~1,2 GB). Ideal para transcrições em português.
- **Máxima qualidade (GPU recomendada):** use **Alta (Large v3)**. É o modelo mais preciso disponível, mas exige ~6–8 GB de VRAM e bastante tempo de processamento.
- **Máxima velocidade (GPU):** use **Large v3-turbo**. Perde pouco em qualidade para o Large v3 completo, mas é 2–3× mais rápido.
- **Inglês apenas:** Se todo o conteúdo for em inglês, prefira uma variante **"EN"** correspondente — são mais rápidas e precisas para este idioma.
- **Hardware muito limitado (≤ 4 GB RAM):** use **Tiny** ou **Base**.
- **Modelos obsoletos:** Large v1 e Large v2 existem apenas para compatibilidade. Prefira Large v3, Large v3-turbo ou Pt-BR Turbo.

---

## 3. Dispositivos de Execução

O Verso suporta quatro modos de execução:

| Modo | Descrição | Quando usar |
|------|-----------|-------------|
| **Automático** | Tenta CUDA → Vulkan → CPU, nesta ordem | Padrão recomendado. Usa GPU se disponível, fallback para CPU. |
| **CPU** | Processa apenas com CPU | Qualquer computador funciona. Mais lento que GPU. |
| **CUDA** | Aceleração por GPU NVIDIA (CUDA) | Placa NVIDIA com VRAM ≥ 4 GB recomendada. |
| **Vulkan** | Aceleração por GPU via Vulkan (AMD, Intel, NVIDIA) | Alternativa ao CUDA para GPUs não-NVIDIA. |

> **Nota:** a variante **GPU** da release inclui os runtimes nativos para CUDA, CUDA 12 e Vulkan. A variante **CPU** inclui apenas o runtime CPU.

---

## 4. Como usar o projeto

### 4.1 Requisitos

- **Windows 10 ou 11** (Photino + WebView2 — Windows-only)
- **Microsoft Edge WebView2 Runtime** — já presente por padrão no Windows 11; no Windows 10 pode precisar instalar
- **FFmpeg** — o aplicativo tenta localizar no `PATH`; se não encontrar, oferece instalação automática
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

As releases são geradas automaticamente pelo GitHub Actions ao publicar uma tag `v*.*.*` no repositório, ou ao fazer push na branch `main` (que auto-incrementa o patch). Cada release contém **duas variantes** do aplicativo.

### 5.1 Variantes CPU e GPU

| Arquivo | Tamanho aproximado | Contém | Para quem |
|---------|:-:|--------|-----------|
| `Verso-X.Y.Z-cpu-win-x64.zip` | **~200 MB** | Runtime CPU apenas | Qualquer computador. Recomendado para quem **não tem** placa NVIDIA. |
| `Verso-X.Y.Z-gpu-win-x64.zip` | **~960 MB** | Runtimes CPU + CUDA + CUDA 12 + Vulkan | Quem tem **placa NVIDIA** (ou quer aceleração por GPU Vulkan). |

Ambas são **self-contained** (não exigem .NET runtime instalado) e **single-file** (DLLs gerenciadas embutidas nos exes). O zip contém `Verso.App.exe`, `Verso.Worker.exe` (iniciado automaticamente na transcrição; sem janela de console), `wwwroot/` e `runtimes/` nativos.

### 5.2 Instalação e execução

1. Baixe o arquivo `.zip` da [página de Releases do GitHub](https://github.com/gnios/Verso/releases)
2. Escolha a variante:
   - **cpu** — se não tiver placa NVIDIA ou quiser simplicidade
   - **gpu** — se tiver placa NVIDIA com drivers CUDA instalados
3. Extraia o conteúdo para uma pasta (qualquer local, sem necessidade de instalação)
4. Execute `Verso.App.exe` — o Worker sobe sozinho quando houver transcrição

**Pré-requisito na máquina do usuário:** apenas o Microsoft Edge WebView2 Runtime (presente por padrão no Windows 11). Nenhuma outra instalação é necessária.

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
