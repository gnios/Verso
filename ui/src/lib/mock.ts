import type {
  DashboardData,
  EditorData,
  FileDescription,
  FolderData,
  NavigationParam,
  SettingsData,
  SidebarData,
  TranscriptionCard,
  UploadFormData,
} from '../types';

/**
 * Backend mock para o navegador (npm run dev sem o app desktop): espelha os métodos
 * registrados no VersoBridge (C#) com dados plausíveis, para o design ser navegável.
 */

let themeDark = false;

const seedCards: TranscriptionCard[] = [
  {
    id: '8e60d1a2-0000-0000-0000-000000000001',
    title: 'Entrevista com Profa. Helena Martins',
    icon: '🎙️',
    preview: '…e então a senhora considera que a reforma alterou a dinâmica das aulas?',
    status: 'Done',
    statusLabel: 'Concluída',
    errorMessage: null,
    duration: 'Áudio · 48 min',
    durationSeconds: 2880,
    date: '12 nov 2026',
    progressPercent: null,
    progressStage: '',
    estimatedTimeLabel: null,
    isInProgress: false,
    isDone: true,
    isError: false,
    canRetry: false,
    canDelete: true,
    canCancel: false,
    tags: [
      { name: 'entrevista', colorName: 'blue' },
      { name: 'urgente', colorName: 'red' },
    ],
  },
  {
    id: '8e60d1a2-0000-0000-0000-000000000002',
    title: 'Aula 07 — Análise de discurso',
    icon: '📚',
    preview: 'Transcrevendo… 62%',
    status: 'InProgress',
    statusLabel: 'Em andamento',
    errorMessage: null,
    duration: 'Áudio · 1h 12min',
    durationSeconds: 4320,
    date: '',
    progressPercent: 62,
    progressStage: 'transcribing',
    estimatedTimeLabel: 'Estimativa · ~14 min',
    isInProgress: true,
    isDone: false,
    isError: false,
    canRetry: false,
    canDelete: true,
    canCancel: true,
    tags: [],
  },
  {
    id: '8e60d1a2-0000-0000-0000-000000000003',
    title: 'Reunião do grupo de pesquisa',
    icon: '👥',
    preview: 'Falha ao carregar o modelo de reconhecimento.',
    status: 'Error',
    statusLabel: 'Erro',
    errorMessage: 'Falha ao carregar o modelo de reconhecimento.',
    duration: 'Áudio · 34 min',
    durationSeconds: 2040,
    date: '',
    progressPercent: null,
    progressStage: '',
    estimatedTimeLabel: null,
    isInProgress: false,
    isDone: false,
    isError: true,
    canRetry: true,
    canDelete: true,
    canCancel: false,
    tags: [{ name: 'reunião', colorName: 'purple' }],
  },
];

const sidebar: SidebarData = {
  folders: [
    {
      id: 1,
      title: 'Pesquisa de mestrado',
      icon: '📁',
      colorHex: '#6366f1',
      transcriptionCount: 2,
      isExpanded: true,
      transcriptions: [
        { id: seedCards[0].id, title: 'Entrevista com Profa. Helena', icon: '🎙️', isDone: true, statusIcon: '' },
        { id: seedCards[3]?.id ?? seedCards[0].id, title: 'Depoimento — campo', icon: '🗂️', isDone: true, statusIcon: '' },
      ],
    },
    {
      id: 2,
      title: 'Aulas',
      icon: '📚',
      colorHex: '#0ea5e9',
      transcriptionCount: 1,
      isExpanded: false,
      transcriptions: [],
    },
  ],
  tags: [
    { id: 1, name: 'entrevista', colorName: 'blue', count: 2 },
    { id: 2, name: 'urgente', colorName: 'red', count: 1 },
  ],
  counts: { total: 12, inProgress: 2, done: 9, error: 1, unassigned: 3 },
  versionLabel: '1.0.0',
  update: { canUpdateNow: false, isReady: false, title: '', label: '' },
};

