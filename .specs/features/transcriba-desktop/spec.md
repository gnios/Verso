# Transcriba Desktop (C#) Specification

## Problem Statement

Hoje existe apenas (1) um protótipo estático em HTML/CSS/JS (`transcriba-v2-icons-transcriptions.html`) que define a experiência visual e de interação de um app de transcrição acadêmica, com dados mockados em memória, e (2) uma prova de conceito em C# (`transcrever.cs`) que comprova um pipeline real de transcrição (Whisper.net + segmentação por silêncio + paralelismo + download automático de modelo), mas roda apenas via console, sem interface gráfica nem persistência. Não existe hoje um aplicativo desktop real, usável, que uma pesquisadora possa instalar e usar no dia a dia para organizar, transcrever, revisar e exportar entrevistas/gravações de pesquisa acadêmica.

## Goals

- [ ] Entregar um aplicativo desktop em C# (Blazor Hybrid — WPF + BlazorWebView) que replica fielmente todas as telas, fluxos e interações visuais do protótipo `transcriba-v2-icons-transcriptions.html`.
- [ ] Integrar o pipeline de transcrição real (baseado em `transcrever.cs`: Whisper.net, VAD por silêncio, paralelismo, download automático de modelo) como motor de back-end por trás do fluxo de upload.
- [ ] Persistir todos os dados (pesquisas, transcrições, tags, locutores, segmentos) localmente em SQLite, com arquivos de mídia copiados para a pasta de dados do app.
- [ ] Permitir edição completa do transcript (texto, divisão/mesclagem de segmentos, atribuição manual de locutores, título, ícone) com player de áudio/vídeo sincronizado.
- [ ] Permitir exportação do transcript em TXT e legendas SRT/VTT.

## Out of Scope

Explicitamente excluído deste MVP. Documentado para evitar scope creep.

| Feature | Motivo |
| --- | --- |
| Captura real de áudio/vídeo do microfone/câmera na tela de Gravação | Decisão do usuário: deferido para fase futura; MVP replica apenas a UI/interação mockada do protótipo |
| Transcrição incremental em tempo real durante gravação | Depende da captura real de áudio, também deferida |
| Diarização automática por Machine Learning (detecção automática de quem fala) | Decisão do usuário: apenas atribuição manual no MVP; `transcrever.cs` não implementa diarização |
| Sincronização em nuvem / multiusuário / contas | Fora do escopo; app é local e single-user |
| Busca funcional no campo de busca da sidebar (topo) | Redundante com a busca funcional do dashboard; mantido apenas visual |
| Suporte multiplataforma (macOS/Linux) | Fora de escopo — stack (WPF + BlazorWebView) é Windows-only por natureza; alvo de desenvolvimento e testes é Windows (ambiente do usuário) |

---

## Assumptions & Open Questions

Toda ambiguidade foi resolvida ou registrada aqui — nada fica sem definição.

