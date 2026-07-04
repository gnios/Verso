# Transcriba Desktop Context

**Gathered:** 2026-07-04
**Spec:** `.specs/features/transcriba-desktop/spec.md`
**Status:** Ready for design

---

## Feature Boundary

Aplicativo desktop em C# ("Transcriba") que replica fielmente o protótipo HTML `transcriba-v2-icons-transcriptions.html` (biblioteca de transcrições, organização por pesquisas/tese, upload, editor de transcrição com player sincronizado, tela de gravação, configurações) e usa o motor de transcrição comprovado em `transcrever.cs` (Whisper.net + segmentação por silêncio + processamento paralelo) como back-end real de transcrição.

---

## Implementation Decisions

### Stack de UI

- Avalonia UI (multiplataforma), permitindo estilização customizada próxima ao CSS do protótipo (sidebar, cards, popups de ícone/cor, dark mode).

### Persistência

- SQLite local (arquivo único de banco de dados) para pesquisas, transcrições, tags, locutores e segmentos.
- Arquivos de mídia (áudio/vídeo importados ou gravados) são **copiados** para uma pasta de dados do app (`%AppData%\Transcriba\media`) — o app nunca depende do arquivo original permanecer no lugar.

### Gravação ao vivo

- Escopo do MVP: a tela "Gravação ao vivo" é implementada com a mesma fidelidade visual/interativa do protótipo (timer, forma de onda, frases "ao vivo" simuladas, fluxo de play/pause/stop navegando para o editor ao final).
- Captura real de áudio do microfone e transcrição incremental em tempo real ficam **fora do MVP** (funcionalidade futura — ver Deferred Ideas).

### Identificação de locutores

- Somente atribuição **manual** de locutor por segmento (dropdown), exatamente como no protótipo. Não há diarização automática por ML no MVP.
- Campo "Locutores" no formulário de upload (Automático/Desativado): "Automático" cria os segmentos com um locutor genérico (`Locutor 1`) atribuível/renomeável depois; "Desativado" cria segmentos sem locutor atribuído.
- Toggle "Identificar locutores" em Configurações define o valor padrão desse campo no formulário de upload.

### Exportação

- Botão "Exportar" no editor (atualmente sem função no protótipo) passa a exportar: TXT (mesmo formato de saída do `transcrever.cs`) e legendas SRT/VTT.

### Busca

- Campo de busca do dashboard (Biblioteca) passa a filtrar de verdade por título e conteúdo da transcrição (no protótipo é apenas visual). Combinável com os filtros de status e tag.
- Campo de busca da sidebar (topo, ao lado do logo) permanece apenas visual/decorativo no MVP — é uma busca "global" redundante com a busca do dashboard e não crítica para o MVP.

### Motor de transcrição (mapeamento de qualidade → modelo Whisper)

- "Padrão" (upload) → modelo `small`.
- "Alta" (upload) → modelo `large-v3`.
- Idioma selecionado no formulário de upload é usado diretamente como idioma forçado da transcrição (sem detecção automática), pois o protótipo não oferece opção "Automático" nesse campo.
- Dispositivo de execução (CPU/CUDA/Vulkan) não existe no protótipo; será adicionado como nova seção em Configurações ("Motor de transcrição") com opção "Automático" (detecta o melhor disponível) — extensão técnica necessária, não uma mudança visual/de fluxo do protótipo.

### Agent's Discretion

- Mapeamento exato de todas as cores/emoji já vem determinado pelos arrays `PAGE_ICONS`, `PAGE_COLORS`, `TRANS_ICONS`, `TAG_COLORS` do protótipo — serão replicados como constantes.
- Comportamento de "segmento ativo": segue exatamente a lógica do protótipo — clicar num segmento chama seek do player para o tempo daquele segmento; o segmento "ativo" (usado por Mesclar e Locutor) é sempre o último segmento cujo tempo de início é ≤ tempo atual de reprodução (não é uma seleção independente da posição de playback). "Dividir" opera sobre o cursor de texto dentro do segmento (independente do segmento ativo por playback).
- Novas tags criadas pelo usuário recebem sempre a cor azul (`blue`) por padrão, replicando o fallback do protótipo.
- Adicionado seletor de arquivo por clique (file dialog nativo) na zona de upload, além do drag-and-drop — o protótipo só implementa drop, mas um app real precisa de um jeito de abrir o diálogo de arquivo por clique.

### Declined / Undiscussed Gray Areas → Assumptions

| Área não discutida | Assunção (padrão escolhido) | Racional |
| --- | --- | --- |
| Progresso de transcrição em andamento (upload) | Mostrar tela de progresso indeterminado/percentual simples enquanto o pipeline do `transcrever.cs` roda em background, depois navega para o editor | Necessário para não travar a UI durante um processo que pode levar minutos |
| Concorrência (rodar 2+ transcrições ao mesmo tempo) | Permitido; cada transcrição roda em sua própria tarefa em background, dashboard mostra status "Em andamento" até concluir | Consistente com Whisper.net sendo thread-safe por instância própria de processor |
| Falha na transcrição (arquivo corrompido, ffmpeg falha, etc.) | Transcrição marcada com status de erro visível no dashboard/editor, mensagem de erro exibida, permite tentar novamente | Dimensão de falha explícita exigida pelo rubric de requisitos implícitos |
| Exclusão de pesquisas/transcrições | Suporte a excluir transcrição e pesquisa (com confirmação) via menu de contexto, mesmo não estando explícito no protótipo (função básica de CRUD) | Necessário para um app real utilizável; protótipo é só front-end de demonstração sem exclusão |

---

## Specific References

- Protótipo HTML/CSS/JS: `transcriba-v2-icons-transcriptions.html` (fonte de verdade visual e de interação).
- Motor de transcrição: `transcrever.cs` (fonte de verdade do pipeline: download de modelo GGML, VAD por silêncio, paralelismo, detecção de idioma, saída formatada).

---

## Deferred Ideas

- Captura real de áudio/vídeo do microfone/câmera e transcrição incremental em tempo real na tela de Gravação (fase futura).
- Diarização automática por ML (fase futura, modelo de dados já compatível).
- Busca global via campo da sidebar.
