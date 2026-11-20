export const TAG_COLORS = {
  blue: 'Azul', cyan: 'Ciano', green: 'Verde', orange: 'Laranja', pink: 'Rosa',
  purple: 'Roxo', teal: 'Verde-azulado', yellow: 'Amarelo', red: 'Vermelho',
} as const;

export function tagVariant(color: string): keyof typeof TAG_COLORS {
  return Object.hasOwn(TAG_COLORS, color) ? color as keyof typeof TAG_COLORS : 'blue';
}