| Assunção / decisão | Padrão escolhido | Racional | Confirmado? |
| --- | --- | --- | --- |
| Framework de UI | Blazor Hybrid (WPF + BlazorWebView) — substitui a tentativa anterior com Avalonia UI, que sofria de crash fatal e não determinístico do runtime (ver AD-005 em `.specs/STATE.md`) | Escolha explícita do usuário | y |
| Persistência | SQLite (arquivo local único) | Escolha explícita do usuário | y |
| Armazenamento de mídia | Copiado para `%AppData%\Transcriba\media` | Escolha explícita do usuário | y |
| Escopo de gravação ao vivo | Apenas UI mockada (sem captura real) | Escolha explícita do usuário | y |
| Diarização | Manual apenas | Escolha explícita do usuário | y |
| Exportação | TXT + SRT/VTT | Escolha explícita do usuário | y |
| Busca do dashboard | Funcional (título + conteúdo) | Escolha explícita do usuário | y |
| Mapeamento Qualidade→Modelo Whisper | Padrão=`small`, Alta=`large-v3` | Não definido no protótipo (só rótulos); alinhado aos modelos já suportados em `transcrever.cs` | n (assumido) |
| Idioma da transcrição | Idioma selecionado no upload é forçado (sem auto-detecção) | Protótipo não oferece opção "Automático" nesse campo | n (assumido) |
| Dispositivo de execução (CPU/CUDA/Vulkan) | Nova seção em Configurações, padrão "Automático" | Não existe no protótipo; necessidade técnica do motor de transcrição | n (assumido) |
| Campo "Locutores: Automático/Desativado" no upload | Automático = cria segmentos com locutor genérico "Locutor 1"; Desativado = sem locutor | Protótipo não define comportamento; é só um `<select>` estático | n (assumido) |
| Toggle "Identificar locutores" em Configurações | Define o valor padrão do campo acima no formulário de upload | Consistência entre configuração global e formulário | n (assumido) |
| Toggle "Transcrição ao vivo" em Configurações | Mantido visualmente, mas inerte no MVP (recurso de gravação ao vivo deferido) | Preserva fidelidade visual sem implicar funcionalidade não implementada | n (assumido) |
| Seleção de arquivo por clique na zona de upload | Adicionado diálogo nativo de arquivo além do drag-and-drop | Protótipo só implementa drop; um app real precisa de alternativa por clique | n (assumido) |
| Progresso de transcrição em andamento | Barra/indicador de progresso + status "Em andamento" no dashboard enquanto roda em background | Não existe no protótipo (upload "finaliza" instantaneamente); necessário para UX real com processo demorado | n (assumido) |
| Falha de transcrição | Status de erro visível + mensagem + opção de tentar novamente | Dimensão de falha obrigatória (ver sweep abaixo); não coberta no protótipo | n (assumido) |
| Exclusão de pesquisa/transcrição | Suportada via menu de contexto, com confirmação | Não existe no protótipo (app de demonstração), mas necessária para uso real (CRUD básico) | n (assumido) |
| Cor de tags novas | Sempre azul (`blue`) por padrão | Replica exatamente o fallback `TAG_COLORS[t]||'blue'` do protótipo | y (fidelidade ao protótipo) |
| Segmento "ativo" para Mesclar/Locutor | Determinado pela posição atual do player (último segmento com tempo de início ≤ tempo atual), não por seleção de clique isolada | Replica exatamente a lógica `highlightSegment()`/`active-seg` do protótipo | y (fidelidade ao protótipo) |
| "Dividir" segmento | Opera sobre a posição do cursor de texto dentro do segmento clicado, independente do segmento "ativo" por playback | Replica exatamente `splitSegment()` do protótipo | y (fidelidade ao protótipo) |

**Open questions:** nenhuma — todas resolvidas ou registradas acima.

---

## User Stories

### P1: Navegação e casca do aplicativo (Shell) ⭐ MVP

**User Story**: Como pesquisadora, quero navegar entre biblioteca, pesquisas, upload, gravação, editor e configurações através de uma barra lateral fixa, para organizar meu fluxo de trabalho como no protótipo.

**Why P1**: Sem isso não existe app — é a estrutura que conecta todas as telas.

**Acceptance Criteria**:

1. WHEN o app é iniciado THEN o sistema SHALL exibir a barra lateral (logo "Transcriba", campo de busca visual, botão "Nova" com menu, seção "Pesquisas", seção "Transcrições" com contadores, seção "Tags", rodapé "Configurações") e a tela "Biblioteca" (Dashboard) ativa por padrão.
2. WHEN o usuário clica no botão de tema (sol/lua) THEN o sistema SHALL alternar entre tema claro e escuro imediatamente em toda a interface e persistir a preferência entre reinicializações do app.
3. WHEN o usuário clica em "Nova" THEN o sistema SHALL exibir um menu com as opções "Pesquisa / Tese", "Transcrição" e "Gravar agora".
4. WHEN o usuário clica em um item de pesquisa na sidebar THEN o sistema SHALL alternar o estado expandido/colapsado da lista de transcrições daquela pesquisa (chevron gira, lista mostra/esconde).
5. WHEN o usuário clica em uma transcrição listada sob uma pesquisa na sidebar THEN o sistema SHALL abrir essa transcrição na tela de Editor.
6. WHEN o usuário clica em uma tag na sidebar THEN o sistema SHALL navegar para o Dashboard filtrado por aquela tag.
7. WHEN o usuário clica em "Configurações" no rodapé THEN o sistema SHALL exibir a tela de Configurações.
8. WHEN o usuário clica no logo "Transcriba" THEN o sistema SHALL navegar para o Dashboard.

**Independent Test**: Abrir o app, alternar tema, abrir/fechar menu "Nova", expandir uma pesquisa, navegar entre todas as telas pela sidebar.

---

### P1: Biblioteca / Dashboard ⭐ MVP

