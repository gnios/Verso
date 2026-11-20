import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Badge } from '@astryxdesign/core/Badge';
import { Button } from '@astryxdesign/core/Button';
import { Card } from '@astryxdesign/core/Card';
import { Collapsible } from '@astryxdesign/core/Collapsible';
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
import { navigateTo } from '../lib/screens';
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
  ChevronLeftIcon,
  DownloadIcon,
  MergeIcon,
  PauseIcon,
  PlayIcon,
  PlusIcon,
  ScissorsIcon,
  UserIcon,
} from '../icons';

interface EditorPageProps {
  parameter: NavigationParam;
}

const TAG_VARIANTS = ['blue', 'cyan', 'green', 'orange', 'pink', 'purple', 'teal', 'yellow', 'red'] as const;

// Cor estável por nome de tag: o mesmo nome sempre rende a mesma variante do
// Badge, sem depender do catálogo limitado do backend.
function tagVariant(name: string): (typeof TAG_VARIANTS)[number] {
  let hash = 2166136261;
  for (let i = 0; i < name.length; i++) {
    hash = Math.imul(hash ^ name.charCodeAt(i), 16777619);
  }
  return TAG_VARIANTS[(hash >>> 0) % TAG_VARIANTS.length];
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

export function EditorPage({ parameter }: EditorPageProps) {
  const [data, setData] = useState<EditorData | null>(null);
  const [segments, setSegments] = useState<EditorSegment[]>([]);
  const [speakers, setSpeakers] = useState<EditorSpeaker[]>([]);
  const [title, setTitle] = useState('');
  const [icon, setIcon] = useState<string | null>(null);
  const [tags, setTags] = useState<CardTag[]>([]);
  const [folderId, setFolderId] = useState<number | null>(null);
  const [allTags, setAllTags] = useState<CardTag[]>([]);
  const [tagPickerOpen, setTagPickerOpen] = useState(false);
  const [tagQuery, setTagQuery] = useState('');
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

  const commitTitle = () => {
    if (title.trim() && title !== data?.title) {
      void invoke('editor.updateMeta', { transcriptionId, title: title.trim() });
    }
  };

  const commitTags = (next: CardTag[]) => {
    setTags(next);
    void invoke('editor.updateMeta', { transcriptionId, tags: next.map((tag) => tag.name) });
  };

  const addTag = (name: string) => {
    const trimmed = name.trim();
    if (!trimmed || tags.some((tag) => tag.name.toLowerCase() === trimmed.toLowerCase())) {
      return;
    }
    const known = allTags.find((tag) => tag.name.toLowerCase() === trimmed.toLowerCase());
    commitTags([...tags, { name: known?.name ?? trimmed, colorName: known?.colorName ?? 'blue' }]);
    setTagQuery('');
    setTagPickerOpen(false);
  };

  const removeTag = (name: string) => {
    commitTags(tags.filter((tag) => tag.name.toLowerCase() !== name.toLowerCase()));
  };

  const tagSuggestions = allTags.filter(
    (tag) =>
      !tags.some((applied) => applied.name.toLowerCase() === tag.name.toLowerCase()) &&
      tag.name.toLowerCase().includes(tagQuery.trim().toLowerCase()),
  );
  const canCreateTag =
    tagQuery.trim().length > 0 &&
    !allTags.some((tag) => tag.name.toLowerCase() === tagQuery.trim().toLowerCase()) &&
    !tags.some((tag) => tag.name.toLowerCase() === tagQuery.trim().toLowerCase());

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

  const assignSpeaker = (segmentId: string, speakerId: string | null) => {
    setSegments((previous) =>
      previous.map((segment) => (segment.id === segmentId ? { ...segment, speakerId } : segment)),
    );
    setActiveSpeakerFor(null);
    if (speakerId) {
      void invoke('editor.assignSpeaker', { segmentId, speakerId });
    }
  };

  const addSpeaker = async (segmentId: string) => {
    const name = newSpeakerName.trim();
    if (!name) {
      return;
    }

    const speaker = await invoke<EditorSpeaker>('editor.addSpeaker', { transcriptionId, name });
    setSpeakers((previous) => [...previous, speaker]);
    setNewSpeakerName('');
    assignSpeaker(segmentId, speaker.id);
  };

  const exportAs = async (format: 'txt' | 'srt' | 'vtt') => {
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

      {/* Título + exportação no topo (pedido do usuário: título editável
          primeiro, junto dos botões de baixar; player vem em seguida). */}
      <HStack gap={2} align="center" padding={4} paddingBlockEnd={2} width="100%">
        <IconButton
          icon={<Icon icon={ChevronLeftIcon} />}
          label="Voltar para a biblioteca"
          variant="ghost"
          onClick={() => void navigateTo('Dashboard')}
        />
        <Text size="lg">{icon ?? '📝'}</Text>
        <TextInput
          label="Título da transcrição"
          isLabelHidden
          value={title}
          onChange={setTitle}
          onBlur={commitTitle}
          width="100%"
          data-testid="editor-title"
        />
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
        {/* flex-shrink 0: sem isso o título (width 100%) espremia o botão
            até o rótulo virar "B…". */}
        <div style={{ flexShrink: 0 }}>
        <DropdownMenu
          button={{
            variant: 'secondary',
            size: 'lg',
            label: 'Baixar',
            icon: <Icon icon={DownloadIcon} size="sm" />,
            isDisabled: !data.hasSegments,
          }}
          menuWidth={200}
          items={[
            { label: 'Texto (TXT)', onClick: () => void exportAs('txt') },
            { label: 'Legendas (SRT)', onClick: () => void exportAs('srt') },
            { label: 'Legendas (VTT)', onClick: () => void exportAs('vtt') },
          ]}
        />
        </div>
      </HStack>

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

      {/* Conteúdo rolável: meta, tags e segmentos */}
      <VStack gap={3} align="start" padding={4} width="100%" isScrollable>
        {/* Meta + tags */}
        <Collapsible
          defaultIsOpen
          trigger={<Text size="sm" color="secondary">Tags</Text>}
          style={{ width: '100%' }}
        >
        <HStack gap={2} wrap="wrap" style={{ marginTop: 8 }}>
          <Badge label={data.meta.model} />
          <Badge label={data.meta.duration} />
          {data.meta.processingTime ? <Badge label={`Processado em ${data.meta.processingTime}`} /> : null}
          {data.meta.speakerCount > 0 ? <Badge label={`${data.meta.speakerCount} locutores`} /> : null}
          {data.isInProgress ? <Badge variant="info" label="Em andamento" /> : null}
          {data.isError ? <Badge variant="error" label={data.errorMessage ?? 'Erro'} /> : null}
          {tags.map((tag) => (
            <span key={tag.name} className="verso-tag-chip">
              <Badge variant={tagVariant(tag.name)} label={tag.name} />
              <button
                type="button"
                className="verso-tag-chip-remove"
                aria-label={`Remover tag ${tag.name}`}
                onClick={() => removeTag(tag.name)}
              >
                ×
              </button>
            </span>
          ))}
          <Popover
            label="Adicionar tag"
            isOpen={tagPickerOpen}
            onOpenChange={setTagPickerOpen}
            placement="below"
            alignment="start"
            width={240}
            padding={2}
            content={
              <VStack gap={2} align="start" width="100%">
                <TextInput
                  label="Tag"
                  isLabelHidden
                  value={tagQuery}
                  onChange={setTagQuery}
                  placeholder="Buscar ou criar tag…"
                  width="100%"
                  onEnter={() => {
                    if (tagSuggestions.length > 0 && !canCreateTag) {
                      addTag(tagSuggestions[0].name);
                    } else if (tagQuery.trim()) {
                      addTag(tagQuery);
                    }
                  }}
                />
                {tagSuggestions.map((tag) => (
                  <Button
                    key={tag.name}
                    variant="ghost"
                    width="100%"
                    label={tag.name}
                    onClick={() => addTag(tag.name)}
                  />
                ))}
                {canCreateTag ? (
                  <Button
                    variant="ghost"
                    width="100%"
                    label={`Criar "${tagQuery.trim()}"`}
                    icon={<Icon icon={PlusIcon} />}
                    onClick={() => addTag(tagQuery)}
                  />
                ) : null}
                {tagSuggestions.length === 0 && !canCreateTag ? (
                  <Text size="sm" color="secondary">
                    {allTags.length === 0 ? 'Digite para criar uma tag.' : 'Nenhuma tag encontrada.'}
                  </Text>
                ) : null}
              </VStack>
            }
          >
            <IconButton
              icon={<Icon icon={PlusIcon} />}
              label="Adicionar tag"
              variant="secondary"
              size="sm"
              onClick={() => setTagPickerOpen(true)}
            />
          </Popover>
        </HStack>
        </Collapsible>

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
                      <button
                        type="button"
                        className={`seg-speaker-chip${speaker ? '' : ' empty'}`}
                        style={speaker ? { color: speaker.colorHex } : undefined}
                        title={speaker ? 'Mudar locutor' : 'Atribuir locutor'}
                        onClick={(event) => {
                          event.stopPropagation();
                          setActiveSpeakerFor(activeSpeakerFor === segment.id ? null : segment.id);
                        }}
                      >
                        {speaker ? (
                          <>
                            <span className="seg-speaker-dot" style={{ background: speaker.colorHex }} />
                            <span>{speaker.name}</span>
                          </>
                        ) : (
                          <>
                            <Icon icon={UserIcon} size="sm" />
                            <span>Locutor</span>
                          </>
                        )}
                      </button>
                    </div>

                    {activeSpeakerFor === segment.id ? (
                      <Card variant="muted" padding={2} width="100%">
                        <VStack gap={2} align="start">
                          <Selector
                            label="Locutor"
                            options={[
                              { value: 'none', label: 'Sem locutor' },
                              ...speakers.map((option) => ({ value: option.id, label: option.name })),
                            ]}
                            value={segment.speakerId ?? 'none'}
                            onChange={(value) => assignSpeaker(segment.id, value === 'none' ? null : value)}
                            width="100%"
                          />
                          <HStack gap={2} width="100%">
                            <TextInput
                              label="Novo locutor"
                              value={newSpeakerName}
                              onChange={setNewSpeakerName}
                              placeholder="Nome do locutor"
                              onEnter={() => void addSpeaker(segment.id)}
                            />
                            <Button
                              size="sm"
                              variant="secondary"
                              label="Adicionar"
                              icon={<Icon icon={PlusIcon} size="sm" />}
                              onClick={() => void addSpeaker(segment.id)}
                            />
                          </HStack>
                        </VStack>
                      </Card>
                    ) : null}

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
      </VStack>
    </VStack>
  );

  function textareaCaret(segmentId: string): number {
    return textareaRefs.current.get(segmentId)?.selectionStart ?? 0;
  }
}
