# Transcriba Desktop — Validation Report

**Feature**: transcriba-desktop  
**Date**: 2026-07-04  
**Verifier**: independent (author ≠ verifier)  
**Diff range**: `17505f9..58b79f3` (159 files, +14.542 lines)  
**Overall**: **PASS condicional** ⚠️

---

## Gate Results

| Gate | Command | Result |
| --- | --- | --- |
| Build | `dotnet build Transcriba.sln` | ✅ SUCCESS (0 erros; warnings NU1903 transitivos SQLitePCLRaw, CS9124) |
| Full | `dotnet test tests/Transcriba.Tests` | ✅ **150/150 passed**, 0 failed, 0 skipped |
| Test delta | baseline `17505f9` → HEAD | 0 → 150 (+150) |

---

## Discrimination Sensor

Scratch worktree em `58b79f3`; mutações descartadas após execução.

| Mutation | Target | Killed? |
| --- | --- | --- |
| M1 | `SegmentEditingService.GetActiveSegment` — `<=` → `<` | ❌ **Survived** |
| M2 | `LibraryService.SearchText` — remove `ToLower()` | ✅ Killed |
| M3 | `TranscriptionQueueService` — `SingleReader=false` | ❌ Survived (ineficaz; serialismo via `await foreach`) |

**Sensor verdict**: ⚠️ **PARTIAL** — 1 mutant comportamental sobreviveu (boundary de segmento ativo).

---

## Spec-Anchored Outcome Check (summary)

| Requirement | Status | Notes |
| --- | --- | --- |
| SHELL-01 | ⚠️ | Navegação/tema testados; layout sidebar 🔍 manual |
| DASH-01 | ⚠️ | Filtros/busca ✅; **AC7 sidebar count — GAP** |
| RESEARCH-01 | ✅ | 4/4 ACs com testes |
| NEWPAGE-01 | ✅ | Modal, título obrigatório, tags azul |
| UPLOAD-01 | ✅ | Formato, enqueue, erro, retry |
| EDITOR-01 | ✅ | Dividir/mesclar/seek/persistência |
| SPEAKER-01 | ✅ | Assign, paleta, disabled sem ativo |
| PLAYER-01 | ⚠️ | Play/seek/speed ✅; **AC6 volume — GAP** |
| REC-01 | ✅ | Mock completo |
| SETTINGS-01 | ⚠️ | Persistência ✅; **CUDA fallback edge — GAP** |
| DATA-01 | ✅ | SQLite + mídia copiada |
| EXPORT-01 | ✅ | TXT/SRT/VTT + bloqueio sem segmentos |
| CRUD-01 | ✅ | Exclusão + cancelamento |

**Coverage**: ~52/60 ACs principais com evidência assertiva; 4 GAPs hard; ~12 manual/UAT para Views.

---

## Ranked Gaps (fix tasks)

1. **GetActiveSegment boundary** — mutant M1 survived; falta teste `position == segment.StartSeconds` (ex.: segmentos 0/10/20s, posição 10s → segmento B). Files: `SegmentEditingService.cs`, `SegmentEditingServiceTests.cs`.
2. **PLAYER-01 AC6 volume** — `PlayerBarViewModel` / `LibVlcPlaybackService` propagam volume; zero teste assertando valor.
3. **DASH-01 AC7 sidebar TotalCount** — refresh após criar/concluir transcrição sem assert.
4. **SETTINGS-01 CUDA fallback** — edge case spec: fallback CPU + informar usuário; sem teste.
5. **UAT visual** — 13 Views .axaml sem teste automatizado (aceito no MVP per design); fidelidade ao protótipo HTML não verificada nesta sessão.

---

## Tasks Traceability

50/50 tasks implementadas. `tasks.md`: T1–T22 e T48–T50 marcadas ✅; T23–T47 implementadas mas flags ✅ inconsistentes (gap documental apenas).

---

## Verdict

