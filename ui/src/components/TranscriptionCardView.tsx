import { Badge } from '@astryxdesign/core/Badge';
import { Button } from '@astryxdesign/core/Button';
import { Card } from '@astryxdesign/core/Card';
import { HStack } from '@astryxdesign/core/HStack';
import { Heading } from '@astryxdesign/core/Heading';
import { Icon } from '@astryxdesign/core/Icon';
import { IconButton } from '@astryxdesign/core/IconButton';
import { ProgressBar } from '@astryxdesign/core/ProgressBar';
import { Text } from '@astryxdesign/core/Text';
import { VStack } from '@astryxdesign/core/VStack';
import { invoke } from '../lib/bridge';
import { navigateTo } from '../lib/screens';
import type { TranscriptionCard } from '../types';
import { RefreshIcon, TrashIcon } from '../icons';

interface TranscriptionCardViewProps {
  card: TranscriptionCard;
  /** Método da bridge para ações (dashboard.action ou folder.action/transcription.action). */
  actionMethod?: string;
  /** Callback após uma ação (para a página recarregar). */
  onAfterAction?: () => void;
}

function statusBadge(card: TranscriptionCard): React.ReactNode {
  const variant = card.isDone ? 'success' : card.isError ? 'error' : 'info';
  return <Badge variant={variant} label={card.statusLabel} />;
}

export function TranscriptionCardView({
  card,
  actionMethod = 'dashboard.action',
  onAfterAction,
}: TranscriptionCardViewProps) {
  const runAction = (action: string) => {
    void invoke(actionMethod, { action, transcriptionId: card.id })
      .then(() => onAfterAction?.())
      .catch(() => undefined);
  };

  return (
    <Card
      elevation="low"
      className="verso-card"
      data-testid="transcription-card"
      onClick={() => void navigateTo('Editor', { transcriptionId: card.id })}
    >
      <VStack gap={3} align="start">
        <HStack justify="between" align="start" gap={3}>
          <HStack gap={2} align="start">
            <Text size="lg">{card.icon}</Text>
            <VStack gap={1} align="start">
              <Heading level={4}>{card.title}</Heading>
              {card.estimatedTimeLabel ? <Text size="sm">{card.estimatedTimeLabel}</Text> : null}
            </VStack>
          </HStack>
          {statusBadge(card)}
        </HStack>

        <Text color="secondary">{card.preview}</Text>

        {card.isInProgress ? (
          <VStack gap={1} align="start" width="100%">
            <ProgressBar
              label={`Progresso de ${card.title}`}
              value={card.progressPercent ?? 0}
              isIndeterminate={card.progressPercent === null}
              isLabelHidden
            />
            <Text size="sm">{card.preview}</Text>
          </VStack>
        ) : null}

        {card.tags.length > 0 ? (
          <HStack gap={1}>
            {card.tags.map((tag) => (
              <Badge key={tag.name} variant={tag.colorName as never} label={tag.name} />
            ))}
          </HStack>
        ) : null}

        <HStack justify="between" align="center" width="100%">
          <Text size="sm" color="secondary">
            {card.duration}
            {card.isDone && card.date ? ` · ${card.date}` : ''}
          </Text>
          <HStack gap={1}>
            {card.canRetry ? (
              <Button
                size="sm"
                variant="ghost"
                label="Tentar novamente"
                icon={<Icon icon={RefreshIcon} size="sm" />}
                onClick={(event) => {
                  event.stopPropagation();
                  runAction('retry');
                }}
              />
            ) : null}
            {card.canCancel ? (
              <Button
                size="sm"
                variant="ghost"
                label="Cancelar"
                onClick={(event) => {
                  event.stopPropagation();
                  runAction('cancel');
                }}
              />
            ) : null}
            {card.canDelete ? (
              <IconButton
                size="sm"
                variant="ghost"
                icon={<Icon icon={TrashIcon} size="sm" />}
                label={`Excluir ${card.title}`}
                onClick={(event) => {
                  event.stopPropagation();
                  runAction('delete');
                }}
              />
            ) : null}
          </HStack>
        </HStack>
      </VStack>
    </Card>
  );
}