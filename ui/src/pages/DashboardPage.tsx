import { EmptyState } from '@astryxdesign/core/EmptyState';
import { Heading } from '@astryxdesign/core/Heading';
import { HStack } from '@astryxdesign/core/HStack';
import { Icon } from '@astryxdesign/core/Icon';
import { Button } from '@astryxdesign/core/Button';
import { Text } from '@astryxdesign/core/Text';
import { TextInput } from '@astryxdesign/core/TextInput';
import { VStack } from '@astryxdesign/core/VStack';
import { invoke, onEvent } from '../lib/bridge';
import { navigateTo } from '../lib/screens';
import { useLibraryViewMode } from '../lib/viewMode';
import { useCallback, useEffect, useRef, useState } from 'react';
import type { CardProgress, DashboardData, NavigationParam } from '../types';
import { PlusIcon, SearchIcon } from '../icons';
import { LibraryViewToggle } from '../components/LibraryViewToggle';
import { TranscriptionCardView } from '../components/TranscriptionCardView';
import { TranscriptionTableView } from '../components/TranscriptionTableView';

interface DashboardPageProps {
  parameter: NavigationParam;
}

const TITLES: Record<string, string> = {
  All: 'Biblioteca',
  Progress: 'Em andamento',
  Done: 'Concluídas',
  Error: 'Com erro',
};

export function DashboardPage({ parameter }: DashboardPageProps) {
  const [data, setData] = useState<DashboardData | null>(null);
  const [search, setSearch] = useState('');
  const [viewMode, setViewMode] = useLibraryViewMode();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const load = useCallback(async (param: NavigationParam, searchText: string) => {
    setLoading(true);
    setError(null);
    try {
      const result = await invoke<DashboardData>(
        searchText ? 'dashboard.search' : 'dashboard.load',
        { ...param, searchText },
      );
      setData(result);
    } catch (err) {
      // Sem catch, load() falho mostrava grade vazia — página "sem conteúdo".
      setError(err instanceof Error ? err.message : 'Falha ao carregar a biblioteca.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    setSearch('');
    void load(parameter, '');
    const offChanged = onEvent<DashboardData>('dashboard.changed', setData);
    const offProgress = onEvent<CardProgress>('dashboard.progress', (progress) => {
      setData((previous) => {
        if (!previous) {
          return previous;
        }
        return {
          ...previous,
          cards: previous.cards.map((card) =>
            card.id === progress.transcriptionId
              ? {
                  ...card,
                  progressPercent: progress.progressPercent,
                  progressStage: progress.progressStage,
                  preview: progress.label,
                }
              : card,
          ),
        };
      });
    });
    return () => {
      offChanged();
      offProgress();
      if (debounceRef.current) {
        clearTimeout(debounceRef.current);
      }
    };
  }, [parameter, load]);

  const onSearchChange = (value: string) => {
    setSearch(value);
    if (debounceRef.current) {
      clearTimeout(debounceRef.current);
    }
    debounceRef.current = setTimeout(() => void load(parameter, value), 400);
  };

  const activeFilter = parameter.unassignedOnly
    ? 'unassigned'
    : parameter.tagId !== undefined
      ? 'tag'
      : (parameter.statusFilter ?? 'All');
  const title = activeFilter === 'unassigned' ? 'Sem pasta' : TITLES[activeFilter] ?? 'Biblioteca';
  const isEmpty = data?.isEmpty ?? false;
  const isProgressEmpty = activeFilter === 'Progress' && !search.trim();

  return (
    <VStack className="library-page" gap={4} align="start">
      <HStack className="library-toolbar" justify="between" align="end" gap={4} width="100%" wrap="wrap">
        <VStack gap={1} align="start">
          <Heading level={1}>{title}</Heading>
          <Text color="secondary">
            {loading ? 'Carregando…' : `${data?.cards.length ?? 0} transcrições`}
          </Text>
        </VStack>
        <HStack className="library-controls" gap={3} align="end" wrap="wrap">
          <div className="library-search">
            <TextInput
              type="search"
              label="Buscar transcrições"
              isLabelHidden
              placeholder="Buscar por título…"
              value={search}
              onChange={onSearchChange}
              startIcon={SearchIcon}
              width="100%"
            />
          </div>
          <LibraryViewToggle value={viewMode} onChange={setViewMode} />
        </HStack>
      </HStack>

      {error ? (
        <HStack justify="center" width="100%">
          <EmptyState
            title="Não foi possível carregar"
            description={error}
            icon={<img src="/brand/verso-mascote-cabeca.png" alt="" style={{ height: 110 }} />}
            actions={
              <Button
                variant="primary"
                label="Tentar de novo"
                onClick={() => void load(parameter, search)}
              />
            }
          />
        </HStack>
      ) : isEmpty ? (
        <HStack justify="center" width="100%">
          <EmptyState
            title={isProgressEmpty ? 'Nenhuma transcrição em andamento' : 'Nenhuma transcrição aqui'}
            description={isProgressEmpty
              ? 'Acompanhe aqui o progresso das suas transcrições.'
              : 'Transcreva um áudio ou vídeo para começar, ou ajuste o filtro de busca.'}
            icon={<img
              src={isProgressEmpty ? '/brand/verso-mascote-em-andamento.png' : '/brand/verso-mascote-biblioteca.png'}
              alt=""
              style={{ height: 150 }}
            />}
            actions={
              <Button
                variant="primary"
                label="Nova transcrição"
                icon={<Icon icon={PlusIcon} />}
                onClick={() => void navigateTo('Upload')}
              />
            }
          />
        </HStack>
      ) : viewMode === 'table' ? (
        data ? (
          <TranscriptionTableView cards={data.cards} />
        ) : null
      ) : (
        <div className="library-card-grid">
          {data?.cards.map((card) => (
            <TranscriptionCardView key={card.id} card={card} />
          ))}
        </div>
      )}
    </VStack>
  );
}
