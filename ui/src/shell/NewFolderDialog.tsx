import { useEffect, useState } from 'react';
import { Button } from '@astryxdesign/core/Button';
import { Dialog, DialogHeader } from '@astryxdesign/core/Dialog';
import { HStack } from '@astryxdesign/core/HStack';
import { Text } from '@astryxdesign/core/Text';
import { TextInput } from '@astryxdesign/core/TextInput';
import { VStack } from '@astryxdesign/core/VStack';
import { invoke } from '../lib/bridge';

/** Cores do ColorCatalog (C#) — mantidas em par com o protótipo. */
const PAGE_COLORS: { name: string; hex: string }[] = [
  { name: 'blue', hex: '#2eaadc' },
  { name: 'green', hex: '#4dab6f' },
  { name: 'purple', hex: '#9065b0' },
  { name: 'orange', hex: '#d9730d' },
  { name: 'pink', hex: '#c14c8a' },
  { name: 'yellow', hex: '#cb912f' },
  { name: 'red', hex: '#eb5757' },
  { name: 'teal', hex: '#0d9488' },
];

const PAGE_ICONS = [
  '📚', '🔬', '📝', '🎓', '💡', '🏛️', '📊', '🧠', '🌍', '💊',
  '⚖️', '🎤', '🎥', '🗂️', '💬', '🤝', '🔍', '📐', '📱', '🧬',
];

interface NewFolderDialogProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NewFolderDialog({ isOpen, onOpenChange }: NewFolderDialogProps) {
  const [title, setTitle] = useState('');
  const [icon, setIcon] = useState(PAGE_ICONS[0]);
  const [colorName, setColorName] = useState(PAGE_COLORS[0].name);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (isOpen) {
      setTitle('');
      setIcon(PAGE_ICONS[0]);
      setColorName(PAGE_COLORS[0].name);
      setError(null);
    }
  }, [isOpen]);

  const confirm = async () => {
    if (!title.trim() || creating) {
      return;
    }

    setCreating(true);
    setError(null);
    try {
      await invoke('folder.create', { title: title.trim(), icon, colorName });
      onOpenChange(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Falha ao criar a pasta.');
    } finally {
      setCreating(false);
    }
  };

  return (
    <Dialog isOpen={isOpen} onOpenChange={onOpenChange} width={480}>
      <DialogHeader title="Nova pasta" />
      <VStack gap={3} align="start">
        <TextInput
          label="Título"
          value={title}
          onChange={setTitle}
          placeholder="Ex.: Pesquisa de mestrado"
          onEnter={() => void confirm()}
        />

        <VStack gap={1} align="start" width="100%">
          <Text size="sm" color="secondary">Ícone</Text>
          <HStack gap={1} wrap="wrap">
            {PAGE_ICONS.map((option) => (
              <Button
                key={option}
                size="sm"
                variant={icon === option ? 'primary' : 'secondary'}
                label={option}
                onClick={() => setIcon(option)}
              >
                {option}
              </Button>
            ))}
          </HStack>
        </VStack>

        <VStack gap={1} align="start" width="100%">
          <Text size="sm" color="secondary">Cor</Text>
          <HStack gap={2}>
            {PAGE_COLORS.map((color) => (
              <Button
                key={color.name}
                size="sm"
                variant={colorName === color.name ? 'primary' : 'secondary'}
                label={color.name}
                isIconOnly
                icon={<span style={{ color: color.hex, fontSize: 18 }}>●</span>}
                onClick={() => setColorName(color.name)}
              />
            ))}
          </HStack>
        </VStack>

        {error ? <Text size="sm">{error}</Text> : null}

        <HStack gap={2} justify="end" width="100%">
          <Button variant="secondary" label="Cancelar" onClick={() => onOpenChange(false)} />
          <Button
            variant="primary"
            label="Criar"
            isDisabled={!title.trim()}
            isLoading={creating}
            onClick={() => void confirm()}
          />
        </HStack>
      </VStack>
    </Dialog>
  );
}