**User Story**: Como pesquisadora, quero ver todas as minhas transcrições em cards, filtrar por status/tag e buscar por texto, para encontrar rapidamente o material que preciso.

**Why P1**: É a tela inicial e principal ponto de acesso a todo o conteúdo.

**Acceptance Criteria**:

1. WHEN o Dashboard é exibido THEN o sistema SHALL renderizar um card por transcrição com ícone+título, preview (2 linhas), tags coloridas, badge de status ("Em andamento"/"Concluída"), data e duração.
2. WHEN o usuário clica no filtro "Todas"/"Em andamento"/"Concluídas" THEN o sistema SHALL exibir apenas os cards correspondentes ao status selecionado e marcar visualmente o filtro ativo.
3. WHEN o usuário digita no campo de busca do dashboard THEN o sistema SHALL filtrar os cards, em tempo real, para exibir apenas transcrições cujo título OU conteúdo dos segmentos contenha o texto buscado (case-insensitive), combinando com o filtro de status/tag ativo.
4. WHEN um filtro de tag está ativo (originado pela sidebar) THEN o sistema SHALL exibir apenas transcrições que contenham aquela tag, e o filtro de tag SHALL ser combinável com a busca textual.
5. WHEN nenhum card corresponde aos filtros/busca ativos THEN o sistema SHALL exibir uma mensagem de estado vazio ("Nenhuma transcrição encontrada").
6. WHEN o usuário clica em um card THEN o sistema SHALL abrir aquela transcrição na tela de Editor.
7. WHEN uma nova transcrição é criada ou concluída THEN o badge "Todas" na sidebar SHALL refletir a contagem total atualizada.

**Independent Test**: Com pelo menos 3 transcrições de status/tags diferentes, aplicar cada filtro isoladamente e em combinação com busca textual, verificar contagem de resultados.

---

### P1: Pesquisas / Teses (Research Pages) ⭐ MVP

**User Story**: Como pesquisadora, quero agrupar minhas transcrições dentro de projetos de pesquisa/tese, para manter entrevistas relacionadas organizadas.

**Why P1**: Organização hierárquica é central ao propósito acadêmico do app.

**Acceptance Criteria**:

1. WHEN o usuário abre uma página de pesquisa THEN o sistema SHALL exibir breadcrumb ("Biblioteca → [pesquisa]"), cabeçalho com ícone colorido, título e descrição, e a lista de transcrições associadas (ícone, título, data, duração, status, tags).
2. WHEN o usuário clica em "Adicionar" na seção de transcrições da pesquisa THEN o sistema SHALL navegar para a tela de Upload com a pesquisa pré-selecionada no formulário.
3. WHEN o usuário clica em uma transcrição da lista da pesquisa THEN o sistema SHALL abrir aquela transcrição no Editor.
4. WHEN uma pesquisa não possui transcrições THEN o sistema SHALL exibir a seção "Transcrições" vazia sem erro.

**Independent Test**: Criar uma pesquisa, adicionar 2 transcrições a ela via upload, navegar pela página da pesquisa e confirmar listagem e navegação.

---

### P1: Criar nova Pesquisa / Transcrição avulsa (modal) ⭐ MVP

**User Story**: Como pesquisadora, quero criar uma nova pesquisa ou uma transcrição avulsa (com título, ícone, cor e tags) por um modal rápido, para começar a organizar meu trabalho sem sair da tela atual.

**Why P1**: Ponto de entrada para toda a organização de conteúdo (pesquisas e tags).

**Acceptance Criteria**:

1. WHEN o usuário seleciona "Pesquisa / Tese" no menu "Nova" THEN o sistema SHALL abrir um modal com seletor de ícone (grade de emojis), seletor de cor (8 cores), preview ao vivo, e campo de título; SEM campo de tags.
2. WHEN o usuário seleciona "Transcrição" no menu "Nova" THEN o sistema SHALL abrir o mesmo modal, porém SEM seletor de cor e COM campo de tags (separadas por vírgula).
3. WHEN o usuário digita um título ou seleciona ícone/cor no modal THEN o sistema SHALL atualizar o preview ao vivo (emoji + texto) imediatamente.
4. WHEN o usuário confirma criação de uma pesquisa THEN o sistema SHALL adicioná-la à sidebar e à lista de pesquisas, sem transcrições associadas, e permanecer/retornar ao Dashboard.
5. WHEN o usuário confirma criação de uma transcrição avulsa THEN o sistema SHALL criar um registro de transcrição vazio (sem segmentos/áudio), status "Em andamento", com as tags informadas (tags novas adicionadas à lista global de tags com cor azul padrão), e abrir a transcrição no Editor.
6. WHEN o título estiver vazio ao confirmar THEN o sistema SHALL impedir a criação e manter o modal aberto.
7. WHEN o usuário clica em "Cancelar" ou fora do modal THEN o sistema SHALL fechar o modal sem criar nada.
8. WHEN uma transcrição avulsa sem segmentos é aberta no Editor THEN o sistema SHALL exibir uma área de transcript vazia com mensagem de estado vazio (em vez de erro).

