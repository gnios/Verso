# STATE

## Decisions

### AD-001
- **Decision**: Stack de UI é Avalonia (MVVM, CommunityToolkit.Mvvm) com Generic Host (`Microsoft.Extensions.Hosting`) para DI e `BackgroundService`s; persistência via EF Core + SQLite (`IDbContextFactory` para acesso thread-safe fora da UI thread).
- **Reason**: Avalonia permite estilização customizada próxima ao CSS do protótipo; EF Core dá migrations automáticas; Generic Host é o padrão comum para apps desktop .NET que precisam de DI + jobs em background.
- **Trade-off**: Mais peças móveis que uma abordagem direta com SQL manual/Task.Run, mas testabilidade e evolução do schema compensam para um app com múltiplas telas e jobs longos.
- **Scope**: Toda a aplicação Transcriba Desktop — qualquer feature futura que precise de acesso a dados ou jobs em background deve seguir esse padrão.
- **Date**: 2026-07-04
- **Status**: superseded by AD-005

### AD-005
- **Decision**: Stack de UI passa de Avalonia para **Blazor Hybrid** — host WPF (`Microsoft.NET.Sdk.Razor`, `net10.0-windows10.0.17763.0`) com `BlazorWebView` (WebView2/Chromium), Razor components no lugar de Views .axaml, mesmo `CommunityToolkit.Mvvm` reaproveitado onde fizer sentido (ViewModels compartilhados via DI). `Transcriba.Core` (motor de transcrição, EF Core/SQLite, LibVLC, serviços de domínio) permanece 100% intacto — só a camada de apresentação (`Transcriba.App`) é trocada.
- **Reason**: A implementação Avalonia sofria de um "Internal CLR error (0x80131506)" fatal e não determinístico. **Diagnóstico original (culpando o pipeline de composição/Skia do Avalonia) estava ERRADO** — ver AD-006: a causa raiz confirmada via dump é corrupção de heap nativa no whisper.net/whisper.cpp durante a transcrição, não na camada de UI. A migração para Blazor Hybrid foi mantida por outros motivos ( reaproveitamento do HTML/CSS do protótipo, stack WebView2 madura), mas **não resolveu o crash** — ele persistiu após a migração porque `Transcriba.Core` não foi tocado. O app é Windows-only (suporte multiplataforma nunca foi meta real). BlazorWebView usa o motor WebView2/Chromium e permite reaproveitar o HTML/CSS do protótipo `transcriba-v2-icons-transcriptions.html` quase verbatim.
- **Trade-off**: Perde-se o binding compilado/XAML "nativo" do Avalonia; interações que hoje são JS puro no protótipo (drag&drop, waveform canvas, popups de ícone/cor) precisam ser portadas para Razor + JS interop pontual. Ganha-se fidelidade visual ao protótipo. **Não resolve o crash 0x80131506** (esse foi tratado em AD-006).
- **Scope**: Toda a aplicação Transcriba Desktop (`Transcriba.App`) — `Transcriba.Core` não é afetado. Qualquer feature futura de UI deve seguir o padrão Blazor Hybrid (Razor components + BlazorWebView), não Avalonia.
- **Date**: 2026-07-04
- **Status**: active (razão original superseded by AD-006)

### AD-006
- **Decision**: Tratar a causa raiz do crash "Fatal error. Internal CLR error (0x80131506)" no `Transcriba.Core/Engine` (whisper.net/whisper.cpp), não na camada de UI. Mitigações aplicadas em `WhisperTranscriptionEngine` e `WhisperProcessorFactory`:
  1. **Fábrica fresca por job** — `_factoryCache.Invalidate(modelPath)` ao fim de cada `TranscribeAsync` (no `finally`), forçando recarga na próxima transcrição. Antes a mesma `WhisperFactory` (cache singleton por `modelPath`) acumulava centenas de ciclos de create/dispose de `WhisperProcessor` entre jobs, frágil no whisper.net 1.9.1.
  2. **Sem `WithStringPool()`** — removido do builder em `WhisperProcessorFactory.CreateProcessor`. O pool nativo de strings era o ponto onde vazamentos/corrupção se manifestavam entre ciclos de create/dispose.
  3. **`CancellationToken` não repassado para a chamada nativa** — `processor.ProcessAsync(samples, CancellationToken.None)` no `WhisperProcessorAdapter`. Cancelamento mid-decode rasgava o enumerável nativo no meio de `whisper_full_with_state`, deixando estado nativo inconsistente. O cancelamento agora é honrado apenas entre chunks (`ThrowIfCancellationRequested` no início de cada iteração do loop de partes).
  4. **`DisposeAsync` explícito por chunk** — `try/finally` por chunk em vez de `await using`, garantindo dispose mesmo se `ProcessAsync` lançar e evitando overlap de lifecycle entre iterações adjacentes (o dump mostrou 2 `WhisperProcessor` vivos simultaneamente).
  5. **Bump de `Whisper.net.AllRuntimes`** — N/A: já está na versão mais recente publicada (1.9.1, 2026-06-01); não há versão mais nova para subir. Reavaliar quando sair >1.9.1.