const models = [
  {
    value: 'Base',
    label: 'Rápido',
    sizeLabel: '~142 MB',
    speedHint: 'Mais rápido',
    description: 'Ideal para checar se o áudio está ok ou fazer uma primeira passagem.',
    whenToUse: 'Rascunho e revisão rápida',
  },
  {
    value: 'Standard',
    label: 'Equilibrado',
    sizeLabel: '~466 MB',
    speedHint: 'Tempo médio',
    description: 'Funciona bem na maioria das entrevistas, aulas e notas de campo.',
    whenToUse: 'Bom para a maioria das entrevistas',
  },
  {
    value: 'LargeV3Turbo',
    label: 'Preciso',
    sizeLabel: '~1,2 GB',
    speedHint: 'Mais lento · maior qualidade',
    description: 'Use quando for citar trechos na tese ou o áudio for difícil de entender.',
    whenToUse: 'Citação e análise fina',
  },
];

const uploadForm: UploadFormData = {
  languages: [
    { code: 'pt', label: 'Português (Brasil)' },
    { code: 'es', label: 'Español' },
    { code: 'en', label: 'English' },
  ],
  models,
  engines: [
    { value: 'Parakeet', label: 'Parakeet', hint: 'Padrão · rápido em CPU · pt-BR TAGARELA' },
    { value: 'Whisper', label: 'Whisper', hint: 'Mais idiomas · mais lento em CPU' },
  ],
  parakeetModels: [
    { value: 'PtBrTagarela', label: 'pt-BR TAGARELA', sizeLabel: '~80 MB' },
    { value: 'MultilingualV3', label: 'TDT v3 (pt/en/es)', sizeLabel: '~1,2 GB' },
  ],
  icons: ['🎤', '🎙️', '📝', '📖', '📋', '🗣️', '🔊', '🎧', '💡', '🔬', '💬', '🎥', '📊', '🗂️', '📑', '✍️', '📞', '🔍'],
  folders: [
    { id: null, name: 'Nenhuma pasta', icon: '' },
    { id: 1, name: 'Pesquisa de mestrado', icon: '📁' },
    { id: 2, name: 'Aulas', icon: '📚' },
  ],
  defaults: {
    language: 'pt',
    quality: 'Standard',
    engine: 'Parakeet',
    parakeetModel: 'PtBrTagarela',
    selectedFolderId: null,
  },
  formats: 'MP3, WAV, M4A, MP4, WEBM, OGG',
};

const editorDetail = (id: string): EditorData => ({
  id,
  title: 'Entrevista com Profa. Helena Martins',
  icon: '🎙️',
  status: 'Done',
  isInProgress: false,
  isDone: true,
  isError: false,
  errorMessage: null,
  meta: {
    date: '12 nov 2026',
    duration: '48 min',
    durationSeconds: 2880,
    processingTime: '12min 30s',
    speakerCount: 2,
    model: 'Equilibrado',
  },
  tags: [{ name: 'entrevista', colorName: 'blue' }],
  folder: { id: 1, title: 'Pesquisa de mestrado' },
  folders: uploadForm.folders,
  icons: uploadForm.icons,
  speakers: [
    { id: 'sp-1', name: 'Entrevistador', colorHex: '#2eaadc' },
    { id: 'sp-2', name: 'Profa. Helena', colorHex: '#4dab6f' },
  ],
  segments: [
    { id: 'sg-1', start: 0, end: 7.4, text: 'Boa tarde, professora. Obrigado por aceitar a entrevista.', speakerId: 'sp-1', sortOrder: 0 },
    { id: 'sg-2', start: 7.4, end: 19.1, text: 'Boa tarde. É um prazer participar da pesquisa.', speakerId: 'sp-2', sortOrder: 1 },
    { id: 'sg-3', start: 19.1, end: 34.8, text: 'Então, quando é que a senhora começou a se interessar por esses temas?', speakerId: 'sp-1', sortOrder: 2 },
    { id: 'sg-4', start: 34.8, end: 58.2, text: 'Foi ainda na graduação, por volta de 2003, quando fiz uma disciplina eletiva sobre políticas públicas.', speakerId: 'sp-2', sortOrder: 3 },
  ],
  mediaUrl: null,
  hasSegments: true,
});

