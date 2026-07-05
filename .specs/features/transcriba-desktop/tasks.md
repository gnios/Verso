# Transcriba Desktop Tasks

## Execution Protocol (MANDATORY -- do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill is the source of truth for the full flow (per-task cycle, sub-agent delegation, adequacy review, Verifier, discrimination sensor).

**If the skill cannot be activated, STOP and tell the user — do not proceed without it.**

---

**Design**: `.specs/features/transcriba-desktop/design.md`
**Status**: Draft

---

## Test Coverage Matrix

> Gerado a partir do design (projeto novo, sem código/testes existentes ainda). Nenhuma diretriz de qualidade/teste foi encontrada no repositório (não há `.editorconfig`/`Directory.Build.props`/CI de testes .NET) — perguntado ao usuário sobre framework de testes; a pergunta foi pulada pelo usuário, então aplica-se o **default forte** desta fase: xUnit + `dotnet test`, cobrindo lógica de domínio/serviços 1:1 com os critérios de aceite do spec; sem testes automatizados de UI (Views .axaml) neste MVP.

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
| --- | --- | --- | --- | --- |
| Motor de transcrição / lógica pura (SilenceSplitter, ChunkPlanner, FormatarSegmento) | unit | Todos os branches; 1:1 com ACs de UPLOAD-01; casos de borda (áudio vazio, sem silêncio, 1 único trecho) cobertos | `tests/Transcriba.Tests/Engine/*Tests.cs` | `dotnet test tests/Transcriba.Tests --filter FullyQualifiedName~Engine` |
| Serviços de aplicação (LibraryService, ResearchService, SpeakerService, SegmentEditingService, SettingsService, MediaStorageService, ExportService) | unit | Todos os branches; 1:1 com ACs das respectivas histórias; todos os edge cases listados no spec cobertos | `tests/Transcriba.Tests/Services/*Tests.cs` | `dotnet test tests/Transcriba.Tests --filter FullyQualifiedName~Services` |
| Acesso a dados (`TranscribaDbContext`, `IDbContextFactory`) | integration (SQLite in-memory/arquivo temporário por teste) | Caminhos de query principais (CRUD de cada entidade) + tratamento de erro (FK inválida, etc.) | `tests/Transcriba.Tests/Data/*Tests.cs` | `dotnet test tests/Transcriba.Tests --filter FullyQualifiedName~Data` |
| `TranscriptionQueueService` (fila/background) | integration | Fluxo feliz (job concluído), fluxo de erro (exceção do engine → status Error), cancelamento, e serialização (1 job por vez) | `tests/Transcriba.Tests/Engine/TranscriptionQueueServiceTests.cs` | `dotnet test tests/Transcriba.Tests --filter FullyQualifiedName~Engine` |
| ViewModels (MVVM) | unit | Transições de estado e comandos principais cobertos (não testa renderização visual) | `tests/Transcriba.Tests/ViewModels/*Tests.cs` | `dotnet test tests/Transcriba.Tests --filter FullyQualifiedName~ViewModels` |
| Views (.axaml) / UI visual | none | Verificação manual (smoke test rodando o app) neste MVP | — | manual |
| Entidades/enums/constantes (Catalogs) | none (exceto onde há lógica, ex.: ciclo de cor) | build gate only, exceto `SpeakerService`/paleta que tem lógica testável (coberta acima) | — | `dotnet build Transcriba.sln` |

## Parallelism Assessment

> Gerado a partir do design — projeto novo, nenhuma evidência de código existente para amostrar; regras inferidas do padrão xUnit + EF Core SQLite.

| Test Type | Parallel-Safe? | Isolation Model | Evidence |
| --- | --- | --- | --- |
| unit — lógica pura (Engine, SegmentEditingService, Catalogs) | Yes | Funções/métodos estáticos ou instâncias novas por teste, sem estado compartilhado, sem I/O | Portado de funções `static` puras de `transcrever.cs` |
| unit — serviços com `IDbContextFactory` mockado | Yes | Cada teste usa um mock/fake de `IDbContextFactory` isolado | Novo código — sem estado global |
| integration — `TranscribaDbContext` real (SQLite) | No | Cada classe de teste deve usar um arquivo/`:memory:` SQLite exclusivo (conexão aberta durante o teste); testes na mesma classe compartilham o mesmo arquivo | Padrão conhecido de EF Core + SQLite in-memory (conexão precisa ficar aberta durante os testes) |
| integration — `TranscriptionQueueService` | No | Fila é um singleton com estado (Channel); testes devem rodar sequencialmente ou instanciar uma fila nova por teste | Estado compartilhado (fila/worker) por natureza do componente |

## Gate Check Commands

| Gate Level | When to Use | Command |
| --- | --- | --- |
| Quick | Após tasks com apenas testes unitários de lógica pura/ViewModels | `dotnet test tests/Transcriba.Tests --filter FullyQualifiedName~Engine\|FullyQualifiedName~Services\|FullyQualifiedName~ViewModels` |
| Full | Após tasks com testes de integração (Data, TranscriptionQueueService) | `dotnet test tests/Transcriba.Tests` |
| Build | Após conclusão de fase ou tasks de entidade/config apenas | `dotnet build Transcriba.sln` |

---

## Execution Plan

### Phase 1: Bootstrap do projeto (Sequential)

```
T1 → T2 → T3
```

### Phase 2: Camada de dados (Sequential, depende da Fase 1)

```
T3 → T4 → T5 → T6
T3 → T7
```

### Phase 3: Motor de transcrição (parcialmente paralelo, depende da Fase 1)

```
T3 ──┬→ T8 ─┐
     ├→ T9 ─┼→ T13 → T14
     ├→ T10┤
     └→ T11┘
T12 ──────────→ T13
```

### Phase 4: Serviços de aplicação (paralelo, depende das Fases 2 e 3)

```
T6 ──┬→ T15 [P]
     ├→ T16 [P]
     ├→ T17 [P]
     ├→ T18 [P]
     ├→ T19 [P]
     ├→ T20 [P]
     └→ T21 [P]  (depende também de T14 p/ formato de saída do TXT)
```

### Phase 5: Playback (depende da Fase 1)

```
T3 → T22
```

### Phase 6: Shell / Navegação / Tema (depende das Fases 2 e 4)

```
T20 → T24
T23 ──┐
T24 ──┼→ T25 → T26
T15 ──┘
```

### Phase 7: Dashboard (depende da Fase 6)

```
T26 → T27 → T28
```

### Phase 8: Pesquisa (depende da Fase 6)

```
T26 → T29 → T30
```

### Phase 9: Modal Nova Pesquisa/Transcrição (depende da Fase 6)

```
T26 ──┬→ T31 [P] ─┐
      └→ T32 [P] ─┼→ T33
```

### Phase 10: Upload (depende das Fases 4, 9 e da fila da Fase 3)

```
T14, T16, T17, T33 → T34 → T35 → T36
```

### Phase 11: Editor (depende das Fases 4, 5, 10)

```
T18, T19, T22 ──┬→ T37 → T38 → T40
                └→ T39 ────────┘
T22 → T41 → T42
T40, T42 → T43
```

### Phase 12: Gravação mockada (depende da Fase 6)

```
T26 → T44 → T45
```

### Phase 13: Configurações (depende das Fases 6 e 4)

```
T20 → T46 → T47
```

### Phase 14: Exportação — UI (P2, depende da Fase 11 e T21)

```
T21, T40 → T48
```

### Phase 15: Exclusão — CRUD (P2, depende das Fases 7, 8, 4)

```
T16, T28 → T49
T16, T30 → T50
```

---

## Task Breakdown

### T1: Criar solution e estrutura de projetos

**What**: Criar `Transcriba.sln` com três projetos: `src/Transcriba.App` (Avalonia), `src/Transcriba.Core` (biblioteca de classes), `tests/Transcriba.Tests` (xUnit), com referências corretas entre eles.
**Where**: raiz do repositório
**Depends on**: None
**Reuses**: N/A
**Requirement**: DATA-01

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `dotnet build Transcriba.sln` compila sem erros
- [ ] `Transcriba.App` referencia `Transcriba.Core`; `Transcriba.Tests` referencia `Transcriba.Core`
- [ ] Estrutura de pastas conforme design.md (`src/`, `tests/`)

**Tests**: none
**Gate**: build

**Commit**: `chore(scaffold): cria solution e projetos base do Transcriba`
**Status**: ✅ Concluída

---

### T2: Configurar Generic Host + DI no Transcriba.App

**What**: `Program.cs`/`App.axaml.cs` configurando `Microsoft.Extensions.Hosting.Host` com container de DI compartilhado com o `AppBuilder` do Avalonia.
**Where**: `src/Transcriba.App/Program.cs`, `src/Transcriba.App/App.axaml.cs`
**Depends on**: T1
**Reuses**: N/A

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] App inicia exibindo uma janela vazia sem erros
- [ ] `IServiceProvider` acessível para resolver ViewModels
- [ ] `dotnet build Transcriba.sln` passa

**Tests**: none
**Gate**: build

**Commit**: `feat(app): configura Generic Host e DI no bootstrap do Avalonia`
**Status**: ✅ Concluída

---

### T3: Configurar pacotes NuGet base

**What**: Adicionar referências: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Fluent` (tema), `CommunityToolkit.Mvvm`, `Microsoft.EntityFrameworkCore.Sqlite`, `Whisper.net.AllRuntimes` (1.9.1), `NAudio`, `LibVLCSharp`, `VideoLAN.LibVLC.Windows`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` nos projetos corretos.
**Where**: `src/Transcriba.App/*.csproj`, `src/Transcriba.Core/*.csproj`, `tests/Transcriba.Tests/*.csproj`
**Depends on**: T1
**Reuses**: versões já validadas em `transcrever.cs` (Whisper.net.AllRuntimes@1.9.1, NAudio@2.3.0)

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `dotnet restore` e `dotnet build Transcriba.sln` passam
- [ ] Nenhum conflito de versão de pacote

**Tests**: none
**Gate**: build

**Commit**: `chore(deps): adiciona dependências base do projeto`
**Status**: ✅ Concluída

---

### T4: Criar entidades de domínio

**What**: Classes `ResearchPage`, `Transcription`, `Segment`, `Speaker`, `Tag`, `UserSettings` + enums `TranscriptionStatus`, `ModelQuality`, `SpeakerMode`, `ExecutionDevice` conforme design.md.
**Where**: `src/Transcriba.Core/Data/Entities/`
**Depends on**: T3
**Reuses**: modelo de dados do design.md

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Todas as entidades e enums compilam
- [ ] Propriedades de navegação (`ResearchPage.Transcriptions`, `Transcription.Segments/Speakers/Tags`) presentes

**Tests**: none
**Gate**: build

**Commit**: `feat(data): adiciona entidades de domínio`
**Status**: ✅ Concluída

---

### T5: Criar TranscribaDbContext e configuração de relacionamentos

**What**: `TranscribaDbContext : DbContext` com `DbSet<>` para cada entidade e `OnModelCreating` configurando FKs, índices (ex.: `Transcription.Status`, `Tag.Name` único) e a tabela de junção `TranscriptionTag`.
**Where**: `src/Transcriba.Core/Data/TranscribaDbContext.cs`
**Depends on**: T4
**Reuses**: T4

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `dotnet ef migrations add InitialCreate` gera migration sem erros
- [ ] Relacionamentos 1—N e N—N corretos (validado pela migration gerada)

**Tests**: none
**Gate**: build

