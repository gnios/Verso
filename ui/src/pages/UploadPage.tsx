import { useCallback, useEffect, useState } from 'react';
import { Badge } from '@astryxdesign/core/Badge';
import { Button } from '@astryxdesign/core/Button';
import { Card } from '@astryxdesign/core/Card';
import { Collapsible } from '@astryxdesign/core/Collapsible';
import { EmptyState } from '@astryxdesign/core/EmptyState';
import { HStack } from '@astryxdesign/core/HStack';
import { Heading } from '@astryxdesign/core/Heading';
import { Icon } from '@astryxdesign/core/Icon';
import { IconButton } from '@astryxdesign/core/IconButton';
import { Popover } from '@astryxdesign/core/Popover';
import { Selector } from '@astryxdesign/core/Selector';
import { Text } from '@astryxdesign/core/Text';
import { TextInput } from '@astryxdesign/core/TextInput';
import { VStack } from '@astryxdesign/core/VStack';
import { invoke } from '../lib/bridge';
import { FOLDERS_ENABLED } from '../lib/features';
import type { NavigationParam } from '../lib/screens';
import type { FileDescription, UploadFormData } from '../types';
import { XIcon, FileIcon, UploadIcon } from '../icons';

interface UploadPageProps {
  parameter: NavigationParam;
}

/** Grade de ícones (emoji) do Verso — igual ao IconPicker do Blazor.
 *  Sem isIconOnly: ele joga o emoji para o aria-label e o botão fica vazio. */
function IconGrid({ icons, selected, onSelect }: { icons: string[]; selected: string | null; onSelect: (icon: string | null) => void }) {
  return (
    <HStack gap={1} wrap="wrap" width="100%">
      {icons.map((icon) => (
        <Button
          key={icon}
          size="sm"
          variant={selected === icon ? 'primary' : 'secondary'}
          label={icon}
          onClick={() => onSelect(selected === icon ? null : icon)}
        />
      ))}
    </HStack>
  );
}

