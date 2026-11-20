import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Badge } from '@astryxdesign/core/Badge';
import { Button } from '@astryxdesign/core/Button';
import { Card } from '@astryxdesign/core/Card';
import { DropdownMenu } from '@astryxdesign/core/DropdownMenu';
import { HStack } from '@astryxdesign/core/HStack';
import { Heading } from '@astryxdesign/core/Heading';
import { Icon } from '@astryxdesign/core/Icon';
import { IconButton } from '@astryxdesign/core/IconButton';
import { Popover } from '@astryxdesign/core/Popover';
import { Selector } from '@astryxdesign/core/Selector';
import { Text } from '@astryxdesign/core/Text';
import { TextInput } from '@astryxdesign/core/TextInput';
import { VStack } from '@astryxdesign/core/VStack';
import { invoke, onEvent } from '../lib/bridge';
import { FOLDERS_ENABLED } from '../lib/features';
import type { NavigationParam } from '../lib/screens';
import type {
  CardTag,
  EditorData,
  SidebarData,
  EditorSegment,
  EditorSpeaker,
  QueueStatusEvent,
  SegmentMergeResult,
  SegmentSplitResult,
} from '../types';
import {
  DownloadIcon,
  MergeIcon,
  PauseIcon,
  PlayIcon,
  PlusIcon,
  ScissorsIcon,
  UserIcon,
  CheckCircleIcon,
} from '../icons';

interface EditorPageProps {
  parameter: NavigationParam;
  isDark: boolean;
}

const TranscriptionEmojiPicker = lazy(() => import('../components/TranscriptionEmojiPicker'));

const TAG_COLORS = {
  blue: 'Azul', cyan: 'Ciano', green: 'Verde', orange: 'Laranja', pink: 'Rosa',
  purple: 'Roxo', teal: 'Verde-azulado', yellow: 'Amarelo', red: 'Vermelho',
} as const;

function tagVariant(color: string): keyof typeof TAG_COLORS {
  return Object.hasOwn(TAG_COLORS, color) ? color as keyof typeof TAG_COLORS : 'blue';
}

function PickerOption({ label, color, selected, onClick }: {
  label: string;
  color?: string;
  selected: boolean;
  onClick: () => void;
}) {
  return (
    <button className="editor-picker-option" type="button" aria-pressed={selected} onClick={onClick}>
      <span className="seg-speaker-dot" style={{ background: color }} aria-hidden="true" />
      <span>{label}</span>
      {selected && <Icon icon={CheckCircleIcon} size="sm" />}
    </button>
  );
}

function formatTime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) {
    return '00:00';
  }

  const minutes = Math.floor(seconds / 60);
  const rest = Math.floor(seconds % 60);
  return `${String(minutes).padStart(2, '0')}:${String(rest).padStart(2, '0')}`;
}

// Port de SegmentEditingService.JoinTexts/CaretAfterJoin (Verso.Core): junta como
// parágrafos do Word — um espaço só quando ambos têm conteúdo e a borda não tem whitespace.
function joinTexts(left: string, right: string): string {
  if (left.length === 0) {
    return right;
  }
  if (right.length === 0) {
    return left;
  }
  return /\s$/.test(left) || /^\s/.test(right) ? left + right : `${left} ${right}`;
}

function caretAfterJoin(left: string, right: string): number {
  return Math.max(0, joinTexts(left, right).length - right.length);
}

// Waveform "fantasma" determinístico — preenche o canvas antes do primeiro
// play. Os picos reais são lazy (buscá-los na abertura quebraria o contrato
// E2E: abrir o editor não pode tocar o servidor de mídia), e a área vazia
// de 64px parecia bug na tela.
const GHOST_PEAKS = Array.from({ length: 90 }, (_, i) => {
  const wave = Math.abs(Math.sin(i * 0.9) * 0.55 + Math.sin(i * 0.31) * 0.45);
  return 0.1 + wave * 0.32;
});

/** Waveform via WebAudio: decodifica o áudio e desenha picos; clique busca.
 *  O fetch da mídia é lazy (só após o primeiro play) — contrato do LocalMediaServer
 *  validado pelo E2E: abrir o editor não pode baixar o arquivo. */