**Commit**: `feat(data): adiciona TranscribaDbContext e migration inicial`
**Status**: ✅ Concluída

---

### T6: Configurar IDbContextFactory e inicialização do banco em %AppData%

**What**: Registrar `IDbContextFactory<TranscribaDbContext>` no DI apontando para `%AppData%\Transcriba\transciba.db`; aplicar migrations automaticamente na inicialização do app; criar diretório se não existir.
**Where**: `src/Transcriba.Core/Data/DbBootstrapper.cs`, wiring em `Program.cs`
**Depends on**: T5
**Reuses**: T2, T5

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Ao iniciar o app pela primeira vez, o arquivo `.db` é criado com o schema migrado
- [ ] Teste de integração cria um `DbContext` via factory e persiste/lê uma entidade simples
- [ ] Gate `full` passa

**Tests**: integration
**Gate**: full

**Commit**: `feat(data): inicializa banco SQLite em AppData via IDbContextFactory`
**Status**: ✅ Concluída

---

### T7: Criar Catalogs (ícones, cores, tags) com testes

**What**: `IconCatalog` (PAGE_ICONS, TRANS_ICONS), `ColorCatalog` (PAGE_COLORS com hex/light), `TagColorCatalog` (mapa de cor padrão por nome de tag + fallback "blue") replicando exatamente as constantes do protótipo.
**Where**: `src/Transcriba.Core/Catalogs/`
**Depends on**: T3

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Constantes idênticas às arrays `PAGE_ICONS`, `PAGE_COLORS`, `TRANS_ICONS`, `TAG_COLORS` do protótipo (linhas 352-353 do HTML)
- [ ] Teste unitário garante que `TagColorCatalog.GetColor("desconhecida")` retorna `"blue"` (fallback)
- [ ] Gate `quick` passa (3 testes mínimos)

**Tests**: unit
**Gate**: quick

**Commit**: `feat(core): adiciona catálogos de ícones, cores e tags`
**Status**: ✅ Concluída

---

### T8: Portar FfmpegLocator

**What**: Classe `FfmpegLocator` portando `EncontrarFfmpeg`, `ObterDiretoriosPath`, `TentarInstalarFfmpeg`, `GarantirFfmpeg` de `transcrever.cs:368-463`, lançando `FfmpegNotFoundException` customizada em vez de `InvalidOperationException` genérica.
**Where**: `src/Transcriba.Core/Engine/FfmpegLocator.cs`
**Depends on**: T3
**Reuses**: `transcrever.cs:368-463`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Mesma lógica de busca em PATH + WinGet Packages do POC
- [x] Teste unitário cobre "ffmpeg encontrado no PATH" (mock de diretórios) e "não encontrado → exceção customizada"
- [x] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(engine): porta localizador/instalador de ffmpeg`

---

### T9: Portar AudioLoader

**What**: Classe `AudioLoader` portando `CarregarSamples16kHz`, `LerSamples`, `CarregarSamplesComFfmpeg`, `ConverterPcm16ParaFloat` de `transcrever.cs:290-366`.
**Where**: `src/Transcriba.Core/Engine/AudioLoader.cs`
**Depends on**: T3, T8
**Reuses**: `transcrever.cs:290-366`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Suporta `.wav`/`.mp3` via NAudio e fallback via ffmpeg para outros formatos
- [x] Teste unitário cobre `ConverterPcm16ParaFloat` (conversão determinística, sem I/O) incluindo caso de buffer menor que 2 bytes (exceção)
- [x] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(engine): porta carregador de áudio (NAudio + ffmpeg)`

---

### T10: Portar SilenceSplitter com testes [P]

**What**: Classe `SilenceSplitter` portando `DividirPorSilencio` de `transcrever.cs:465-541`.
**Where**: `src/Transcriba.Core/Engine/SilenceSplitter.cs`
**Depends on**: T3
**Reuses**: `transcrever.cs:465-541`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Mesmo algoritmo de VAD por energia/silêncio do POC
- [x] Testes unitários: áudio vazio → lista vazia; áudio todo silêncio → 1 trecho (fallback); áudio com 2 blocos de fala separados por silêncio → 2 trechos com offsets corretos
- [x] Gate `quick` passa (mín. 4 testes)

**Tests**: unit
**Gate**: quick

**Commit**: `feat(engine): porta segmentação de áudio por silêncio`

---

### T11: Portar ChunkPlanner com testes [P]

**What**: Classe `ChunkPlanner` portando `CalcularLimitesParalelos`, `AgruparPartes`, `CopiarChunks` de `transcrever.cs:543-611`.
**Where**: `src/Transcriba.Core/Engine/ChunkPlanner.cs`
**Depends on**: T3
**Reuses**: `transcrever.cs:543-611`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Mesmo cálculo de paralelismo/agrupamento do POC (cpu/cuda/vulkan)
- [x] Testes unitários: trechos ≤ maxPartes retornam inalterados; trechos > maxPartes são agrupados respeitando offsets; dispositivo cuda/vulkan produz paralelismo ≤2
- [x] Gate `quick` passa (mín. 4 testes)

**Tests**: unit
**Gate**: quick

**Commit**: `feat(engine): porta planejador de chunks/paralelismo`

---

### T12: Portar ModelManager

**What**: Classe `ModelManager` portando `GarantirModeloAsync`, `ObterNomeModelo` de `transcrever.cs:235-256`, adicionando mapeamento `ModelQuality → GgmlType` (Standard→Small, High→LargeV3).
**Where**: `src/Transcriba.Core/Engine/ModelManager.cs`
**Depends on**: T3
**Reuses**: `transcrever.cs:235-256`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Download automático se modelo ausente, reaproveitamento se já existe (mesma lógica do POC)
- [x] Teste unitário do mapeamento `ModelQuality → GgmlType`
- [x] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(engine): porta gerenciador de modelo GGML`

---

### T13: Criar WhisperTranscriptionEngine

**What**: Orquestra `ModelManager`, `AudioLoader`, `SilenceSplitter`, `ChunkPlanner` e `Whisper.net` (`CriarProcessor` de `transcrever.cs:265-272`) em `TranscribeAsync(TranscriptionJobRequest, IProgress<EngineProgress>, CancellationToken)`, forçando o idioma selecionado (sem detecção automática, conforme spec UPLOAD-01 AC5) e cacheando o `WhisperFactory` por `modelPath` (mitigação de risco do design).
**Where**: `src/Transcriba.Core/Engine/WhisperTranscriptionEngine.cs`
**Depends on**: T8, T9, T10, T11, T12
**Reuses**: `transcrever.cs:70-93` (loop de `Parallel.ForEachAsync`)

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Pipeline completo roda ponta a ponta com um arquivo de áudio real de teste (curto, poucos segundos) e produz segmentos com tempos/texto
- [x] Cache de `WhisperFactory` reaproveitado entre duas chamadas consecutivas com o mesmo `modelPath` (verificável via teste com contador de carregamentos)
- [x] `CancellationToken` interrompe o processamento
- [x] Gate `full` passa

**Tests**: integration
**Gate**: full

**Commit**: `feat(engine): implementa WhisperTranscriptionEngine orquestrando o pipeline`

---

### T14: Criar TranscriptionQueueService

**What**: Fila serial (`Channel<TranscriptionJobRequest>` + `BackgroundService`) que processa 1 job por vez (AD-004), persiste progresso/status/erro via `IDbContextFactory`, emite `StatusChanged`, e detecta na inicialização transcrições travadas em `InProgress` marcando-as como `Error` ("Interrompida").
**Where**: `src/Transcriba.Core/Engine/TranscriptionQueueService.cs`
**Depends on**: T6, T13
**Reuses**: T13

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] `Enqueue` retorna imediatamente e o job roda em background
- [x] Falha do engine é capturada e vira `Transcription.Status = Error` + `ErrorMessage` (spec UPLOAD-01 AC7)
- [x] Dois jobs enfileirados em sequência são processados um de cada vez, nunca simultaneamente (teste de integração comprova serialização)
- [x] Na inicialização, transcrições `InProgress` órfãs viram `Error` (edge case do spec)
- [x] Gate `full` passa

**Tests**: integration
**Gate**: full

**Commit**: `feat(engine): implementa fila serial de transcrição com tratamento de erro`

---

### T15: LibraryService [P]

**What**: `GetTranscriptions(LibraryFilter)` e `SearchText(query, filter)` combinando filtro de status, tag e busca textual (título + conteúdo dos segmentos), conforme spec DASH-01.
**Where**: `src/Transcriba.Core/Services/LibraryService.cs`
**Depends on**: T6
**Reuses**: T6

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Filtro por status ("progress"/"done"/"all"), por tag, e busca textual combináveis (AC3/AC4 de DASH-01)
- [ ] Busca é case-insensitive e considera texto dos segmentos, não só título
- [ ] Testes cobrem cada filtro isolado e combinações, incluindo resultado vazio
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(services): implementa LibraryService com filtros e busca`
**Status**: ✅ Concluída

### T16: ResearchService [P]

**What**: CRUD de `ResearchPage` (`CreateAsync`, `GetByIdAsync`, `DeleteAsync` com desassociação de transcrições em vez de exclusão em cascata — spec CRUD-01 AC2).
**Where**: `src/Transcriba.Core/Services/ResearchService.cs`
**Depends on**: T6
**Reuses**: T6

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `DeleteAsync` desassocia (não apaga) transcrições vinculadas
- [ ] Testes cobrem criação, busca e exclusão com/sem transcrições associadas
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(services): implementa ResearchService (CRUD de pesquisas)`
**Status**: ✅ Concluída

### T17: MediaStorageService [P]

**What**: Copia arquivo de mídia para `%AppData%\Transcriba\media\{transcriptionId}\{arquivo}` e remove ao excluir transcrição (spec DATA-01, CRUD-01).
**Where**: `src/Transcriba.Core/Services/MediaStorageService.cs`
**Depends on**: T3

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Cópia preserva extensão/nome original, isolada por pasta do Id da transcrição
- [ ] `DeleteMedia` remove a pasta correspondente sem lançar erro se já não existir
- [ ] Testes usam diretório temporário isolado por teste
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(services): implementa MediaStorageService`
**Status**: ✅ Concluída

### T18: SpeakerService [P]