export function UploadPage({ parameter }: UploadPageProps) {
  const [form, setForm] = useState<UploadFormData | null>(null);
  const [file, setFile] = useState<FileDescription | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [title, setTitle] = useState('');
  const [tagsText, setTagsText] = useState('');
  const [icon, setIcon] = useState<string | null>(null);
  const [iconPickerOpen, setIconPickerOpen] = useState(false);
  const [language, setLanguage] = useState('pt');
  const [quality, setQuality] = useState('Standard');
  const [engine, setEngine] = useState('Parakeet');
  const [parakeetModel, setParakeetModel] = useState('PtBrTagarela');
  const [speakerMode, setSpeakerMode] = useState('Automatic');
  const [folderId, setFolderId] = useState<number | null>(null);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadForm = useCallback(
    async (preselectedPath?: string) => {
      const data = await invoke<UploadFormData>('upload.load', { folderId: parameter.folderId });
      setForm(data);
      setLanguage(data.defaults.language);
      setQuality(data.defaults.quality);
      setEngine(data.defaults.engine);
      setParakeetModel(data.defaults.parakeetModel);
      setSpeakerMode(data.defaults.speakerMode);
      setFolderId(parameter.folderId ?? data.defaults.selectedFolderId ?? null);
      setIcon(data.icons[0] ?? null);
      if (preselectedPath) {
        const description = await invoke<FileDescription>('upload.selectFile', { path: preselectedPath });
        applyFile(description);
      }
    },
    [parameter.folderId],
  );

  const applyFile = (description: FileDescription) => {
    if (description.path) {
      setFile(description);
      setFileError(null);
      setTitle((current) => current || description.fileName.replace(/\.[^.]+$/, ''));
    } else {
      setFile(null);
      setFileError(description.error ?? 'Arquivo inválido.');
    }
  };

  useEffect(() => {
    void loadForm(parameter.recordingPath).catch(() => setError('Falha ao carregar o formulário.'));
    // recordingPath: vindo da tela de gravação (arquivo temporário já validado).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const pickFile = async () => {
    const description = await invoke<FileDescription>('upload.pickFile');
    applyFile(description);
  };

  const start = async () => {
    if (!file?.path || !title.trim() || starting) {
      return;
    }

    setStarting(true);
    setError(null);
    try {
      // A cópia do arquivo para o storage pode demorar em mídias grandes.
      await invoke('upload.start', {
        filePath: file.path,
        title: title.trim(),
        tags: tagsText
          .split(',')
          .map((tag) => tag.trim())
          .filter((tag) => tag.length > 0),
        icon,
        language,
        quality,
        engine,
        parakeetModel,
        speakerMode,
        folderId,
      }, 5 * 60_000);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Falha ao iniciar a transcrição.');
    } finally {
      setStarting(false);
    }
  };

  if (!form) {
    return (
      <VStack padding={6} align="start" gap={2}>
        <Heading level={1}>Nova transcrição</Heading>
        <Text color="secondary">Carregando…</Text>
      </VStack>
    );
  }

  const canStart = !!file?.path && !!title.trim() && !starting;
  const isLanguageLocked = engine === 'Parakeet' && parakeetModel === 'PtBrTagarela';

  return (
    // Fluxo linear de 3 decisões para o usuário leigo: arquivo → qualidade →
    // transcrever. Título/tags/idioma/motor ficam em "Mais opções" (defaults
    // vêm de Configurações e o título é auto-preenchido pelo nome do arquivo).
    <VStack gap={5} padding={4} align="center" isScrollable>
      <VStack gap={5} padding={4} align="start" width="100%" style={{ maxWidth: 680 }}>
        <VStack gap={1} align="start">
          <Heading level={1}>Nova transcrição</Heading>
          <Text color="secondary">Formatos: {form.formats}</Text>
        </VStack>

        <Card padding={4} width="100%">
          <VStack gap={3} align="start">
            <Heading level={3}>Arquivo</Heading>
            {file?.path ? (
              <HStack justify="between" align="center" width="100%" gap={2}>
                <HStack gap={2} align="center">
                  <Text size="lg">🎵</Text>
                  <VStack gap={0} align="start">
                    <Text>{file.fileName}</Text>
                    <Text size="sm" color="secondary">{file.fileSize}</Text>
                  </VStack>
                </HStack>
                <IconButton
                  icon={<Icon icon={XIcon} size="sm" />}
                  label="Remover arquivo"
                  variant="ghost"
                  onClick={() => {
                    setFile(null);
                    setTitle('');
                  }}
                />
              </HStack>
            ) : (
              <HStack justify="center" width="100%">
                <EmptyState
                  title="Selecione um arquivo"
                  description="Áudio ou vídeo para transcrever."
                  icon={<Icon icon={UploadIcon} size="lg" />}
                  actions={
                    <Button variant="primary" label="Selecionar arquivo" onClick={() => void pickFile()} />
                  }
                />
              </HStack>
            )}
            {fileError ? <Badge variant="error" label={fileError} /> : null}
          </VStack>
        </Card>

        <Card padding={4} width="100%">
          <VStack gap={3} align="start">
            <Heading level={3}>Detalhes</Heading>
            <HStack gap={2} align="end" width="100%">
              <Popover
                label="Ícone"
                isOpen={iconPickerOpen}
                onOpenChange={setIconPickerOpen}
                placement="below"
                alignment="start"
                width={252}
                padding={2}
                content={
                  <IconGrid
                    icons={form.icons}
                    selected={icon}
                    onSelect={(next) => {
                      setIcon(next ?? icon);
                      setIconPickerOpen(false);
                    }}
                  />
                }
              >
                <Button variant="secondary" size="lg" label={icon ?? '🎤'} />
              </Popover>
              <TextInput
                label="Título"
                value={title}
                onChange={setTitle}
                placeholder="Título da transcrição"
                description="Preenchido automaticamente pelo nome do arquivo."
                width="100%"
              />
            </HStack>
            <TextInput
              label="Tags"
              value={tagsText}
              onChange={setTagsText}
              placeholder="entrevista, urgente (separadas por vírgula)"
              description="Separe as tags por vírgula."
              width="100%"
            />
          </VStack>
        </Card>

        <HStack gap={3} width="100%" justify="end">
          {error ? <Badge variant="error" label={error} /> : null}
          <Button
            variant="primary"
            size="lg"
            label={starting ? 'Iniciando…' : 'Transcrever'}
            isDisabled={!canStart}
            isLoading={starting}
            icon={<Icon icon={FileIcon} />}
            onClick={() => void start()}
          />
        </HStack>

        {/* Avançado: personalização e opções técnicas (motor/modelo/idioma). */}
        <Collapsible
          defaultIsOpen={false}
          trigger={<Text size="sm" color="secondary">Mais opções</Text>}
          style={{ width: '100%' }}
        >
          <Card padding={4} width="100%" style={{ marginTop: 8 }}>
            <VStack gap={3} align="start">
              {FOLDERS_ENABLED && (
              <Selector
                label="Pasta"
                options={form.folders.map((option) => ({
                  value: option.id === null ? 'none' : String(option.id),
                  label: option.name,
                }))}
                value={folderId === null ? 'none' : String(folderId)}
                onChange={(value) => setFolderId(value === 'none' ? null : Number(value))}
                width="100%"
              />
              )}
              <Selector
                label="Idioma"
                options={form.languages.map((option) => ({ value: option.code, label: option.label }))}
                value={isLanguageLocked ? 'pt' : language}
                onChange={setLanguage}
                isDisabled={isLanguageLocked}
                width="100%"
              />
              {isLanguageLocked ? (
                <Text size="sm" color="secondary">O modelo pt-BR TAGARELA transcreve apenas português.</Text>
              ) : null}
              <Selector
                label="Motor"
                options={form.engines.map((option) => ({
                  value: option.value,
                  label: option.label,
                  description: option.hint,
                }))}
                value={engine}
                onChange={setEngine}
                width="100%"
              />
              {engine === 'Parakeet' ? (
                <Selector
                  label="Modelo Parakeet"
                  options={form.parakeetModels.map((option) => ({
                    value: option.value,
                    label: option.label,
                    description: option.sizeLabel,
                  }))}
                  value={parakeetModel}
                  onChange={setParakeetModel}
                  width="100%"
                />
              ) : null}
              {/* Qualidade só se aplica ao Whisper — o Parakeet ignora
                  ModelQuality (a escolha é o modelo TAGARELA/TDT v3). */}
              {engine === 'Whisper' ? (
                <VStack gap={2} align="start" width="100%">
                  <Text size="sm" color="secondary">Qualidade da transcrição (Whisper)</Text>
                  {form.models.map((model) => (
                    <Card
                      key={model.value}
                      variant={quality === model.value ? 'blue' : 'default'}
                      padding={3}
                      width="100%"
                      className="verso-card"
                      onClick={() => setQuality(model.value)}
                    >
                      <VStack gap={1} align="start">
                        <HStack justify="between" width="100%">
                          <Heading level={4}>{model.label}</Heading>
                          <Badge label={model.speedHint} />
                        </HStack>
                        <Text size="sm">{model.description}</Text>
                      </VStack>
                    </Card>
                  ))}
                </VStack>
              ) : null}
            </VStack>
          </Card>
        </Collapsible>
      </VStack>
    </VStack>
  );
}