const folderData: FolderData = {
  id: 1,
  title: 'Pesquisa de mestrado',
  description: 'Entrevistas da dissertação.',
  icon: '📁',
  colorName: 'blue',
  cards: seedCards.slice(0, 2),
  isEmpty: false,
};

let settingsData: SettingsData = {
  profile: { name: 'Eugen', email: '', institution: 'UFPR' },
  languages: uploadForm.languages,
  defaultLanguage: 'pt',
  liveTranscriptionEnabled: true,
  identifySpeakersDefault: true,
  devices: [
    { value: 'Auto', label: 'Automático' },
    { value: 'Cpu', label: 'Somente processador' },
    { value: 'Cuda', label: 'GPU NVIDIA' },
    { value: 'Vulkan', label: 'GPU (outras placas)' },
  ],
  device: 'Auto',
  models,
  defaultQuality: 'Standard',
  engines: uploadForm.engines,
  defaultEngine: 'Parakeet',
  parakeetModels: uploadForm.parakeetModels,
  defaultParakeetModel: 'PtBrTagarela',
  recommendation: { quality: 'Standard', reason: 'Com 16 GB de RAM, o perfil Equilibrado tem bom desempenho.', detectedRamGb: 16 },
  developer: {
    gpus: [{ name: 'NVIDIA GeForce RTX 3060', vendor: 'NVIDIA', driverVersion: '566.36', kindLabel: 'Dedicada', ramLabel: '12 GB' }],
    runtimePreference: 'CUDA → Vulkan → CPU',
    isRuntimeLoaded: false,
    loadedRuntime: '—',
    loadedBackend: '—',
    activeGpu: 'NVIDIA GeForce RTX 3060',
    activeGpuSource: 'nvidia-smi',
    logDirectory: 'C:\\Verso\\data\\logs',
    appVersion: '1.0.0',
    updateStatus: 'Sem canal de atualização (build de desenvolvimento).',
  },
};

function dashboardSnapshot(param: NavigationParam = {}): DashboardData {
  let cards = [...seedCards];
  if (param.statusFilter === 'Progress') cards = cards.filter((c) => c.isInProgress);
  if (param.statusFilter === 'Done') cards = cards.filter((c) => c.isDone);
  if (param.statusFilter === 'Error') cards = cards.filter((c) => c.isError);
  if (param.unassignedOnly) cards = cards.slice(0, 2);
  if (param.tagId === 1) cards = cards.filter((c) => c.tags.some((t) => t.name === 'entrevista'));
  return {
    activeStatusFilter: param.statusFilter ?? 'All',
    isEmpty: cards.length === 0,
    cards,
  };
}

