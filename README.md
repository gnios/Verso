# Verso

App desktop de transcrição de áudio/vídeo com [whisper.net](https://github.com/sandrohanea/whisper.net), em português. Windows, Linux e macOS (Apple Silicon).

**Stack:** .NET 10 · Photino.NET (WebView2 / WebKitGTK / WKWebView) · EF Core 10 + SQLite · whisper.net 1.9.1 · NAudio · **UI em React 19 + Astryx** (design system da Meta, em `ui/`)

## UI — React + Astryx

A UI Blazor foi substituída por uma SPA **React 19 + [Astryx](https://github.com/meta/astryx)** (`@astryxdesign/core` + tema `neutral`). O shell Photino carrega `http://127.0.0.1:{port}/` (LocalAppServer serve o build em `wwwroot`), e o React fala com o backend .NET por uma **ponte nativa** (`window.external.sendMessage` ⇄ `SendWebMessage`) — sem HTTP para a lógica do app; áudio continua pelo LocalMediaServer (com Range).

```
ui/                       SPA React + Vite + TypeScript + Astryx
ui/src/lib/bridge.ts      cliente da ponte (invoke/onEvent; mock no navegador)
ui/src/astryx-themes.ts   tema (gerado por `astryx theme add`)
src/Verso.App/Services/NativeBridge.cs   protocolo JSON request/response + eventos
src/Verso.App/Services/VersoBridge.cs    métodos da aplicação expostos ao React
src/Verso.App/Services/LocalAppServer.cs servidor HTTP estático do wwwroot
```

**Build da UI (antes de `dotnet build`/`dotnet run`):**

```bash
cd ui
npm install
npm run build        # tsc --noEmit + vite build → src/Verso.App/wwwroot
```

**Dev da UI no navegador** (mock embutido, sem o app desktop): `cd ui && npm run dev` → http://localhost:5173. O guia do Astryx para agentes está em `ui/AGENTS.md` (use `npx astryx component <Nome>` para props de qualquer componente).

**Métodos da ponte** (ver `VersoBridge.cs`): `app.info`, `theme.get/toggle`, `sidebar.load`, `navigation.navigate`, `dashboard.load/action/search`, `transcription.action`, `ui.confirm.respond`, `upload.pickFile/selectFile/load/start`, `recording.*`, `editor.load/saveText/split/merge/assignSpeaker/addSpeaker/updateMeta/export`, `settings.load/update/verifyRuntime`, `folder.load/create/update/delete`, `feedback.send`. Eventos push: `navigation.changed`, `theme.changed`, `ui.confirm`, `dashboard.changed`, `dashboard.progress`, `folder.changed`, `queue.status`, `model.download.*`, `media.command`. A verificação E2E da ponte e das telas via CDP do WebView2 está em `scripts/verify-bridge.mjs` — `scripts/run-ui-checks.ps1` executa o fluxo inteiro (build → seed → app com CDP → checks).

Todas as telas estão portadas: **Dashboard, Upload, Gravação, Configurações, Pastas e Editor** (player, segmentos editáveis, locutores, split/merge, tags, export TXT/SRT/VTT). A biblioteca tem visualização em **tabela** (padrão) e **cartões**, alternável no cabeçalho e persistida em `localStorage`.

### Testes de interface e funcionalidade

| Comando | O que valida |
|---|---|
| `./scripts/run-ui-checks.ps1` | Fluxo completo: build UI + app, seed, sobe o app com CDP :9223 e roda `verify-bridge.mjs` (ponte JS⇄C# + DOM de todas as telas: tabela/cartões, upload, gravação, settings, pastas, editor). `-SkipBuild` para iterar rápido, `-RestartApp` para reiniciar um app aberto sem CDP |
| `python scripts/seed-test-transcription.py` | Semeia a transcrição E2E (`E2E — Entrevista de teste` + WAV). **Rode antes do verify** — os checks de editor mutam o banco |
| `node scripts/verify-bridge.mjs` | 40+ checks da ponte + telas contra um app rodando com `--remote-debugging-port=9223` |
| `node scripts/screenshot.mjs <saida.png>` | Screenshot da tela atual via CDP |
| `node scripts/debug-editor.mjs` | Sonda: cards do dashboard + operações do editor com o id semeado |
| `./scripts/run-e2e.ps1` | Testes Playwright (`tests/Verso.E2E`) — DOM real, lazy loading de mídia e Range requests |
| `dotnet test` | Suite completa (xUnit + E2E) |

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
src/Verso.Updater Aplica update após o App sair (Windows trava o exe em uso)
src/Verso.Core    Engine (whisper.net), serviços, dados (EF Core + SQLite), export
tests/Verso.Tests testes xUnit
```

## Releases

Pipeline em `.github/workflows/release.yml`. Tag `v*.*.*` ou push na `main` gera um GitHub Release com **6 zips** (cpu/gpu × win-x64 / linux-x64 / osx-arm64) e, no Windows, **instaladores** `*-setup.exe`:

| Arquivo | Aceleração |
|---------|------------|
| `*-cpu-win-x64-setup.exe` / `*-gpu-win-x64-setup.exe` | Instalador Windows (sem admin), Menu Iniciar |
| `*-cpu-win-x64.zip` / `*-gpu-win-x64.zip` | Portátil Windows · CPU · CUDA/Vulkan |
| `*-cpu-linux-x64.zip` / `*-gpu-linux-x64.zip` | Portátil Linux · CPU · CUDA/Vulkan |
| `*-cpu-osx-arm64.zip` / `*-gpu-osx-arm64.zip` | Portátil macOS · CPU · Core ML |

O zip **portátil continua first-class**: extraia e execute. O setup só facilita atalho e pasta padrão (`%LOCALAPPDATA%\Programs\Verso`).

Cada pacote tem o app + `Verso.Updater` + Worker (`engine/`) + `wwwroot/` + `runtimes/`. Self-contained (sem .NET instalado). **Dados portáteis** em `data/` ao lado do executável.

**Atualização automática:** nas versões de release (`verso-channel.json` presente), o app consulta a latest GitHub Release, baixa o zip da mesma variante (cpu/gpu e SO) e substitui só os binários. A pasta `data/` (banco, modelos, mídia, logs) e qualquer arquivo extra na pasta do app **não são apagados**. Transcrição em andamento não é interrompida — o update aplica no próximo start. Em `dotnet run` (sem channel) o check não roda.

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

A próxima tag assina os `.exe` da raiz (`Verso.App.exe`, `Verso.Updater.exe` e `Verso.Worker.exe` em `engine/`) automaticamente.

## Roadmap

### Pendente de validação

- **Confirmar estabilidade do crash nativo (`0x80131506`)** — mitigações aplicadas no engine (fábrica fresca por job, sem `WithStringPool`, cancelamento entre chunks, dispose explícito); falta UAT com transcrição longa real (AD-006).
- **UAT ponta a ponta** — upload → transcrição → editor → exportação, nunca validado manualmente.

### Planejado

- **Drag & drop funcional no upload** — hoje só seleção por clique
- **Progresso determinado no download de modelos** — barra atual é indeterminada

### Futuro (fora de escopo atual)

- **macOS Intel (osx-x64)** — release atual foca em Apple Silicon (`osx-arm64`).