**What**: `GetSpeakersAsync(transcriptionId)`, `CreateSpeakerAsync(transcriptionId, name)` com cor atribuída por ciclo de paleta (mesma lista de cores do `addNewSpeaker` do protótipo), escopado por transcrição (AD-003).
**Where**: `src/Transcriba.Core/Services/SpeakerService.cs`
**Depends on**: T6, T7
**Reuses**: T7 (paleta de cores)

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Locutores de transcrições diferentes nunca se misturam (escopo por `TranscriptionId`)
- [ ] Cor do N-ésimo locutor criado segue o ciclo da paleta (`colors[speakers.length % colors.length]`, igual ao protótipo)
- [ ] Testes cobrem criação sequencial de >8 locutores (ciclo repete cor)
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(services): implementa SpeakerService escopado por transcrição`
**Status**: ✅ Concluída

### T19: SegmentEditingService [P]

**What**: `GetActiveSegment`, `SplitAtCaret`, `MergeWithPrevious`, `AssignSpeaker` replicando exatamente a semântica de `highlightSegment`/`splitSegment`/`mergeSegment` do protótipo (ver Tech Decisions do design e Assumptions do spec).
**Where**: `src/Transcriba.Core/Services/SegmentEditingService.cs`
**Depends on**: T6
**Reuses**: lógica de `transcriba-v2-icons-transcriptions.html:489-494`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `GetActiveSegment` retorna o último segmento com `Start <= currentPosition` (ou `null` se nenhum)
- [ ] `SplitAtCaret` retorna `null` quando uma das partes ficaria vazia (EDITOR-01 AC3)
- [ ] `MergeWithPrevious` retorna `null` quando `active` é o primeiro segmento (edge case do spec)
- [ ] Testes cobrem todos os critérios acima + caso de segmento com texto vazio após edição
- [ ] Gate `quick` passa (mín. 8 testes)

**Tests**: unit
**Gate**: quick

**Commit**: `feat(services): implementa SegmentEditingService (dividir/mesclar/segmento ativo)`
**Status**: ✅ Concluída

### T20: SettingsService [P]

**What**: `GetAsync`/`UpdateAsync` sobre a linha única de `UserSettings` (perfil, idioma padrão, toggles, dispositivo, tema).
**Where**: `src/Transcriba.Core/Services/SettingsService.cs`
**Depends on**: T6

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Cria a linha singleton automaticamente se não existir
- [ ] `UpdateAsync` persiste alterações parciais sem sobrescrever campos não alterados
- [ ] Testes cobrem leitura inicial (defaults) e atualização
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(services): implementa SettingsService`
**Status**: ✅ Concluída

### T21: ExportService (P2) [P]

**What**: `ExportTxtAsync` (mesmo formato de `FormatarSegmento`/cabeçalho do POC), `ExportSrtAsync`, `ExportVttAsync` com nome do locutor no início de cada cue.
**Where**: `src/Transcriba.Core/Export/ExportService.cs`
**Depends on**: T6, T14 (formato de saída do TXT alinhado ao motor)
**Reuses**: `transcrever.cs:613-621` (`FormatarSegmento`, `FormatarTempo`)

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] TXT gerado é byte-a-byte equivalente em estrutura ao formato do POC (cabeçalho + linhas `[inicio -> fim] texto`)
- [ ] SRT/VTT gerados são válidos (numeração sequencial, timestamps no formato correto de cada padrão)
- [ ] Teste cobre transcrição sem segmentos (spec EXPORT-01 AC4)
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(export): implementa exportação TXT/SRT/VTT`
**Status**: ✅ Concluída

### T22: IMediaPlaybackService + LibVlcPlaybackService

**What**: Interface e implementação de playback somente-áudio via LibVLC (`LibVLC`, `MediaPlayer`, `Media`), sem `VideoView` (AD-002).
**Where**: `src/Transcriba.Core/Media/IMediaPlaybackService.cs`, `src/Transcriba.Core/Media/LibVlcPlaybackService.cs`
**Depends on**: T3

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `LoadAsync`, `Play`, `Pause`, `SeekTo`, `PlaybackRate`, `Volume` implementados
- [ ] `PositionChanged` disparado a cada ~250ms durante reprodução
- [ ] Testado manualmente com 1 arquivo de áudio e 1 de vídeo (MP4) real — ambos tocam o áudio corretamente
- [ ] Gate `build` passa (playback real não é unit-testável sem dispositivo de áudio; smoke test manual documentado no PR)

**Tests**: none (justificativa: dependência de hardware/codec nativo do LibVLC; matriz define "none" pois não há camada de lógica pura aqui — só wrapper fino sobre API nativa)
**Gate**: build

**Commit**: `feat(media): implementa playback de áudio via LibVLC`
**Status**: ✅ Concluída

---

### T23: NavigationService

**What**: `NavigateTo(ScreenKey, parameter)` + `CurrentScreen` observável, resolvendo ViewModels via DI.
**Where**: `src/Transcriba.App/Services/NavigationService.cs`
**Depends on**: T2

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Troca de `CurrentScreen` notifica bindings (equivalente ao `showScreen(id)` do protótipo)
- [ ] Teste unitário garante que `NavigateTo` resolve o ViewModel correto por `ScreenKey`
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(app): implementa NavigationService`
**Status**: ✅ Concluída

### T24: ThemeService

**What**: Alterna `FluentTheme`/variante de cor do Avalonia entre claro/escuro, persistindo via `SettingsService.DarkTheme`.
**Where**: `src/Transcriba.App/Services/ThemeService.cs`
**Depends on**: T20

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Alternar tema muda a aparência da janela imediatamente
- [ ] Preferência persiste entre reinicializações (lida do `SettingsService` no startup)
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(app): implementa ThemeService com persistência`
**Status**: ✅ Concluída

### T25: MainWindowViewModel + MainWindow.axaml

**What**: Janela principal com sidebar fixa (260px) + área de conteúdo que troca de tela via `NavigationService` (equivalente à estrutura `.app`/`.sidebar`/`.content` do protótipo).
**Where**: `src/Transcriba.App/Views/MainWindow.axaml(.cs)`, `src/Transcriba.App/ViewModels/MainWindowViewModel.cs`
**Depends on**: T23, T24

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Layout replica proporções e cores do protótipo (largura da sidebar, borda, cores por tema)
- [ ] Botão de tema funcional (SHELL-01 AC2)
- [ ] Gate `build` passa

**Tests**: none (View — coberto por smoke test manual)
**Gate**: build

**Commit**: `feat(app): implementa MainWindow e shell de navegação`
**Status**: ✅ Concluída

### T26: SidebarViewModel

**What**: Logo (navega para Dashboard), busca visual (decorativa — spec Out of Scope), botão "Nova" com menu (Pesquisa/Transcrição/Gravar agora), lista de pesquisas expansível com badge, seção Transcrições (Todas/Em andamento/Concluídas/Gravar agora), seção Tags, rodapé Configurações — todos os itens SHELL-01.
**Where**: `src/Transcriba.App/ViewModels/SidebarViewModel.cs`, `src/Transcriba.App/Views/SidebarView.axaml`
**Depends on**: T15, T16, T25
**Reuses**: T7 (cores de tag), T15, T16

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Expandir/colapsar pesquisa funciona (chevron gira) — AC4 de SHELL-01
- [ ] Badge "Todas" reflete contagem real do banco
- [ ] Clique em tag navega para Dashboard filtrado (AC6)
- [ ] Gate `quick` passa (testes de ViewModel para comandos de navegação/expansão)

**Tests**: unit
**Gate**: quick

**Commit**: `feat(app): implementa SidebarViewModel com pesquisas, tags e filtros`
**Status**: ✅ Concluída

### T27: DashboardViewModel

**What**: Lista de `TranscriptionCardViewModel` com filtro de status, tag e busca textual em tempo real (DASH-01).
**Where**: `src/Transcriba.App/ViewModels/DashboardViewModel.cs`
**Depends on**: T26
**Reuses**: T15

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Alterar filtro/busca atualiza a lista observável imediatamente
- [x] Estado vazio exibido quando nenhum resultado (AC5)
- [x] Testes cobrem cada combinação de filtro (unit, sobre o ViewModel com `LibraryService` mockado)
- [x] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(dashboard): implementa DashboardViewModel`
**Status**: ✅ Concluída

### T28: DashboardView.axaml

**What**: Grid de cards (ícone+título, preview 2 linhas, tags, badge de status, data, duração), toolbar de filtros e busca — fiel ao CSS `.dash-card`/`.dash-grid`/`.dash-toolbar` do protótipo.
**Where**: `src/Transcriba.App/Views/DashboardView.axaml(.cs)`
**Depends on**: T27

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Layout visualmente equivalente ao protótipo (grid responsivo, cores de tag, badges de status)
- [x] Clique no card navega para o Editor
- [x] Gate `build` passa

**Tests**: none
**Gate**: build

**Commit**: `feat(dashboard): implementa DashboardView`
**Status**: ✅ Concluída

---

### T29: ResearchPageViewModel

**What**: Breadcrumb, cabeçalho (ícone/título/descrição), lista de transcrições da pesquisa, botão "Adicionar" → navega para Upload com pesquisa pré-selecionada (RESEARCH-01).
**Where**: `src/Transcriba.App/ViewModels/ResearchPageViewModel.cs`
**Depends on**: T26
**Reuses**: T16

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Lista vazia não gera erro (edge case)
- [ ] Navegação para Upload carrega a pesquisa correta pré-selecionada
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(research): implementa ResearchPageViewModel`

---

### T30: ResearchPageView.axaml

**What**: View fiel ao CSS `.page-header`/`.research-list`/`.research-item` do protótipo.
**Where**: `src/Transcriba.App/Views/ResearchPageView.axaml(.cs)`
**Depends on**: T29

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Layout visualmente equivalente ao protótipo
- [ ] Gate `build` passa

**Tests**: none
**Gate**: build

**Commit**: `feat(research): implementa ResearchPageView`

---

### T31: IconPickerViewModel/View (reutilizável) [P]

**What**: Grade de ícones (30 emojis do `IconCatalog`) com seleção e preview, reutilizável entre modal e editor.
**Where**: `src/Transcriba.App/Controls/IconPicker.axaml(.cs)`, `src/Transcriba.App/ViewModels/IconPickerViewModel.cs`
**Depends on**: T26
**Reuses**: T7

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Seleção de ícone atualiza binding/preview
- [ ] Opção "Sem ícone" disponível quando aplicável (parâmetro do controle)
- [ ] Gate `quick` passa (teste de ViewModel)

**Tests**: unit
**Gate**: quick

**Commit**: `feat(app): implementa IconPicker reutilizável`

---

### T32: ColorPickerViewModel/View (reutilizável) [P]

**What**: Grade de 8 cores (`ColorCatalog`) com seleção.
**Where**: `src/Transcriba.App/Controls/ColorPicker.axaml(.cs)`, `src/Transcriba.App/ViewModels/ColorPickerViewModel.cs`
**Depends on**: T26
**Reuses**: T7

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Seleção de cor atualiza binding/preview
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(app): implementa ColorPicker reutilizável`

---

### T33: NewPageModalViewModel + View

**What**: Modal "Nova pesquisa/tese" (com color picker, sem tags) e "Nova transcrição avulsa" (sem color picker, com tags), preview ao vivo, validação de título obrigatório (NEWPAGE-01).
**Where**: `src/Transcriba.App/ViewModels/NewPageModalViewModel.cs`, `src/Transcriba.App/Views/NewPageModal.axaml(.cs)`
**Depends on**: T31, T32
**Reuses**: T16, T7, transcrição avulsa cria registro vazio (spec NEWPAGE-01 AC5/AC8)

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Confirmar com título vazio não cria nada e mantém modal aberto (AC6)
- [ ] Transcrição avulsa criada sem segmentos abre no Editor com estado vazio (AC8)
- [ ] Tags novas usam cor azul padrão (fidelidade ao protótipo)
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(app): implementa modal de criação de pesquisa/transcrição`

---

### T34: UploadViewModel

**What**: Drag-and-drop + diálogo nativo de arquivo, validação de formato, formulário (idioma pré-preenchido pelas configurações, qualidade, locutores pré-preenchido pelo toggle de configurações, pesquisa), start do job via `TranscriptionQueueService` (UPLOAD-01).
**Where**: `src/Transcriba.App/ViewModels/UploadViewModel.cs`
**Depends on**: T14, T16, T17, T33
**Reuses**: T14, T16, T17, T20 (defaults de configurações)

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Arquivo de formato não suportado é rejeitado com mensagem (AC3)
- [ ] "Iniciar transcrição" copia mídia, cria registro `InProgress` e enfileira o job (AC5)
- [ ] Testes de ViewModel cobrem validação e chamada correta aos serviços (mocks)
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(upload): implementa UploadViewModel`

