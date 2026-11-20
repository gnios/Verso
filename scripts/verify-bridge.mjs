/* Verificação E2E da ponte native (JS⇄C#) e de todas as telas via CDP do WebView2.
 * Requer o app rodando com a porta CDP (scripts/run-ui-checks.ps1 cuida de tudo). */
import { fileURLToPath } from 'node:url';
import { connectPage, EDITOR_ID } from './lib/cdp.mjs';

async function main() {
  const page = await connectPage();
  const { ws, evaluate, call, navigate, sleep, domText, screenshot } = page;

  const results = [];
  const check = (name, condition, detail = '') => {
    results.push({ name, pass: !!condition, detail });
    if (!condition) {
      console.log(`  ✗ ${name} ${detail}`);
    }
  };

  // ---------------------------------------------------------------- telas

  console.log('== Dashboard ==');
  await navigate('Dashboard', {});
  await sleep(800);
  let text = await domText();
  check('dashboard: título', text.includes('Biblioteca'));
  // Pastas estão escondidas atrás de FOLDERS_ENABLED — a sidebar exibe Tags.
  check('dashboard: sidebar', text.includes('Nova transcrição') && text.includes('Tags'));

  console.log('== Biblioteca UI (tabela/cartões) ==');
  // A preferência fica no localStorage e sobrevive entre runs — se um run
  // anterior deixou "cartões", clica em Tabela antes de medir.
  await evaluate(`(() => {
    if (document.querySelector('[data-testid="transcription-table"]')) return;
    const el = [...document.querySelectorAll('[role=radio]')].find((e) =>
      ((e.getAttribute('aria-label') || '') + e.textContent).includes('Tabela'));
    el?.click();
  })()`);
  await sleep(700);
  // A lista padrão é a TABELA do Astryx; a linha-título é o botão que abre o
  // editor (carrega data-testid="transcription-card" — contrato do E2E).
  check('tabela: container', await evaluate(`!!document.querySelector('[data-testid="transcription-table"]')`));
  const rowCount = await evaluate(`document.querySelectorAll('.verso-row-open').length`);
  check('tabela: linhas', rowCount >= 1, `${rowCount}`);
  check(
    'tabela: testid nos itens',
    (await evaluate(`document.querySelectorAll('[data-testid="transcription-card"]').length`)) === rowCount,
  );
  check(
    'tabela: headers',
    await evaluate(`(() => {
      const h = [...document.querySelectorAll('th')].map((x) => x.textContent.trim());
      return h[0] === 'Transcrição' && h.includes('Status');
    })()`),
  );
  check(
    'tabela: sem scroll horizontal',
    await evaluate(`(() => {
      const s = document.querySelector('.verso-table-wrap > div');
      return s && s.scrollWidth <= s.clientWidth + 1;
    })()`),
  );

  // Toggle Tabela/Cartões: exercita o SegmentedControl + preferência persistida.
  await evaluate(`(() => {
    const el = [...document.querySelectorAll('[role=radio]')]
      .find((e) => ((e.getAttribute('aria-label') || '') + e.textContent).includes('Cartões'));
    el?.click();
  })()`);
  await sleep(700);
  check('cartões: grid', (await evaluate(`document.querySelectorAll('.verso-card').length`)) === rowCount);
  check('cartões: preferência', (await evaluate(`localStorage.getItem('verso.library.view')`)) === 'cards');
  await evaluate(`(() => {
    const el = [...document.querySelectorAll('[role=radio]')].find((e) =>
      ((e.getAttribute('aria-label') || '') + e.textContent).includes('Tabela'));
    el?.click();
  })()`);
  await sleep(700);
  check('tabela: volta', (await evaluate(`document.querySelectorAll('.verso-row-open').length`)) === rowCount);

  // Clique na linha abre o editor (fluxo principal da tabela).
  await evaluate(`document.querySelector('.verso-row-open')?.click()`);
  await sleep(1200);
  check('linha: abre editor', await evaluate(`!!document.querySelector('[data-testid="transcript-segments"]')`));
  await navigate('Dashboard', {});
  await sleep(700);

  console.log('== Upload ==');
  const uploadForm = await call('upload.load', {});
  check('upload.load: 3 perfis', uploadForm.models.length === 3);
  check('upload.load: motores', uploadForm.engines.length === 2);
  await navigate('Upload', {});
  await sleep(900);
  text = await domText();
  // Qualidade só se aplica ao Whisper — com Parakeet o card some da tela.
  const whisperDefault = uploadForm.defaults.engine === 'Whisper';
  check(
    'upload: perfis condicionais',
    whisperDefault ? text.includes('Qualidade da transcrição') : !text.includes('Qualidade da transcrição'),
  );
  check('upload: selecionar arquivo', text.includes('Selecionar arquivo') || text.includes('Selecione um arquivo'));
  // Título/Ícone/Tags ficam no fluxo principal (card Detalhes), não em "Mais opções".
  check('upload: detalhes no fluxo', text.includes('Detalhes') && text.includes('Título') && text.includes('Tags'));
  // Ícone é um dropdown estilo Notion: botão-emoji abre o popover com a grade.
  check(
    'upload: picker de emoji',
    await evaluate(`(() => {
      const emoji = /^[\\u{1F300}-\\u{1FAFF}\\u{2600}-\\u{27BF}]$/u;
      const trg = [...document.querySelectorAll('button')].find(
        (b) => emoji.test(b.innerText.trim()) && b.offsetWidth > 0,
      );
      if (!trg) return false;
      trg.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      trg.click();
      return true;
    })()`),
  );
  await sleep(600);
  check(
    'upload: grade de emoji visível',
    await evaluate(`(() => {
      const emoji = /^[\\u{1F300}-\\u{1FAFF}\\u{2600}-\\u{27BF}]$/u;
      const visiveis = [...document.querySelectorAll('button')].filter(
        (b) => emoji.test(b.innerText.trim()) && b.offsetWidth > 0 && b.offsetHeight > 0,
      );
      return visiveis.length >= 10; // trigger + grade do popover
    })()`),
  );
  await evaluate(`(() => {
    const emoji = /^[\\u{1F300}-\\u{1FAFF}\\u{2600}-\\u{27BF}]$/u;
    const item = [...document.querySelectorAll('button')].filter(
      (b) => emoji.test(b.innerText.trim()) && b.offsetWidth > 0,
    )[2];
    item?.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    item?.click();
  })()`);
  await sleep(500);
  // "Identificar locutores" está escondido — a funcionalidade ainda não funciona.
  await evaluate(`(() => {
    const tr = [...document.querySelectorAll('button, [role=button]')].find((b) => b.innerText.includes('Mais opções'));
    tr?.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true })); tr?.click();
  })()`);
  await sleep(600);
  text = await domText();
  check('upload: sem locutores', !text.includes('Identificar locutores'));
  // Trocar o Motor para Whisper faz o card de qualidade aparecer dinamicamente.
  if (!whisperDefault) {
    await evaluate(`(() => {
      const labels = [...document.querySelectorAll('label, span')].filter((e) => e.textContent.trim() === 'Motor');
      const field = labels[labels.length - 1]?.closest('.astryx-field') ?? labels[labels.length - 1]?.parentElement?.parentElement;
      const btn = field?.querySelector('button, [role=combobox]');
      btn?.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true })); btn?.click();
    })()`);
    await sleep(500);
    await evaluate(`(() => {
      const item = [...document.querySelectorAll('[role=option]')].find((e) => e.innerText.trim().startsWith('Whisper'));
      item?.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true })); item?.click();
    })()`);
    await sleep(700);
    text = await domText();
    check('upload: perfis com Whisper', text.includes('Qualidade da transcrição') && text.includes('Equilibrado'));
  }

  console.log('== Gravação ==');
  await navigate('Recording', {});
  await sleep(700);
  text = await domText();
  check('gravação: título', text.includes('Gravação'));
  check('gravação: botão gravar', text.includes('Gravar'));

  console.log('== Configurações ==');
  const settings = await call('settings.load', {});
  check('settings.load: dispositivos', settings.devices.length >= 4);
  check('settings.load: recomendação', !!settings.recommendation.quality);
  check('settings.load: dev info', 'appVersion' in settings.developer);
  await navigate('Settings', {});
  await sleep(900);
  text = await domText();
  check('settings: perfil', text.includes('Perfil'));
  // "Perfil de precisão" só existe com motor Whisper — Parakeet o esconde.
  check(
    'settings: precisão (só Whisper)',
    settings.defaultEngine === 'Whisper'
      ? text.includes('Perfil de precisão')
      : !text.includes('Perfil de precisão'),
  );
  check('settings: avançado', text.includes('Avançado'));
  const updated = await call('settings.update', { institution: 'E2E Teste' });
  check('settings.update: persistiu', updated.profile.institution === 'E2E Teste');

  console.log('== Pasta ==');
  const created = await call('folder.create', { title: 'E2E Pasta Teste', icon: '🧪', colorName: 'teal' });
  check('folder.create', created.id > 0);
  const folder = await call('folder.load', { folderId: created.id });
  check('folder.load: título', folder.title === 'E2E Pasta Teste');
  await navigate('Folder', { folderId: created.id });
  await sleep(900);
  text = await domText();
  check('folder: página', text.includes('E2E Pasta Teste'));
  // delete pede confirmação (BridgeConfirmationService → diálogo React) — clica em Confirmar.
  const deletePromise = call('folder.delete', { folderId: created.id });
  await sleep(1200);
  const clicked = await evaluate(`(() => {
    const btn = [...document.querySelectorAll('button')].find((b) => b.innerText.trim() === 'Confirmar');
    if (btn) { btn.click(); return true; }
    return false;
  })()`);
  const deleted = await deletePromise;
  check('folder.delete: diálogo', clicked);
  check('folder.delete', deleted.deleted === true);

  console.log('== Editor ==');
  const editor = await call('editor.load', { transcriptionId: EDITOR_ID });
  check('editor.load: título', editor.title === 'E2E — Entrevista de teste');
  check('editor.load: 3 segmentos', editor.segments.length === 3);
  check('editor.load: 2 locutores', editor.speakers.length === 2);
  check('editor.load: mediaUrl do seed', typeof editor.mediaUrl === 'string' && editor.mediaUrl.includes('path='));

  await navigate('Editor', { transcriptionId: EDITOR_ID });
  await sleep(1000);
  text = await domText();
  check('editor: título na página', text.includes('E2E — Entrevista de teste'));
  check('editor: segmentos visíveis', text.includes('Boa tarde, doutora'));
  check('editor: locutor', text.includes('Entrevistador') && text.includes('Dra. Ana'));
  // Export ficou num DropdownMenu "Baixar" — abre e confere os itens.
  check('editor: botão baixar', text.includes('Baixar'));
  await evaluate(`(() => {
    const btn = [...document.querySelectorAll('button')].find((b) => b.innerText.trim().includes('Baixar'));
    btn?.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    btn?.click();
  })()`);
  await sleep(900);
  text = await domText();
  check('editor: export menu', text.includes('TXT') && text.includes('SRT') && text.includes('VTT'));
  await evaluate(`document.activeElement?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))`);
  await sleep(300);
  // Layout: título editável + botões de baixar no topo; player em superfície própria.
  check('editor: superfície do player', await evaluate(`!!document.querySelector('.verso-player')`));
  check(
    'editor: título acima do player',
    await evaluate(`(() => {
      const t = document.querySelector('[data-testid=editor-title]');
      const p = document.querySelector('[data-testid=player-play]');
      return t && p && t.getBoundingClientRect().top < p.getBoundingClientRect().top;
    })()`),
  );

  // Ações do editor: saveText, split, merge, assignSpeaker, updateMeta.
  const first = editor.segments[0];
  await call('editor.saveText', { segmentId: first.id, text: first.text + ' (editado)' });
  const reloaded = await call('editor.load', { transcriptionId: EDITOR_ID });
  check('editor.saveText', reloaded.segments[0].text.endsWith('(editado)'));

  const split = await call('editor.split', {
    transcriptionId: EDITOR_ID,
    segmentId: reloaded.segments[1].id,
    text: reloaded.segments[1].text,
    caretIndex: 10,
  });
  // SplitAtCaret faz Trim() nas metades — a soma pode perder o espaço do ponto de corte.
  check(
    'editor.split: antes/depois',
    split &&
      split.before.text === reloaded.segments[1].text.slice(0, 10).trim() &&
      split.after.text === reloaded.segments[1].text.slice(10).trim(),
  );
  const afterSplit = await call('editor.load', { transcriptionId: EDITOR_ID });
  check('editor.split: persistiu 4', afterSplit.segments.length === 4);

  const merge = await call('editor.merge', {
    transcriptionId: EDITOR_ID,
    segmentId: afterSplit.segments[2].id,
    text: afterSplit.segments[2].text,
  });
  const afterMerge = await call('editor.load', { transcriptionId: EDITOR_ID });
  check('editor.merge: voltou a 3', afterMerge.segments.length === 3);

  await call('editor.assignSpeaker', { segmentId: afterMerge.segments[0].id, speakerId: editor.speakers[1].id });
  const afterAssign = await call('editor.load', { transcriptionId: EDITOR_ID });
  check('editor.assignSpeaker', afterAssign.segments[0].speakerId === editor.speakers[1].id);

  const newSpeaker = await call('editor.addSpeaker', { transcriptionId: EDITOR_ID, name: 'Locutor E2E' });
  check('editor.addSpeaker', newSpeaker.id.length > 10);

  await call('editor.updateMeta', { transcriptionId: EDITOR_ID, title: 'E2E — Entrevista renomeada', tags: ['e2e', 'teste'] });
  const afterMeta = await call('editor.load', { transcriptionId: EDITOR_ID });
  check('editor.updateMeta: título', afterMeta.title === 'E2E — Entrevista renomeada');
  check('editor.updateMeta: tags', afterMeta.tags.length === 2);

  // Screenshot final (editor).
  if (await screenshot(fileURLToPath(new URL('./verso-react-editor.png', import.meta.url)))) {
    console.log('SCREENSHOT: scripts/verso-react-editor.png');
  }

  // Volta para o dashboard (deixa o app em estado útil).
  await navigate('Dashboard', {});
  await sleep(500);

  const failed = results.filter((r) => !r.pass);
  console.log(`\nRESULTADO: ${results.length - failed.length}/${results.length} checks OK`);
  if (failed.length > 0) {
    console.log('FALHAS:', failed.map((f) => f.name).join(', '));
    process.exitCode = 1;
  }

  ws.close();
}

main().catch((err) => {
  console.error('FALHOU:', err.message);
  process.exit(1);
});
