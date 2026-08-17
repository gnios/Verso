# Performance de navegação — Photino + Blazor

Documento de descobertas após a migração Avalonia → Photino.Blazor, focado em lentidão de navegação/UI mantendo o app **cross-platform** (Windows / macOS / Linux).

**Data:** 2026-07-18  
**Stack:** Photino.Blazor 4.x · .NET 10 · WebView nativo (`blazor.webview.js`) — **não** é Blazor Server nem WASM.

---

## Arquitetura (resumo)

A UI roda em WebView nativo (WebView2 no Windows; WebKit no macOS/Linux). O root é `MainLayout` em `#app`; a navegação é um **shell customizado** (`NavigationService` + `ScreenKey`), sem `Router` do ASP.NET. ViewModels de tela são **Transient** (novo a cada `NavigateTo`); serviços de shell (nav, áudio HTML5, sidebar, temas) são **Singleton**. Dados EF passam por `IServiceScopeFactory`. Áudio: `<audio>` + scheme `versomedia://` (substituiu LibVLC).

---

## Bottlenecks encontrados

### 1. Playback → cascata de UI a cada `timeupdate` (crítico)

- `wwwroot/js/audio.js` chama `OnPositionChanged` em todo `timeupdate` (~4 Hz).
- `PlayerBarViewModel` → `PositionChanged` → `EditorViewModel.SetPlaybackPosition` → `UpdateActiveSegmentHighlight` (percorre **todos** os segmentos, seta `IsActive`) + `SpeakerDropdown.RefreshActiveIndicator` + `OnPropertyChanged(HasActiveSegment)`.
- `ScrollToSegmentRequested` dispara **sempre** que há segmento ativo (mesmo se não mudou) → JS `scrollToSegmentById` a cada tick.
- `Editor.razor` faz `StateHasChanged` em **qualquer** `PropertyChanged` do VM; cada `SegmentItem` também.

**Arquivos:** `audio.js`, `Html5AudioPlaybackService.cs`, `PlayerBarViewModel.cs`, `EditorViewModel.cs`, `Editor.razor`, `SegmentItem.razor`.

### 2. Handlers vazando (Transient → Singleton)

`DashboardViewModel`, `FolderViewModel`, `EditorViewModel` assinam `TranscriptionQueueService` (singleton) e **não** implementam `IDisposable` / unsubscribe. `PlayerBarViewModel` assina `IMediaPlaybackService` e nunca remove o handler. Cada visita ao Editor/Dashboard acumula listeners → trabalho fantasma + mais `UiThread.Invoke`.

**Arquivos:** `DashboardViewModel.cs`, `FolderViewModel.cs`, `EditorViewModel.cs`, `PlayerBarViewModel.cs`, `AppServiceCollectionExtensions.cs`.

### 3. `NavigateTo` bloqueia a UI ao sair do Editor

```csharp
UnloadAsync().GetAwaiter().GetResult(); // síncrono na thread que navega
```

Interop JS + unload do `<audio>` na thread de UI — hitch na troca de tela.

**Arquivo:** `NavigationService.cs`.

### 4. Dashboard: busca SQLite a cada tecla

`OnSearchTextChanged` → `LoadAsync` → `LibraryService.SearchText` com `Segments.Any(...)` — query cara, sem debounce.

**Arquivos:** `DashboardViewModel.cs`, `LibraryService.cs`.

### 5. `LoadAsync` duplicado na entrada do Dashboard

`ApplyNavigationParameter` seta `ActiveStatusFilter` / `UnassignedOnly` (cada um dispara `LoadAsync` via `On*Changed`) e depois `Initialize` chama `LoadAsync` de novo.

**Arquivo:** `DashboardViewModel.cs`.

### 6. Sidebar: load duplo no startup + 5 counts

`LoadAsync` faz `GetCountAsync` × 5 + folders + tags. Construtor já chama `_ = LoadAsync()`; `Sidebar.razor` chama de novo em `OnInitializedAsync`.

**Arquivos:** `SidebarViewModel.cs`, `Sidebar.razor`.

