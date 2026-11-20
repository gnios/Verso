import { useEffect, useState } from 'react';
import { Badge } from '@astryxdesign/core/Badge';
import { Button } from '@astryxdesign/core/Button';
import { Dialog, DialogHeader } from '@astryxdesign/core/Dialog';
import { HStack } from '@astryxdesign/core/HStack';
import { Text } from '@astryxdesign/core/Text';
import { TextArea } from '@astryxdesign/core/TextArea';
import { TextInput } from '@astryxdesign/core/TextInput';
import { VStack } from '@astryxdesign/core/VStack';
import { invoke } from '../lib/bridge';

interface FeedbackDialogProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
}

export function FeedbackDialog({ isOpen, onOpenChange }: FeedbackDialogProps) {
  const [isBug, setIsBug] = useState(true);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [status, setStatus] = useState('');
  const [isSuccess, setIsSuccess] = useState(false);

  useEffect(() => {
    if (!isOpen) {
      return;
    }

    setIsBug(true);
    setTitle('');
    setDescription('');
    setStatus('');
    setIsSuccess(false);
    void invoke<{ statusMessage: string }>('feedback.open').then((result) => {
      if (result.statusMessage) {
        setStatus(result.statusMessage);
      }
    });
  }, [isOpen]);

  const submit = async () => {
    if (submitting || !title.trim() || !description.trim()) {
      return;
    }

    setSubmitting(true);
    try {
      const result = await invoke<{ statusMessage: string; isSuccess: boolean }>('feedback.send', {
        isBug,
        title: title.trim(),
        description: description.trim(),
      });
      setStatus(result.statusMessage);
      setIsSuccess(result.isSuccess);
      if (result.isSuccess) {
        setTimeout(() => onOpenChange(false), 1500);
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Dialog isOpen={isOpen} onOpenChange={onOpenChange} width={520}>
      <DialogHeader title="Reportar bug / Sugerir melhoria" />
      <VStack gap={3} align="start">
        <HStack gap={2}>
          <Button
            size="sm"
            variant={isBug ? 'primary' : 'secondary'}
            label="Bug"
            onClick={() => setIsBug(true)}
          />
          <Button
            size="sm"
            variant={!isBug ? 'primary' : 'secondary'}
            label="Melhoria"
            onClick={() => setIsBug(false)}
          />
        </HStack>

        <TextInput
          label="Título"
          value={title}
          onChange={setTitle}
          placeholder="Resumo curto"
        />
        <TextArea
          label="Descrição"
          value={description}
          onChange={setDescription}
          placeholder="O que aconteceu? O que você esperava?"
        />
        <Text size="sm" color="secondary">
          Enviado com informações do sistema (CPU/RAM/GPU/últimas transcrições — sem conteúdo).
        </Text>

        {status ? <Badge variant={isSuccess ? 'success' : 'neutral'} label={status} /> : null}

        <HStack gap={2} justify="end" width="100%">
          <Button variant="secondary" label="Cancelar" onClick={() => onOpenChange(false)} />
          <Button
            variant="primary"
            label="Enviar"
            isDisabled={!title.trim() || !description.trim()}
            isLoading={submitting}
            onClick={() => void submit()}
          />
        </HStack>
      </VStack>
    </Dialog>
  );
}