**PASS condicional** — MVP funcional com gates verdes e cobertura forte de serviços/engine. Sensor identificou teste fraco em segmento-ativo (gap #1). Recomendado: fix tasks 1–3 antes de marcar feature **Verified** pleno.

---

## Addendum — Migração para Blazor Hybrid (AD-005, 2026-07-04)

**Contexto**: a implementação Avalonia acima (validada em PASS condicional) foi descontinuada por um crash fatal e não determinístico do runtime Windows (`Internal CLR error 0x80131506`) que persistia mesmo após todas as mitigações tentadas (GPU/composição/GC/JIT). Ver `.specs/STATE.md` AD-005. Esta seção documenta a validação do **bootstrap** da nova stack (Fase 16 / T51–T54 em `tasks.md`) — não é uma reavaliação completa do MVP, que segue pendente de reimplementação (Fases 17–25).

### Smoke test do bootstrap (T54)

| # | Método de execução | Resultado | Observação |
| --- | --- | --- | --- |
| 1 | `dotnet run --project src/Transcriba.App` | Processo ficou de pé, sem crash, mas **nenhuma janela visível ao usuário** | Causa raiz #1 (ver abaixo) |
| 2 | `Start-Process ...\Transcriba.App.exe` direto | Console de debug abriu; **nenhuma tela** | Confirma causa raiz #1 |
| 3 | Idem, após corrigir causa raiz #1 | Janela abriu, mas presa em **"Carregando…"** indefinidamente | Causa raiz #2 |
| 4 | Idem, após corrigir causa raiz #2 | **Janela abriu e renderizou o componente Razor real** (`Shell.razor`, texto "Bootstrap Blazor Hybrid OK") | ✅ Confirmado pelo usuário |

**Causas raiz encontradas e corrigidas** (nenhuma delas é o crash `0x80131506` original — são bugs novos de setup do WPF+BlazorWebView, já corrigidos no código):
1. `Program.cs` criava `new App()` e chamava `Run()` sem chamar `app.InitializeComponent()` — método gerado a partir do `App.xaml` que aplica a propriedade `StartupUri` à instância. Sem essa chamada o loop de mensagens do WPF sobe (processo vivo) mas nenhuma janela é criada. Corrigido chamando `app.InitializeComponent()` antes de `app.Run()`.
2. `wwwroot/index.html` tinha `<script src="_framework/blazor.webview.js" autostart="false">` sem nenhum `Blazor.start()` manual correspondente — a página carregava mas o placeholder `<div id="app">Carregando…</div>` nunca era substituído pelo componente Razor real. Corrigido removendo `autostart="false"` (autostart padrão).

**Resultado**: nenhuma reincidência do `Internal CLR error 0x80131506` (nem dos crashes silenciosos sem exceção/WER associados às causas raiz acima, que na verdade eram bugs de configuração, não crashes de runtime) em nenhuma das execuções. A decisão AD-005 (Blazor Hybrid como substituto do Avalonia) está **validada no nível de bootstrap** — build limpo (`Transcriba.sln`, 0 erros) e app abre/renderiza. Fases 17–25 (portar as telas reais) ainda não começaram.

**Ambiente**: Windows 10.0.26200, sessão interativa local (`eugen`), .NET 10, `Transcriba.App.exe` rodando fora de depurador.

**Verificação adicional (2026-07-04, pós-Fase 17)**: um sub-agente da Fase 17 encontrou 24 ocorrências do erro `0x80131506` no Visualizador de Eventos do Windows (Application log, provider `.NET Runtime`, evento 1023) e levantou a suspeita de que teriam ocorrido durante o smoke test do bootstrap Blazor, contradizendo esta seção. Investigação: todos os 24 eventos têm `TimeCreated` entre `18:25:05` e `23:23:31`; o primeiro arquivo específico da stack Blazor Hybrid (`App.xaml`) tem `CreationTime` de `23:38:24` — **15 minutos depois do último crash registrado**. Ou seja, os 24 eventos são anteriores à existência da stack Blazor (era Avalonia) e não representam reincidência do crash na nova stack. Nenhum evento `0x80131506` foi registrado após `23:38:24` até o momento desta verificação. Alarme falso — conclusão original desta seção permanece válida.
