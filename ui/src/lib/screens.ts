import { invoke } from './bridge';
import type { NavigationParam } from '../types';

export type { NavigationParam } from '../types';

export type ScreenKey = 'Dashboard' | 'Folder' | 'Upload' | 'Recording' | 'Editor' | 'Settings';

export function navigateTo(
  screen: ScreenKey,
  parameter: Omit<NavigationParam, 'screen'> = {},
): Promise<unknown> {
  return invoke('navigation.navigate', { screen, ...parameter });
}

export const SCREEN_TITLES: Record<ScreenKey, string> = {
  Dashboard: 'Biblioteca',
  Folder: 'Pasta',
  Upload: 'Nova transcrição',
  Recording: 'Gravação',
  Editor: 'Editor',
  Settings: 'Configurações',
};