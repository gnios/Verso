import { useCallback, useState } from 'react';

export type LibraryViewMode = 'table' | 'cards';

const STORAGE_KEY = 'verso.library.view';

/**
 * Preferência de visualização das listas de transcrições (Biblioteca e pastas).
 * Persistida no WebView; padrão = tabela.
 */
export function useLibraryViewMode(): [LibraryViewMode, (value: string) => void] {
  const [mode, setMode] = useState<LibraryViewMode>(() =>
    localStorage.getItem(STORAGE_KEY) === 'cards' ? 'cards' : 'table',
  );

  const update = useCallback((value: string) => {
    const next: LibraryViewMode = value === 'cards' ? 'cards' : 'table';
    setMode(next);
    localStorage.setItem(STORAGE_KEY, next);
  }, []);

  return [mode, update];
}