- **Reason**: Diagnóstico confirmado por análise direta do dump `Transcriba.App.exe.18284.dmp` com `dotnet-dump`: (a) `pe` → `System.ExecutionEngineException` HResult `0x80131506` (sinal do CLR de heap corrompida além de recuperação); (b) `dumpheap -stat` recusou percurso com "GC heap is not in a valid state" (corrupção confirmada); (c) `clrstack -all` mostrou thread `0x8240` (ThreadPool, não-UI) em P/Invoke `IL_STUB_PInvoke(... Whisper.net.Native.WhisperFullParams ...)` = `whisper_full_with_state` no instante do crash; (d) 138 `WhisperSegmentData` vivos = transcrição do `sample-test-23min.mp4` em andamento. A stack de UI (`MouseDevice.UpdateCursorPrivate`) é apenas onde a corrupção foi *percebida*, não *causada* — exatamente como `scripts/run-with-crash-dump.ps1:7-9` já descrevia. Issue upstream: `sandrohanea/whisper.net#341`.
- **Trade-off**: Fábrica fresca por job recarrega o modelo a cada transcrição (mais lento) — aceitável dado que transcrições são jobs longos e o custo de reload é dwarfed pelo decode. Cancelamento entre chunks (em vez de mid-decode) significa que um chunk em andamento precisa terminar antes do cancelamento efetivar — aceitável dado que chunks são limitados (~10s cada pelo `SilenceSplitter`/`ChunkPlanner`).
- **Scope**: `Transcriba.Core/Engine/WhisperTranscriptionEngine.cs`, `Transcriba.Core/Engine/WhisperProcessorFactory.cs`, `tests/Transcriba.Tests/Engine/WhisperTranscriptionEngineTests.cs`. `Transcriba.App` (UI) não é afetado — confirma que a migração AD-005 era ortogonal a este bug.
- **Date**: 2026-07-05
- **Status**: active

### AD-002
- **Decision**: Reprodução de áudio/vídeo usa LibVLC (`LibVLCSharp`) apenas para saída de áudio (sem `VideoView`/renderização de vídeo).
- **Reason**: O player do protótipo nunca exibe vídeo, só controla áudio; usar `VideoView` introduziria os problemas conhecidos de "airspace" do LibVLC em Avalonia (janela nativa sobreposta) sem necessidade real.
- **Trade-off**: Se uma feature futura precisar de preview de vídeo, será necessário resolver o airspace hack (ex.: `NativeControlHost`) — não coberto por este design.
- **Scope**: `IMediaPlaybackService` e qualquer feature de reprodução de mídia.
- **Date**: 2026-07-04
- **Status**: active

### AD-003
- **Decision**: `Speaker` (locutor) é escopado por transcrição, não global.
- **Reason**: O protótipo usa um array global de locutores só porque é dado mockado de demonstração de uma única transcrição; em uso real, cada entrevista tem seus próprios participantes.
- **Trade-off**: Nenhum locutor é reaproveitado automaticamente entre transcrições diferentes (usuário recria o nome se necessário) — aceitável dado o domínio (entrevistas de pesquisa geralmente têm participantes distintos).
- **Scope**: Modelo de dados (`Speaker`), `SpeakerService`, editor de transcrição.
- **Date**: 2026-07-04
- **Status**: active

### AD-004
- **Decision**: Fila de transcrição (`TranscriptionQueueService`) processa **1 job por vez** por padrão (serial), mesmo com múltiplos uploads enfileirados.
- **Reason**: O cálculo de paralelismo herdado de `transcrever.cs` (`CalcularLimitesParalelos`) assume uso exclusivo da CPU/GPU da máquina; rodar 2+ jobs simultâneos sobrecarregaria os núcleos e invalidaria essa premissa.
- **Trade-off**: Uploads múltiplos ficam em fila em vez de processar em paralelo entre si (mas cada job individual já usa paralelismo interno).
- **Scope**: `TranscriptionQueueService` e qualquer feature futura que enfileire jobs de transcrição.
- **Date**: 2026-07-04
- **Status**: active

## Handoff

- **Feature**: transcriba-desktop (`.specs/features/transcriba-desktop/`)
- **Phase / Task**: Migração Blazor Hybrid (AD-005) **Execute concluído** — Fases 16–25 (T51–T68) ✅. Todas as telas portadas para Razor; `MainLayout.razor` integra Dashboard, Research, Upload, Recording, Settings e Editor.
- **Completed**: Specify, Design, Tasks, Execute (Fases 1–15 Core + serviços Avalonia descontinuados); Fases 16–25 (T51–T68, stack Blazor Hybrid completa)
- **In-progress**: nenhum
- **Next step**: UAT manual ponta a ponta (`dotnet run --project src/Transcriba.App`) — upload → transcrição → editor → exportação; commitar arquivos pendentes do bootstrap (Fase 16) ainda não commitados (`Program.cs`, `App.xaml*`, `MainWindow.xaml*`, remoções Avalonia, etc.); re-verifier opcional (`validation.md`)
- **Blockers**: none
- **Limitações conhecidas**: drag&drop de upload sem path absoluto (só seleção por clique); barra de progresso do download de modelo indeterminada; AD-002 ainda cita LibVLC mas playback pode ter migrado para NAudio (ver diff não commitado em `Transcriba.Core`)
- **Branch**: master
