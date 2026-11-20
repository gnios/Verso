import { useCallback, useEffect, useRef, useState } from 'react';
import { Badge } from '@astryxdesign/core/Badge';
import { Button } from '@astryxdesign/core/Button';
import { Card } from '@astryxdesign/core/Card';
import { HStack } from '@astryxdesign/core/HStack';
import { Heading } from '@astryxdesign/core/Heading';
import { Icon } from '@astryxdesign/core/Icon';
import { Text } from '@astryxdesign/core/Text';
import { VStack } from '@astryxdesign/core/VStack';
import { navigateTo } from '../lib/screens';
import { MicIcon, PauseIcon, PlayIcon, StopIcon } from '../icons';

/**
 * Gravação real do microfone (MediaRecorder + AnalyserNode para o VU meter).
 * Ao parar, o blob é enviado por POST /recordings (LocalAppServer), que salva um
 * .webm temporário e devolve o caminho — a tela Upload pré-seleciona o arquivo.
 */
export function RecordingPage() {
  const [state, setState] = useState<'idle' | 'recording' | 'paused' | 'saving'>('idle');
  const [elapsed, setElapsed] = useState(0);
  const [levels, setLevels] = useState<number[]>(() => new Array(48).fill(4));
  const [error, setError] = useState<string | null>(null);

  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const streamRef = useRef<MediaStream | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const rafRef = useRef<number | null>(null);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const cleanup = useCallback(() => {
    if (rafRef.current !== null) {
      cancelAnimationFrame(rafRef.current);
      rafRef.current = null;
    }
    if (timerRef.current) {
      clearInterval(timerRef.current);
      timerRef.current = null;
    }
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    void audioContextRef.current?.close().catch(() => undefined);
    audioContextRef.current = null;
    analyserRef.current = null;
    mediaRecorderRef.current = null;
  }, []);

  useEffect(() => cleanup, [cleanup]);

  const start = async () => {
    setError(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      streamRef.current = stream;

      const audioContext = new AudioContext();
      audioContextRef.current = audioContext;
      const analyser = audioContext.createAnalyser();
      analyser.fftSize = 256;
      audioContext.createMediaStreamSource(stream).connect(analyser);
      analyserRef.current = analyser;

      const recorder = new MediaRecorder(stream);
      chunksRef.current = [];
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) {
          chunksRef.current.push(event.data);
        }
      };
      recorder.onstop = () => void saveRecording();
      recorder.start(1000);
      mediaRecorderRef.current = recorder;

      setElapsed(0);
      timerRef.current = setInterval(() => setElapsed((seconds) => seconds + 1), 1000);
      const data = new Uint8Array(analyser.frequencyBinCount);
      const tick = () => {
        analyser.getByteFrequencyData(data);
        const bars: number[] = [];
        const step = Math.floor(data.length / 48) || 1;
        for (let i = 0; i < 48; i++) {
          let sum = 0;
          for (let j = 0; j < step; j++) {
            sum += data[i * step + j] ?? 0;
          }
          bars.push(4 + (sum / step / 255) * 60);
        }
        setLevels(bars);
        rafRef.current = requestAnimationFrame(tick);
      };
      rafRef.current = requestAnimationFrame(tick);
      setState('recording');
    } catch (err) {
      setError(
        err instanceof Error && err.name === 'NotAllowedError'
          ? 'Permissão de microfone negada.'
          : 'Não foi possível acessar o microfone.',
      );
    }
  };

  const togglePause = () => {
    const recorder = mediaRecorderRef.current;
    if (!recorder) {
      return;
    }

    if (state === 'recording') {
      recorder.pause();
      setState('paused');
    } else if (state === 'paused') {
      recorder.resume();
      setState('recording');
    }
  };

  const saveRecording = async () => {
    const chunks = chunksRef.current;
    cleanup();
    setState('saving');
    try {
      const blob = new Blob(chunks, { type: 'audio/webm' });
      const response = await fetch('/recordings', {
        method: 'POST',
        headers: { 'Content-Type': 'audio/webm' },
        body: blob,
      });
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const result = (await response.json()) as { path?: string };
      if (!result.path) {
        throw new Error('caminho não retornado');
      }

      // Vai para o Upload com o arquivo pré-selecionado (título/tags/perfil lá).
      void navigateTo('Upload', { recordingPath: result.path });
    } catch {
      setError('Falha ao salvar a gravação.');
      setState('idle');
    }
  };

  const stop = () => mediaRecorderRef.current?.stop();

  const minutes = String(Math.floor(elapsed / 60)).padStart(2, '0');
  const seconds = String(elapsed % 60).padStart(2, '0');
  const isRecording = state === 'recording' || state === 'paused';

  return (
    <VStack gap={5} padding={4} align="center" justify="center" height="100%">
      <VStack gap={1} align="center">
        <Heading level={1}>Gravação</Heading>
        <Text color="secondary">
          {state === 'idle' && 'Pronto para gravar'}
          {state === 'recording' && 'Gravando…'}
          {state === 'paused' && 'Pausado'}
          {state === 'saving' && 'Salvando gravação…'}
        </Text>
      </VStack>

      <Card padding={6} width="100%" maxWidth={720}>
        <VStack gap={5} align="center">
          <Heading level={2}>{`${minutes}:${seconds}`}</Heading>

          <HStack gap={1} align="end" height={64}>
            {levels.map((level, index) => (
              <div
                key={index}
                className="verso-wave-bar"
                style={{
                  width: 4,
                  height: `${level}px`,
                  borderRadius: 2,
                  background: isRecording && state === 'recording'
                    ? 'var(--color-accent)'
                    : 'var(--color-border)',
                }}
              />
            ))}
          </HStack>

          <HStack gap={3} justify="center">
            {isRecording ? (
              <>
                <Button
                  variant="secondary"
                  label={state === 'paused' ? 'Continuar' : 'Pausar'}
                  icon={<Icon icon={state === 'paused' ? PlayIcon : PauseIcon} />}
                  onClick={togglePause}
                />
                <Button
                  variant="destructive"
                  label="Parar e transcrever"
                  icon={<Icon icon={StopIcon} />}
                  onClick={stop}
                />
              </>
            ) : (
              <Button
                variant="primary"
                size="lg"
                label="Gravar"
                icon={<Icon icon={MicIcon} />}
                isLoading={state === 'saving'}
                isDisabled={state === 'saving'}
                onClick={() => void start()}
              />
            )}
          </HStack>

          {error ? <Badge variant="error" label={error} /> : null}
          <Text size="sm" color="secondary">
            Ao parar, a gravação abre na tela “Nova transcrição” para revisar título e perfil.
          </Text>
        </VStack>
      </Card>
    </VStack>
  );
}