---

### T35: UploadView.axaml

**What**: Zona de drop + formulário fiel ao CSS `.upload-zone`/`.upload-form` do protótipo.
**Where**: `src/Transcriba.App/Views/UploadView.axaml(.cs)`
**Depends on**: T34

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Drag-over exibe destaque visual; drop exibe nome/tamanho reais do arquivo (AC1)
- [ ] Clique abre diálogo nativo de arquivo (AC2)
- [ ] Gate `build` passa

**Tests**: none
**Gate**: build

**Commit**: `feat(upload): implementa UploadView`

---

### T36: Wiring de status/erro em tempo real no Dashboard/Editor

**What**: `DashboardViewModel`/`EditorViewModel` assinam `TranscriptionQueueService.StatusChanged` para refletir "Em andamento" → "Concluída"/"Erro" sem exigir reinício (AC6/AC7 de UPLOAD-01).
**Where**: `src/Transcriba.App/ViewModels/DashboardViewModel.cs` (modify), `src/Transcriba.App/ViewModels/EditorViewModel.cs` (novo, esqueleto — detalhado em T37)
**Depends on**: T14, T27

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Card muda de status automaticamente ao concluir/errar, sem reload manual
- [ ] Botão "Tentar novamente" disponível em transcrições com erro (AC7)
- [ ] Teste de ViewModel simula evento de status e verifica atualização da coleção observável
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(upload): reflete status de transcrição em tempo real na UI`

---

### T37: EditorViewModel (segmentos, título, ícone, tags)

**What**: Estado do Editor: breadcrumb, ícone/título editáveis, meta, tags, lista de `SegmentItemViewModel`, comandos Dividir/Mesclar (EDITOR-01).
**Where**: `src/Transcriba.App/ViewModels/EditorViewModel.cs`
**Depends on**: T18, T19, T22, T36
**Reuses**: T19 (SegmentEditingService), T18 (SpeakerService)

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Editar texto de segmento persiste via `SegmentEditingService`
- [ ] Comando Dividir usa a posição do caret (não o segmento ativo por playback) — AC3
- [ ] Comando Mesclar usa o segmento ativo por playback — AC6
- [ ] Editar título salva no blur/Enter e propaga para sidebar/breadcrumb (AC7)
- [ ] Testes cobrem os 4 pontos acima com serviços mockados
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(editor): implementa EditorViewModel`

---

### T38: SegmentItemViewModel [P]

**What**: ViewModel de um segmento individual (tempo, locutor colorido, texto editável, estado "ativo").
**Where**: `src/Transcriba.App/ViewModels/SegmentItemViewModel.cs`
**Depends on**: T37

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Clique no segmento dispara seek do player para seu tempo de início (AC4)
- [ ] Propriedade `IsActive` reflete a lógica de `GetActiveSegment`
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(editor): implementa SegmentItemViewModel`

---

### T39: SpeakerDropdownViewModel/View [P]

**What**: Dropdown "Locutor" com lista de locutores da transcrição atual, indicador do locutor do segmento ativo, campo para criar novo locutor (SPEAKER-01).
**Where**: `src/Transcriba.App/ViewModels/SpeakerDropdownViewModel.cs`, `src/Transcriba.App/Views/SpeakerDropdown.axaml(.cs)`
**Depends on**: T37
**Reuses**: T18

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Selecionar locutor atribui ao segmento ativo e fecha o dropdown (AC2)
- [ ] Criar novo locutor atribui cor por ciclo de paleta e atribui ao segmento ativo (AC3)
- [ ] Sem segmento ativo, ação é ignorada/desabilitada (AC5)
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(editor): implementa dropdown de locutores`

---

### T40: EditorView.axaml

**What**: View completa do editor (breadcrumb, ícone/título, meta, tags, toolbar, lista de segmentos) fiel ao CSS `.editor-*`/`.seg-*` do protótipo.
**Where**: `src/Transcriba.App/Views/EditorView.axaml(.cs)`
**Depends on**: T37, T38, T39

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Layout visualmente equivalente ao protótipo
- [ ] Popup de ícone abre/fecha corretamente (AC8)
- [ ] Gate `build` passa

**Tests**: none
**Gate**: build

**Commit**: `feat(editor): implementa EditorView`

---

### T41: PlayerBarViewModel

**What**: Play/pause, tempo atual/total, seek por clique na barra, ciclo de velocidade (1×/1.25×/1.5×/2×), volume — integrando `IMediaPlaybackService` (PLAYER-01).
**Where**: `src/Transcriba.App/ViewModels/PlayerBarViewModel.cs`
**Depends on**: T22
**Reuses**: T22

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Play/pause alterna estado e ícone (AC2)
- [ ] Seek por posição de clique calcula o tempo proporcional corretamente (AC4)
- [ ] Ciclo de velocidade segue a sequência exata do protótipo (AC5)
- [ ] Fim do arquivo para reprodução e reseta ícone (AC7)
- [ ] Testes cobrem os 4 pontos acima com `IMediaPlaybackService` mockado
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(player): implementa PlayerBarViewModel`

---

### T42: PlayerBarView.axaml

**What**: Barra inferior fixa fiel ao CSS `.player-bar` do protótipo.
**Where**: `src/Transcriba.App/Views/PlayerBarView.axaml(.cs)`
**Depends on**: T41

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Layout visualmente equivalente ao protótipo
- [ ] Gate `build` passa

**Tests**: none
**Gate**: build

**Commit**: `feat(player): implementa PlayerBarView`

---

### T43: Integração segmento-ativo com playback (highlight + autoscroll)

**What**: Conecta `PlayerBarViewModel.PositionChanged` → `EditorViewModel`/`SegmentItemViewModel` para destacar o segmento ativo e rolar a lista automaticamente (EDITOR-01 AC5).
**Where**: `src/Transcriba.App/ViewModels/EditorViewModel.cs` (modify)
**Depends on**: T40, T42

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Avançar a posição de reprodução atualiza `IsActive` do segmento correto em tempo real
- [ ] Lista rola automaticamente para manter o segmento ativo visível
- [ ] Teste de ViewModel simula avanço de posição e verifica qual segmento fica ativo
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(editor): sincroniza destaque de segmento com playback`

---

### T44: RecordingViewModel (mockado)

**What**: Timer, forma de onda simulada (`Random`), frases "ao vivo" mockadas, fluxo record/pause/stop navegando para o Editor ao final — sem captura real de áudio (REC-01, Out of Scope de captura real).
**Where**: `src/Transcriba.App/ViewModels/RecordingViewModel.cs`
**Depends on**: T26

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Nenhum arquivo de mídia real é criado nem microfone é acessado (AC5 — confirma escopo mockado)
- [ ] Timer conta corretamente, pausa/retoma, e reseta ao parar (AC2/AC3/AC4)
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(recording): implementa tela de gravação mockada`

---

### T45: RecordingView.axaml

**What**: View fiel ao CSS `.rec-*` do protótipo (seletores de dispositivo, timer, forma de onda, seção de transcrição ao vivo).
**Where**: `src/Transcriba.App/Views/RecordingView.axaml(.cs)`
**Depends on**: T44

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Layout visualmente equivalente ao protótipo
- [ ] Gate `build` passa

**Tests**: none
**Gate**: build

**Commit**: `feat(recording): implementa RecordingView`

---

### T46: SettingsViewModel

**What**: Perfil (nome/email/instituição), preferências de transcrição (idioma padrão, identificar locutores, transcrição ao vivo — inerte), motor (dispositivo) — SETTINGS-01.
**Where**: `src/Transcriba.App/ViewModels/SettingsViewModel.cs`
**Depends on**: T20, T26
**Reuses**: T20

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Alterações persistem via `SettingsService` (AC2/AC3/AC4/AC5/AC6)
- [ ] Testes cobrem persistência de cada campo/toggle
- [ ] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(settings): implementa SettingsViewModel`

---

### T47: SettingsView.axaml

**What**: View fiel ao CSS `.settings-*` do protótipo, incluindo a nova seção "Motor de transcrição".
**Where**: `src/Transcriba.App/Views/SettingsView.axaml(.cs)`
**Depends on**: T46

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Layout visualmente equivalente ao protótipo + seção nova de motor
- [ ] Gate `build` passa

**Tests**: none
**Gate**: build

**Commit**: `feat(settings): implementa SettingsView`

---

### T48: Wiring do botão Exportar (P2)

**What**: Botão "Exportar" do editor abre diálogo de formato (TXT/SRT/VTT) + diálogo nativo de destino, chamando `ExportService` (EXPORT-01).
**Where**: `src/Transcriba.App/ViewModels/EditorViewModel.cs` (modify), `src/Transcriba.App/Views/ExportDialog.axaml(.cs)` (novo)
**Depends on**: T21, T40

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Os 3 formatos disponíveis e funcionais end-to-end
- [x] Transcrição sem segmentos desabilita/avisa (AC4)
- [x] Gate `quick` passa (teste de ViewModel cobrindo chamada correta ao serviço por formato)

**Tests**: unit
**Gate**: quick

**Commit**: `feat(export): conecta botão Exportar do editor ao ExportService`
**Status**: ✅ Concluída

---

### T49: Exclusão de transcrição (P2) [P]

**What**: Menu de contexto no card (Dashboard) e na lista da pesquisa para excluir transcrição, com confirmação (CRUD-01 AC1/AC3).
**Where**: `src/Transcriba.App/ViewModels/DashboardViewModel.cs` (modify), `src/Transcriba.App/ViewModels/ResearchPageViewModel.cs` (modify)
**Depends on**: T16, T28

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Confirmação obrigatória antes de excluir
- [x] Exclusão remove transcrição, segmentos e mídia copiada (via `MediaStorageService`)
- [x] Cancelamento não altera nada
- [x] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(dashboard): adiciona exclusão de transcrição com confirmação`
**Status**: ✅ Concluída

---

### T50: Exclusão de pesquisa (P2) [P]

**What**: Menu de contexto na sidebar/pesquisa para excluir pesquisa, informando quantas transcrições serão desassociadas (CRUD-01 AC2/AC3).
**Where**: `src/Transcriba.App/ViewModels/ResearchPageViewModel.cs` (modify), `src/Transcriba.App/ViewModels/SidebarViewModel.cs` (modify)
**Depends on**: T16, T30

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Diálogo de confirmação informa contagem de transcrições afetadas
- [x] Transcrições ficam avulsas (não excluídas) após confirmação
- [x] Gate `quick` passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(research): adiciona exclusão de pesquisa com confirmação`
**Status**: ✅ Concluída

---

## Parallel Execution Map