**Independent Test**: Criar uma pesquisa nova e uma transcrição avulsa nova, confirmar que aparecem corretamente na sidebar/dashboard com ícone/cor/tags escolhidos.

---

### P1: Upload e transcrição real de arquivo ⭐ MVP

**User Story**: Como pesquisadora, quero importar um arquivo de áudio/vídeo e obter uma transcrição real gerada pelo motor Whisper, para não precisar transcrever manualmente.

**Why P1**: É o núcleo funcional do produto — sem isso o app não gera valor real.

**Acceptance Criteria**:

1. WHEN o usuário arrasta um arquivo suportado (MP3, WAV, M4A, MP4, WEBM, OGG) sobre a zona de upload THEN o sistema SHALL exibir destaque visual da zona durante o arrasto e, ao soltar, mostrar nome e tamanho reais do arquivo selecionado.
2. WHEN o usuário clica na zona de upload THEN o sistema SHALL abrir um diálogo nativo de seleção de arquivo filtrado pelos formatos suportados.
3. WHEN um arquivo de formato não suportado é solto/selecionado THEN o sistema SHALL rejeitar o arquivo e exibir mensagem de erro, sem prosseguir.
4. WHEN um arquivo válido está selecionado THEN o sistema SHALL exibir o formulário com Idioma (Português BR/Español/English), Qualidade (Padrão/Alta), Locutores (Automático/Desativado) e Pesquisa (dropdown com "Nenhuma (avulsa)" + pesquisas existentes).
5. WHEN o usuário clica em "Iniciar transcrição" THEN o sistema SHALL: copiar o arquivo de mídia para a pasta de dados do app; criar o registro de transcrição com status "Em andamento"; iniciar o pipeline de transcrição em background (carregar/baixar modelo Whisper conforme Qualidade, extrair amostras de áudio a 16kHz via NAudio/ffmpeg, segmentar por silêncio, transcrever em paralelo usando o Idioma selecionado como idioma forçado); e navegar imediatamente para o Dashboard ou Editor mostrando o status "Em andamento".
6. WHEN a transcrição em background é concluída com sucesso THEN o sistema SHALL salvar os segmentos gerados (tempo início/fim, texto, locutor conforme opção "Automático"/"Desativado") no banco local, atualizar o status para "Concluída" e refletir isso no Dashboard/sidebar sem exigir reinício do app.
7. WHEN a transcrição falha (ex.: ffmpeg indisponível, modelo não pôde ser baixado, arquivo corrompido) THEN o sistema SHALL marcar a transcrição com status de erro visível, exibir mensagem descritiva do erro, e oferecer opção de tentar novamente.
8. WHEN o modelo Whisper necessário ainda não foi baixado localmente THEN o sistema SHALL baixá-lo automaticamente antes de transcrever, exibindo indicação de que o download está em andamento.
9. WHEN uma pesquisa é selecionada no formulário THEN a transcrição criada SHALL aparecer associada a essa pesquisa (sidebar e página da pesquisa); WHEN "Nenhuma (avulsa)" é selecionada THEN a transcrição SHALL aparecer apenas na lista geral do Dashboard.

**Independent Test**: Importar um arquivo de áudio real de poucos minutos, escolher Português/Padrão/Automático, iniciar, aguardar conclusão e verificar segmentos reais gerados no Editor.

---

### P1: Editor de transcrição — segmentos e edição de texto ⭐ MVP

**User Story**: Como pesquisadora, quero revisar e corrigir o texto transcrito, dividindo ou mesclando trechos quando necessário, para obter uma transcrição fiel e bem segmentada.

**Why P1**: Transcrição automática sempre precisa de revisão manual — esta é a funcionalidade central de "edição".

**Acceptance Criteria**:

1. WHEN o Editor é aberto para uma transcrição THEN o sistema SHALL exibir breadcrumb (Biblioteca → [pesquisa, se houver] → [ícone] título), ícone editável, título editável, meta (data, duração, nº de locutores), tags, barra de ferramentas (Dividir, Mesclar, Locutor, Exportar) e a lista de segmentos (tempo, locutor colorido, texto editável).
2. WHEN o usuário edita o texto de um segmento (contenteditable) e sai do campo THEN o sistema SHALL persistir o novo texto do segmento.
3. WHEN o usuário posiciona o cursor dentro do texto de um segmento e clica em "Dividir" THEN o sistema SHALL dividir o segmento em dois na posição do cursor, mantendo o mesmo tempo/locutor original no primeiro e criando um novo segmento (inserido logo após) com o texto restante; WHEN o cursor está no início ou fim do texto (uma das partes ficaria vazia) THEN o sistema SHALL não realizar a divisão.
4. WHEN o usuário clica em um segmento (não editando texto) THEN o sistema SHALL posicionar a reprodução do player no tempo de início daquele segmento.
5. WHEN o tempo de reprodução avança THEN o sistema SHALL destacar visualmente como "ativo" o último segmento cujo tempo de início seja ≤ tempo atual, e rolar a lista automaticamente para mantê-lo visível.
6. WHEN o usuário clica em "Mesclar" THEN o sistema SHALL concatenar o texto do segmento ativo (definido pela posição de playback) ao final do texto do segmento imediatamente anterior, removendo o segmento ativo da lista; WHEN não há segmento anterior (segmento ativo é o primeiro) THEN o sistema SHALL não realizar a ação.
7. WHEN o usuário edita o título (input) e sai do campo (blur) ou pressiona Enter THEN o sistema SHALL salvar o novo título e refletir a mudança na sidebar e no breadcrumb.
8. WHEN o usuário clica no ícone da transcrição THEN o sistema SHALL abrir um popup com grade de ícones e opção "Sem ícone"; WHEN um ícone é escolhido ou "Sem ícone" é confirmado THEN o sistema SHALL atualizar o ícone exibido no editor e na sidebar.

**Independent Test**: Abrir uma transcrição com segmentos, editar texto de um segmento, dividir um segmento em dois, mesclar dois segmentos de volta, renomear título e trocar ícone.

---

### P1: Atribuição manual de locutores ⭐ MVP

**User Story**: Como pesquisadora, quero atribuir manualmente quem fala em cada trecho, para identificar corretamente os participantes da entrevista.

**Why P1**: Sem identificação de locutor, o transcript perde valor analítico em pesquisas qualitativas.

**Acceptance Criteria**:

1. WHEN o usuário clica em "Locutor" na barra de ferramentas THEN o sistema SHALL abrir um dropdown listando todos os locutores já conhecidos (com cor + nome), marcando com um indicador o locutor do segmento ativo (por playback), e um campo para adicionar novo locutor.
2. WHEN o usuário seleciona um locutor da lista THEN o sistema SHALL atribuir esse locutor ao segmento ativo e fechar o dropdown.
3. WHEN o usuário digita um nome novo e confirma (botão "Adicionar" ou Enter) THEN o sistema SHALL criar um novo locutor com cor atribuída automaticamente (ciclo de paleta de cores), atribuí-lo ao segmento ativo, e disponibilizá-lo para os demais segmentos.
4. WHEN o usuário clica fora do dropdown THEN o sistema SHALL fechá-lo sem alterar nada.
5. WHEN não há segmento ativo (nenhuma reprodução iniciada) THEN o sistema SHALL desabilitar ou ignorar a ação de atribuição de locutor.

**Independent Test**: Com uma transcrição de 2+ locutores, reatribuir o locutor de um segmento e criar um locutor novo, verificando cor consistente em toda a transcrição.

---

### P1: Player de áudio/vídeo sincronizado ⭐ MVP

**User Story**: Como pesquisadora, quero ouvir o áudio original enquanto leio/edito a transcrição, com destaque automático do trecho correspondente, para conferir a fidelidade do texto.

**Why P1**: A revisão de transcrição depende diretamente de audição sincronizada com o texto.

**Acceptance Criteria**:

1. WHEN o Editor é aberto THEN o sistema SHALL exibir a barra do player (botão play/pause, tempo atual, barra de progresso clicável, tempo total, botão de velocidade, controle de volume) carregando o arquivo de mídia real associado à transcrição.
2. WHEN o usuário clica em play/pause THEN o sistema SHALL iniciar/pausar a reprodução real do áudio e atualizar o ícone do botão.
3. WHEN a reprodução avança THEN o sistema SHALL atualizar o tempo atual exibido e o preenchimento da barra de progresso a cada segundo (ou intervalo curto), e destacar o segmento correspondente (ver critério de "segmento ativo" da história do Editor).
4. WHEN o usuário clica em um ponto da barra de progresso THEN o sistema SHALL buscar (seek) a reprodução para o tempo correspondente àquele ponto.
5. WHEN o usuário clica no botão de velocidade THEN o sistema SHALL ciclar entre 1×, 1.25×, 1.5×, 2× e ajustar a velocidade real de reprodução, refletindo o valor no botão.
6. WHEN o usuário ajusta o controle de volume THEN o sistema SHALL ajustar o volume real da reprodução.
7. WHEN a reprodução chega ao final do arquivo THEN o sistema SHALL parar a reprodução e resetar o ícone do botão para "play".
8. WHEN o usuário clica em um segmento no transcript (ver Editor) THEN o player SHALL buscar (seek) para o tempo daquele segmento imediatamente.

**Independent Test**: Reproduzir um arquivo real, testar seek pela barra e por clique em segmento, alternar velocidade e volume, confirmar destaque de segmento acompanha a reprodução.

---

### P1: Tela de Gravação ao vivo (interativa, sem captura real) ⭐ MVP

**User Story**: Como pesquisadora, quero acessar uma tela de gravação com a mesma aparência e interações do protótipo, para manter fidelidade visual mesmo sem a funcionalidade real de captura implementada nesta fase.

**Why P1**: Faz parte da navegação principal (menu "Nova" e sidebar) e da fidelidade visual exigida ao protótipo.

**Acceptance Criteria**:

1. WHEN a tela de Gravação é aberta THEN o sistema SHALL exibir seletores de dispositivo (microfone e fonte de vídeo/áudio), timer "00:00", indicador de status ("Pronto para gravar"), botão de gravar (círculo vermelho), forma de onda (barras) e nenhuma seção de transcrição ao vivo visível.
2. WHEN o usuário clica no botão de gravar THEN o sistema SHALL: iniciar contagem do timer a cada segundo; exibir botões de pausar e parar; mudar o ícone do botão principal para "parar"; ativar o indicador visual "ao vivo" (ponto vermelho pulsante) e o texto "Gravando…"; exibir a seção de transcrição ao vivo com frases de exemplo aparecendo periodicamente; animar as barras da forma de onda.
3. WHEN o usuário clica em pausar durante a gravação THEN o sistema SHALL pausar a contagem do timer e mudar o status para "Pausado", sem parar a animação de forma definitiva; clicar novamente retoma.
4. WHEN o usuário clica em parar THEN o sistema SHALL resetar timer, forma de onda e status, esconder botões de pausar/parar, e após um breve intervalo navegar para a tela de Editor.
5. WHEN esta tela é utilizada, nenhum áudio real SHALL ser capturado nem nenhum arquivo de mídia real SHALL ser criado (comportamento mockado, consistente com o protótipo).

**Independent Test**: Iniciar, pausar, retomar e parar uma "gravação" mockada, confirmando timer, forma de onda e navegação final para o editor.

---

### P1: Configurações ⭐ MVP

**User Story**: Como pesquisadora, quero configurar meu perfil e preferências padrão de transcrição, para não precisar reconfigurar a cada upload.

**Why P1**: Preferências (idioma padrão, identificação de locutores) afetam diretamente o fluxo de upload, que é P1.

**Acceptance Criteria**:

1. WHEN a tela de Configurações é aberta THEN o sistema SHALL exibir seção "Perfil" (Nome, E-mail, Instituição), seção "Transcrição" (Idioma padrão, toggle "Identificar locutores", toggle "Transcrição ao vivo") e seção "Motor de transcrição" (Dispositivo: Automático/CPU/CUDA/Vulkan).
2. WHEN o usuário edita um campo de perfil e sai do campo THEN o sistema SHALL persistir o novo valor.
3. WHEN o usuário altera o toggle "Identificar locutores" THEN o sistema SHALL persistir a preferência e usá-la como valor padrão do campo "Locutores" no formulário de Upload.
4. WHEN o usuário altera o Idioma padrão THEN o sistema SHALL usá-lo como valor pré-selecionado no formulário de Upload.
5. WHEN o usuário altera o toggle "Transcrição ao vivo" THEN o sistema SHALL persistir o valor, sem efeito funcional nesta versão (feature de gravação real fora de escopo).
6. WHEN o usuário altera o Dispositivo de execução THEN o sistema SHALL usar essa preferência na próxima transcrição iniciada; WHEN "Automático" está selecionado THEN o sistema SHALL detectar e usar o melhor dispositivo disponível (CUDA > Vulkan > CPU).