export function mockInvoke(method: string, args?: unknown): Promise<unknown> {
  const param = (args ?? {}) as NavigationParam;

  switch (method) {
    case 'app.info':
      return Promise.resolve({ version: '0.1.0-dev (mock)', hasUpdateChannel: false });
    case 'theme.get':
      return Promise.resolve({ isDark: themeDark });
    case 'theme.toggle':
      themeDark = !themeDark;
      return Promise.resolve({ isDark: themeDark });
    case 'sidebar.load':
      return Promise.resolve(sidebar);
    case 'navigation.navigate':
      return Promise.resolve({ screen: param.screen });
    case 'dashboard.load':
      return Promise.resolve(dashboardSnapshot(param));
    case 'dashboard.search': {
      const text = (args as { searchText?: string })?.searchText ?? '';
      const snapshot = dashboardSnapshot();
      snapshot.cards = snapshot.cards.filter((c) =>
        c.title.toLowerCase().includes(text.toLowerCase()),
      );
      return Promise.resolve(snapshot);
    }
    case 'dashboard.action':
    case 'folder.action':
    case 'transcription.action': {
      const { action, transcriptionId } = args as { action?: string; transcriptionId?: string };
      if (action === 'delete') {
        const index = seedCards.findIndex((c) => c.id === transcriptionId);
        if (index >= 0) seedCards.splice(index, 1);
      }
      if (action === 'retry') {
        const card = seedCards.find((c) => c.id === transcriptionId);
        if (card) {
          card.status = 'InProgress';
          card.statusLabel = 'Em andamento';
          card.isInProgress = true;
          card.isError = false;
          card.canRetry = false;
          card.canCancel = true;
          card.errorMessage = null;
          card.progressPercent = 0;
          card.progressStage = 'loading';
          card.preview = 'Carregando modelo…';
        }
      }
      if (action === 'cancel') {
        const card = seedCards.find((c) => c.id === transcriptionId);
        if (card) {
          card.status = 'Error';
          card.statusLabel = 'Cancelada';
          card.isInProgress = false;
          card.canCancel = false;
          card.errorMessage = 'Cancelada';
          card.preview = 'Cancelada';
        }
      }
      return Promise.resolve(dashboardSnapshot());
    }
    case 'ui.confirm.respond':
      return Promise.resolve(null);
    case 'upload.pickFile':
      return Promise.resolve<FileDescription>({
        path: 'C:/mock/entrevista.wav',
        fileName: 'entrevista.wav',
        fileSize: '42,5 MB',
        error: null,
      });
    case 'upload.selectFile':
      return Promise.resolve<FileDescription>({
        path: (args as { path?: string }).path ?? null,
        fileName: 'gravacao.webm',
        fileSize: '1,2 MB',
        error: null,
      });
    case 'upload.load':
      return Promise.resolve(uploadForm);
    case 'upload.start':
      return Promise.resolve({ transcriptionId: 'mock-new' });
    case 'editor.load':
      return Promise.resolve(editorDetail(param.transcriptionId ?? 'sg-1'));
    case 'editor.saveText':
    case 'editor.assignSpeaker':
    case 'editor.renameSpeaker':
    case 'editor.updateMeta':
      return Promise.resolve(null);
    case 'editor.addSpeaker':
      return Promise.resolve({ id: `sp-${Date.now()}`, name: (args as { name?: string }).name, colorHex: '#9065b0' });
    case 'editor.split': {
      const { segmentId, text, caretIndex } = args as { segmentId: string; text: string; caretIndex: number };
      const detail = editorDetail('x');
      const segment = detail.segments.find((s) => s.id === segmentId) ?? detail.segments[0];
      const cut = Math.max(1, Math.min(caretIndex || Math.floor(text.length / 2), text.length - 1));
      return Promise.resolve({
        before: { ...segment, id: segmentId, text: text.slice(0, cut), end: segment.start + (segment.end - segment.start) / 2 },
        after: { ...segment, id: `sg-${Date.now()}`, text: text.slice(cut), start: segment.start + (segment.end - segment.start) / 2 },
      });
    }
    case 'editor.merge': {
      const { segmentId, text } = args as { segmentId: string; text: string };
      const detail = editorDetail('x');
      const segment = detail.segments.find((s) => s.id === segmentId) ?? detail.segments[0];
      return Promise.resolve({
        merged: { ...segment, id: 'sg-merged', text: `Texto anterior ${text}`, start: Math.max(0, segment.start - 10) },
        removedId: segmentId,
      });
    }
    case 'editor.export':
      return Promise.resolve({ path: 'C:/mock/transcricao.txt', error: null });
    case 'settings.load':
      return Promise.resolve(settingsData);
    case 'settings.update':
      settingsData = { ...settingsData, ...(args as Partial<SettingsData>) };
      return Promise.resolve(settingsData);
    case 'settings.verifyRuntime':
      return Promise.resolve({ message: 'Runtime já carregado: CUDA (backend cuda). A troca só passa a valer após reiniciar o aplicativo.' });
    case 'settings.openLogs':
      return Promise.resolve(null);
    case 'folder.load':
      return Promise.resolve(folderData);
    case 'folder.create':
      return Promise.resolve({ id: 99 });
    case 'folder.delete':
      return Promise.resolve({ deleted: true });
    case 'feedback.open':
      return Promise.resolve({ statusMessage: '' });
    case 'feedback.send':
      return Promise.resolve({ statusMessage: 'Feedback enviado com sucesso!', isSuccess: true });
    default:
      return Promise.reject(new Error(`Mock desconhecido: ${method}`));
  }
}
