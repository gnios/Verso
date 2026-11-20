import EmojiPicker, { Categories, EmojiStyle, SuggestionMode, Theme } from 'emoji-picker-react';
import type { PickerProps } from 'emoji-picker-react';

const categories: PickerProps['categories'] = [
  { category: Categories.SUGGESTED, name: 'Recentes' },
  { category: Categories.SMILEYS_PEOPLE, name: 'Rostos e pessoas' },
  { category: Categories.ANIMALS_NATURE, name: 'Animais e natureza' },
  { category: Categories.FOOD_DRINK, name: 'Comidas e bebidas' },
  { category: Categories.TRAVEL_PLACES, name: 'Viagens e lugares' },
  { category: Categories.ACTIVITIES, name: 'Atividades' },
  { category: Categories.OBJECTS, name: 'Objetos' },
  { category: Categories.SYMBOLS, name: 'Símbolos' },
  { category: Categories.FLAGS, name: 'Bandeiras' },
];

export default function TranscriptionEmojiPicker({ isDark, onSelect }: {
  isDark: boolean;
  onSelect: (emoji: string) => void;
}) {
  return (
    <EmojiPicker
      categories={categories}
      emojiStyle={EmojiStyle.NATIVE}
      theme={isDark ? Theme.DARK : Theme.LIGHT}
      searchPlaceholder="Buscar emoji…"
      searchClearButtonLabel="Limpar busca"
      suggestedEmojisMode={SuggestionMode.RECENT}
      previewConfig={{ showPreview: false }}
      width="100%"
      height="min(360px, 65vh)"
      onEmojiClick={({ emoji }) => onSelect(emoji)}
    />
  );
}