### 7. Editor: carga pesada síncrona à navegação

`Initialize` → `LoadAsync`: detail com `Include` de segmentos/speakers/tags/folder, montagem de N `SegmentItemViewModel`, e `PlayerBar.LoadAsync` (espera metadata até 15s).

**Arquivos:** `EditorViewModel.cs`, `LibraryService.cs`.

### 8. Listas sem virtualização

Dashboard/Folder/Editor usam `@foreach` completo (há `@key`, mas sem `Virtualize`). `Cards.Clear()` + new VMs a cada reload — remount de todos os cards.

### 9. Progresso da fila → re-render por card (+ reload no Done)

`ProgressPercent`/`ProgressStage` → vários `PropertyChanged` → `TranscriptionCard.StateHasChanged`. No Done do Dashboard: `LoadAsync()` + `_sidebar.LoadAsync()` — rebuild da grid + sidebar.

### 10. Logging de arquivo com flush por linha

`FileLogger` faz `Flush()` a cada log sob lock. Não é o hot path da UI, mas durante jobs compete por I/O.

### 11. `UiThread` fire-and-forget (saturável)

`Invoke` enfileira no `Dispatcher` sem coalescer. Com vazamento de handlers + `timeupdate` + progresso, a fila do dispatcher incha.

### 12. DI Transient “barato” mas caro no efeito

Telas Transient evitam estado stale, mas sem dispose + trabalho no `Initialize` tornam **cada navegação** = novo VM + DB + subscriptions.

---

## Plano de ação

### Fase 1 — Quick wins (esta fase)

| # | Item | Status |
|---|------|--------|
| QW1 | Highlight/scroll de segmento só quando o `activeId` mudar | ✅ |
| QW2 | Throttle de posição no JS (~100 ms; force em seek/ended) | ✅ |
| QW3 | `IDisposable` nos VMs Transient + unsubscribe; dispose do VM antigo em `NavigateTo` | ✅ |
| QW4 | Debounce da busca do Dashboard (300 ms) | ✅ |
| QW5 | Evitar double `LoadAsync` (flag suppress + Sidebar sem load no ctor) | ✅ |
| QW6 | `NavigateTo` sem `GetResult()` no unload (fire-and-forget) | ✅ |

### Fase 2 — Médio prazo

1. `StateHasChanged` seletivo nas pages (só props usadas).
2. `Virtualize` em cards e segmentos.
3. Progresso da fila: atualizar só o card afetado; no Done, patch em vez de reload total.
4. Sidebar: uma query agregada de counts.

### Fase 3 — Refactors

1. Editor em estágios (meta primeiro; segmentos depois; áudio sem bloquear primeiro paint).
2. Cache leve de summaries da biblioteca com invalidação na fila.
3. Logging com buffer / flush periódico.

---

## O que **não** fazer (neste stack)

- Trocar Photino por WinUI / WPF / Avalonia-only por “perf”.
- Migrar para Blazor Server ou WASM.
- APIs WebView2 proprietárias sem equivalente Photino.
- Voltar LibVLC só por performance — otimizar interop/`timeupdate`.

---

## Como validar (após Fase 1)

1. Abrir Editor com muitos segmentos, dar play: scroll/highlight só ao trocar de segmento; progress bar ainda suave.
2. Navegar Dashboard ↔ Editor ↔ Folder várias vezes: sem crescimento de handlers (CPU ociosa estável).
3. Digitar na busca do Dashboard: uma query após pausa, não a cada tecla.
4. Clicar filtros da sidebar: um único load por navegação.
5. Sair do Editor: navegação não “trava” no unload do áudio.

---

## Transição Biblioteca → Editor (investigação)

### Sintoma

Ao clicar numa transcrição, há demora perceptível até o conteúdo aparecer. A tela do Editor montava “vazia” enquanto `LoadAsync` rodava até o fim.

### Causa (pipeline antigo de `EditorViewModel.LoadAsync`)

Ordem **sequencial** na abertura:

