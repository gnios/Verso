import { Badge } from '@astryxdesign/core/Badge';
import { HStack } from '@astryxdesign/core/HStack';
import { Icon } from '@astryxdesign/core/Icon';
import { IconButton } from '@astryxdesign/core/IconButton';
import { ProgressBar } from '@astryxdesign/core/ProgressBar';
import { proportional, Table } from '@astryxdesign/core/Table';
import type { TableColumn } from '@astryxdesign/core/Table';
import { Text } from '@astryxdesign/core/Text';
import { VStack } from '@astryxdesign/core/VStack';
import { useEffect, useRef, useState } from 'react';
import { invoke } from '../lib/bridge';
import { navigateTo } from '../lib/screens';
import type { TranscriptionCard } from '../types';
import { RefreshIcon, TrashIcon, XIcon } from '../icons';

interface TranscriptionTableViewProps {
  cards: TranscriptionCard[];
  /** Método da bridge para ações (dashboard.action ou folder.action). */
  actionMethod?: string;
  /** Callback após uma ação (para a página recarregar). */
  onAfterAction?: () => void;
}

/**
 * Lista de transcrições em formato tabela (visualização padrão da biblioteca).
 * A célula do título é o botão que abre o editor — espelha o clique do Card.
 */
export function TranscriptionTableView({
  cards,
  actionMethod = 'dashboard.action',
  onAfterAction,
}: TranscriptionTableViewProps) {
  const runAction = (card: TranscriptionCard, action: string) => {
    void invoke(actionMethod, { action, transcriptionId: card.id })
      .then(() => onAfterAction?.())
      .catch(() => undefined);
  };

  // Sem scroll: mede a área real e TROCA o conjunto de colunas em vez de
  // espremer tudo até ficar ilegível. 3 faixas: larga (6 col), média (sem
  // Data), estreita (sem Data/Tags) — pisos sempre abaixo da faixa seguinte.
  const wrapRef = useRef<HTMLDivElement>(null);
  const [wrapWidth, setWrapWidth] = useState(0);
  useEffect(() => {
    const el = wrapRef.current;
    if (!el) {
      return;
    }
    const observer = new ResizeObserver(([entry]) => setWrapWidth(entry.contentRect.width));
    observer.observe(el);
    return () => observer.disconnect();
  }, []);
  const tier = wrapWidth === 0 ? 'wide' : wrapWidth < 560 ? 'narrow' : wrapWidth < 760 ? 'mid' : 'wide';
  const wide = tier === 'wide';
  const narrow = tier === 'narrow';

  // Regra de largura: peso = minWidth/10 em todas as colunas → o piso da
  // tabela (max(minW_i × Σp / p_i)) vira exatamente a soma dos mínimos:
  // larga 675px, média 550px, estreita 450px — sempre abaixo da faixa.
  const columns: TableColumn<TranscriptionCard>[] = [
    {
      key: 'title',
      header: 'Transcrição',
      width: narrow
        ? proportional(18, { minWidth: 180 })
        : wide
          ? proportional(20, { minWidth: 200 })
          : proportional(19, { minWidth: 190 }),
      renderCell: (card) => (
        <button
          type="button"
          className="verso-row-open"
          data-testid="transcription-card"
          onClick={() => void navigateTo('Editor', { transcriptionId: card.id })}
        >
          <span className="verso-row-icon">{card.icon}</span>
          <span className="verso-row-texts">
            <span className="verso-row-title">{card.title}</span>
            <span className="verso-row-preview">{card.preview}</span>
          </span>
        </button>
      ),
    },
    {
      key: 'duration',
      header: 'Duração',
      width: narrow
        ? proportional(6, { minWidth: 60 })
        : wide
          ? proportional(8, { minWidth: 80 })
          : proportional(7, { minWidth: 70 }),
      align: 'end',
      // "Áudio · 54 min" → "54 min": o tipo já aparece no ícone do título.
      renderCell: (card) =>
        card.duration.includes('·')
          ? card.duration.split('·').slice(1).join('·').trim()
          : card.duration,
    },
    ...(wide
      ? [
          {
            key: 'date',
            header: 'Data',
            width: proportional(11, { minWidth: 110 }),
          } satisfies TableColumn<TranscriptionCard>,
        ]
      : []),
    ...(narrow
      ? []
      : [
          {
            key: 'tags',
            header: 'Tags',
            width: wide ? proportional(8, { minWidth: 80 }) : proportional(9, { minWidth: 90 }),
            renderCell: (card: TranscriptionCard) =>
              card.tags.length > 0 ? (
                <HStack gap={1}>
                  {card.tags.map((tag) => (
                    <Badge key={tag.name} variant={tag.colorName as never} label={tag.name} />
                  ))}
                </HStack>
              ) : null,
          } satisfies TableColumn<TranscriptionCard>,
        ]),
    {
      key: 'status',
      header: 'Status',
      width: proportional(18, { minWidth: 180 }),
      align: 'end',
      // Badge + ações na MESMA célula, encostados na direita — antes eram duas
      // colunas e a fatia proporcional da coluna de ações virava um vão entre
      // o badge e a lixeira.
      renderCell: (card) => (
        <HStack gap={2} align="center" justify="end" width="100%">
          {card.isInProgress ? (
            <VStack gap={1} align="start" style={{ flex: 1 }}>
              <ProgressBar
                label={`Progresso de ${card.title}`}
                value={card.progressPercent ?? 0}
                isIndeterminate={card.progressPercent === null}
                isLabelHidden
              />
              <Text size="sm" color="secondary" maxLines={1}>
                {card.estimatedTimeLabel ?? card.progressStage}
              </Text>
            </VStack>
          ) : (
            <Badge
              variant={card.isDone ? 'success' : card.isError ? 'error' : 'info'}
              label={card.statusLabel}
            />
          )}
          {card.canRetry ? (
            <IconButton
              size="sm"
              variant="ghost"
              icon={<Icon icon={RefreshIcon} size="sm" />}
              label={`Tentar novamente ${card.title}`}
              onClick={() => runAction(card, 'retry')}
            />
          ) : null}
          {card.canCancel ? (
            <IconButton
              size="sm"
              variant="ghost"
              icon={<Icon icon={XIcon} size="sm" />}
              label={`Cancelar ${card.title}`}
              onClick={() => runAction(card, 'cancel')}
            />
          ) : null}
          {card.canDelete ? (
            <IconButton
              size="sm"
              variant="ghost"
              icon={<Icon icon={TrashIcon} size="sm" />}
              label={`Excluir ${card.title}`}
              onClick={() => runAction(card, 'delete')}
            />
          ) : null}
        </HStack>
      ),
    },
  ];

  return (
    <div className="verso-table-wrap" ref={wrapRef} data-testid="transcription-table">
      <Table
        data={cards}
        columns={columns}
        idKey="id"
        density="balanced"
        hasHover
        textOverflow="truncate"
      />
    </div>
  );
}