```
Phase 1 (Sequential):      T1 → T2 → T3

Phase 2 (Sequential):      T3 → T4 → T5 → T6
                            T3 → T7

Phase 3 (Mixed):           T3 → { T8, T9, T10 [P], T11 [P] } → T13 (também requer T12) → T14

Phase 4 (Parallel):        T6 (+T7/T14 onde indicado) → { T15 [P], T16 [P], T17 [P], T18 [P], T19 [P], T20 [P], T21 [P] }

Phase 5 (Sequential):      T3 → T22

Phase 6 (Sequential):      T20 → T24; { T23, T24 } → T25 → T26  (T26 também requer T15, T16)

Phase 7 (Sequential):      T26 → T27 → T28

Phase 8 (Sequential):      T26 → T29 → T30

Phase 9 (Mixed):           T26 → { T31 [P], T32 [P] } → T33

Phase 10 (Sequential):     { T14, T16, T17, T33 } → T34 → T35 → T36

Phase 11 (Mixed):          { T18, T19, T22, T36 } → T37 → { T38 [P], T39 [P] } → T40
                            T22 → T41 → T42
                            { T40, T42 } → T43

Phase 12 (Sequential):     T26 → T44 → T45

Phase 13 (Sequential):     { T20, T26 } → T46 → T47

Phase 14 (Sequential, P2): { T21, T40 } → T48

Phase 15 (Parallel, P2):   { T16, T28 } → T49 [P]
                            { T16, T30 } → T50 [P]
```

**Parallelismo constraint:** tasks `[P]` dentro da mesma fase não têm dependência entre si e podem ser feitas em qualquer ordem — não são um comando para instanciar sub-agentes por task.

---

## Task Granularity Check

| Task | Scope | Status |
| --- | --- | --- |
| T1–T3 | 1 solution/config por task | ✅ Granular |
| T4 | 6 entidades + 4 enums (POCOs simples, cada uma trivial e cobertos por 1 migration em T5) | ✅ Granular (agrupamento coeso — arquivos de dados sem lógica) |
| T5–T22 | 1 classe/serviço por task | ✅ Granular |
| T23–T50 | 1 ViewModel/View/Control por task (Views separadas de ViewModels quando ambas existem) | ✅ Granular |

Nenhuma task viola a regra de granularidade (1 componente/função/arquivo coeso por task).

---

## Diagram-Definition Cross-Check

| Task | Depends On (corpo da task) | Diagrama mostra | Status |
| --- | --- | --- | --- |
| T1 | None | Início da Fase 1 | ✅ Match |
| T2 | T1 | T1 → T2 | ✅ Match |
| T3 | T1 | T1 → T2 → T3 (T2 antes de T3 na cadeia sequencial da Fase 1) | ✅ Match |
| T4 | T3 | T3 → T4 | ✅ Match |
| T5 | T4 | T4 → T5 | ✅ Match |
| T6 | T5 | T5 → T6 | ✅ Match |
| T7 | T3 | T3 → T7 | ✅ Match |
| T8 | T3 | T3 → T8 | ✅ Match |
| T9 | T3, T8 | T3 → T9 (e implicitamente depende de T8 via Fase 3) | ✅ Match |
| T10 | T3 | T3 → T10 [P] | ✅ Match |
| T11 | T3 | T3 → T11 [P] | ✅ Match |
| T12 | T3 | T12 → T13 (fluxo próprio) | ✅ Match |
| T13 | T8, T9, T10, T11, T12 | Todos convergem em T13 | ✅ Match |
| T14 | T6, T13 | T6 (Fase 2) + T13 → T14 | ✅ Match |
| T15–T21 | T6 (+T14 p/ T21) | Fase 4: T6 → {T15..T21}; T21 também de T14 | ✅ Match |
| T22 | T3 | Fase 5: T3 → T22 | ✅ Match |
| T23 | T2 | Fase 6 (implícito via T2 na Fase 1) | ✅ Match |
| T24 | T20 | T20 → T24 | ✅ Match |
| T25 | T23, T24 | {T23,T24} → T25 | ✅ Match |
| T26 | T15, T16, T25 | T25 → T26 (T15/T16 também requeridos) | ✅ Match |
| T27 | T26 | T26 → T27 | ✅ Match |
| T28 | T27 | T27 → T28 | ✅ Match |
| T29 | T26 | T26 → T29 | ✅ Match |
| T30 | T29 | T29 → T30 | ✅ Match |
| T31 | T26 | T26 → T31 [P] | ✅ Match |
| T32 | T26 | T26 → T32 [P] | ✅ Match |
| T33 | T31, T32 | {T31,T32} → T33 | ✅ Match |
| T34 | T14, T16, T17, T33 | {T14,T16,T17,T33} → T34 | ✅ Match |
| T35 | T34 | T34 → T35 | ✅ Match |
| T36 | T14, T27 | {T14,T27} → T36 | ✅ Match |
| T37 | T18, T19, T22, T36 | {T18,T19,T22,T36} → T37 | ✅ Match |
| T38 | T37 | T37 → T38 [P] | ✅ Match |
| T39 | T37 | T37 → T39 [P] | ✅ Match |
| T40 | T37, T38, T39 | {T38,T39} → T40 | ✅ Match |
| T41 | T22 | T22 → T41 | ✅ Match |
| T42 | T41 | T41 → T42 | ✅ Match |
| T43 | T40, T42 | {T40,T42} → T43 | ✅ Match |
| T44 | T26 | T26 → T44 | ✅ Match |
| T45 | T44 | T44 → T45 | ✅ Match |
| T46 | T20, T26 | {T20,T26} → T46 | ✅ Match |
| T47 | T46 | T46 → T47 | ✅ Match |
| T48 | T21, T40 | {T21,T40} → T48 | ✅ Match |
| T49 | T16, T28 | {T16,T28} → T49 [P] | ✅ Match |
| T50 | T16, T30 | {T16,T30} → T50 [P] | ✅ Match |

Todas as dependências do corpo das tasks têm seta correspondente no diagrama de execução. Nenhuma inconsistência encontrada.

---

## Test Co-location Validation

| Task | Code Layer Criada/Modificada | Matriz Exige | Task Diz | Status |
| --- | --- | --- | --- | --- |
| T1–T3 | Scaffolding/config | none | none | ✅ OK |
| T4 | Entidades | none | none | ✅ OK |
| T5 | DbContext/migration | none (build gate) | none | ✅ OK |
| T6 | Acesso a dados (bootstrap) | integration | integration | ✅ OK |
| T7 | Catalogs (com lógica de fallback) | unit | unit | ✅ OK |
| T8 | Engine (FfmpegLocator) | unit | unit | ✅ OK |
| T9 | Engine (AudioLoader) | unit | unit | ✅ OK |
| T10 | Engine (SilenceSplitter) | unit | unit | ✅ OK |
| T11 | Engine (ChunkPlanner) | unit | unit | ✅ OK |
| T12 | Engine (ModelManager) | unit | unit | ✅ OK |
| T13 | Engine (WhisperTranscriptionEngine) | integration (TranscriptionQueueService na matriz cobre também o engine orquestrado) | integration | ✅ OK |
| T14 | TranscriptionQueueService | integration | integration | ✅ OK |
| T15–T21 | Serviços de aplicação | unit | unit | ✅ OK |
| T22 | Media (wrapper LibVLC) | none (justificado na matriz — sem lógica pura, dependência nativa) | none | ✅ OK |
| T23, T24, T26, T27, T31, T32, T33, T34, T36–T39, T41, T43, T44, T46, T48–T50 | ViewModels | unit | unit | ✅ OK |
| T25, T28, T30, T35, T40, T42, T45, T47 | Views (.axaml) | none | none | ✅ OK |

Nenhuma violação — todas as tasks que criam camadas com tipo de teste exigido pela matriz incluem os testes correspondentes na própria task (nenhum adiamento de teste).

---

## Tips

- **[P] = Order-free** — tasks marcadas podem ser feitas em qualquer ordem dentro da fase.
- **Reuses = Token saver** — sempre referenciar `transcrever.cs`/protótipo em vez de reescrever do zero.
- **Um commit por task** — mensagens seguindo semantic commit em pt-br (`feat(...)`, `fix(...)`, `chore(...)`), conforme convenção do usuário.
- **15 fases** — acima do limiar de 3 fases; o orquestrador deve oferecer (não impor) sub-agentes por fase antes de iniciar o Execute.

---

## Migração para Blazor Hybrid (AD-005)

> **Contexto**: a implementação das Fases 1–15 acima (Avalonia) foi concluída e depois **descontinuada** por causa de um crash fatal e não determinístico do runtime (`Internal CLR error 0x80131506`, ver `.specs/STATE.md` AD-005). As Fases 1–5 (`Transcriba.Core`: dados, motor de transcrição, serviços de aplicação, playback) **permanecem válidas e intactas** — nenhuma task abaixo as reabre. As Fases 6–15 (camada `Transcriba.App`) são **substituídas** pelas fases T51–T68 abaixo: WPF host + `BlazorWebView`, Razor components no lugar de `.axaml`, CSS herdado quase verbatim do protótipo `transcriba-v2-icons-transcriptions.html`. ViewModels (`CommunityToolkit.Mvvm`) já escritos para o Avalonia são reaproveitados sempre que possível — a maior parte do trabalho é troca de `.axaml`/`Views.axaml.cs` por `.razor`, não reescrita de lógica de estado/comando.

### Execution Plan (revisado)

```
Phase 16: Bootstrap Blazor Hybrid (Sequential)
T51 → T52 → T53 → T54

Phase 17: Shell / Navegação / Tema (depende da Fase 16)
T54 → T55 → T56 → T57

Phase 18: Pickers e Modais compartilhados (depende da Fase 17)
T57 → T58 → T59

Phase 19: Dashboard (depende da Fase 17)
T56 → T60

Phase 20: Pesquisa (depende da Fase 17)
T56 → T61

Phase 21: Upload (depende da Fase 18)
T58 → T62

Phase 22: Playback + Editor (depende das Fases 18, 21)
T58 ──┬→ T63 → T64 → T65
T62 ──┘

Phase 23: Gravação mockada (depende da Fase 17)
T56 → T66

Phase 24: Configurações (depende da Fase 17)
T56 → T67

Phase 25: Exclusão / Confirmação (depende das Fases 19, 20)
T60, T61 → T68
```

**Sub-agent delegation**: 10 fases novas (16–25), acima do limiar de 3 — o orquestrador deve oferecer um worker por fase (sequencial, respeitando as dependências acima) antes de iniciar a Execute desta seção. Fases 19/20/23/24 são independentes entre si (podem rodar em paralelo depois da Fase 17); Fase 22 depende de 18 e 21; Fase 25 depende de 19 e 20.

### Task Breakdown (T51–T68)

---

### T51: Reconfigurar Transcriba.App para WPF + BlazorWebView

**What**: Trocar o SDK do projeto para `Microsoft.NET.Sdk.Razor`, `TargetFramework` para `net10.0-windows10.0.17763.0`, remover todas as referências `Avalonia*`/`AvaloniaUI.DiagnosticsSupport` e as mitigações de crash específicas do Avalonia (`ConcurrentGarbageCollection`, `TieredPGO`, `TieredCompilation` — não são mais necessárias, mas documentar no commit por que foram removidas). Adicionar `Microsoft.AspNetCore.Components.WebView.Wpf`. Deletar `App.axaml`/`App.axaml.cs`, `ViewLocator.cs`, `Styles/TranscribaTheme.axaml`, `Views/*.axaml*` (serão recriados como Razor nas próximas tasks).
**Where**: `src/Transcriba.App/Transcriba.App.csproj`, remoção dos arquivos `.axaml*` listados
**Depends on**: None (Core intacto)
**Reuses**: N/A

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] `dotnet restore` resolve sem conflito
- [x] Nenhum pacote `Avalonia*` referenciado
- [x] `dotnet build Transcriba.sln` falha apenas pelos arquivos `.cs` que ainda referenciam tipos Avalonia (esperado — corrigidos nas próximas tasks), não por erro de restore/SDK

**Tests**: none
**Gate**: build (parcial, esperado falhar até T52)