1. `GetTranscriptionDetailAsync` — SQLite com `Include` de **todos** os segmentos + speakers + tags + folder (custo cresce com o tamanho da transcrição).
2. `SpeakerDropdown.LoadSpeakersAsync` — **query redundante** (speakers já vinham no detail).
3. `LoadFolderOptionsAsync` — outra query.
4. `LoadTagOptionsAsync` — outra query.
5. Montagem de N `SegmentItemViewModel` + primeiro render Blazor de N `SegmentItem`.
6. `PlayerBar.LoadAsync` — **await de metadata do `<audio>` com timeout de até 15s** (JS interop). Isso mantinha `LoadAsync` “vivo” mesmo depois do texto já estar em memória.

Enquanto isso, não havia `IsLoading`: o usuário via shell vazia ou hitch sem feedback.

### Mitigações aplicadas (2026-07-18)

| Item | O quê |
|------|--------|
| Feedback | `IsLoading` + UI “Abrindo transcrição…” com barra indeterminada |
| Time-to-content | `IsLoading = false` assim que header/segmentos estão prontos |
| Áudio | `LoadPlaybackAsync` em background (não bloqueia o paint) |
| Speakers | `SetSpeakers` a partir do detail (sem query extra) |
| Comboboxes | `LoadFolderOptions` + `LoadTagOptions` em paralelo **depois** do conteúdo principal |

### Freeze ao abrir com muitos segmentos (corrigido)

DB local tinha transcrições com **~1200–2200 segmentos**. Mesmo com áudio lazy, ao sair do loading o Blazor montava **N** `SegmentItem` (cada um com `<textarea>` + handlers) → UI freeze.

Mitigações:
1. `<Virtualize>` na lista (só ~20 componentes no DOM)
2. Scrollport com altura limitada (`flex:1; height:0; min-height:0; overflow-y:auto`) — **sem isso o Virtualize monta todos os itens**
3. `ReplaceSegments` (troca a coleção de uma vez — sem N× `CollectionChanged`)
4. Montagem dos VMs com `ConfigureAwait(false)` + apply na UI thread
5. Highlight O(1) no segmento anterior/atual (não percorre todos a cada tick)

Nota: se o app (Verso.App / Verso.Worker) estiver aberto, o build não consegue sobrescrever as DLLs — mudanças parecem “não fazer efeito”.

### Ainda na fila (próximos passos Editor)

1. Detail sem trazer todos os segmentos de uma vez (paginação SQLite).
2. Skeleton do header com título já conhecido do card (passar summary no `NavigationParameter`).

---

## Áudio: custom scheme Photino vs HTTP local

### Sintoma nos logs

Mensagens enormes com blob **base64** ao abrir transcrição. Há duas fontes possíveis:

1. **IPC do Blazor WebView** (`SendWebMessage(__bwv:[...])`) — ruído de render (já filtrado do Trace; ver `Program.ConfigureLogging`).
2. **Custom scheme `versomedia://`** — o Photino exige `IntPtr` + `outNumBytes` (`CppWebResourceRequestedDelegate`). Ou seja, o handler C# devolve um `Stream` e o native **lê o arquivo inteiro para um buffer** antes de entregar ao WebView. Áudio de dezenas/centenas de MB vira um “dump” gigante na abertura.

### Correção

| Antes | Depois |
|-------|--------|
| `versomedia://` via `RegisterCustomSchemeHandler` | `LocalMediaServer` em `http://127.0.0.1:{port}/` com **Range** |
| `LoadAsync` setava `audio.src` + await metadata (até 15s) | Lazy: `audio.src` só no **primeiro Play/Seek** |
| Duração vinha do metadata do arquivo | Duração do **DB** na barra; metadata atualiza depois se necessário |

Arquivos: `LocalMediaServer.cs`, `Html5AudioPlaybackService.cs`, `PlayerBarViewModel.cs`, `Program.cs`.

### E2E (Playwright)

Projeto `tests/Verso.E2E` — ver `tests/Verso.E2E/README.md` e `scripts/run-e2e.ps1`.

- Harness Chromium + `LocalMediaServer` (sempre)
- App Photino via CDP (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=…`)
