# STATE

## Decisions

### AD-001
- **Decision**: Stack de UI é Avalonia (MVVM, CommunityToolkit.Mvvm) com Generic Host (`Microsoft.Extensions.Hosting`) para DI e `BackgroundService`s; persistência via EF Core + SQLite (`IDbContextFactory` para acesso thread-safe fora da UI thread).
- **Reason**: Avalonia permite estilização customizada próxima ao CSS do protótipo; EF Core dá migrations automáticas; Generic Host é o padrão comum para apps desktop .NET que precisam de DI + jobs em background.
- **Trade-off**: Mais peças móveis que uma abordagem direta com SQL manual/Task.Run, mas testabilidade e evolução do schema compensam para um app com múltiplas telas e jobs longos.
- **Scope**: Toda a aplicação Transcriba Desktop — qualquer feature futura que precise de acesso a dados ou jobs em background deve seguir esse padrão.
- **Date**: 2026-07-04
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
- **Phase / Task**: Tasks concluído (50 tasks, 15 fases, `tasks.md` gerado com Test Coverage Matrix/Parallelism/Gate Commands e as 3 validações pré-aprovação). Aguardando aprovação do usuário para iniciar Execute.
- **Completed**: Specify (spec.md + context.md), Design (design.md), Tasks (tasks.md)
- **In-progress**: nenhum
- **Next step**: Confirmar com o usuário (a) aprovação de `tasks.md`, (b) se deseja sub-agentes por fase durante o Execute (>3 fases, pergunta anterior foi pulada), e então iniciar Execute pela Fase 1 (T1: criar solution e projetos).
- **Blockers**: none
- **Uncommitted files**: `.specs/features/transcriba-desktop/spec.md`, `.specs/features/transcriba-desktop/context.md`, `.specs/features/transcriba-desktop/design.md`, `.specs/features/transcriba-desktop/tasks.md`, `.specs/STATE.md`
- **Branch**: (verificar com `git branch --show-current`)