**Independent Test**: Alterar cada configuração, reabrir o app, e confirmar que os valores persistem e que Idioma/Locutor padrão aparecem pré-selecionados num novo upload.

---

### P1: Persistência de dados e armazenamento de mídia ⭐ MVP

**User Story**: Como pesquisadora, quero que meu trabalho seja salvo automaticamente e sobreviva a reinícios do app, para não perder transcrições e edições.

**Why P1**: Sem persistência real, o app não é utilizável além de uma demonstração.

**Acceptance Criteria**:

1. WHEN qualquer dado (pesquisa, transcrição, tag, locutor, segmento, configuração) é criado ou alterado THEN o sistema SHALL persisti-lo no banco SQLite local imediatamente (ou em lote de curto intervalo), sem exigir ação explícita de "salvar".
2. WHEN um arquivo de mídia é importado ou associado a uma transcrição THEN o sistema SHALL copiá-lo para a pasta de dados do app (`%AppData%\Transcriba\media`) antes de referenciá-lo internamente.
3. WHEN o app é reiniciado THEN o sistema SHALL restaurar integralmente sidebar, dashboard, pesquisas, tags, transcrições e segmentos a partir do banco local.
4. WHEN o arquivo de mídia original (fora da pasta de dados do app) é movido ou apagado pelo usuário após a cópia THEN o sistema SHALL continuar funcionando normalmente (reproduzindo a cópia local).

**Independent Test**: Criar dados, fechar e reabrir o app, confirmar que tudo permanece; mover/apagar o arquivo original importado e confirmar que a reprodução continua funcionando.

---

### P2: Exportação de transcrição

**User Story**: Como pesquisadora, quero exportar a transcrição em TXT ou legendas SRT/VTT, para usar em outros softwares ou anexar à minha pesquisa.

**Why P2**: Importante para o fluxo de trabalho acadêmico, mas o app já é utilizável (visualização/edição na própria UI) sem isso.

**Acceptance Criteria**:

1. WHEN o usuário clica em "Exportar" na barra de ferramentas do Editor THEN o sistema SHALL exibir opções de formato: TXT, SRT, VTT.
2. WHEN o usuário escolhe TXT THEN o sistema SHALL gerar um arquivo no mesmo formato de saída do `transcrever.cs` (cabeçalho com metadados + linhas `[inicio -> fim] texto`) e permitir escolher local de salvamento.
3. WHEN o usuário escolhe SRT ou VTT THEN o sistema SHALL gerar um arquivo de legenda válido no formato escolhido, numerado sequencialmente, com timestamps no formato apropriado a cada padrão, incluindo o nome do locutor no início do texto de cada cue.
4. WHEN a transcrição não possui segmentos THEN o sistema SHALL desabilitar ou avisar que não há conteúdo para exportar.

**Independent Test**: Exportar uma transcrição com múltiplos locutores nos 3 formatos e validar a estrutura de cada arquivo gerado.

---

### P2: Exclusão de pesquisas e transcrições

**User Story**: Como pesquisadora, quero excluir pesquisas ou transcrições que não preciso mais, para manter minha biblioteca organizada.

**Why P2**: Não existe no protótipo (é uma extensão de CRUD básico necessária para uso real), mas o MVP funciona sem isso no primeiro uso.

**Acceptance Criteria**:

1. WHEN o usuário aciona a exclusão de uma transcrição (menu de contexto no card/lista) THEN o sistema SHALL pedir confirmação e, se confirmado, remover a transcrição, seus segmentos e o arquivo de mídia copiado.
2. WHEN o usuário aciona a exclusão de uma pesquisa THEN o sistema SHALL pedir confirmação informando quantas transcrições estão associadas, e ao confirmar SHALL desassociar (não excluir) as transcrições, mantendo-as na lista geral como avulsas.
3. WHEN a exclusão é cancelada no diálogo de confirmação THEN o sistema SHALL não alterar nenhum dado.