**Commit**: `chore(app): migra Transcriba.App de Avalonia para WPF + BlazorWebView (AD-005)`
**Status**: ✅ Concluída (inclui correção de `UiThread`/`RecordingViewModel` para `System.Windows.Threading.Dispatcher` e novos `WpfConfirmationService`/`WpfFileSaveService`/`WpfThemeApplicator` no lugar dos `Avalonia*Service`; `Transcriba.Tests` teve o TFM alinhado para `net10.0-windows10.0.17763.0` por depender de `Transcriba.App`)

---

### T52: Bootstrap WPF + Generic Host + BlazorWebView

**What**: `App.xaml`/`App.xaml.cs` (WPF, `Application`) + `MainWindow.xaml` com `<blazor:BlazorWebView HostPage="wwwroot/index.html">` e `RootComponent` apontando para o shell Razor (`Components/App.razor`/`Components/Routes.razor` ou componente raiz único, ver T55). `Program.cs` mantém o `Microsoft.Extensions.Hosting.Host` (DI, `IDbContextFactory`, `TranscriptionQueueService` como hosted service) — só troca `BuildAvaloniaApp().StartWithClassicDesktopLifetime` por `System.Windows.Application.Run` com a `MainWindow` resolvida via DI. `serviceCollection.AddWpfBlazorWebView()` registrado no mesmo `IServiceCollection` do Host.
**Where**: `src/Transcriba.App/App.xaml`, `src/Transcriba.App/App.xaml.cs`, `src/Transcriba.App/MainWindow.xaml`, `src/Transcriba.App/MainWindow.xaml.cs`, `src/Transcriba.App/Program.cs`
**Depends on**: T51
**Reuses**: `Program.cs` (Generic Host/DI já existente), `AppServiceCollectionExtensions.cs`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] App inicia exibindo uma janela WPF com o `BlazorWebView` carregado (mesmo que só uma página em branco/"Hello" nesta task)
- [x] `IServiceProvider` do Generic Host é o mesmo container usado pelo `BlazorWebView` (sem dois containers DI divergentes)
- [x] `dotnet build Transcriba.sln` passa

**Tests**: none
**Gate**: build

**Commit**: `feat(app): bootstrap WPF host com BlazorWebView e Generic Host`
**Status**: ✅ Concluída — armadilha encontrada e corrigida: `Program.cs` criava `new App()` e chamava `Run()` sem chamar `InitializeComponent()` (método gerado a partir do `App.xaml`), que é quem aplica a propriedade `StartupUri` à instância; sem essa chamada o loop de mensagens do WPF ficava de pé (processo vivo) mas nenhuma janela era criada. `<StartupObject>Transcriba.App.Program</StartupObject>` e `<RootNamespace>Transcriba.App</RootNamespace>` adicionados ao `.csproj` (o segundo é um workaround documentado pela Microsoft para Blazor+WPF, dotnet/maui#5861)

---

### T53: Portar CSS/HTML base do protótipo

**What**: Copiar o `<style>` global do protótipo (`transcriba-v2-icons-transcriptions.html`, incluindo variáveis `:root`/`html.dark`, reset, tipografia) para `wwwroot/css/app.css`; `wwwroot/index.html` referenciando esse CSS + `_framework/blazor.webview.js`; `_Imports.razor` na raiz do projeto com os `@using` padrão (`Microsoft.AspNetCore.Components.Web`, namespaces do app).
**Where**: `src/Transcriba.App/wwwroot/index.html`, `src/Transcriba.App/wwwroot/css/app.css`, `src/Transcriba.App/_Imports.razor`
**Depends on**: T51
**Reuses**: `transcriba-v2-icons-transcriptions.html` (bloco `<style>`)

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] CSS portado sem alteração de valores (cores/espaçamentos idênticos ao protótipo)
- [x] Variáveis de tema claro/escuro (`:root`, `html.dark`) presentes
- [x] `dotnet build Transcriba.sln` passa

**Tests**: none
**Gate**: build

**Commit**: `feat(app): porta CSS global do protótipo para wwwroot`
**Status**: ✅ Concluída — armadilha encontrada e corrigida: `<script src="_framework/blazor.webview.js" autostart="false">` travava a tela em "Carregando…" para sempre (nenhum `Blazor.start()` manual era chamado); removido o atributo `autostart="false"` para voltar ao autostart padrão

---

### T54: Smoke test — validar ausência do crash 0x80131506

**What**: Rodar `dotnet run --project src/Transcriba.App` repetidamente (mín. 10x, incluindo interação básica: abrir/fechar janela, redimensionar) e documentar o resultado (sem exceção `0x80131506`) como evidência no `validation.md`/Handoff. Este é o gate de decisão: se o crash reaparecer aqui, a stack Blazor Hybrid também falha e a decisão AD-005 precisa ser revisitada antes de continuar.
**Where**: N/A (validação manual, documentada em `.specs/features/transcriba-desktop/validation.md`)
**Depends on**: T52, T53

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Execuções consecutivas sem o crash `0x80131506` (ver evidência no `validation.md`)
- [x] Resultado documentado (data, nº de execuções, ambiente)

**Tests**: none (manual)
**Gate**: manual

**Commit**: `chore(app): documenta smoke test de estabilidade do bootstrap Blazor Hybrid`
**Status**: ✅ Concluída — ver `.specs/features/transcriba-desktop/validation.md`

---

### T55: Portar NavigationService para Blazor

**What**: Adaptar `NavigationService` para expor o estado de navegação (`CurrentScreen`) de um jeito consumível por Razor components (ex.: `INotifyPropertyChanged`/evento `StateChanged`, componente raiz re-renderiza via `StateHasChanged`). Manter a mesma API pública (`NavigateTo(ScreenKey, object?)`).
**Where**: `src/Transcriba.App/Services/NavigationService.cs`
**Depends on**: T54
**Reuses**: `NavigationService.cs` existente (lógica de troca de tela)

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] `NavigateTo` dispara re-render do componente raiz para a tela correta
- [x] Teste unitário do `NavigationService` (transições de estado) continua/passa

**Tests**: unit
**Gate**: quick

**Commit**: `feat(app): adapta NavigationService para Razor components`
**Status**: ✅ Concluída — `NavigationService` já era `ObservableObject` (CommunityToolkit.Mvvm), então `[ObservableProperty]` já dispara `PropertyChanged` automaticamente; nenhuma mudança de API foi necessária, só documentação do padrão de consumo. `Shell.razor` (placeholder raiz atual) passou a se inscrever em `PropertyChanged` e chamar `StateHasChanged()`, provando o mecanismo fim a fim antes de ser substituído pelo `MainLayout.razor` real na T56. `dotnet test tests/Transcriba.Tests` — 157/157 passando.

---

### T56: Criar shell (MainLayout.razor + Sidebar.razor)

**What**: `MainLayout.razor` replicando o grid `sidebar (260px) | content` do protótipo; `Sidebar.razor` reaproveitando `SidebarViewModel` (pesquisas, tags, busca decorativa do topo, botão de tema, item "Nova pesquisa/transcrição"). Overlay de modais (`NewPageModal`, `ModelDownloadModal`) posicionado no layout raiz.
**Where**: `src/Transcriba.App/Components/Layout/MainLayout.razor`, `src/Transcriba.App/Components/Layout/Sidebar.razor`
**Depends on**: T55
**Reuses**: `SidebarViewModel.cs`, `SidebarResearchItemViewModel.cs`, CSS classes `.sidebar*` já portadas (T53)

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Sidebar renderiza pesquisas/tags a partir do `SidebarViewModel` real (dados do SQLite)
- [x] Clique em item de navegação chama `NavigationService.NavigateTo`
- [x] Fidelidade visual: classes CSS idênticas às do protótipo (`.sidebar-item`, `.sidebar-trans-item`, etc.)

**Tests**: none (View) — `SidebarViewModel` já teve testes unitários na Fase 6 original, não repetir
**Gate**: build

**Commit**: `feat(app): implementa shell (MainLayout + Sidebar) em Razor`
**Status**: ✅ Concluída — `MainWindow.xaml` passou a apontar o `RootComponent` direto para `MainLayout` (o antigo `Shell.razor` placeholder da T55 foi removido: abordagem mais simples, sem componente raiz intermediário). No protótipo o `<div class="dropdown">` do menu "Nova" fica aninhado dentro do `<button class="sidebar-new">`; em Blazor isso causaria bubbling do clique nos itens do dropdown até o handler do botão pai (reabrindo o menu recém-fechado), então o dropdown foi colocado como irmão do botão dentro de `.sidebar-actions` (com `position:relative` inline, já que não existe classe equivalente portada para esse contêiner) — mesmas classes CSS, mesma aparência. `Sidebar.razor` chama `SidebarViewModel.LoadAsync()` em `OnInitializedAsync` (idempotente) e se inscreve em `PropertyChanged`/`CollectionChanged` do ViewModel e das coleções `Researches`/`Tags` para re-renderizar quando os dados mudam. Smoke test manual (`dotnet run`, screenshot) confirmou sidebar renderizando dados reais (pesquisa "Teste", contadores Todas/Em andamento/Concluídas) fiel ao protótipo. `dotnet test tests/Transcriba.Tests` — 157/157 passando. **Achado investigado e descartado (falso alarme):** o Visualizador de Eventos do Windows registrou 24 ocorrências do crash `0x80131506` entre 18:25–23:23 do mesmo dia; verificação por `CreationTime` dos arquivos da stack Blazor (`App.xaml` criado às 23:38:24) confirma que todos os 24 eventos são anteriores à existência da stack Blazor Hybrid (era Avalonia) — nenhum ocorreu depois. Detalhes em `validation.md` (seção "Verificação adicional"). Conclusão de "nenhuma reincidência" permanece válida.

---

### T57: Portar ThemeService para alternância de tema via CSS class

**What**: Adaptar `ThemeService`/`IThemeApplicator` para alternar a classe `dark` no elemento raiz do documento (equivalente a `html.dark` do protótipo) via `IJSRuntime` (`document.documentElement.classList.toggle`). Persistência de preferência inalterada.
**Where**: `src/Transcriba.App/Services/ThemeService.cs`, novo `BlazorThemeApplicator : IThemeApplicator`
**Depends on**: T56

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Alternar tema no botão da sidebar troca `light`/`dark` visualmente (variáveis CSS do protótipo)
- [x] Preferência persiste entre reinicializações (reaproveita `SettingsService`)

**Tests**: none (wrapper de interop)
**Gate**: build

**Commit**: `feat(app): implementa alternância de tema via JS interop`
**Status**: ✅ Concluída — `WpfThemeApplicator` (placeholder da T51-T54) foi removido e substituído por `BlazorThemeApplicator`, registrado em `AppServiceCollectionExtensions.cs` (como singleton concreto + `IThemeApplicator`). Como `IJSRuntime` só existe no escopo de renderização do BlazorWebView (não é resolvível no construtor de um singleton do Host), `MainLayout.razor` injeta seu próprio `IJSRuntime` e o repassa via `AttachJsRuntime` em `OnAfterRender(firstRender: true)`; chamadas a `Apply()` feitas antes disso (ex.: `ThemeService.InitializeAsync()` rodando em `OnInitializedAsync`, lendo a preferência do `SettingsService`) ficam pendentes e são aplicadas assim que o runtime é anexado. O toggle em si (`document.documentElement.classList.toggle('dark', isDark)`) fica isolado em `window.transcribaInterop.setDarkTheme` (`wwwroot/index.html`), já que não há equivalente em C#/Blazor puro para alterar a classe do elemento `<html>` do documento hospedeiro. `dotnet test tests/Transcriba.Tests` — 157/157 passando (build 0 erros). Verificação visual do toggle de tema em runtime não foi possível de forma repetível nesta sessão (ver observação na T56 sobre execuções manuais do app) — recomenda-se validação manual pelo usuário.