function WaveformCanvas({
  mediaUrl,
  loadPeaks,
  duration,
  expectedDuration,
  segments,
  speakers,
  onSeek,
}: {
  mediaUrl: string;
  loadPeaks: boolean;
  duration: number;
  expectedDuration: number;
  segments: EditorSegment[];
  speakers: EditorSpeaker[];
  onSeek: (seconds: number) => void;
}) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const [peaks, setPeaks] = useState<number[] | null>(null);
  const peaksRequestedRef = useRef(false);

  // Reset ao trocar de mídia (declarado antes: roda antes do efeito de carga).
  useEffect(() => {
    peaksRequestedRef.current = false;
    setPeaks(null);
  }, [mediaUrl]);

  // Picos calculados no servidor (ffmpeg, streaming 8kHz, memória constante) —
  // UMA request por mídia. Antes, o efeito dependia de currentTime e cada seek
  // disparava outro fetch → N ffmpegs concorrentes afogavam o servidor de mídia
  // e o <audio> morria de fome ("parou de reproduzir").
  useEffect(() => {
    if (!loadPeaks || peaksRequestedRef.current) {
      return;
    }

    peaksRequestedRef.current = true;
    let cancelled = false;
    void (async () => {
      try {
        const response = await fetch(
          `${mediaUrl}&peaks=600&duration=${encodeURIComponent(String(expectedDuration || 0))}`,
        );
        if (!response.ok) {
          return;
        }

        const payload = (await response.json()) as { peaks?: number[] };
        if (!cancelled && payload.peaks?.length) {
          setPeaks(payload.peaks);
        }
      } catch {
        // Sem waveform (endpoint/formato falhou) — só a barra de progresso.
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [mediaUrl, loadPeaks, expectedDuration]);

  useEffect(() => {
    const draw = () => {
      const canvas = canvasRef.current;
      if (!canvas) {
        return;
      }

      const rect = canvas.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      canvas.width = Math.max(1, Math.round(rect.width * dpr));
      canvas.height = Math.max(1, Math.round(rect.height * dpr));
      const context = canvas.getContext('2d');
      if (!context) {
        return;
      }

      const styles = getComputedStyle(canvas);
      // Read resolved colors so the canvas receives RGB rather than light-dark().
      const green = styles.color;
      const speakerColors = new Map(speakers.map((speaker) => [speaker.id, speaker.colorHex]));
      const timelineDuration = duration || expectedDuration || Math.max(1, segments.at(-1)?.end ?? 0);
      context.clearRect(0, 0, canvas.width, canvas.height);

      // Sem picos reais ainda (antes do play ou fetch em andamento): desenha
      // o fantasma com alpha menor — preenche a área sem fingir ser dado real.
      const sourceBars = peaks ?? GHOST_PEAKS;
      // Mantém a waveform esparsa e colorida como a prévia do site, mesmo que
      // o endpoint entregue centenas de picos para permitir buscas precisas.
      const intervals = segments.length ? segments : [{ start: 0, end: timelineDuration, speakerId: null }];
      context.globalAlpha = peaks ? 1 : 0.75;
      for (const segment of intervals) {
        const start = Math.max(0, segment.start) / timelineDuration;
        const end = Math.min(timelineDuration, segment.end) / timelineDuration;
        if (end <= start) continue;

        const segmentWidth = (end - start) * canvas.width;
        const barCount = Math.max(1, Math.round(segmentWidth / (5 * dpr)));
        const barWidth = segmentWidth / barCount;
        const strokeWidth = Math.min(2 * dpr, barWidth * 0.75);
        context.fillStyle = (segment.speakerId && speakerColors.get(segment.speakerId)) || green;

        for (let i = 0; i < barCount; i++) {
          const barStart = start + (end - start) * i / barCount;
          const barEnd = start + (end - start) * (i + 1) / barCount;
          const firstPeak = Math.floor(barStart * sourceBars.length);
          const lastPeak = Math.max(firstPeak + 1, Math.ceil(barEnd * sourceBars.length));
          const peak = Math.max(0, ...sourceBars.slice(firstPeak, lastPeak));
          const height = Math.max(2 * dpr, peak * canvas.height * 0.9);
          const x = barStart * canvas.width + (barWidth - strokeWidth) / 2;
          context.fillRect(x, (canvas.height - height) / 2, strokeWidth, height);
        }
      }
      context.globalAlpha = 1;
    };

    const observer = new ResizeObserver(() => draw());
    const themeObserver = new MutationObserver(draw);
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
    if (canvasRef.current) {
      observer.observe(canvasRef.current);
    }
    draw();
    return () => {
      observer.disconnect();
      themeObserver.disconnect();
    };
  }, [duration, expectedDuration, peaks, segments, speakers]);

  const seek = (event: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    if (!canvas || duration <= 0) {
      return;
    }

    const rect = canvas.getBoundingClientRect();
    const ratio = (event.clientX - rect.left) / rect.width;
    onSeek(ratio * duration);
  };

  return (
    <canvas
      ref={canvasRef}
      className="verso-waveform"
      onClick={seek}
      style={{ width: '100%', height: 32, display: 'block', cursor: 'pointer' }}
    />
  );
}

export function EditorPage({ parameter, isDark }: EditorPageProps) {
  const [data, setData] = useState<EditorData | null>(null);
  const [segments, setSegments] = useState<EditorSegment[]>([]);
  const [speakers, setSpeakers] = useState<EditorSpeaker[]>([]);
  const [title, setTitle] = useState('');
  const [icon, setIcon] = useState<string | null>(null);
  const [emojiPickerOpen, setEmojiPickerOpen] = useState(false);
  const [savingIcon, setSavingIcon] = useState(false);
  const [tags, setTags] = useState<CardTag[]>([]);
  const [folderId, setFolderId] = useState<number | null>(null);
  const [allTags, setAllTags] = useState<CardTag[]>([]);
  const [tagPickerOpen, setTagPickerOpen] = useState(false);
  const [tagQuery, setTagQuery] = useState('');
  const [tagColor, setTagColor] = useState<string | null>(null);
  const [savingTags, setSavingTags] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [isPlaying, setIsPlaying] = useState(false);
  const [playbackRate, setPlaybackRate] = useState(1);
  const [mediaActivated, setMediaActivated] = useState(false);
  const [focusedSegmentId, setFocusedSegmentId] = useState<string | null>(null);
  const [activeSpeakerFor, setActiveSpeakerFor] = useState<string | null>(null);
  const [newSpeakerName, setNewSpeakerName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const textareaRefs = useRef(new Map<string, HTMLTextAreaElement>());
  const pendingFocus = useRef<{ segmentId: string; caret: number } | null>(null);

  const transcriptionId = parameter.transcriptionId;

  const load = useCallback(async () => {
    if (!transcriptionId) {
      return;
    }

    try {
      const detail = await invoke<EditorData>('editor.load', { transcriptionId });
      setData(detail);
      setSegments(detail.segments);
      setSpeakers(detail.speakers);
      setTitle(detail.title);
      setIcon(detail.icon);
      setTags(detail.tags);
      setFolderId(detail.folder.id);
      setCurrentTime(0);
      setDuration(detail.meta.durationSeconds);
      setPlaybackRate(1);
      void invoke<SidebarData>('sidebar.load')
        .then((side) => setAllTags(side.tags.map((tag) => ({ name: tag.name, colorName: tag.colorName }))))
        .catch(() => undefined);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Falha ao carregar a transcrição.');
    }
  }, [transcriptionId]);

  useEffect(() => {
    setData(null);
    setMediaActivated(false);
    void load();
    const offQueue = onEvent<QueueStatusEvent>('queue.status', (status) => {
      if (status.transcriptionId === transcriptionId
        && (status.status === 'Done' || status.status === 'Error')) {
        void load();
      }
    });
    return () => offQueue();
  }, [load, transcriptionId]);

  useEffect(() => {
    const pending = pendingFocus.current;
    if (!pending) {
      return;
    }

    const textarea = textareaRefs.current.get(pending.segmentId);
    if (textarea) {
      textarea.focus();
      textarea.setSelectionRange(pending.caret, pending.caret);
      pendingFocus.current = null;
    }
  }, [segments]);

  useEffect(() => {
    if (audioRef.current) {
      audioRef.current.playbackRate = playbackRate;
    }
  }, [playbackRate, data?.mediaUrl]);

  // Último segmento com start <= posição (mesmo critério de SegmentEditingService.GetActiveSegment).
  const activeSegmentId = useMemo(() => {
    let active: string | null = null;
    for (const segment of segments) {
      if (segment.start <= currentTime) {
        active = segment.id;
      } else {
        break;
      }
    }
    return active;
  }, [segments, currentTime]);

  // Sync áudio→texto: rola até o segmento ativo durante o playback (scrollIntoView
  // centralizado, como o versoEditor.scrollToSegmentById do Blazor).
  useEffect(() => {
    if (!isPlaying || !activeSegmentId) {
      return;
    }
    document
      .querySelector(`[data-segment-id="${activeSegmentId}"]`)
      ?.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }, [activeSegmentId, isPlaying]);

  const seek = (seconds: number) => {
    const audio = audioRef.current;
    if (audio) {
      audio.currentTime = seconds;
      setCurrentTime(seconds);
    }
  };

  const togglePlay = () => {
    const audio = audioRef.current;
    if (!audio) {
      return;
    }

    if (audio.paused) {
      setMediaActivated(true);
      // AbortError: um seek/play logo em seguida interrompe o play() pendente —
      // é esperado, não erro. Demais falhas: o estado real vem de onPlay/onPause.
      void audio.play().catch(() => undefined);
    } else {
      audio.pause();
    }
  };

  const commitTitle = async () => {
    const next = title.trim() || data?.title || '';
    setTitle(next);
    if (next === data?.title) return;
    try {
      await invoke('editor.updateMeta', { transcriptionId, title: next });
      setData((previous) => previous && { ...previous, title: next });
    } catch (err) {
      setTitle(data?.title ?? '');
      setError(err instanceof Error ? err.message : 'Falha ao salvar o título.');
    }
  };

  const commitIcon = async (nextIcon: string) => {
    setEmojiPickerOpen(false);
    if (savingIcon || nextIcon === icon) return;
    setSavingIcon(true);
    try {
      await invoke('editor.updateMeta', { transcriptionId, icon: nextIcon });
      setIcon(nextIcon);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Falha ao salvar o emoji.');
    } finally {
      setSavingIcon(false);
    }
  };

  const changeTagPicker = (open: boolean) => {
    setTagPickerOpen(open);
    setTagQuery('');
    setTagColor(null);
  };

  const commitTags = async (next: CardTag[], created?: CardTag) => {
    if (savingTags) return;
    setSavingTags(true);
    try {
      await invoke('editor.updateMeta', {
        transcriptionId,
        tags: next.map((tag) => tag.name),
        tagColors: created ? { [created.name]: created.colorName } : undefined,
      });
      setTags(next);
      if (created) setAllTags((previous) => [...previous, created]);
      changeTagPicker(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Falha ao salvar as tags.');
    } finally {
      setSavingTags(false);
    }
  };

  const query = tagQuery.trim().toLowerCase();
  const knownTags = new Map([...allTags, ...tags].map((tag) => [tag.name.toLowerCase(), tag]));
  const tagMatches = [...knownTags.values()].filter((tag) => tag.name.toLowerCase().includes(query));
  const canCreateTag = query.length > 0 && !knownTags.has(query);
  const createTag = () => {
    if (!canCreateTag || !tagColor) return;
    const created = { name: tagQuery.trim(), colorName: tagColor };
    void commitTags([...tags, created], created);
  };

  const commitFolder = (next: number | null) => {
    setFolderId(next);
    void invoke('editor.updateMeta', { transcriptionId, folderId: next });
  };

  const saveSegmentText = (segmentId: string, text: string) => {
    setSegments((previous) => previous.map((segment) => (segment.id === segmentId ? { ...segment, text } : segment)));
    void invoke('editor.saveText', { segmentId, text }).catch(() => undefined);
  };

  const splitSegment = async (segment: EditorSegment, caretIndex: number) => {
    const result = await invoke<SegmentSplitResult | null>('editor.split', {
      transcriptionId,
      segmentId: segment.id,
      text: segment.text,
      caretIndex,
    });
    if (!result) {
      return;
    }

    setSegments((previous) => {
      const index = previous.findIndex((item) => item.id === segment.id);
      if (index < 0) {
        return previous;
      }
      return [
        ...previous.slice(0, index),
        result.before,
        result.after,
        ...previous.slice(index + 1),
      ];
    });
    pendingFocus.current = { segmentId: result.after.id, caret: 0 };
  };

  const applyMergeResult = useCallback((merged: EditorSegment, removedId: string) => {
    setSegments((current) => {
      const removedIndex = current.findIndex((item) => item.id === removedId);
      if (removedIndex <= 0) {
        return current;
      }
      return [
        ...current.slice(0, removedIndex - 1),
        merged,
        ...current.slice(removedIndex + 1),
      ];
    });
  }, []);

  // Backspace no início / botão Mesclar: funde o segmento no ANTERIOR
  // (o anterior sobrevive e absorve o texto + intervalo do atual).
  const mergeSegment = async (segment: EditorSegment) => {
    const index = segments.findIndex((item) => item.id === segment.id);
    if (index <= 0) {
      return;
    }

    const previous = segments[index - 1];
    const joinCaret = caretAfterJoin(previous.text, segment.text);
    // O merge no backend junta previous.Text do banco — commita edições não salvas antes.
    await invoke('editor.saveText', { segmentId: previous.id, text: previous.text });
    const result = await invoke<SegmentMergeResult | null>('editor.merge', {
      transcriptionId,
      segmentId: segment.id,
      text: segment.text,
    });
    if (!result) {
      return;
    }

    applyMergeResult(result.merged, result.removedId);
    pendingFocus.current = { segmentId: result.merged.id, caret: joinCaret };
  };

  // Delete no fim do segmento: funde o PRÓXIMO neste (este sobrevive).
  const mergeNextIntoCurrent = async (segment: EditorSegment) => {
    const index = segments.findIndex((item) => item.id === segment.id);
    if (index < 0 || index >= segments.length - 1) {
      return;
    }

    const next = segments[index + 1];
    const joinCaret = caretAfterJoin(segment.text, next.text);
    await invoke('editor.saveText', { segmentId: segment.id, text: segment.text });
    const result = await invoke<SegmentMergeResult | null>('editor.merge', {
      transcriptionId,
      segmentId: next.id,
      text: next.text,
    });
    if (!result) {
      return;
    }

    applyMergeResult(result.merged, result.removedId);
    pendingFocus.current = { segmentId: result.merged.id, caret: joinCaret };
  };

  // Edição estilo documento único (port do versoEditor.shouldInterceptWordLikeKey +
  // WordLikeEditResolver do Blazor): teclas nas bordas do textarea agem no documento.
  const onSegmentKeyDown = (
    segment: EditorSegment,
    index: number,
    event: React.KeyboardEvent<HTMLTextAreaElement>,
  ) => {
    const element = event.currentTarget;
    if (event.ctrlKey || event.metaKey || event.altKey) {
      return;
    }

    const start = element.selectionStart;
    const end = element.selectionEnd;
    const length = element.value.length;
    const collapsed = start === end;
    const before = element.value.slice(0, start);
    const after = element.value.slice(end);
    const isFirstLine = !before.includes('\n');
    const isLastLine = !after.includes('\n');
    const column = start - (before.lastIndexOf('\n') + 1);
    const isFirst = index === 0;
    const isLast = index === segments.length - 1;

    const focus = (segmentId: string, caret: number) => {
      const target = textareaRefs.current.get(segmentId);
      if (!target) {
        return;
      }
      event.preventDefault();
      target.focus();
      target.setSelectionRange(caret, caret);
    };

    switch (event.key) {
      case 'Enter':
        if (!event.shiftKey) {
          event.preventDefault();
          void splitSegment(segment, start);
        }
        return;
      case 'Backspace':
        if (collapsed && start === 0 && !isFirst) {
          event.preventDefault();
          void mergeSegment(segment);
        }
        return;
      case 'Delete':
        if (collapsed && start === length && !isLast) {
          event.preventDefault();
          void mergeNextIntoCurrent(segment);
        }
        return;
      case 'ArrowLeft':
        if (collapsed && start === 0 && !isFirst) {
          focus(segments[index - 1].id, segments[index - 1].text.length);
        }
        return;
      case 'ArrowRight':
        if (collapsed && start === length && !isLast) {
          focus(segments[index + 1].id, 0);
        }
        return;
      case 'ArrowUp':
        if (isFirstLine && !isFirst) {
          focus(segments[index - 1].id, Math.min(column, segments[index - 1].text.length));
        }
        return;
      case 'ArrowDown':
        if (isLastLine && !isLast) {
          focus(segments[index + 1].id, Math.min(column, segments[index + 1].text.length));
        }
        return;
      default:
        return;
    }
  };

  const saveSpeaker = async (segmentId: string, speakerId: string | null, name?: string) => {
    try {
      if (name) {
        const speaker = await invoke<EditorSpeaker>('editor.addSpeaker', { transcriptionId, name });
        setSpeakers((previous) => previous.some((option) => option.id === speaker.id)
          ? previous
          : [...previous, speaker]);
        speakerId = speaker.id;
      }
      await invoke('editor.assignSpeaker', { segmentId, speakerId });
      setSegments((previous) =>
        previous.map((segment) => (segment.id === segmentId ? { ...segment, speakerId } : segment)),
      );
      setActiveSpeakerFor(null);
      setNewSpeakerName('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Falha ao atribuir o locutor.');
    }
  };

  const speakerPicker = (segment: EditorSegment, trigger: React.ReactNode) => (
    <span onClick={(event) => event.stopPropagation()}>
      <Popover
        label="Selecionar locutor"
        placement="below"
        alignment="start"
        width={274}
        padding={0}
        isOpen={activeSpeakerFor === segment.id}
        onOpenChange={(open) => {
          setActiveSpeakerFor(open ? segment.id : null);
          setNewSpeakerName('');
        }}
        content={(
          <div className="editor-picker">
            <div className="editor-picker-title">Selecionar locutor</div>
            <div className="editor-picker-list">
              {speakers.map((speaker) => (
                <PickerOption key={speaker.id} label={speaker.name} color={speaker.colorHex}
                  selected={segment.speakerId === speaker.id}
                  onClick={() => void saveSpeaker(segment.id, speaker.id)} />
              ))}
              <PickerOption label="Sem locutor" selected={!segment.speakerId}
                onClick={() => void saveSpeaker(segment.id, null)} />
            </div>
            <div className="editor-picker-create">
              <TextInput label="Novo locutor" isLabelHidden value={newSpeakerName} width="100%"
                onChange={setNewSpeakerName} placeholder="Novo locutor…"
                onEnter={() => {
                  if (newSpeakerName.trim()) void saveSpeaker(segment.id, null, newSpeakerName.trim());
                }} />
              <Button size="sm" variant="primary" label="Adicionar" isDisabled={!newSpeakerName.trim()}
                onClick={() => void saveSpeaker(segment.id, null, newSpeakerName.trim())} />
            </div>
          </div>
        )}
      >
        {trigger}
      </Popover>
    </span>
  );

  const exportAs = async (format: 'txt' | 'srt' | 'vtt' | 'rtf') => {
    try {
      const result = await invoke<{ path: string | null; error: string | null }>('editor.export', {
        transcriptionId,
        format,
      });
      setError(result.error);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Falha ao exportar.');
    }
  };

  if (!transcriptionId) {
    return (
      <VStack padding={6} align="start">
        <Heading level={1}>Editor</Heading>
        <Text color="secondary">Nenhuma transcrição selecionada.</Text>
      </VStack>
    );
  }

  if (!data) {
    return (
      <VStack padding={6} align="start">
        <Heading level={1}>Editor</Heading>
        <Text color="secondary">{error ?? 'Carregando…'}</Text>
      </VStack>
    );
  }

  const speakerById = new Map(speakers.map((speaker) => [speaker.id, speaker]));

  return (
    <VStack gap={0} align="start" height="100%">
      {data.mediaUrl ? (
        <audio
          ref={audioRef}
          src={data.mediaUrl}
          preload="none"
          onTimeUpdate={(event) => setCurrentTime(event.currentTarget.currentTime)}
          onLoadedMetadata={(event) => {
            setDuration(event.currentTarget.duration || data.meta.durationSeconds);
          }}
          onPlay={() => setIsPlaying(true)}
          onPause={() => setIsPlaying(false)}
          onError={(event) => {
            // Erro terminal do elemento (rede afogada, fetch abortado): play()
            // seguinte falharia para sempre — load() rearma e retoma a posição.
            const el = event.currentTarget;
            const wasPlaying = !el.paused;
            const position = el.currentTime;
            el.load();
            el.currentTime = position;
            if (wasPlaying) {
              void el.play().catch(() => undefined);
            }
          }}
        />
      ) : null}

      <div className="editor-scroll">
        <div className="editor-document">
          <div className="editor-title-row">
            <Popover
              label="Escolher emoji da transcrição"
              isOpen={emojiPickerOpen}
              onOpenChange={setEmojiPickerOpen}
              placement="below"
              alignment="start"
              width={340}
              padding={0}
              content={emojiPickerOpen && (
                <Suspense fallback={<div className="editor-emoji-loading" role="status">Carregando emojis…</div>}>
                  <TranscriptionEmojiPicker isDark={isDark} onSelect={(emoji) => void commitIcon(emoji)} />
                </Suspense>
              )}
            >
              <button type="button" className="editor-emoji-trigger" aria-label="Alterar emoji da transcrição"
                title="Alterar emoji da transcrição" disabled={savingIcon}>
                <span aria-hidden="true">{icon ?? '📝'}</span>
              </button>
            </Popover>
            <h1>
              <input className="editor-title" aria-label="Título da transcrição"
                value={title} onChange={(event) => setTitle(event.target.value)}
                onBlur={() => void commitTitle()}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') event.currentTarget.blur();
                }}
                data-testid="editor-title" />
            </h1>
          </div>
          <div className="editor-meta">
            <span>{data.meta.duration}</span>
            <span>{data.meta.model}</span>
            {data.meta.processingTime && <span>Processado em {data.meta.processingTime}</span>}
            {data.meta.speakerCount > 0 && <span>{data.meta.speakerCount} locutores</span>}
            {data.isInProgress && <Badge variant="info" label="Em andamento" />}
            {data.isError && <Badge variant="error" label={data.errorMessage ?? 'Erro'} />}
            <div className="editor-export">
              <DropdownMenu
                button={{
                  variant: 'ghost',
                  size: 'sm',
                  label: 'Baixar',
                  icon: <Icon icon={DownloadIcon} size="sm" />,
                  isDisabled: !data.hasSegments,
                }}
                menuWidth={240}
                items={[
                  { label: 'Texto (TXT)', onClick: () => void exportAs('txt') },
                  { label: 'Documento acadêmico (RTF)', onClick: () => void exportAs('rtf') },
                  { label: 'Legendas (SRT)', onClick: () => void exportAs('srt') },
                  { label: 'Legendas (VTT)', onClick: () => void exportAs('vtt') },
                ]}
              />
            </div>
          </div>
          <HStack className="editor-tags" gap={2} wrap="wrap" align="center">
            {tags.map((tag) => (
              <Badge key={tag.name} variant={tagVariant(tag.colorName)} label={tag.name} />
            ))}
            <Popover label="Selecionar tags" isOpen={tagPickerOpen}
              onOpenChange={changeTagPicker} placement="below" alignment="start" width={290} padding={0}
              content={
                <fieldset className="editor-picker" disabled={savingTags}>
                  <div className="editor-picker-title">Selecionar tags</div>
                  <div className="editor-picker-list">
                    {tagMatches.map((tag) => {
                      const selected = tags.some((item) => item.name.toLowerCase() === tag.name.toLowerCase());
                      return (
                        <PickerOption key={tag.name} label={tag.name} selected={selected}
                          color={`var(--color-text-${tagVariant(tag.colorName)})`}
                          onClick={() => void commitTags(selected
                            ? tags.filter((item) => item.name.toLowerCase() !== tag.name.toLowerCase())
                            : [...tags, tag])} />
                      );
                    })}
                  </div>
                  <div className="editor-picker-create">
                    <TextInput label="Buscar ou criar tag" isLabelHidden value={tagQuery}
                      onChange={setTagQuery} placeholder="Buscar ou criar tag…"
                      onEnter={createTag} width="100%" />
                    <Button size="sm" variant="primary" label="Criar"
                      isDisabled={!canCreateTag || !tagColor} onClick={createTag} />
                  </div>
                  {canCreateTag && (
                    <div className="editor-tag-colors" role="group" aria-label="Cor da nova tag">
                      {Object.entries(TAG_COLORS).map(([color, label]) => (
                        <button key={color} className="editor-tag-color" type="button" aria-label={label}
                          title={label} aria-pressed={tagColor === color} onClick={() => setTagColor(color)}>
                          <span style={{ background: `var(--color-text-${color})` }} />
                        </button>
                      ))}
                    </div>
                  )}
                </fieldset>
              }
            >
              <Button label="Tags" variant="ghost" size="sm" icon={<Icon icon={PlusIcon} />} />
            </Popover>
          </HStack>
            {FOLDERS_ENABLED && (
              <Selector
                label="Pasta"
                options={data.folders.map((folder) => ({
                  value: folder.id === null ? 'none' : String(folder.id),
                  label: folder.name,
                }))}
                value={folderId === null ? 'none' : String(folderId)}
                onChange={(value) => commitFolder(value === 'none' ? null : Number(value))}
              />
            )}
          {error ? <Badge variant="error" label={error} /> : null}

          {/* Segmentos */}
          <VStack gap={2} align="start" width="100%" paddingBlockEnd={6} data-testid="transcript-segments">
            {data.isInProgress ? (
              <Card padding={4} width="100%">
                <VStack gap={2} align="start">
                  <Heading level={3}>Transcrição em andamento</Heading>
                  <Text color="secondary">
                    Os segmentos aparecem aqui assim que a transcrição terminar.
                  </Text>
                </VStack>
              </Card>
            ) : segments.length === 0 ? (
              <Card padding={4} width="100%">
                <Text color="secondary">
                  {data.isError ? 'A transcrição falhou — tente novamente pela biblioteca.' : 'Nenhum segmento.'}
                </Text>
              </Card>
            ) : (
              segments.map((segment, index) => {
                const speaker = segment.speakerId ? speakerById.get(segment.speakerId) : undefined;
                const isActive = segment.id === activeSegmentId;
                return (
                  <div
                    key={segment.id}
                    data-segment-id={segment.id}
                    className={`seg${isActive ? ' active-seg' : ''}${focusedSegmentId === segment.id ? ' editing' : ''}`}
                    onClick={() => seek(segment.start)}
                  >
                    <button
                      type="button"
                      className="seg-time"
                      title="Ir para este ponto"
                      onClick={(event) => {
                        event.stopPropagation();
                        seek(segment.start);
                      }}
                    >
                      {formatTime(segment.start)}
                    </button>

                    <div className="seg-body">
                      <div className="seg-speaker-slot">
                        {speakerPicker(segment, (
                          <button type="button" className={`seg-speaker-chip${speaker ? '' : ' empty'}`}
                            style={speaker ? { color: speaker.colorHex } : undefined}
                            title={speaker ? 'Mudar locutor' : 'Atribuir locutor'}>
                            {speaker ? <span>{speaker.name}</span> : (
                              <>
                                <Icon icon={UserIcon} size="sm" />
                                <span>Locutor</span>
                              </>
                            )}
                          </button>
                        ))}
                      </div>

                      <textarea
                        ref={(element) => {
                          if (element) {
                            textareaRefs.current.set(segment.id, element);
                          } else {
                            textareaRefs.current.delete(segment.id);
                          }
                        }}
                        className="seg-text"
                        rows={1}
                        value={segment.text}
                        onChange={(event) =>
                          setSegments((previous) =>
                            previous.map((item) =>
                              item.id === segment.id ? { ...item, text: event.target.value } : item,
                            ),
                          )
                        }
                        onFocus={() => setFocusedSegmentId(segment.id)}
                        onBlur={(event) => {
                          setFocusedSegmentId(null);
                          saveSegmentText(segment.id, event.target.value);
                        }}
                        onKeyDown={(event) => onSegmentKeyDown(segment, index, event)}
                        onClick={(event) => event.stopPropagation()}
                      />
                    </div>

                    <div className="seg-actions" onClick={(event) => event.stopPropagation()}>
                      <IconButton
                        icon={<Icon icon={ScissorsIcon} size="sm" />}
                        label="Dividir no cursor"
                        variant="ghost"
                        size="sm"
                        onClick={() => void splitSegment(segment, textareaCaret(segment.id))}
                      />
                      <IconButton
                        icon={<Icon icon={MergeIcon} size="sm" />}
                        label="Mesclar com o anterior"
                        variant="ghost"
                        size="sm"
                        isDisabled={index === 0}
                        onClick={() => void mergeSegment(segment)}
                      />
                    </div>
                  </div>
                );
              })
            )}
          </VStack>
        </div>
      </div>
      {/* Player compacto: botão, tempo e waveform seguem a prévia da landing page. */}
      <div className="verso-player-wrap">
        <div className="verso-player">
          <IconButton
            icon={<Icon icon={isPlaying ? PauseIcon : PlayIcon} />}
            label={isPlaying ? 'Pausar' : 'Reproduzir'}
            variant="primary"
            size="md"
            data-testid="player-play"
            onClick={togglePlay}
            isDisabled={!data.mediaUrl}
          />
          <span className="verso-player-time" aria-label="Tempo atual">
            {formatTime(currentTime)}
          </span>
          <div className="verso-player-timeline">
            {data.mediaUrl ? (
              <WaveformCanvas
                mediaUrl={data.mediaUrl}
                loadPeaks={mediaActivated}
                duration={duration}
                expectedDuration={data.meta.durationSeconds}
                segments={segments}
                speakers={speakers}
                onSeek={seek}
              />
            ) : null}
            <input
              type="range"
              className="verso-seek"
              min={0}
              max={duration || 0}
              step={0.1}
              value={currentTime}
              aria-label="Posição do áudio"
              disabled={!data.mediaUrl || duration <= 0}
              onChange={(event) => seek(Number(event.target.value))}
            />
          </div>
          <span className="verso-player-time verso-player-duration" aria-label="Duração do áudio">
            {formatTime(duration)}
          </span>
          <select
            className="verso-speed"
            aria-label="Velocidade de reprodução"
            value={playbackRate}
            disabled={!data.mediaUrl}
            onChange={(event) => setPlaybackRate(Number(event.target.value))}
          >
            <option value={1}>1×</option>
            <option value={1.25}>1,25×</option>
            <option value={1.5}>1,5×</option>
            <option value={2}>2×</option>
          </select>
        </div>
      </div>
    </VStack>
  );

  function textareaCaret(segmentId: string): number {
    return textareaRefs.current.get(segmentId)?.selectionStart ?? 0;
  }
}
