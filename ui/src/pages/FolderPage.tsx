import { useCallback, useEffect, useState } from 'react';
import { Badge } from '@astryxdesign/core/Badge';
import { Button } from '@astryxdesign/core/Button';
import { EmptyState } from '@astryxdesign/core/EmptyState';
import { Grid } from '@astryxdesign/core/Grid';
import { HStack } from '@astryxdesign/core/HStack';
import { Heading } from '@astryxdesign/core/Heading';
import { Icon } from '@astryxdesign/core/Icon';
import { Text } from '@astryxdesign/core/Text';
import { VStack } from '@astryxdesign/core/VStack';
import { invoke, onEvent } from '../lib/bridge';
import { navigateTo } from '../lib/screens';
import { useLibraryViewMode } from '../lib/viewMode';
import type { NavigationParam } from '../lib/screens';
import type { FolderData } from '../types';
import { PlusIcon, TrashIcon } from '../icons';
import { LibraryViewToggle } from '../components/LibraryViewToggle';
import { TranscriptionCardView } from '../components/TranscriptionCardView';
import { TranscriptionTableView } from '../components/TranscriptionTableView';

interface FolderPageProps {
  parameter: NavigationParam;
}

export function FolderPage({ parameter }: FolderPageProps) {
  const [data, setData] = useState<FolderData | null>(null);
  const [viewMode, setViewMode] = useLibraryViewMode();
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!parameter.folderId) {
      return;
    }

    try {
      setData(await invoke<FolderData>('folder.load', { folderId: parameter.folderId }));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Falha ao carregar a pasta.');
    }
  }, [parameter.folderId]);

  useEffect(() => {
    setData(null);
    void load();
    const offFolder = onEvent('folder.changed', () => void load());
    const offQueue = onEvent('queue.status', () => void load());
    return () => {
      offFolder();
      offQueue();
    };
  }, [load]);

  const deleteFolder = () => {
    if (parameter.folderId) {
      void invoke('folder.delete', { folderId: parameter.folderId });
    }
  };

  if (!parameter.folderId) {
    return (
      <VStack padding={6} align="start">
        <Heading level={1}>Pasta</Heading>
        <Text color="secondary">Nenhuma pasta selecionada.</Text>
      </VStack>
    );
  }

  if (!data) {
    return (
      <VStack padding={6} align="start" gap={2}>
        <Heading level={1}>Pasta</Heading>
        <Text color="secondary">{error ?? 'Carregando…'}</Text>
      </VStack>
    );
  }

  return (
    <VStack gap={4} padding={4} align="start" isScrollable>
      <HStack justify="between" align="end" width="100%">
        <VStack gap={1} align="start">
          <HStack gap={2} align="center">
            <Text size="lg">{data.icon}</Text>
            <Heading level={1}>{data.title}</Heading>
            <Badge label={`${data.cards.length} transcrições`} />
          </HStack>
          {data.description ? <Text color="secondary">{data.description}</Text> : null}
        </VStack>
        <HStack gap={2} align="center">
          <LibraryViewToggle value={viewMode} onChange={setViewMode} />
          <Button
            variant="primary"
            label="Transcrever aqui"
            icon={<Icon icon={PlusIcon} />}
            onClick={() => void navigateTo('Upload', { folderId: data.id })}
          />
          <Button
            variant="ghost"
            label="Excluir pasta"
            icon={<Icon icon={TrashIcon} />}
            onClick={() => deleteFolder()}
          />
        </HStack>
      </HStack>

      {data.isEmpty ? (
        <HStack justify="center" width="100%">
          <EmptyState
            title="Pasta vazia"
            description="Transcreva um áudio ou vídeo e associe a esta pasta."
            icon={<img src="/brand/verso-mascote.png" alt="" style={{ height: 150 }} />}
            actions={
              <Button
                variant="primary"
                label="Transcrever aqui"
                icon={<Icon icon={PlusIcon} />}
                onClick={() => void navigateTo('Upload', { folderId: data.id })}
              />
            }
          />
        </HStack>
      ) : viewMode === 'table' ? (
        <TranscriptionTableView
          cards={data.cards}
          actionMethod="folder.action"
          onAfterAction={() => void load()}
        />
      ) : (
        <Grid columns={{ minWidth: 340, max: 3 }} gap={4} width="100%">
          {data.cards.map((card) => (
            <TranscriptionCardView
              key={card.id}
              card={card}
              actionMethod="folder.action"
              onAfterAction={() => void load()}
            />
          ))}
        </Grid>
      )}
    </VStack>
  );
}