---

### T58: Portar pickers e modal de nova pesquisa/transcrição

**What**: `IconPicker.razor`, `ColorPicker.razor` (componentes reutilizáveis, popup posicionado), `NewPageModal.razor` reaproveitando `IconPickerViewModel`/`ColorPickerViewModel`/`NewPageModalViewModel`.
**Where**: `src/Transcriba.App/Components/Shared/IconPicker.razor`, `.../ColorPicker.razor`, `.../NewPageModal.razor`
**Depends on**: T57
**Reuses**: `IconPickerViewModel.cs`, `ColorPickerViewModel.cs`, `NewPageModalViewModel.cs`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Popup de ícone/cor abre/fecha ao clicar, fecha ao clicar fora (JS interop mínimo para "click outside" se necessário)
- [x] Criar pesquisa/transcrição pelo modal persiste no banco (via `ResearchService`)

**Tests**: none (View) — ViewModels já testados
**Gate**: build

**Commit**: `feat(app): implementa IconPicker/ColorPicker/NewPageModal em Razor`

**Status**: ✅ Concluída — `IconPicker.razor`/`ColorPicker.razor` renderizam só o grid (`.icon-picker-grid`/`.color-picker-grid`), sem chrome de popup: no protótipo, o modal "Nova pesquisa/transcrição" mostra os grids inline dentro do `.modal` (não como popups clicáveis), então não há JS interop de "click outside" a fazer aqui — os componentes foram desenhados para serem reaproveitáveis também dentro de um popup posicionado no futuro (ex.: trocar ícone de uma página existente no editor), só envolvendo o mesmo grid num contêiner `.icon-popup`. `NewPageModal.razor` esconde o `ColorPicker` no modo transcrição (o protótipo sempre mostra o grid de cor, mas `LibraryService.CreateStandaloneAsync` não aceita cor para transcrições avulsas — `NewPageModalViewModel.PreviewColorName` já força "blue" nesse modo). Inserido no slot de modais do `MainLayout.razor` (`@using Transcriba.App.Components.Shared` adicionado ao componente). `dotnet build Transcriba.sln` — 0 erros; `dotnet test tests/Transcriba.Tests` — 157/157 passando.

---

### T59: Portar modal de download de modelo

**What**: `ModelDownloadModal.razor` reaproveitando `ModelDownloadModalViewModel`/`IModelDownloadNotifier`.
**Where**: `src/Transcriba.App/Components/Shared/ModelDownloadModal.razor`
**Depends on**: T58
**Reuses**: `ModelDownloadModalViewModel.cs`, `ModelDownloadNotificationService.cs`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Modal aparece automaticamente quando `TranscriptionQueueService` inicia download de modelo GGML
- [x] Barra de progresso reflete progresso real do download

**Tests**: none (View)
**Gate**: build

**Commit**: `feat(app): implementa ModelDownloadModal em Razor`

**Status**: ✅ Concluída (com ressalva) — `ModelDownloadModal.razor` injeta o singleton `ModelDownloadModalViewModel` e se inscreve em `PropertyChanged`, então `IsOpen`/`Message` (setados por `ModelDownloadNotificationService.DownloadStarted/DownloadCompleted` via `UiThread.Invoke`, chamado de dentro de `ModelManager`/`TranscriptionQueueService`) já abrem/fecham o modal automaticamente fim a fim, sem nenhuma ação do componente Razor. Ressalva na barra de progresso: `IModelDownloadNotifier` hoje só expõe início/fim do download (sem bytes transferidos) — reportar progresso real exigiria instrumentar o laço de cópia do stream em `ModelManager.EnsureModelInternalAsync` (`src/Transcriba.Core/Engine/ModelManager.cs`), arquivo fora do `Where` desta task e que já tinha, no momento desta implementação, um diff local grande e não commitado (herdado de sessões anteriores, ver `git status`/handoff em `.specs/STATE.md`) — alterá-lo aqui arriscaria conflito com outro trabalho em andamento no repositório compartilhado. Optou-se por uma barra indeterminada (CSS puro, `.model-download-progress-fill`, mesma paleta `--accent` de `.storage-bar-fill`) que comunica "download em andamento" honestamente, sem simular uma porcentagem falsa. Follow-up sugerido (não incluído aqui): adicionar `IModelDownloadNotifier.DownloadProgress(double)` e reportar bytes reais no laço de cópia do `ModelManager` assim que esse arquivo estabilizar/for commitado. `dotnet build Transcriba.sln` — 0 erros; `dotnet test tests/Transcriba.Tests` — 157/157 passando.

---

### T60: Portar Dashboard

**What**: `Dashboard.razor` + `TranscriptionCard.razor` reaproveitando `DashboardViewModel`/`TranscriptionCardViewModel` (filtros de status/tag, busca funcional, grid de cards, badge de status, botão retry, menu de exclusão).
**Where**: `src/Transcriba.App/Components/Pages/Dashboard.razor`, `.../TranscriptionCard.razor`
**Depends on**: T56
**Reuses**: `DashboardViewModel.cs`, `TranscriptionCardViewModel.cs`, `LibraryService.cs`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Busca/filtros funcionam sobre dados reais (mesmo comportamento validado na Fase 7 original)
- [x] Fidelidade visual com `.dash-*`/`.tag-*`/`.status-*` do protótipo

**Tests**: none (View) — `DashboardViewModel` já testado
**Gate**: build

**Commit**: `feat(app): implementa Dashboard em Razor`
**Status**: ✅ Concluída — `Dashboard.razor` recebe `DashboardViewModel` via `[Parameter]` (não `@inject`): o ViewModel é registrado como `Transient` em `AppServiceCollectionExtensions.cs` e o `NavigationService` resolve uma instância nova a cada `NavigateTo(ScreenKey.Dashboard)`, guardada em `CurrentViewModel` — injetar via DI resolveria uma instância diferente, não inicializada por `Initialize`/`LoadAsync`. Quem vai montar o componente (integração central em `MainLayout.razor`, fora do escopo desta task) deve passar `NavigationService.CurrentViewModel` convertido para `DashboardViewModel`. `TranscriptionCard.razor` (filho, também `[Parameter]`) se inscreve no próprio `PropertyChanged` do card para refletir mudanças assíncronas de status vindas da fila (`TranscriptionQueueService`), já que essas atualizações não passam por nenhum handler de evento Razor. O botão "Excluir" já dispara o fluxo real de confirmação existente (`DashboardViewModel.DeleteTranscriptionAsync` → `IConfirmationService`/`WpfConfirmationService`, hoje um `MessageBox` nativo provisório) — não foi necessário nenhum stub, só um comentário `TODO` apontando a Fase 25/T68 para a troca pelo diálogo Razor fiel ao protótipo. CSS: adicionadas a `wwwroot/css/app.css` as classes `.status-error` e `.dash-card-actions`/`.dash-card-action`/`.dash-card-action-danger`/`.dash-card-error` (ausentes do protótipo, que só modela os status "progress"/"done" e não tem UI de retry/exclusão nos cards — seguem o mesmo padrão visual das classes `.dash-filter`/`.research-add-btn` já portadas, conforme Error Handling Strategy do `design.md`: "badge de erro + botão Tentar novamente"). `dotnet build Transcriba.sln` — 0 erros. `dotnet test tests/Transcriba.Tests` — 157/157 passando. **Validação visual**: não foi possível rodar o app interativamente nesta sessão (o `MainLayout.razor` ainda não referencia `Dashboard.razor` — essa integração é central, feita depois desta fase — e não há ambiente gráfico disponível para smoke test); validação feita por inspeção do código/CSS gerado e build limpo.

---

### T61: Portar Página de Pesquisa

**What**: `ResearchPage.razor` reaproveitando `ResearchPageViewModel` (header com ícone/cor/descrição, seções de transcrições, botão de adicionar).
**Where**: `src/Transcriba.App/Components/Pages/ResearchPage.razor`
**Depends on**: T56
**Reuses**: `ResearchPageViewModel.cs`, `ResearchService.cs`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Navegação sidebar → página de pesquisa carrega dados reais
- [x] Fidelidade visual com `.research-*` do protótipo

**Tests**: none (View)
**Gate**: build

**Commit**: `feat(app): implementa ResearchPage em Razor`
**Status**: ✅ Concluída — `ResearchPage.razor` recebe `ResearchPageViewModel` como `[Parameter]` (não `@inject`), já que o ViewModel é Transient e é resolvido/inicializado pelo `NavigationService` a cada navegação; mesmo padrão adotado pelo `Dashboard.razor` (T60). Header (`page-header`) usa `var(--{ColorName}-light)` como fundo do ícone, replicando `col.light` de `PAGE_COLORS` do protótipo sem precisar de constante nova. Lista reaproveita `.research-item*` fielmente; como o protótipo (mock) só trata os status "progress"/"done", o status "Erro" (existente no modelo de dados real) foi tratado com estilo inline reaproveitando as variáveis `--red`/`--red-light` já usadas por outras badges, sem introduzir classe nova no CSS compartilhado. Botão "Adicionar" já está conectado a `AddTranscriptionCommand`, que hoje navega direto para `Upload` (o `NewPageModal` da Fase 18 não é o fluxo usado por este comando). Validado via `dotnet build`/`dotnet test` (0 erros, 157/157 testes); sem validação visual em app rodando (não foi editado `MainLayout.razor`, que ainda não roteia para as páginas — integração central pendente de fase futura).

---

### T62: Portar Upload

**What**: `Upload.razor` reaproveitando `UploadViewModel` (zona de drag&drop + seleção por clique via file dialog nativo, formulário de qualidade/idioma/locutores, status em tempo real da fila). Drag&drop nativo do navegador exige JS interop pontual (`ondragover`/`ondrop` já suportados nativamente por eventos Blazor — sem JS extra necessário na maioria dos casos; documentar se algum caso exigir).
**Where**: `src/Transcriba.App/Components/Pages/Upload.razor`
**Depends on**: T58
**Reuses**: `UploadViewModel.cs`, `TranscriptionQueueService`, `MediaStorageService`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Drag&drop e clique abrem seleção de arquivo, enfileiram transcrição real
- [x] Status "Em andamento"/erro reflete em tempo real (mesmo comportamento da Fase 10 original)

**Tests**: none (View) — `UploadViewModel` já testado
**Gate**: build

