import { useEffect, useState } from 'react';
import { Dialog, DialogHeader } from '@astryxdesign/core/Dialog';
import { ProgressBar } from '@astryxdesign/core/ProgressBar';
import { Text } from '@astryxdesign/core/Text';
import { VStack } from '@astryxdesign/core/VStack';
import { onEvent } from '../lib/bridge';
import type { ModelDownloadInfo } from '../types';

/**
 * Notificação de download de modelo (eventos model.download.started/completed,
 * emitidos pelo ModelDownloadNotificationService quando a fila precisa de um modelo).
 */
export function ModelDownloadDialog() {
  const [info, setInfo] = useState<ModelDownloadInfo | null>(null);

  useEffect(() => {
    const offStarted = onEvent<ModelDownloadInfo>('model.download.started', setInfo);
    const offCompleted = onEvent('model.download.completed', () => setInfo(null));
    return () => {
      offStarted();
      offCompleted();
    };
  }, []);

  return (
    <Dialog isOpen={info !== null} onOpenChange={() => undefined} width={440} purpose="required">
      <DialogHeader title={info?.title ?? ''} />
      <VStack gap={3} align="start">
        <Text>{info?.message}</Text>
        <ProgressBar label="Progresso do download" isIndeterminate isLabelHidden />
      </VStack>
    </Dialog>
  );
}