**Independent Test**: Excluir uma transcrição e verificar remoção do dashboard/sidebar/disco; excluir uma pesquisa com transcrições e verificar que elas permanecem como avulsas.

---

## Edge Cases

- WHEN o usuário tenta iniciar upload sem selecionar arquivo THEN o sistema SHALL manter o botão "Iniciar transcrição" desabilitado ou exibir erro de validação.
- WHEN dois uploads são iniciados em sequência antes do primeiro terminar THEN o sistema SHALL processar ambos de forma independente em background, cada um com seu próprio status "Em andamento" no dashboard.
- WHEN o app é fechado enquanto uma transcrição está em andamento THEN, ao reabrir, o sistema SHALL marcar essa transcrição como erro/interrompida (não fica presa em "Em andamento" para sempre) e permitir tentar novamente.
- WHEN um segmento fica com texto vazio após edição manual THEN o sistema SHALL permitir salvar (segmento vazio é um estado válido, ex.: trecho inaudível), mas SHALL exibir algum indicador visual (ex.: placeholder "…") quando vazio.
- WHEN o usuário tenta mesclar o primeiro segmento da lista THEN o sistema SHALL ignorar a ação (não há segmento anterior).
- WHEN o usuário tenta dividir um segmento sem posicionar o cursor dentro de um texto de segmento (ex.: seleção fora da área) THEN o sistema SHALL ignorar a ação sem erro.
- WHEN uma tag é digitada com nome já existente (case-insensitive) no campo de tags do modal THEN o sistema SHALL reutilizar a tag existente em vez de criar duplicata.
- WHEN o título de uma pesquisa/transcrição é muito longo THEN o sistema SHALL truncar visualmente com reticências na sidebar (mesmo limite do protótipo, ~18 caracteres), mantendo o título completo em tooltips/telas de detalhe.
- WHEN nenhum locutor foi criado ainda e o usuário abre o dropdown "Locutor" THEN o sistema SHALL exibir a lista vazia mas permitir criar o primeiro locutor pelo campo "Novo locutor".
- WHEN o dispositivo de execução configurado (ex.: CUDA) não está disponível no momento de transcrever THEN o sistema SHALL fazer fallback automático para CPU e informar o usuário.

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| SHELL-01 | P1: Navegação e casca do aplicativo | Design | Pending |
| DASH-01 | P1: Biblioteca / Dashboard | Design | Pending |
| RESEARCH-01 | P1: Pesquisas / Teses | Design | Pending |
| NEWPAGE-01 | P1: Criar nova Pesquisa / Transcrição avulsa | Design | Pending |
| UPLOAD-01 | P1: Upload e transcrição real de arquivo | Design | Pending |
| EDITOR-01 | P1: Editor — segmentos e edição de texto | Design | Pending |
| SPEAKER-01 | P1: Atribuição manual de locutores | Design | Pending |
| PLAYER-01 | P1: Player de áudio/vídeo sincronizado | Design | Pending |
| REC-01 | P1: Tela de Gravação ao vivo (mockada) | Design | Pending |
| SETTINGS-01 | P1: Configurações | Design | Pending |
| DATA-01 | P1: Persistência de dados e mídia | Design | Pending |
| EXPORT-01 | P2: Exportação de transcrição | T48 | Implementing |
| CRUD-01 | P2: Exclusão de pesquisas e transcrições | T49, T50 | Implementing |

**ID format:** `[CATEGORY]-[NUMBER]`

**Status values:** Pending → In Design → In Tasks → Implementing → Verified

**Coverage:** 13 total, 0 mapeados a tasks ainda, 13 não mapeados ⚠️ (mapeamento ocorre na fase de Tasks)

---

## Success Criteria

- [ ] Todas as telas do protótipo (Dashboard, Pesquisa, Upload, Gravação, Editor, Configurações, modais) existem no app com fidelidade visual e de interação equivalente.
- [ ] Um arquivo de áudio real importado gera uma transcrição real (não mockada) em menos de um tempo proporcional razoável ao motor `transcrever.cs`, visível e editável no Editor.
- [ ] Dados sobrevivem ao fechamento/reabertura do app (persistência real).
- [x] Exportação em TXT/SRT/VTT produz arquivos válidos e abríveis por outras ferramentas.
- [ ] Zero funcionalidades do protótipo ficam sem equivalente no app (fidelidade total), exceto os itens listados em "Out of Scope".