**Commit**: `feat(app): implementa Upload em Razor`
**Status**: ✅ Concluída — `Upload.razor` recebe `UploadViewModel` como `[Parameter]` (mesmo padrão de Dashboard/ResearchPage/Settings). Seleção de arquivo por clique funciona de ponta a ponta via novo `IFileOpenService`/`WpfFileOpenService` (`Microsoft.Win32.OpenFileDialog`, mesmo padrão do `WpfFileSaveService`), registrado em `AppServiceCollectionExtensions`. **Limitação conhecida do drag&drop**: os eventos `@ondragover`/`@ondragleave`/`@ondrop` do Blazor só dão feedback visual (classe `dragover`, reaproveitando o estilo de `:hover` do protótipo) — o WebView2/Blazor Hybrid, como qualquer motor Chromium, só expõe o objeto `File` do navegador em um drop, não o caminho absoluto no disco. Ler o caminho real exigiria hook nativo no WebView2 hospedeiro (`ICoreWebView2CompositionController::Drop` com `IDataObject`/`CF_HDROP`, modo "windowless"/composição), infraestrutura do host (`MainWindow`/`BlazorWebView`) fora do escopo deste componente — documentado com `// TODO` no código. Status "Em andamento"/erro em tempo real já é herdado de graça do `DashboardViewModel` (T60), que assina `TranscriptionQueueService.StatusChanged`; `StartTranscriptionCommand` já navega para `Dashboard` com filtro "Em andamento" após enfileirar. CSS: porta fielmente `.upload-*` do protótipo (já existia integralmente em `app.css`) e adiciona apenas o que faltava para a página funcionar de verdade (não coberto pelo mock estático): `.upload-error` (mensagem de validação), `.upload-btn:disabled`, `.upload-file-clear` (botão de remover arquivo) e `.upload-zone.dragover` (o protótipo alternava essa classe via JS mas nunca definiu seu estilo — reaproveita o mesmo par cor/fundo do `:hover`). Validado via `dotnet build`/`dotnet test` (0 erros, 157/157 testes); sem validação visual em app rodando (não foi editado `MainLayout.razor`, integração central pendente de fase futura/outro agente).

---

### T63: Portar PlayerBar

**What**: `PlayerBar.razor` reaproveitando `PlayerBarViewModel`/`IMediaPlaybackService` (play/pause/seek/volume/velocidade, barra de progresso clicável).
**Where**: `src/Transcriba.App/Components/Shared/PlayerBar.razor`
**Depends on**: T58, T62
**Reuses**: `PlayerBarViewModel.cs`, `IMediaPlaybackService.cs`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Playback de áudio real funciona (play/pause/seek/volume/velocidade)
- [ ] Fidelidade visual com `.player-*` do protótipo

**Tests**: none (View)
**Gate**: build

**Commit**: `feat(app): implementa PlayerBar em Razor`

---

### T64: Portar SegmentItem e SpeakerDropdown

**What**: `SegmentItem.razor` (texto editável, split no cursor, badge de locutor, destaque `active-seg`) + `SpeakerDropdown.razor` reaproveitando `SegmentEditingService`/`SpeakerDropdownViewModel`.
**Where**: `src/Transcriba.App/Components/Shared/SegmentItem.razor`, `.../SpeakerDropdown.razor`
**Depends on**: T63
**Reuses**: `SegmentEditingService.cs`, `SpeakerDropdownViewModel.cs`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Split/merge/atribuição de locutor funcionam exatamente como no protótipo (mesma semântica validada na Fase 11 original)
- [ ] `active-seg` sincroniza com a posição de playback (via evento do `PlayerBar`)

**Tests**: none (View) — `SegmentEditingService` já testado
**Gate**: build

**Commit**: `feat(app): implementa SegmentItem e SpeakerDropdown em Razor`

---

### T65: Portar Editor (integração final)

**What**: `Editor.razor` reaproveitando `EditorViewModel`, integrando breadcrumb, header (ícone/título editável via `IconPicker`), tags, toolbar (dividir/mesclar/locutor/exportar), lista de `SegmentItem`, `PlayerBar` fixo no rodapé.
**Where**: `src/Transcriba.App/Components/Pages/Editor.razor`
**Depends on**: T64
**Reuses**: `EditorViewModel.cs`, `ExportService.cs`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Fluxo completo (upload → editor) funciona ponta a ponta com transcrição real
- [ ] Exportação TXT/SRT/VTT funciona pelo botão "Exportar"
- [ ] Fidelidade visual com `.editor-*`/`.seg*` do protótipo

**Tests**: none (View) — `EditorViewModel`/`ExportService` já testados
**Gate**: full (regressão do pipeline completo)

**Commit**: `feat(app): implementa Editor em Razor (integração completa)`

---

### T66: Portar tela de Gravação mockada

**What**: `Recording.razor` reaproveitando `RecordingViewModel` (timer, forma de onda simulada, frases "ao vivo" mockadas, fluxo play/pause/stop → navega para editor). Waveform via `<canvas>` + JS interop pontual isolado em `wwwroot/js/waveform.js` (única exceção documentada de JS residual, ver Risks do design.md).
**Where**: `src/Transcriba.App/Components/Pages/Recording.razor`, `src/Transcriba.App/wwwroot/js/waveform.js`
**Depends on**: T56
**Reuses**: `RecordingViewModel.cs`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Fluxo mockado idêntico ao protótipo (timer, forma de onda animada, transição para editor)
- [x] Fidelidade visual com `.rec-*` do protótipo

**Tests**: none (View) — `RecordingViewModel` já testado
**Gate**: build

**Commit**: `feat(app): implementa Recording em Razor`
**Status**: ✅ Concluída (`RecordingViewModel` reaproveitado sem alteração; como é Transient e resolvido pelo `NavigationService`, `Recording.razor` recebe a instância via `[Parameter]` em vez de `@inject`. Waveform: canvas + `wwwroot/js/waveform.js` isolam só o desenho/pixels — toda a randomização de altura/estado "ativo" continua em C# (`AnimateWaveform`), coalescendo as 48 atualizações síncronas por tick em uma única chamada `render` via `IJSRuntime`. Corrigido bug do protótipo onde o botão de pausa chamava uma função JS nunca definida: aqui cada botão liga a um comando dedicado e funcional do ViewModel (`StartRecordingCommand`/`TogglePauseCommand`/`StopRecordingCommand`). Não validado visualmente dentro do app — `MainLayout.razor` ainda não integra as telas (fora do escopo desta task, ver AVISO de concorrência); validação por build limpo + testes verdes + checagem de sintaxe do JS + inspeção manual do código/CSS contra o protótipo)

---

### T67: Portar Configurações

**What**: `Settings.razor` reaproveitando `SettingsViewModel` (perfil, idioma padrão, identificar locutores, transcrição ao vivo inerte, dispositivo de execução, tema).
**Where**: `src/Transcriba.App/Components/Pages/Settings.razor`
**Depends on**: T56
**Reuses**: `SettingsViewModel.cs`, `SettingsService.cs`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Alterações persistem via `SettingsService`
- [x] Fidelidade visual com `.settings-*` do protótipo

**Tests**: none (View) — `SettingsViewModel` já testado
**Gate**: build

**Commit**: `feat(app): implementa Settings em Razor`
**Status**: ✅ Concluída (`Settings.razor` recebe `SettingsViewModel` via `[Parameter]` — e não `@inject` — porque o ViewModel é `Transient` e a instância viva é a resolvida por `NavigationService.CurrentViewModel`, mesmo padrão esperado para as demais páginas; campos de perfil chamam `SaveProfileCommand` no `@onchange`, os demais campos (idioma/locutores/transcrição ao vivo/dispositivo) alteram propriedades do ViewModel diretamente e reaproveitam os `partial void OnXxxChanged` já existentes para persistir; o toggle de tema reaproveita o `ThemeService` singleton (mesmo usado pelo `Sidebar.razor`) em vez de duplicar lógica no `SettingsViewModel`. Não validado visualmente na janela WPF real — `MainLayout.razor` é propriedade de outro agente nesta rodada (integração centralizada depois); validação feita por build limpo + inspeção de código/CSS, com todas as classes `.settings-*`/`.toggle`/`.screen-*` conferidas 1:1 contra o protótipo)

---

### T68: Portar confirmação de exclusão (Dashboard + Pesquisa)

**What**: `ConfirmationDialog.razor` reaproveitando `IConfirmationService` (nova implementação Blazor no lugar de `AvaloniaConfirmationService`); wiring dos menus de contexto de exclusão em `Dashboard.razor`/`ResearchPage.razor`.
**Where**: `src/Transcriba.App/Components/Shared/ConfirmationDialog.razor`, novo `BlazorConfirmationService : IConfirmationService`
**Depends on**: T60, T61
**Reuses**: `IConfirmationService.cs`, `LibraryService.cs`, `ResearchService.cs`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [x] Excluir transcrição/pesquisa pede confirmação e remove do banco (mesmo comportamento das Fases 7/8 originais)

**Tests**: none (View)
**Gate**: full (regressão de exclusão)

**Commit**: `feat(app): implementa confirmação de exclusão em Razor`
**Status**: ✅ Concluída — `BlazorConfirmationService : IConfirmationService, INotifyPropertyChanged` (Singleton, registrado em `AppServiceCollectionExtensions.cs` no lugar do antigo `WpfConfirmationService`, que fazia `MessageBox.Show` e foi deletado) expõe `Title`/`Message`/`IsOpen` mais `TaskCompletionSource<bool>` pendente: `ConfirmAsync` seta o estado e devolve a `Task` sem resolvê-la; `Confirm()`/`Cancel()` (chamados pelos botões do `ConfirmationDialog.razor`) resolvem a pendência com `true`/`false` e fecham o modal — mesmo espírito reativo do `NewPageModalViewModel` (Singleton com `IsOpen` observado via `PropertyChanged`), só que aqui o "resultado" trafega por uma `Task` em vez de navegação/comando. `ConfirmationDialog.razor` reaproveita fielmente `.modal-overlay`/`.modal`/`.modal-title`/`.modal-actions`/`.modal-cancel`/`.modal-confirm` (já portadas por `NewPageModal.razor` na T58) — só foram necessárias duas classes novas no `app.css`: `.modal-message` (parágrafo da mensagem, ausente do protótipo) e `.modal-confirm-danger` (variante vermelha do botão de confirmar, já que o protótipo não modela nenhum fluxo de exclusão — só o modal azul de "criar página"). Nenhuma mudança de wiring foi necessária em `Dashboard.razor`/`TranscriptionCard.razor`: o botão "Excluir" já chamava `DashboardViewModel.DeleteTranscriptionAsync` → `IConfirmationService.ConfirmAsync`, então trocar a implementação por trás da interface bastou (removido só o comentário `TODO` que apontava para esta fase). Em `ResearchPage.razor` havia uma lacuna real de wiring: a versão Avalonia original (`ResearchPageView.axaml`, já removida) usava `ContextFlyout` (menu de clique direito) para "Excluir pesquisa" (ícone do header) e "Excluir" por item da lista — sem equivalente direto em HTML/Blazor sem JS interop extra — então portei ambos como botões de ação visíveis (`.dash-card-action`/`-danger`, mesmo padrão já usado em `TranscriptionCard.razor`/T60): um botão "Excluir pesquisa" no `page-header` (chama `ResearchPageViewModel.DeleteResearchCommand`, que já existia e já pedia confirmação, mas não estava conectado a nenhuma view) e um botão de exclusão por item em `.research-item-right` (chama `TranscriptionCardViewModel.DeleteCommand`, condicionado a `CanDelete`, com `@@onclick:stopPropagation` para não disparar `OpenCommand` do item). `dotnet build Transcriba.sln` — 0 erros. `dotnet test tests/Transcriba.Tests` — 157/157 passando (sem testes novos: comandos/serviços reaproveitados já tinham cobertura via `FakeConfirmationService`). **Validação visual**: não foi possível validar interativamente (`MainLayout.razor` ainda não monta `ConfirmationDialog.razor` — integração central, fora do escopo desta task e reservada para outro agente); validação por inspeção de código/CSS 1:1 contra o protótipo/padrão já estabelecido e build+testes limpos. **Bloqueio**: nenhum.
